using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace HardwareDeck
{
    public sealed partial class DeckWindow
    {
        bool PresetFresh()
        {
            DateTime captured;
            return HasFreshSample() && latest.Preset != null && DateTime.TryParse(latest.Preset.CapturedAt, null, DateTimeStyles.RoundtripKind, out captured) && (DateTime.UtcNow - captured.ToUniversalTime()).TotalSeconds >= 0 && (DateTime.UtcNow - captured.ToUniversalTime()).TotalSeconds <= Math.Max(8, interval / 1000.0 * 3);
        }

        void RequestPreset(PresetRequest request)
        {
            if (modeBusy || !PresetFresh())
                return;
            var state = latest.Preset;
            PresetTarget target;
            try
            {
                target = PresetPolicy.Resolve(request, state);
            }
            catch
            {
                return;
            }

            if (latest.ThermalMode == null || latest.ThermalMode.Presets == null || !latest.ThermalMode.Presets.Any(p => p.Id == target.Cooling))
                return;
            if (target.Matches(state))
                return;
            modeBusy = true;
            pendingRequest = request;
            presetNotice = null;
            Text("ModeNotice", "Applying in background · Verifying CPU and cooling...");
            UpdateModeButtons();
            // Offscreen fixture/preview windows never launch a hardware worker.
            if (!window.IsVisible || !string.IsNullOrEmpty(preview))
                return;
            var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            Task.Run(() =>
            {
                PresetResult result;
                try
                {
                    result = PresetWorker.Execute(request, handle);
                }
                catch (Exception e)
                {
                    result = new PresetResult
                    {
                        Message = "Preset switch failed: " + e.Message
                    };
                }

                PostIfOpen(() => CompletePreset(result));
            });
        }

        void UpdateModeButtons()
        {
            var mode = latest == null ? null : latest.ThermalMode;
            var state = latest == null ? null : latest.Preset;
            bool fresh = PresetFresh();
            bool enabled = fresh && state.CanChange && mode != null && mode.CanChange && !modeBusy;
            bool conflict = fresh && state.Cpu > 0 && state.Cooling != PresetPolicy.Performance;
            Text("ModeCurrent", !fresh || !state.CpuReadable || !state.PowerVerified ? "Unverified" : conflict ? "Conflict" : state.LinkedPreset.HasValue ? "Linked" : "Independent");
            Find<TextBlock>("ModeCurrent").ToolTip = state == null ? "Waiting for CPU and cooling readback" : state.Detail;
            foreach (int id in PresetPolicy.CoolingModes)
            {
                string name = PresetPolicy.Name(id);
                bool supported = mode != null && mode.Presets != null && mode.Presets.Any(p => p.Id == id);
                SetPresetButton("Mode" + name, name, enabled && supported, fresh && state.LinkedPreset == id, "preset", id, "Apply the full AWCC " + name + " preset");
                SetPresetButton("Cooling" + name, name.Substring(0, 1), enabled && supported && (id != PresetPolicy.Performance || state.Cpu.HasValue), fresh && state.Cooling == id, "cooling", id, name + " cooling" + (id == PresetPolicy.Performance ? " · Preserve CPU Off / OC01 / OC02" : " · CPU OC switches Off first"));
            }

            SetPresetButton("CpuOff", "Off", enabled && state.Cooling.HasValue && PresetPolicy.IsCooling(state.Cooling.Value), fresh && state.CpuReadable && state.Cpu == 0, "cpu", 0, "OC Off · Retain cooling and its BIOS power policy");
            SetPresetButton("CpuOC01", "OC01", enabled && state.Cooling == PresetPolicy.Performance, fresh && state.CpuReadable && state.Cpu == 1, "cpu", 1, "CPU OC01 requires Performance cooling");
            SetPresetButton("CpuOC02", "OC02", enabled && state.Cooling == PresetPolicy.Performance, fresh && state.CpuReadable && state.Cpu == 2, "cpu", 2, "CPU OC02 · Manual only · Requires Performance cooling");
            if (!modeBusy)
                Text("ModeNotice", state != null && !state.CanChange ? state.Detail : !fresh ? "Waiting for fresh CPU and cooling readback" : conflict ? "CPU OC requires Performance cooling · Select a main preset" : !state.CanChange || !state.Cpu.HasValue || !state.PowerVerified ? state.Detail : presetNotice ?? "OC and cooling verified");
        }

        void SetPresetButton(string control, string label, bool enabled, bool current, string kind, int value, string hint)
        {
            var button = Find<Button>(control);
            bool pending = modeBusy && pendingRequest != null && pendingRequest.Kind == kind && pendingRequest.Value == value;
            button.Tag = current ? "Current" : pending ? "Pending" : null;
            button.Content = label + (pending ? "…" : "");
            button.IsEnabled = enabled;
            button.ToolTip = modeBusy ? "Waiting for CPU and cooling verification" : hint;
            System.Windows.Automation.AutomationProperties.SetName(button, hint);
            System.Windows.Automation.AutomationProperties.SetItemStatus(button, current ? "Current" : pending ? "Switching" : enabled ? "Available" : "Unavailable");
        }

        void CompletePreset(PresetResult result)
        {
            CpuPresetReader.InvalidateAll();
            modeBusy = false;
            pendingRequest = null;
            presetNotice = result.Message;
            if (latest != null)
            {
                latest.Preset = result.Actual ?? new PresetState
                {
                    CapturedAt = DateTime.UtcNow.ToString("o"),
                    Detail = result.Message
                };
            }

            Text("ModeNotice", result.Message);
            UpdateModeButtons();
            UpdateCpuPower();
        }
    }
}
