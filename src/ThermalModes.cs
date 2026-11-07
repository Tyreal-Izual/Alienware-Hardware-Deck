using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace HardwareDeck
{
    public sealed class ThermalPreset
    {
        public int Id { get; set; }
        public string Name { get; set; }

        public override string ToString()
        {
            return Name;
        }
    }

    public sealed class ThermalModeState
    {
        public int? CurrentId { get; set; }
        public string CurrentName { get; set; }
        public bool CanChange { get; set; }
        public List<ThermalPreset> Presets { get; set; }
        public string Detail { get; set; }
    }

    public sealed class ModeResult
    {
        public bool Success { get; set; }
        public int Requested { get; set; }
        public int? Actual { get; set; }
        public string Message { get; set; }
    }

    public static class ModeCatalog
    {
        public static List<ThermalPreset> Parse(string json, IEnumerable<int> supported)
        {
            var allowed = PresetPolicy.CoolingModes.ToDictionary(
                id => "Ustt" + PresetPolicy.Name(id),
                id => new ThermalPreset { Id = id, Name = PresetPolicy.Name(id) });
            var result = new List<ThermalPreset>();
            var root = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
            object raw;
            if (root == null || !root.TryGetValue("profiles", out raw))
                return result;
            var rows = raw as object[];
            if (rows == null)
                return result;
            foreach (var entry in rows)
            {
                var row = entry as Dictionary<string, object>;
                object name, id, readOnly;
                if (row == null || !row.TryGetValue("name", out name) || !row.TryGetValue("thermal_mode", out id) || !row.TryGetValue("readonly", out readOnly) || !(readOnly is bool) || !(bool)readOnly)
                    continue;
                ThermalPreset preset;
                if (id is int && allowed.TryGetValue(Convert.ToString(name), out preset) && (int)id == preset.Id && supported.Contains(preset.Id) && !result.Any(p => p.Id == preset.Id))
                    result.Add(preset);
            }

            return result.OrderBy(p => PresetPolicy.CoolingModes.IndexOf(p.Id)).ToList();
        }

        public static List<ThermalPreset> Load(IEnumerable<int> supported)
        {
            try
            {
                var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Alienware\Alienware Command Center\Subsystems\Performance\SubAgent\SCSubAgent\OCControlService\OCControl.Service.Thermal.Data\Profile.json");
                return Parse(File.ReadAllText(path), supported);
            }
            catch
            {
                return new List<ThermalPreset>();
            }
        }

        public static uint ControlArgument(int mode, IEnumerable<ThermalPreset> presets)
        {
            if (!PresetPolicy.IsCooling(mode) || !presets.Any(p => p.Id == mode))
                throw new ArgumentOutOfRangeException("mode", "Only discovered Quiet/Balanced/Performance presets are allowed.");
            return (uint)(1 | (mode << 8));
        }
    }
}
