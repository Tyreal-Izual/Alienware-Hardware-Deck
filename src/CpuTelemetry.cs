using System;
using System.Diagnostics;
using System.Globalization;
using System.Management;

namespace HardwareDeck
{
    public sealed class CpuTelemetry
    {
        public double? FrequencyGHz { get; set; }
        public double? PowerWatts { get; set; }
        public double? VoltageVolts { get; set; }
        public string FrequencySource { get; set; }
        public string PowerSource { get; set; }
        public string VoltageSource { get; set; }
        public string VoltageLabel { get; set; }
    }

    public sealed class CpuTelemetryReader : IDisposable
    {
        PerformanceCounter nominal, performance;
        bool frequencyPrimed;
        ManagementObject powerSensor;
        ManagementObject voltageSensor;
        DateTime nextPowerProbe = DateTime.MinValue, nextVoltageProbe = DateTime.MinValue;
        string voltageName;
        bool powerPrimed;
        readonly XtuVoltageReader xtuVoltage = new XtuVoltageReader();
        public CpuTelemetryReader()
        {
            try
            {
                nominal = new PerformanceCounter("Processor Information", "Processor Frequency", "_Total", true);
                performance = new PerformanceCounter("Processor Information", "% Processor Performance", "_Total", true);
                performance.NextValue();
                frequencyPrimed = true;
            }
            catch
            {
                if (nominal != null)
                    nominal.Dispose();
                if (performance != null)
                    performance.Dispose();
                nominal = null;
                performance = null;
            }
        }

        public static double? Frequency(double baseMHz, double percent)
        {
            var value = Values.Range(baseMHz * percent / 100000.0, 0.05, 10);
            return baseMHz > 0 && percent > 0 ? value : null;
        }

        public static double? RaplPower(double raw)
        {
            // This R16 provider occasionally emits an invalid startup spike.
            // Reject it; never clamp it into a plausible-looking reading.
            return Values.Range(raw / 10.0, 0, 500);
        }

        void FindPower()
        {
            if (powerSensor != null || DateTime.UtcNow < nextPowerProbe)
                return;
            nextPowerProbe = DateTime.UtcNow.AddMinutes(1);
            try
            {
                string processor = null;
                using (var search = new ManagementObjectSearcher("SELECT DeviceID,Name FROM Win32_PnPEntity WHERE Name LIKE '%Processor Participant%'"))
                using (var rows = search.Get())
                    foreach (ManagementObject row in rows)
                        using (row)
                        {
                            processor = Convert.ToString(row["DeviceID"]);
                            break;
                        }

                if (string.IsNullOrEmpty(processor))
                    return;
                using (var definition = new ManagementClass(@"root\wmi:EsifDeviceInformation"))
                {
                    definition.Options.UseAmendedQualifiers = true;
                    definition.Get();
                    var description = Convert.ToString(definition.Properties["Power"].Qualifiers["Description"].Value);
                    if (description.IndexOf(".1 Watts", StringComparison.OrdinalIgnoreCase) < 0)
                        return;
                }

                using (var search = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM EsifDeviceInformation"))
                using (var rows = search.Get())
                    foreach (ManagementObject row in rows)
                    {
                        string instance = Convert.ToString(row["InstanceName"]);
                        if (string.Equals(instance, processor + "_0", StringComparison.OrdinalIgnoreCase))
                        {
                            powerSensor = row;
                            powerPrimed = false;
                            break;
                        }

                        row.Dispose();
                    }
            }
            catch
            {
                powerSensor = null;
            }
        }

        void FindVoltage()
        {
            if (voltageSensor != null || DateTime.UtcNow < nextVoltageProbe)
                return;
            nextVoltageProbe = DateTime.UtcNow.AddMinutes(1);
            // Optional existing provider only. This does not install/start a sensor driver.
            foreach (string provider in new[]
            {
                "LibreHardwareMonitor",
                "OpenHardwareMonitor"
            }

            )
            {
                try
                {
                    using (var search = new ManagementObjectSearcher(@"root\" + provider, "SELECT * FROM Sensor WHERE SensorType='Voltage'"))
                    using (var rows = search.Get())
                        foreach (ManagementObject row in rows)
                        {
                            string parent = Convert.ToString(row["Parent"]), name = Convert.ToString(row["Name"]);
                            if (parent.IndexOf("intelcpu", StringComparison.OrdinalIgnoreCase) >= 0 && (name.Equals("CPU Core", StringComparison.OrdinalIgnoreCase) || name.Equals("CPU VCore", StringComparison.OrdinalIgnoreCase)))
                            {
                                voltageSensor = row;
                                voltageName = provider + " · " + name;
                                return;
                            }

                            row.Dispose();
                        }
                }
                catch
                {
                }
            }
        }

        public CpuTelemetry Read()
        {
            var value = new CpuTelemetry
            {
                FrequencySource = "Estimated from Windows performance counters",
                PowerSource = "Intel IPF · RAPL / CPU participant 0",
                VoltageSource = "No live voltage source; nominal SMBIOS voltage is not used",
                VoltageLabel = "Voltage"
            };
            if (nominal != null && performance != null)
            {
                try
                {
                    if (frequencyPrimed)
                        value.FrequencyGHz = Frequency(nominal.NextValue(), performance.NextValue());
                }
                catch
                {
                }
            }

            FindPower();
            if (powerSensor != null)
            {
                try
                {
                    powerSensor.Get();
                    if (powerPrimed && powerSensor["Power"] != null)
                        value.PowerWatts = RaplPower(Convert.ToDouble(powerSensor["Power"], CultureInfo.InvariantCulture));
                    powerPrimed = true;
                }
                catch
                {
                    powerSensor.Dispose();
                    powerSensor = null;
                }
            }

            value.VoltageVolts = xtuVoltage.Read();
            value.VoltageSource = xtuVoltage.Status;
            if (!value.VoltageVolts.HasValue)
            {
                FindVoltage();
                if (voltageSensor != null)
                {
                    try
                    {
                        voltageSensor.Get();
                        value.VoltageVolts = Values.Range(Convert.ToDouble(voltageSensor["Value"], CultureInfo.InvariantCulture), 0.1, 2.5);
                        if (value.VoltageVolts.HasValue)
                            value.VoltageSource = voltageName;
                    }
                    catch
                    {
                        voltageSensor.Dispose();
                        voltageSensor = null;
                    }
                }
            }

            return value;
        }

        public void Dispose()
        {
            if (nominal != null)
                nominal.Dispose();
            if (performance != null)
                performance.Dispose();
            if (powerSensor != null)
                powerSensor.Dispose();
            if (voltageSensor != null)
                voltageSensor.Dispose();
            xtuVoltage.Dispose();
        }
    }
}
