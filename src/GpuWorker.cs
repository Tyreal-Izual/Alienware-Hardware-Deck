using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace HardwareDeck
{
    internal interface IGpuSampleSource : IDisposable
    {
        GpuData Read();
    }

    internal static class GpuWorker
    {
        internal const string Handshake = "HardwareDeck.Gpu/1";
        internal const int MaximumLine = 16384;

        [DllImport("kernel32.dll")]
        static extern uint SetErrorMode(uint mode);

        internal static async Task<string> ReadLine(TextReader reader, int maximum)
        {
            var line = new StringBuilder();
            var buffer = new char[1];
            while (await reader.ReadAsync(buffer, 0, 1).ConfigureAwait(false) != 0)
            {
                char value = buffer[0];
                if (value == '\n')
                    return line.ToString().TrimEnd('\r');
                if (line.Length >= maximum)
                    throw new InvalidDataException("GPU worker response exceeds its size limit");
                line.Append(value);
            }
            if (line.Length != 0)
                throw new InvalidDataException("GPU worker response was truncated");
            return null;
        }

        internal static GpuData Parse(string line)
        {
            if (string.IsNullOrEmpty(line) || line.Length > MaximumLine)
                throw new InvalidDataException("GPU worker returned an empty or oversized response");
            var data = new JavaScriptSerializer { MaxJsonLength = MaximumLine }.Deserialize<GpuData>(line);
            if (data == null || (data.Name != null && data.Name.Length > 256) || (data.Error != null && data.Error.Length > 4096))
                throw new InvalidDataException("GPU worker returned invalid data");
            var values = new[] { data.Temperature, data.Load, data.FanPercent, data.PowerWatts,
                data.PowerLimitWatts, data.ClockMHz, data.MemoryUsedMiB, data.MemoryTotalMiB };
            if (values.Any(v => v.HasValue && (double.IsNaN(v.Value) || double.IsInfinity(v.Value) || v.Value < 0)) ||
                data.Load > 100 || data.FanPercent > 100 || data.Temperature > 150 || data.MemoryUsedMiB > data.MemoryTotalMiB)
                throw new InvalidDataException("GPU worker returned invalid readings");
            if (string.IsNullOrEmpty(data.Error) && !data.Temperature.HasValue && !data.Load.HasValue && !data.MemoryTotalMiB.HasValue)
                throw new InvalidDataException("GPU worker returned no readings");
            return data;
        }

        internal static int Run()
        {
            return Run(() => new NvmlReader());
        }

        internal static int Run(Func<IGpuSampleSource> open)
        {
            // Only this disposable reader process suppresses native fault dialogs.
            SetErrorMode(0x0001 | 0x0002 | 0x8000);
            if (!Console.IsInputRedirected || !Console.IsOutputRedirected)
                return 2;
            try
            {
                // Use the streams directly: Console.In's synchronized wrapper can
                // block ReadAsync before the handshake timeout even starts.
                // A winexe also has no console code page to change.
                using (var input = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8))
                using (var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true })
                {
                    var handshake = ReadLine(input, 64);
                    if (!handshake.Wait(5000) || handshake.Result != Handshake)
                        return 2;
                    using (var reader = open())
                    {
                        while (true)
                        {
                            GpuData data;
                            try { data = reader.Read(); }
                            catch (Exception error) { data = new GpuData { Error = error.Message }; }
                            output.WriteLine(new JavaScriptSerializer().Serialize(data));
                            if (!string.IsNullOrEmpty(data.Error))
                                return 1;
                            Thread.Sleep(1000);
                        }
                    }
                }
            }
            catch
            {
                return 2; // Broken parent pipes and malformed startup are quiet failures.
            }
        }
    }
}
