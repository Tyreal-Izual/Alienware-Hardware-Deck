using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Web.Script.Serialization;

namespace HardwareDeck
{
    public sealed class Reading
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Kind { get; set; }
        public double? Value { get; set; }
        public double? Maximum { get; set; }
        public string Source { get; set; }
        public int? FanSensorCode { get; set; }
        public string FanTemperatureSensorId { get; set; }
        public string FanAssociationStatus { get; set; }
    }

    public sealed class GpuData
    {
        public string Name { get; set; }
        public double? Temperature { get; set; }
        public double? FanPercent { get; set; }
        public double? Load { get; set; }
        public double? MemoryUsedMiB { get; set; }
        public double? MemoryTotalMiB { get; set; }
        public double? PowerWatts { get; set; }
        public double? PowerLimitWatts { get; set; }
        public double? ClockMHz { get; set; }
        public string Error { get; set; }
    }

    public sealed class Snapshot
    {
        public int SchemaVersion { get; set; }
        public string CapturedAt { get; set; }
        public string ComputerModel { get; set; }
        public string CpuName { get; set; }
        public bool Administrator { get; set; }
        public double? CpuLoad { get; set; }
        public CpuTelemetry Cpu { get; set; }
        public ThermalModeState ThermalMode { get; set; }
        public PresetState Preset { get; set; }
        public double? MemoryUsedGiB { get; set; }
        public double? MemoryTotalGiB { get; set; }
        public GpuData Gpu { get; set; }
        public NetworkData Network { get; set; }
        public List<Reading> Sensors { get; set; }
        public Reading CpuTemperature { get; set; }
        public string CpuTemperatureSensorId { get; set; }
        public bool CpuTemperatureSourceExplicit { get; set; }
        public string CpuTemperatureSelection { get; set; }
        public string AwccStatus { get; set; }
        public double SampleMilliseconds { get; set; }
    }

    public static class Values
    {
        public static double? Range(double? value, double minimum, double maximum)
        {
            if (!value.HasValue || double.IsNaN(value.Value) || double.IsInfinity(value.Value) || value < minimum || value > maximum)
                return null;
            return value;
        }
    }

    public static class SnapshotFile
    {
        static readonly int processId = ReadProcessId();
        static int ReadProcessId()
        {
            using (var process = Process.GetCurrentProcess())
                return process.Id;
        }

        public static string DataDirectory
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HardwareDeck");
            }
        }

        public static void Write(string path, object data)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temporary = path + ".tmp." + processId;
            File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(data), new System.Text.UTF8Encoding(false));
            try
            {
                if (File.Exists(path))
                    File.Replace(temporary, path, null);
                else
                    File.Move(temporary, path);
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }
    }
}
