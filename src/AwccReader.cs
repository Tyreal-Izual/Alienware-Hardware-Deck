using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Management;
using System.Threading;

namespace HardwareDeck
{
    public sealed class AwccReader : IDisposable
    {
        ManagementObject device;
        ManagementBaseObject queryTemplate, fanSensorTemplate;
        readonly Dictionary<int, int?> fanSensorCodes = new Dictionary<int, int?>();
        public const string FanSensorMethod = "GetFanSensors";
        public static uint FanSensorArgument(int fanId)
        {
            if (fanId < 1 || fanId > 255)
                throw new ArgumentOutOfRangeException("fanId");
            return (uint)(2 | (fanId << 8));
        }

        public static string MatchFanTemperature(int? code, IEnumerable<int> knownTemperatures)
        {
            if (!code.HasValue)
                return null;
            int id = code.Value;
            if (id > 0x100 && id < 0x110)
                id -= 0x100;
            if (id < 1 || id > 15 || !knownTemperatures.Contains(id))
                return null;
            return "awcc.temp." + id.ToString(CultureInfo.InvariantCulture);
        }

        int? ReadFanSensorCode(int fanId)
        {
            if (fanSensorTemplate == null)
                return null;
            try
            {
                using (var input = (ManagementBaseObject)fanSensorTemplate.Clone())
                {
                    input["arg2"] = FanSensorArgument(fanId);
                    using (var output = device.InvokeMethod(FanSensorMethod, input, new InvokeMethodOptions { Timeout = TimeSpan.FromSeconds(2) }))
                    {
                        if (output == null || output["argr"] == null)
                            return null;
                        long raw = Convert.ToInt64(output["argr"], CultureInfo.InvariantCulture);
                        return raw >= 0 && raw < int.MaxValue ? (int?)raw : null;
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        readonly List<int> temperatureIds = new List<int>(), fanIds = new List<int>();
        readonly Dictionary<int, double?> fanMaximums = new Dictionary<int, double?>();
        List<ThermalPreset> presets = new List<ThermalPreset>();
        readonly Dictionary<string, string> labels;
        DateTime nextProbe = DateTime.MinValue;
        public string Status { get; private set; }

        public AwccReader(Dictionary<string, string> labels)
        {
            this.labels = labels;
            Status = "Waiting for AWCC sensors";
        }

        // Read operations are separate from the explicitly requested, allowlisted mode setter.
        public static uint QueryArgument(int command, int index)
        {
            if (command != 3 && command != 4 && command != 5 && command != 9 && command != 11)
                throw new ArgumentOutOfRangeException("command", "Only sensor information queries are allowed.");
            if (index < 0 || index > 255)
                throw new ArgumentOutOfRangeException("index");
            return (uint)(command | (index << 8));
        }

        int Query(int command, int index)
        {
            using (var input = (ManagementBaseObject)queryTemplate.Clone())
            {
                input["arg2"] = QueryArgument(command, index);
                using (var output = device.InvokeMethod("Thermal_Information", input, new InvokeMethodOptions { Timeout = TimeSpan.FromSeconds(2) }))
                {
                    if (output == null || output["argr"] == null)
                        return -1;
                    return unchecked((int)Convert.ToUInt32(output["argr"], CultureInfo.InvariantCulture));
                }
            }
        }

        void Probe()
        {
            if (device != null || DateTime.UtcNow < nextProbe)
                return;
            nextProbe = DateTime.UtcNow.AddSeconds(60);
            temperatureIds.Clear();
            fanIds.Clear();
            fanMaximums.Clear();
            fanSensorCodes.Clear();
            try
            {
                var scope = new ManagementScope(@"\\.\root\wmi", new ConnectionOptions { EnablePrivileges = true });
                using (var search = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM AWCCWmiMethodFunction"), new EnumerationOptions { Timeout = TimeSpan.FromSeconds(3) }))
                using (var instances = search.Get())
                {
                    foreach (ManagementObject item in instances)
                    {
                        device = item;
                        break;
                    }
                }

                if (device == null)
                {
                    Status = "No compatible AWCC interface on this computer";
                    return;
                }

                queryTemplate = device.GetMethodParameters("Thermal_Information");
                // Upstream routes association opcode 2 to GetFanSensors, not Thermal_Information.
                try
                {
                    fanSensorTemplate = device.GetMethodParameters(FanSensorMethod);
                }
                catch
                {
                    fanSensorTemplate = null;
                }

                var seen = new HashSet<int>();
                for (int index = 0; index < 64; index++)
                {
                    int id = Query(3, index);
                    if (id <= 0 || !seen.Add(id))
                        break;
                    if (id > 0x100 && id < 0x110)
                        temperatureIds.Add(id & 255);
                    else if (id < 0x90)
                        fanIds.Add(id);
                }

                presets = ModeCatalog.Load(seen);
                foreach (int id in fanIds)
                {
                    fanMaximums[id] = Values.Range(Query(9, id), 1, 30000);
                    fanSensorCodes[id] = ReadFanSensorCode(id);
                }

                Status = (fanIds.Count + temperatureIds.Count > 0) ? "AWCC sensors connected" : "AWCC returned no compatible sensors";
            }
            catch (UnauthorizedAccessException)
            {
                Reset("AWCC needs administrator access · Select Enable sensors");
            }
            catch (ManagementException ex)
            {
                Reset(ex.ErrorCode == ManagementStatus.AccessDenied ? "AWCC needs administrator access · Select Enable sensors" : "AWCC unavailable · " + ex.ErrorCode);
            }
            catch (Exception ex)
            {
                Reset("AWCC unavailable · " + ex.GetType().Name);
            }
        }

        void Reset(string message)
        {
            if (queryTemplate != null)
                queryTemplate.Dispose();
            queryTemplate = null;
            if (fanSensorTemplate != null)
                fanSensorTemplate.Dispose();
            fanSensorTemplate = null;
            if (device != null)
                device.Dispose();
            device = null;
            Status = message;
        }

        string Label(string id, string fallback)
        {
            string value;
            return labels.TryGetValue(id, out value) ? value : fallback;
        }

        public List<Reading> Read()
        {
            Probe();
            var readings = new List<Reading>();
            if (device == null)
                return readings;
            try
            {
                for (int i = 0; i < temperatureIds.Count; i++)
                {
                    int id = temperatureIds[i];
                    string key = "awcc.temp." + id;
                    readings.Add(new Reading { Id = key, Name = Label(key, "AWCC temp " + (i + 1)), Kind = "temperature", Value = Values.Range(Query(4, id), 0, 150), Source = "AWCC WMI" });
                }

                for (int i = 0; i < fanIds.Count; i++)
                {
                    int id = fanIds[i];
                    string key = "awcc.fan." + id;
                    readings.Add(new Reading { Id = key, Name = Label(key, "Fan " + (i + 1)), Kind = "fan", Value = Values.Range(Query(5, id), 0, 30000), Maximum = fanMaximums[id], Source = "AWCC WMI", FanSensorCode = fanSensorCodes[id], FanTemperatureSensorId = MatchFanTemperature(fanSensorCodes[id], temperatureIds), FanAssociationStatus = !fanSensorCodes[id].HasValue ? "query-unavailable" : MatchFanTemperature(fanSensorCodes[id], temperatureIds) != null ? "temperature-id-match" : "unmatched-code" });
                }
            }
            catch (Exception ex)
            {
                Reset("AWCC readings unavailable · " + ex.GetType().Name);
            }

            return readings;
        }

        public ThermalModeState ReadMode()
        {
            int? current = null;
            if (device != null)
            {
                try
                {
                    int raw = Query(11, 0);
                    if (raw >= 0 && raw <= 255)
                        current = raw;
                }
                catch
                {
                }
            }

            var preset = presets.FirstOrDefault(p => p.Id == current);
            return new ThermalModeState
            {
                CurrentId = current,
                CurrentName = preset != null ? preset.Name : current == 0 ? "Custom" : current.HasValue ? "Unknown mode " + current : "Disconnected",
                Presets = presets.ToList(),
                CanChange = device != null && Collector.IsAdmin && presets.Count > 0 && current.HasValue,
                Detail = "Standard firmware presets only. AWCC automation may override the selected mode."
            };
        }

        // Low-level cooling write for the guarded preset worker only.
        internal ModeResult ApplyMode(int mode)
        {
            var result = new ModeResult
            {
                Requested = mode
            };
            try
            {
                if (device == null || !Collector.IsAdmin)
                    throw new InvalidOperationException("Connected AWCC sensors and administrator access are required");
                uint argument = ModeCatalog.ControlArgument(mode, presets);
                var before = ReadMode();
                if (!before.CurrentId.HasValue)
                    throw new InvalidOperationException("Cannot read the current mode; no changes made");
                if (before.CurrentId == mode)
                    return new ModeResult
                    {
                        Success = true,
                        Requested = mode,
                        Actual = mode,
                        Message = "The selected preset is already active"
                    };
                using (var input = device.GetMethodParameters("Thermal_Control"))
                {
                    input["arg2"] = argument;
                    using (var output = device.InvokeMethod("Thermal_Control", input, new InvokeMethodOptions { Timeout = TimeSpan.FromSeconds(2) }))
                    {
                        if (output == null)
                            throw new InvalidOperationException("Firmware did not acknowledge the mode request");
                    }
                }

                for (int attempt = 0; attempt < 4; attempt++)
                {
                    Thread.Sleep(150);
                    result.Actual = ReadMode().CurrentId;
                    if (result.Actual == mode)
                    {
                        result.Success = true;
                        result.Message = "Applied and verified: " + presets.First(p => p.Id == mode).Name;
                        return result;
                    }
                }

                result.Message = "Readback does not match the request. AWCC automation may have overridden it.";
            }
            catch (Exception ex)
            {
                result.Message = "Mode change failed: " + ex.Message;
                result.Actual = ReadMode().CurrentId;
            }

            return result;
        }

        public void Dispose()
        {
            if (queryTemplate != null)
                queryTemplate.Dispose();
            if (fanSensorTemplate != null)
                fanSensorTemplate.Dispose();
            if (device != null)
                device.Dispose();
        }
    }
}
