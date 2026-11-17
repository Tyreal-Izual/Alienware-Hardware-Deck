using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace HardwareDeck
{
    // The collector only reads this cache; native calls and pipe waits stay off its thread.
    public sealed class NvidiaReader : IDisposable
    {
        readonly object gate = new object();
        readonly CancellationTokenSource stop = new CancellationTokenSource();
        readonly Func<IGpuProcess> start;
        readonly Action<string, string> log;
        readonly int timeoutMs, retryMs;
        Task monitor;
        IGpuProcess active;
        GpuData latest = new GpuData { Error = "GPU connecting" };
        long received;
        bool disposed;

        public NvidiaReader() : this(() => new GpuProcess(), DiagnosticLog.Write, 5000, 5000) { }

        internal NvidiaReader(Func<IGpuProcess> start, Action<string, string> log, int timeoutMs, int retryMs)
        {
            this.start = start;
            this.log = log;
            this.timeoutMs = timeoutMs;
            this.retryMs = retryMs;
        }

        public GpuData Read()
        {
            lock (gate)
            {
                if (disposed)
                    return new GpuData { Error = "GPU reader closed" };
                if (monitor == null)
                    monitor = Task.Run((Func<Task>)Monitor);
                if (string.IsNullOrEmpty(latest.Error) && (Stopwatch.GetTimestamp() - received) * 1000.0 / Stopwatch.Frequency > timeoutMs)
                    return new GpuData { Name = latest.Name, Error = "GPU data stale · Reconnecting" };
                return latest;
            }
        }

        internal static int RetryDelay(int failures, int initial)
        {
            return (int)Math.Min(60000L, (long)initial * (1 << Math.Min(Math.Max(failures - 1, 0), 4)));
        }

        void Record(string kind, string detail)
        {
            try { log(kind, detail); } catch { }
        }

        void Close(IGpuProcess child)
        {
            if (child == null)
                return;
            try { child.Dispose(); }
            catch (Exception error) { Record("gpu.cleanup", error.Message); }
        }

        async Task Monitor()
        {
            int failures = 0;
            bool recovering = false;
            while (!stop.IsCancellationRequested)
            {
                IGpuProcess child = null;
                int pid = 0;
                string failure = null;
                try
                {
                    child = start();
                    pid = child.Id;
                    lock (gate)
                    {
                        if (!disposed)
                            active = child;
                    }
                    stop.Token.ThrowIfCancellationRequested();
                    int healthy = 0;
                    while (!stop.IsCancellationRequested)
                    {
                        var read = child.NextLine();
                        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop.Token))
                        {
                            var delay = Task.Delay(timeoutMs, deadline.Token);
                            if (await Task.WhenAny(read, delay).ConfigureAwait(false) != read)
                            {
                                ObserveFailure(read);
                                stop.Token.ThrowIfCancellationRequested();
                                throw new TimeoutException("GPU worker did not respond within " + timeoutMs + " ms");
                            }
                            deadline.Cancel();
                        }
                        string line = await read.ConfigureAwait(false);
                        if (line == null)
                            throw new IOException("GPU worker closed its output");
                        var data = GpuWorker.Parse(line);
                        if (!string.IsNullOrEmpty(data.Error))
                            throw new IOException("NVML unavailable: " + data.Error);
                        lock (gate)
                        {
                            if (disposed)
                                break;
                            latest = data;
                            received = Stopwatch.GetTimestamp();
                        }
                        if (recovering)
                        {
                            Record("gpu.recovered", "Worker " + pid + " resumed valid readings");
                            recovering = false;
                        }
                        // One good frame must not reset a repeatedly crashing worker's backoff.
                        if (++healthy >= 10)
                            failures = 0;
                    }
                }
                catch (Exception error)
                {
                    if (!stop.IsCancellationRequested)
                    {
                        int? code = child == null ? null : child.ExitCode;
                        failure = error.GetType().Name + ": " + error.Message + " · Worker " + pid +
                            (code.HasValue ? " · Exit 0x" + unchecked((uint)code.Value).ToString("X8") : "");
                        lock (gate)
                            latest = new GpuData { Name = latest.Name, Error = "GPU unavailable · Retrying shortly" };
                    }
                }
                finally
                {
                    lock (gate) { active = null; }
                    Close(child);
                }
                if (stop.IsCancellationRequested)
                    break;
                int retry = RetryDelay(++failures, retryMs);
                Record("gpu.failure", failure + " · Retry in " + retry + " ms");
                recovering = true;
                try { await Task.Delay(retry, stop.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }

        static void ObserveFailure(Task task)
        {
            // A killed process can complete its pending read after the timeout.
            task.ContinueWith(t => { var ignored = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
        }

        public void Dispose()
        {
            IGpuProcess child;
            Task task;
            lock (gate)
            {
                if (disposed)
                    return;
                disposed = true;
                child = active;
                task = monitor;
            }
            stop.Cancel();
            Close(child);
            if (task == null)
                stop.Dispose();
            else
                task.ContinueWith(t => { var ignored = t.Exception; stop.Dispose(); }, TaskScheduler.Default);
        }
    }
}
