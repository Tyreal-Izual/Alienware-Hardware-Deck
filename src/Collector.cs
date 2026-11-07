using System;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Security.Principal;

namespace HardwareDeck
{
    public sealed class Collector : IDisposable
    {
        readonly SystemReader system = new SystemReader();
        readonly CpuTelemetryReader cpu = new CpuTelemetryReader();
        readonly NvidiaReader nvidia = new NvidiaReader();
        readonly NetworkReader network = new NetworkReader();
        readonly CpuPresetReader cpuPreset = new CpuPresetReader();
        readonly AwccReader awcc;
        readonly SensorConfiguration configuration;
        readonly string cpuName, computerModel;
        static readonly bool administrator = ReadAdministratorStatus();
        static bool ReadAdministratorStatus()
        {
            using (var identity = WindowsIdentity.GetCurrent())
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }

        public static bool IsAdmin
        {
            get
            {
                return administrator;
            }
        }

        public Collector(string root)
        {
            configuration = SensorConfiguration.Load(Path.Combine(root, "sensor-labels.json"));
            awcc = new AwccReader(configuration.Labels);
            cpuName = Info("Win32_Processor", "Name");
            computerModel = Info("Win32_ComputerSystem", "Model");
        }

        static string Info(string type, string property)
        {
            try
            {
                using (var search = new ManagementObjectSearcher("SELECT " + property + " FROM " + type))
                using (var rows = search.Get())
                    foreach (ManagementObject item in rows)
                    {
                        using (item)
                            return Convert.ToString(item[property]).Trim();
                    }
            }
            catch
            {
            }

            return "Unknown";
        }

        public Snapshot Sample()
        {
            var timer = Stopwatch.StartNew();
            var result = new Snapshot
            {
                SchemaVersion = 1,
                CapturedAt = DateTime.UtcNow.ToString("o"),
                CpuName = cpuName,
                ComputerModel = computerModel,
                Administrator = IsAdmin
            };
            system.Read(result);
            result.Cpu = cpu.Read();
            result.Gpu = nvidia.Read();
            result.Sensors = awcc.Read();
            result.CpuTemperature = configuration.SelectCpuTemperature(result.Sensors);
            result.CpuTemperatureSensorId = configuration.CpuTemperatureSensorId ?? (result.CpuTemperature == null ? null : result.CpuTemperature.Id);
            result.CpuTemperatureSourceExplicit = configuration.CpuTemperatureSensorId != null;
            result.CpuTemperatureSelection = configuration.DescribeSelection(result.CpuTemperature);
            result.AwccStatus = awcc.Status;
            result.ThermalMode = awcc.ReadMode();
            result.Preset = PresetStateFactory.Create(cpuPreset.ReadForCooling(result.ThermalMode.CurrentId), result.ThermalMode);
            result.Network = network.Read();
            result.SampleMilliseconds = timer.Elapsed.TotalMilliseconds;
            result.CapturedAt = DateTime.UtcNow.ToString("o");
            return result;
        }

        public void Dispose()
        {
            awcc.Dispose();
            nvidia.Dispose();
            cpu.Dispose();
            cpuPreset.Dispose();
        }
    }
}
