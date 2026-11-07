using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;

namespace HardwareDeck
{
    public sealed class NetworkData
    {
        public double? DownloadBytesPerSecond { get; set; }
        public double? UploadBytesPerSecond { get; set; }
        public int AdapterCount { get; set; }
        public string[] AdapterNames { get; set; }
        public string Status { get; set; }
        public string Detail { get; set; }
    }

    internal sealed class NetworkCounter
    {
        public ulong Id, Received, Sent, ReceiveLinkSpeed, TransmitLinkSpeed;
        public string Name;
        public bool Hardware, Filter, Connected;
        public uint Type;
    }

    public static class NetworkRates
    {
        public static double Divisor(double value)
        {
            return value >= 1e12 ? 1e12 : value >= 1e9 ? 1e9 : value >= 1e6 ? 1e6 : value >= 1000 ? 1000 : 1;
        }

        public static string Unit(double divisor)
        {
            return divisor >= 1e12 ? "TB/s" : divisor >= 1e9 ? "GB/s" : divisor >= 1e6 ? "MB/s" : divisor >= 1000 ? "KB/s" : "B/s";
        }

        public static string Format(double? value)
        {
            if (!value.HasValue || value < 0 || double.IsNaN(value.Value) || double.IsInfinity(value.Value))
                return "—";
            double divisor = Divisor(value.Value);
            return (value.Value / divisor).ToString(divisor == 1 ? "0" : "0.0", CultureInfo.InvariantCulture) + " " + Unit(divisor);
        }

        public static double AxisMaximum(double peak)
        {
            if (double.IsNaN(peak) || double.IsInfinity(peak) || peak < 0)
                peak = 0;
            double target = Math.Max(1000, peak * 1.15);
            double power = Math.Pow(10, Math.Floor(Math.Log10(target)));
            double fraction = target / power;
            return (fraction <= 1 ? 1 : fraction <= 2 ? 2 : fraction <= 5 ? 5 : 10) * power;
        }
    }

    public sealed class NetworkReader
    {
        struct Baseline
        {
            public ulong Received, Sent;
        }

        readonly Func<IList<NetworkCounter>> read;
        readonly Func<double> clock;
        Dictionary<ulong, Baseline> previous;
        double previousTime;
        public NetworkReader()
        {
            read = ReadNative;
            clock = () => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        }

        internal NetworkReader(Func<IList<NetworkCounter>> read, Func<double> clock)
        {
            this.read = read;
            this.clock = clock;
        }

        static bool Eligible(NetworkCounter value)
        {
            return value.Hardware && !value.Filter && value.Connected && value.Type != 24 && value.Type != 131;
        }

        void Remember(IList<NetworkCounter> counters, double now)
        {
            previous = counters.ToDictionary(c => c.Id, c => new Baseline { Received = c.Received, Sent = c.Sent });
            previousTime = now;
        }

        public NetworkData Read()
        {
            try
            {
                var counters = read().Where(Eligible).GroupBy(c => c.Id).Select(g => g.First()).OrderBy(c => c.Id).ToList();
                double now = clock();
                var result = new NetworkData
                {
                    AdapterCount = counters.Count,
                    AdapterNames = counters.Select(c => string.IsNullOrWhiteSpace(c.Name) ? "Network adapter" : c.Name).ToArray()
                };
                if (counters.Count == 0)
                {
                    previous = null;
                    result.Status = "Disconnected";
                    result.Detail = "No active physical network adapter";
                    return result;
                }

                double elapsed = now - previousTime;
                bool reset = previous == null || previous.Count != counters.Count || counters.Any(c => !previous.ContainsKey(c.Id)) || double.IsNaN(elapsed) || double.IsInfinity(elapsed) || elapsed <= 0 || elapsed > 30;
                double download = 0, upload = 0;
                if (!reset)
                    foreach (var counter in counters)
                    {
                        var before = previous[counter.Id];
                        if (counter.Received < before.Received || counter.Sent < before.Sent)
                        {
                            reset = true;
                            break;
                        }

                        double incoming = (counter.Received - before.Received) / elapsed;
                        double outgoing = (counter.Sent - before.Sent) / elapsed;
                        // Reject gross counter discontinuities, not normal bursts around the link rate.
                        if ((counter.ReceiveLinkSpeed > 0 && incoming > counter.ReceiveLinkSpeed / 2.0) || (counter.TransmitLinkSpeed > 0 && outgoing > counter.TransmitLinkSpeed / 2.0))
                        {
                            reset = true;
                            break;
                        }

                        download += incoming;
                        upload += outgoing;
                    }

                if (double.IsNaN(download) || double.IsInfinity(download) || double.IsNaN(upload) || double.IsInfinity(upload))
                    reset = true;
                Remember(counters, now);
                result.Status = reset ? "Sampling" : "Connected";
                result.Detail = reset ? "Collecting network samples" : "Physical adapter traffic · Includes LAN transfers";
                if (!reset)
                {
                    result.DownloadBytesPerSecond = download;
                    result.UploadBytesPerSecond = upload;
                }

                return result;
            }
            catch (Exception ex)
            {
                previous = null;
                return new NetworkData
                {
                    AdapterNames = new string[0],
                    Status = "Unavailable",
                    Detail = "Network counters unavailable (" + ex.GetType().Name + ")"
                };
            }
        }

        // GetIfTable2 exposes 64-bit counters and hardware/filter flags, avoiding WMI and 32-bit wrap.
        // See the Windows IP Helper MIB_IF_ROW2 contract. MAC/IP addresses are not exposed or stored.
        internal static int NativeRowSize
        {
            get
            {
                return Marshal.SizeOf(typeof(NativeRow));
            }
        }

        internal static int NativeTableOffset
        {
            get
            {
                return Marshal.OffsetOf(typeof(NativeTable), "FirstRow").ToInt32();
            }
        }

        static IList<NetworkCounter> ReadNative()
        {
            IntPtr table = IntPtr.Zero;
            try
            {
                uint error = GetIfTable2(out table);
                if (error != 0)
                    throw new Win32Exception((int)error);
                if (table == IntPtr.Zero)
                    throw new InvalidOperationException("Missing network interface table");
                int count = Marshal.ReadInt32(table), size = NativeRowSize, offset = NativeTableOffset;
                if (count < 0 || count > 4096 || size != 1352 || offset != 8)
                    throw new InvalidOperationException("Unexpected network table layout");
                var result = new List<NetworkCounter>();
                for (int i = 0; i < count; i++)
                {
                    var row = (NativeRow)Marshal.PtrToStructure(IntPtr.Add(table, offset + i * size), typeof(NativeRow));
                    result.Add(new NetworkCounter { Id = row.Luid, Name = row.Alias, Received = row.InOctets, Sent = row.OutOctets, ReceiveLinkSpeed = row.ReceiveLinkSpeed, TransmitLinkSpeed = row.TransmitLinkSpeed, Type = row.Type, Hardware = (row.Flags & 1) != 0, Filter = (row.Flags & 2) != 0, Connected = row.OperStatus == 1 && row.AdminStatus == 1 && row.MediaConnectState != 2 });
                }

                return result;
            }
            finally
            {
                if (table != IntPtr.Zero)
                    FreeMibTable(table);
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 8)]
        struct NativeRow
        {
            public ulong Luid;
            public uint Index;
            public Guid InterfaceGuid;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)]
            public string Alias;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)]
            public string Description;
            public uint PhysicalAddressLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public byte[] PhysicalAddress;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public byte[] PermanentPhysicalAddress;
            public uint Mtu, Type, TunnelType, MediaType, PhysicalMediumType, AccessType, DirectionType;
            public byte Flags;
            public uint OperStatus, AdminStatus, MediaConnectState;
            public Guid NetworkGuid;
            public uint ConnectionType;
            public ulong TransmitLinkSpeed, ReceiveLinkSpeed, InOctets, InUcastPkts, InNUcastPkts, InDiscards, InErrors, InUnknownProtos, InUcastOctets, InMulticastOctets, InBroadcastOctets, OutOctets, OutUcastPkts, OutNUcastPkts, OutDiscards, OutErrors, OutUcastOctets, OutMulticastOctets, OutBroadcastOctets, OutQLen;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        struct NativeTable
        {
            public uint Count;
            public NativeRow FirstRow;
        }

        [DllImport("iphlpapi.dll")]
        static extern uint GetIfTable2(out IntPtr table);
        [DllImport("iphlpapi.dll")]
        static extern void FreeMibTable(IntPtr table);
    }
}
