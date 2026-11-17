using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace HardwareDeck
{
    internal static class DiagnosticLog
    {
        static readonly object gate = new object();
        internal const int MaximumBytes = 1024 * 1024;

        internal static void Write(string kind, string detail)
        {
            WriteTo(Path.Combine(SnapshotFile.DataDirectory, "runtime.log"), kind, detail);
        }

        internal static void WriteTo(string path, string kind, string detail)
        {
            // Diagnostics must never stop monitoring, including on read-only/full disks.
            try
            {
                if (detail != null && detail.Length > 4096)
                    detail = detail.Substring(0, 4096);
                int pid;
                using (var process = Process.GetCurrentProcess())
                    pid = process.Id;
                string line = new JavaScriptSerializer().Serialize(new
                {
                    At = DateTime.UtcNow.ToString("o"), Process = pid, Event = kind, Detail = detail
                }) + Environment.NewLine;
                lock (gate)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    if (File.Exists(path) && new FileInfo(path).Length + Encoding.UTF8.GetByteCount(line) > MaximumBytes)
                    {
                        string previous = path + ".1";
                        if (File.Exists(previous))
                            File.Delete(previous);
                        File.Move(path, previous);
                    }
                    File.AppendAllText(path, line, new UTF8Encoding(false));
                }
            }
            catch
            {
            }
        }
    }
}
