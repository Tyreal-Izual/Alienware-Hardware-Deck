using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HardwareDeck
{
    internal interface IGpuProcess : IDisposable
    {
        int Id { get; }
        int? ExitCode { get; }
        Task<string> NextLine();
    }

    internal sealed class GpuProcess : IGpuProcess
    {
        readonly Process process = new Process();
        IntPtr job;
        int disposed;
        public int Id { get { return process.Id; } }
        public int? ExitCode
        {
            get
            {
                try { return process.WaitForExit(100) ? (int?)process.ExitCode : null; }
                catch { return null; }
            }
        }

        internal GpuProcess() : this(typeof(Program).Assembly.Location, "--gpu-worker") { }

        // Alternate executables are only used by the compiled integration tests.
        internal GpuProcess(string executable, string arguments)
        {
            try
            {
                job = CreateJobObject(IntPtr.Zero, null);
                var limits = new JobLimits();
                limits.Basic.Flags = 0x2000; // KILL_ON_JOB_CLOSE, handle is not inherited.
                if (job == IntPtr.Zero || !SetInformationJobObject(job, 9, ref limits, Marshal.SizeOf(typeof(JobLimits))))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot own the GPU worker lifetime");
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = executable, Arguments = arguments,
                    WorkingDirectory = Path.GetDirectoryName(executable),
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardInput = true, RedirectStandardOutput = true,
                    StandardOutputEncoding = Encoding.UTF8
                };
                process.Start();
                if (!AssignProcessToJobObject(job, process.Handle))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot track the GPU worker");
                // No NVML access until ownership is established. EOF/timeout handles a
                // parent that exits in the short interval before job assignment.
                process.StandardInput.WriteLine(GpuWorker.Handshake);
                process.StandardInput.Flush();
            }
            catch
            {
                try { process.Kill(); } catch { }
                Dispose();
                throw;
            }
        }

        public Task<string> NextLine()
        {
            return GpuWorker.ReadLine(process.StandardOutput, GpuWorker.MaximumLine);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
                return;
            var owned = Interlocked.Exchange(ref job, IntPtr.Zero);
            if (owned != IntPtr.Zero)
                CloseHandle(owned);
            try { process.WaitForExit(1000); } catch { }
            process.Dispose();
        }

        [StructLayout(LayoutKind.Sequential)]
        struct BasicLimits
        {
            public long ProcessTime, JobTime;
            public uint Flags;
            public UIntPtr MinimumWorkingSet, MaximumWorkingSet;
            public uint ActiveProcesses;
            public UIntPtr Affinity;
            public uint Priority, Scheduling;
        }
        [StructLayout(LayoutKind.Sequential)]
        struct JobLimits
        {
            public BasicLimits Basic;
            public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes;
            public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr CreateJobObject(IntPtr security, string name);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool SetInformationJobObject(IntPtr job, int info, ref JobLimits limits, int size);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
        [DllImport("kernel32.dll")]
        static extern bool CloseHandle(IntPtr handle);
    }
}
