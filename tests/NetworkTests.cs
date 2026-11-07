using System;
using System.Collections.Generic;
using System.ComponentModel;
using HardwareDeck;

public static class NetworkTests
{
    static int count;
    static void Check(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
        count++;
    }

    static NetworkCounter Counter(ulong id, string name)
    {
        return new NetworkCounter
        {
            Id = id,
            Name = name,
            Hardware = true,
            Connected = true,
            Type = 6,
            ReceiveLinkSpeed = 1000000000,
            TransmitLinkSpeed = 1000000000
        };
    }

    public static void Run()
    {
        Check(NetworkReader.NativeRowSize == 1352 && NetworkReader.NativeTableOffset == 8, "Match the x64 MIB_IF_TABLE2 native alignment");
        double now = 0;
        bool fail = false;
        var ethernet = Counter(1, "Ethernet");
        ethernet.Received = 9000000000;
        ethernet.Sent = 6000000000;
        var wifi = Counter(2, "Wi-Fi");
        var vpn = Counter(3, "VPN");
        vpn.Hardware = false;
        var filter = Counter(4, "Filter");
        filter.Filter = true;
        var loopback = Counter(5, "Loopback");
        loopback.Type = 24;
        var tunnel = Counter(6, "Tunnel");
        tunnel.Type = 131;
        var down = Counter(7, "Disconnected");
        down.Connected = false;
        IList<NetworkCounter> counters = new List<NetworkCounter>
        {
            ethernet,
            vpn,
            filter,
            loopback,
            tunnel,
            down,
            ethernet
        };
        var reader = new NetworkReader(() =>
        {
            if (fail)
                throw new Win32Exception(5);
            return counters;
        }, () => now);
        var sample = reader.Read();
        Check(sample.Status == "Sampling" && !sample.DownloadBytesPerSecond.HasValue, "First sample must not show lifetime counters as a rate");
        Check(sample.AdapterCount == 1 && sample.AdapterNames[0] == "Ethernet", "Exclude virtual, filter, loopback, tunnel and disconnected interfaces; deduplicate IDs");
        now = 1;
        ethernet.Received += 1250000;
        ethernet.Sent += 500000;
        sample = reader.Read();
        Check(sample.DownloadBytesPerSecond == 1250000 && sample.UploadBytesPerSecond == 500000, "Compute separate download/upload rates from counter deltas");
        now = 2;
        sample = reader.Read();
        Check(sample.Status == "Connected" && sample.DownloadBytesPerSecond == 0 && sample.UploadBytesPerSecond == 0, "An idle connected interface must show zero");
        now = 4.5;
        ethernet.Received += 5000;
        ethernet.Sent += 1250;
        sample = reader.Read();
        Check(sample.DownloadBytesPerSecond == 2000 && sample.UploadBytesPerSecond == 500, "Use actual elapsed time instead of the nominal refresh interval");
        counters = new List<NetworkCounter>
        {
            ethernet,
            wifi
        };
        now = 5.5;
        Check(reader.Read().Status == "Sampling", "Adding an interface must reset the aggregate baseline");
        now = 6.5;
        ethernet.Received += 1000;
        wifi.Received += 2000;
        ethernet.Sent += 100;
        wifi.Sent += 200;
        sample = reader.Read();
        Check(sample.AdapterCount == 2 && sample.DownloadBytesPerSecond == 3000 && sample.UploadBytesPerSecond == 300, "Sum independent physical adapter rates once");
        counters = new List<NetworkCounter>
        {
            ethernet
        };
        now = 7.5;
        Check(reader.Read().Status == "Sampling", "Removing an interface must create a baseline gap instead of a false jump");
        now = 8.5;
        ethernet.Received = 0;
        Check(reader.Read().Status == "Sampling", "A receive counter reset must never produce a spike or negative rate");
        now = 9.5;
        ethernet.Received = 1000;
        ethernet.Sent += 100;
        Check(reader.Read().DownloadBytesPerSecond == 1000, "Recover after a receive counter reset");
        now = 10.5;
        ethernet.Sent = 0;
        Check(reader.Read().Status == "Sampling", "A transmit counter reset must invalidate the interval");
        now = 41.5;
        ethernet.Received += 100000;
        Check(reader.Read().Status == "Sampling", "Rebaseline after sleep or a long sampling gap");
        now = 42.5;
        ethernet.Received += 500;
        Check(reader.Read().DownloadBytesPerSecond == 500, "Resume rates after a long gap");
        Check(reader.Read().Status == "Sampling", "A zero-duration interval must not divide by zero");
        now = 40;
        Check(reader.Read().Status == "Sampling", "A backwards test clock must not yield invalid rates");
        fail = true;
        now = 41;
        sample = reader.Read();
        Check(sample.Status == "Unavailable" && !sample.DownloadBytesPerSecond.HasValue, "Counter errors must report unavailable rather than stale data");
        fail = false;
        now = 42;
        Check(reader.Read().Status == "Sampling", "Reconnect with a fresh baseline after a query error");
        counters = new List<NetworkCounter>();
        now = 43;
        sample = reader.Read();
        Check(sample.Status == "Disconnected" && sample.AdapterCount == 0 && !sample.UploadBytesPerSecond.HasValue, "Disconnection is unavailable, not idle zero");
        ethernet.Received = (ulong)uint.MaxValue - 1000;
        ethernet.Sent = 0;
        counters = new[]
        {
            ethernet
        };
        now = 44;
        reader.Read();
        ethernet.Received += 5000;
        now = 45;
        Check(reader.Read().DownloadBytesPerSecond == 5000, "Retain 64-bit counter precision across the 4 GiB boundary");
        ethernet.ReceiveLinkSpeed = 8000;
        ethernet.Received += 1000000;
        now = 46;
        Check(reader.Read().Status == "Sampling", "Reject a gross counter discontinuity relative to the negotiated link rate");
        ethernet.Received += 500;
        now = 47;
        Check(reader.Read().DownloadBytesPerSecond == 500, "Resume after an invalid counter jump");
        Check(NetworkRates.Format(0) == "0 B/s", "Format idle rate clearly");
        Check(NetworkRates.Format(1250000) == "1.3 MB/s" && NetworkRates.Format(42000) == "42.0 KB/s", "Use decimal byte-per-second units");
        Check(NetworkRates.Format(null) == "—" && NetworkRates.Format(double.NaN) == "—" && NetworkRates.Format(-1) == "—", "Do not format invalid rates as data");
        Check(NetworkRates.AxisMaximum(0) == 1000, "Keep a readable axis when traffic is idle");
        Check(NetworkRates.AxisMaximum(1200000) == 2000000, "Auto-scale network history above its peak with legible tick values");
        Console.WriteLine(count + " network selection, rate and recovery checks passed.");
    }
}
