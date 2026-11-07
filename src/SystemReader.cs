using System;
using System.Runtime.InteropServices;

namespace HardwareDeck
{
    public sealed class SystemReader
    {
        [StructLayout(LayoutKind.Sequential)]
        struct FileTime
        {
            public uint Low, High;
            public ulong Ticks
            {
                get
                {
                    return ((ulong)High << 32) | Low;
                }
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        sealed class MemoryStatus
        {
            public uint Length = (uint)Marshal.SizeOf(typeof(MemoryStatus));
            public uint Load;
            public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatus status);
        ulong? idlePrevious, totalPrevious;
        public void Read(Snapshot snapshot)
        {
            FileTime idle, kernel, user;
            if (GetSystemTimes(out idle, out kernel, out user))
            {
                ulong total = kernel.Ticks + user.Ticks;
                if (totalPrevious.HasValue && total > totalPrevious.Value && idle.Ticks >= idlePrevious.Value)
                {
                    double delta = total - totalPrevious.Value;
                    snapshot.CpuLoad = Values.Range(100.0 * (delta - (idle.Ticks - idlePrevious.Value)) / delta, 0, 100);
                }

                idlePrevious = idle.Ticks;
                totalPrevious = total;
            }

            var memory = new MemoryStatus();
            if (GlobalMemoryStatusEx(memory))
            {
                snapshot.MemoryTotalGiB = memory.TotalPhysical / 1073741824.0;
                snapshot.MemoryUsedGiB = (memory.TotalPhysical - memory.AvailablePhysical) / 1073741824.0;
            }
        }
    }
}
