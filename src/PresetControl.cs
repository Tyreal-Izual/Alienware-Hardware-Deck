using System;

namespace HardwareDeck
{
    public sealed class PresetState
    {
        public int? Cooling { get; set; }
        public int? Cpu { get; set; } // 0 = OEM Off, 1 = OEM OC01; null = unknown/custom.
        public bool CpuReadable { get; set; }
        public decimal? PowerLimitWatts { get; set; }
        public decimal? ShortPowerLimitWatts { get; set; }
        public bool PowerVerified { get; set; }
        public bool CanChange { get; set; }
        public string Detail { get; set; }
        public string CapturedAt { get; set; }
        public int? LinkedPreset {
            get {
                if (!CpuReadable || !PowerVerified || !Cooling.HasValue || !Cpu.HasValue) return null;
                return Cpu == (Cooling == 161 ? 1 : 0) && PresetPolicy.IsCooling(Cooling.Value) ? Cooling : null;
            }
        }
    }
    public sealed class PresetRequest
    {
        public string Kind { get; set; }
        public int Value { get; set; }
    }
    public sealed class PresetTarget
    {
        public int Cooling { get; set; }
        public int Cpu { get; set; }
        public bool Matches(PresetState state) { return state != null && state.CpuReadable && state.PowerVerified && state.Cooling == Cooling && state.Cpu == Cpu; }
    }
    public sealed class PresetResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public PresetState Actual { get; set; }
    }
    public static class PresetPolicy
    {
        public static bool IsCooling(int id) { return id == 163 || id == 160 || id == 161; }
        public static string Name(int id) { return id == 163 ? "Quiet" : id == 160 ? "Balanced" : id == 161 ? "Performance" : "Unknown"; }
        public static PresetTarget Resolve(PresetRequest request, PresetState current)
        {
            if (request == null || current == null || !current.CanChange || !current.Cooling.HasValue || !current.CpuReadable)
                throw new InvalidOperationException("Fresh CPU and cooling readback is required");
            var target = new PresetTarget();
            if (request.Kind == "preset" && IsCooling(request.Value)) {
                target.Cooling = request.Value; target.Cpu = request.Value == 161 ? 1 : 0;
            } else if (request.Kind == "cooling" && IsCooling(request.Value)) {
                target.Cooling = request.Value;
                if (request.Value == 161 && !current.Cpu.HasValue) throw new InvalidOperationException("Select a main preset to leave an unknown CPU configuration");
                target.Cpu = request.Value == 161 ? current.Cpu.Value : 0;
            } else if (request.Kind == "cpu" && (request.Value == 0 || request.Value == 1)) {
                target.Cooling = current.Cooling.Value; target.Cpu = request.Value;
            } else throw new InvalidOperationException("Unsupported preset request");
            if (!IsCooling(target.Cooling) || (target.Cpu != 0 && target.Cpu != 1) || (target.Cpu == 1 && target.Cooling != 161))
                throw new InvalidOperationException("OC01 requires Performance cooling");
            return target;
        }
    }
    internal static class CoolingPowerPolicy
    {
        // Independently observed after original AWCC transitions on the audited
        // R16/14900KF: cooling changes PL1 even when the CPU OC profile is Off.
        internal static bool Matches(int? cooling,decimal? pl1,decimal? pl2)
        {
            decimal? expected=cooling==163?(decimal?)170:cooling==160?(decimal?)180:cooling==161?(decimal?)253:null;
            return expected.HasValue && pl1==expected && pl2==253;
        }
    }
    internal interface IPresetBackend
    {
        PresetState Read();
        void ApplyAwcc(int preset);
        void ApplyCooling(int cooling);
        void Pause();
    }
    internal static class PresetTransaction
    {
        // Only four supported combinations. CPU-off is confirmed before independent
        // cooling changes. All main presets use AWCC's own full-preset workflow.
        internal static PresetResult Execute(PresetRequest request, IPresetBackend backend)
        {
            PresetTarget target = null;
            try {
                var before = backend.Read();
                target = PresetPolicy.Resolve(request, before);
                if (target.Matches(before)) return new PresetResult { Success = true, Actual = before, Message = "CPU and cooling already verified" };
                if (target.Cpu == 1) {
                    // Establish adequate cooling before asking AWCC to enable OC.
                    if (before.Cooling != 161) backend.ApplyCooling(161);
                    backend.ApplyAwcc(161);
                } else if (request.Kind == "preset" || target.Cooling != 161 || before.Cpu != 0) {
                    backend.ApplyAwcc(target.Cooling == 161 ? 160 : target.Cooling);
                }
                var actual = WaitFor(backend, s => s.CpuReadable && s.Cpu == target.Cpu);
                if (actual == null) throw new InvalidOperationException("CPU target could not be verified");
                if (actual.Cooling != target.Cooling) {
                    if (target.Cooling != 161 && actual.Cpu != 0) throw new InvalidOperationException("CPU Off must be verified before reducing cooling");
                    backend.ApplyCooling(target.Cooling);
                }
                actual = WaitFor(backend, target.Matches);
                if (actual == null) throw new InvalidOperationException("CPU or cooling readback did not settle at the requested target");
                return new PresetResult { Success = true, Actual = actual,
                    Message = "Verified: " + PresetPolicy.Name(target.Cooling) + " cooling · " + (target.Cpu == 1 ? "OC01" : "OC Off") };
            } catch (Exception error) {
                PresetState actual = null;
                try {
                    actual = backend.Read();
                    if (target!=null && (!actual.CpuReadable || actual.Cpu != 0) && actual.Cooling.HasValue && actual.Cooling != 161) {
                        backend.ApplyCooling(161); actual = backend.Read();
                    }
                } catch { }
                return new PresetResult { Success = false, Actual = actual, Message = "Not verified: " + error.Message };
            }
        }
        static PresetState WaitFor(IPresetBackend backend, Func<PresetState, bool> matches)
        {
            int stable = 0;
            for (int i = 0; i < 20; i++) {
                var state = backend.Read();
                stable = matches(state) ? stable + 1 : 0;
                if (stable >= 4) return state;
                backend.Pause();
            }
            return null;
        }
    }
}
