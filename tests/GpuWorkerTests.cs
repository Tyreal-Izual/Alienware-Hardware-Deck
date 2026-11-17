using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using HardwareDeck;

public static class GpuWorkerTests
{
    static int checks;
    static readonly string executable = typeof(GpuWorkerTests).Assembly.Location;
    static readonly string frame = new JavaScriptSerializer().Serialize(new GpuData
    {
        Name = "Fixture GPU", Temperature = 42, Load = 12, MemoryUsedMiB = 1024,
        MemoryTotalMiB = 24576, FanPercent = 30, PowerWatts = 80, PowerLimitWatts = 450, ClockMHz = 900
    });

    static void Check(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
        checks++;
    }

    static void Wait(Func<bool> condition, string message, int milliseconds = 8000)
    {
        var timer = Stopwatch.StartNew();
        while (!condition() && timer.ElapsedMilliseconds < milliseconds)
            Thread.Sleep(10);
        Check(condition(), message);
    }

    [DllImport("kernel32.dll")]
    static extern bool TerminateProcess(IntPtr process, uint code);

    static int Fake(string kind)
    {
        // CI may provide redirected handles without an attached console/code page.
        Console.SetIn(new StreamReader(Console.OpenStandardInput(), Encoding.UTF8));
        Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true });
        if (Console.ReadLine() != GpuWorker.Handshake)
            return 2;
        if (kind == "crash")
        {
            using (var self = Process.GetCurrentProcess())
                TerminateProcess(self.Handle, 0xc0000005);
            return 1;
        }
        if (kind == "invalid")
        {
            Console.WriteLine("not JSON");
            Console.Out.Flush();
            Thread.Sleep(Timeout.Infinite);
        }
        if (kind == "oversized")
        {
            Console.WriteLine(new string('x', GpuWorker.MaximumLine + 1));
            Console.Out.Flush();
            Thread.Sleep(Timeout.Infinite);
        }
        if (kind == "error")
        {
            Console.WriteLine("{\"Error\":\"driver unavailable\"}");
            Console.Out.Flush();
            Thread.Sleep(Timeout.Infinite);
        }
        if (kind != "hang")
        {
            Console.WriteLine(frame);
            Console.Out.Flush();
        }
        if (kind == "hang" || kind == "stale")
            Thread.Sleep(Timeout.Infinite);
        while (true)
        {
            Thread.Sleep(50);
            Console.WriteLine(frame);
            Console.Out.Flush();
        }
    }

    static void Recovery(string kind, string fixture)
    {
        int starts = 0;
        var events = new ConcurrentQueue<string>();
        bool previousExited = false;
        Process firstHandle = null;
        using (var reader = new NvidiaReader(() =>
        {
            int attempt = Interlocked.Increment(ref starts);
            if (attempt > 1)
                previousExited = firstHandle == null || firstHandle.HasExited;
            var child = new GpuProcess(fixture, "--fake-gpu " + (attempt == 1 ? kind : "normal"));
            if (attempt == 1)
            {
                // An immediately crashing child can exit before this test attaches.
                try { firstHandle = Process.GetProcessById(child.Id); }
                catch (ArgumentException) { firstHandle = null; }
            }
            return child;
        }, (name, message) => events.Enqueue(name + " " + message), 1500, 300))
        {
            var timer = Stopwatch.StartNew();
            Check(reader.Read().Error != null && timer.ElapsedMilliseconds < 300, kind + ": read never waits for process start");
            if (kind == "stale")
                Wait(() => reader.Read().Temperature == 42, "Receive a frame before the helper hangs");
            Wait(() => events.Any(e => e.StartsWith("gpu.failure")), kind + ": failure is logged");
            var missing = reader.Read();
            Check(!missing.Temperature.HasValue && !missing.Load.HasValue && missing.Error != null,
                kind + ": old GPU values are cleared during recovery");
            timer.Restart();
            for (int i = 0; i < 1000; i++)
                reader.Read();
            Check(timer.ElapsedMilliseconds < 300, kind + ": cached reads remain responsive while unavailable");
            try
            {
                Wait(() => events.Any(e => e.StartsWith("gpu.recovered")) && reader.Read().Temperature == 42,
                    kind + ": a replacement returns valid readings");
            }
            catch
            {
                Console.Error.WriteLine(string.Join(Environment.NewLine, events));
                throw;
            }
            Check(previousExited && starts == 2, kind + ": replace only after the old worker exits");
            Check(events.Count(e => e.StartsWith("gpu.failure")) == 1, kind + ": no per-frame fault spam");
            if (kind == "crash")
                Check(events.Any(e => e.Contains("0xC0000005")), "Native-style child exit code reaches the parent log");
            if (kind == "hang" || kind == "stale")
                Check(events.Any(e => e.Contains("TimeoutException")), "Hangs use a bounded timeout");
        }
        if (firstHandle != null)
            firstHandle.Dispose();
    }

    static void Protocol()
    {
        Check(GpuWorker.Parse(frame).PowerLimitWatts == 450, "Protocol retains all GPU readings and units");
        foreach (string invalid in new[] { "null", "{}", "not json", "{\"Load\":101}", "{\"Temperature\":-1}",
            "{\"Load\":1,\"MemoryUsedMiB\":2,\"MemoryTotalMiB\":1}" })
        {
            bool rejected = false;
            try { GpuWorker.Parse(invalid); } catch { rejected = true; }
            Check(rejected, "Reject malformed/invalid frame: " + invalid);
        }
        Check(GpuWorker.ReadLine(new StringReader("{}\r\n"), 8).Result == "{}", "Read a CRLF frame");
        foreach (string input in new[] { "unterminated", new string('x', 17) + "\n" })
        {
            bool rejected = false;
            try { GpuWorker.ReadLine(new StringReader(input), 16).GetAwaiter().GetResult(); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Reject truncated or oversized pipe data");
        }
        Check(new[] { 1, 2, 3, 4, 5, 20 }.Select(n => NvidiaReader.RetryDelay(n, 5000))
            .SequenceEqual(new[] { 5000, 10000, 20000, 40000, 60000, 60000 }), "Backoff grows to a bounded 60 seconds");
    }

    static void Lifetime(string directory)
    {
        Process childHandle;
        var child = new GpuProcess(executable, "--fake-gpu hang");
        childHandle = Process.GetProcessById(child.Id);
        child.Dispose();
        child.Dispose();
        Check(childHandle.WaitForExit(3000), "Closing the owned job terminates a hung child; disposal is idempotent");
        childHandle.Dispose();

        string pidFile = Path.Combine(directory, "orphan-" + Guid.NewGuid().ToString("N") + ".txt");
        using (var parent = Process.Start(new ProcessStartInfo(executable, "--orphan-parent \"" + pidFile + "\"")
        { UseShellExecute = false, CreateNoWindow = true }))
        {
            try
            {
                Wait(() => File.Exists(pidFile) && new FileInfo(pidFile).Length > 0, "Test parent owns its worker");
                using (var orphan = Process.GetProcessById(int.Parse(File.ReadAllText(pidFile))))
                {
                    parent.Kill();
                    parent.WaitForExit(3000);
                    Check(orphan.WaitForExit(3000), "Parent crash automatically kills its GPU worker");
                }
            }
            finally
            {
                if (!parent.HasExited)
                    parent.Kill();
            }
        }

        int attempts = 0;
        using (var reader = new NvidiaReader(() =>
        {
            Interlocked.Increment(ref attempts);
            throw new IOException("start failure");
        }, (name, detail) => { throw new IOException("log failure"); }, 200, 5000))
        {
            reader.Read();
            Wait(() => Volatile.Read(ref attempts) == 1, "Start failure is contained even when logging fails");
            reader.Dispose();
            Thread.Sleep(100);
            Check(attempts == 1 && reader.Read().Error == "GPU reader closed", "Close cancels pending retries");
        }
    }

    static void Logs(string directory)
    {
        string path = Path.Combine(directory, "runtime-" + Guid.NewGuid().ToString("N") + ".log");
        DiagnosticLog.WriteTo(path, "gpu.failure", "line1\nline2");
        Check(File.ReadAllLines(path).Length == 1 && File.ReadAllText(path).Contains("gpu.failure"), "Diagnostic events are one JSON line");
        File.WriteAllText(path, new string('x', DiagnosticLog.MaximumBytes));
        DiagnosticLog.WriteTo(path, "gpu.recovered", new string('x', 10000));
        Check(File.Exists(path + ".1") && new FileInfo(path).Length < 6000, "Log rotates and bounds event detail");
        DiagnosticLog.WriteTo(Path.Combine(path, "cannot-write.log"), "failure", "unwritable");
        Check(File.Exists(path), "Logging failure is harmless");
    }

    sealed class FixtureSource : IGpuSampleSource
    {
        public GpuData Read() { return GpuWorker.Parse(frame); }
        public void Dispose() { }
    }

    static void SilentStartup(string fixture)
    {
        using (var child = Process.Start(new ProcessStartInfo(fixture, "--protocol-fixture")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true
        }))
        {
            try
            {
                Check(child.WaitForExit(8000), "An open but silent startup pipe must time out before opening NVML");
                Check(child.ExitCode == 2 && child.StandardOutput.ReadToEnd() == "", "Silent startup exits without data");
            }
            finally
            {
                if (!child.HasExited)
                {
                    child.Kill();
                    child.WaitForExit(3000);
                }
            }
        }
    }

    public static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--protocol-fixture")
            return GpuWorker.Run(() => new FixtureSource());
        if (args.Length == 2 && args[0] == "--fake-gpu")
            return Fake(args[1]);
        if (args.Length == 2 && args[0] == "--orphan-parent")
        {
            using (var child = new GpuProcess(executable, "--fake-gpu hang"))
            {
                File.WriteAllText(args[1], child.Id.ToString());
                Thread.Sleep(Timeout.Infinite);
            }
        }
        try
        {
            Protocol();
            SilentStartup(args[1]);
            using (var fixture = new GpuProcess(args[1], "--protocol-fixture"))
            {
                var response = fixture.NextLine();
                Check(response.Wait(5000) && GpuWorker.Parse(response.Result).Temperature == 42,
                    "Production worker protocol runs in a winexe without a console code page or NVML");
            }
            foreach (string kind in new[] { "crash", "hang", "stale", "invalid", "oversized", "error" })
                Recovery(kind, args[1]);
            Lifetime(args[0]);
            Logs(args[0]);
            using (var current = Process.GetCurrentProcess())
                Check(!current.Modules.Cast<ProcessModule>().Any(m => m.ModuleName.Equals("nvml.dll", StringComparison.OrdinalIgnoreCase)),
                    "Supervision tests never load NVML into the parent");
            Console.WriteLine("GPU isolation, recovery and diagnostics checks passed: " + checks);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }
}
