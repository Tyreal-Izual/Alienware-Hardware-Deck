using System;

namespace HardwareDeck
{
    public sealed class PresetState
    {
        public int? Cooling { get; set; }
        public int? Cpu { get; set; } // 0 = OEM Off, 1 = OEM OC01, 2 = OEM OC02; null = unknown/custom.
        public bool CpuReadable { get; set; }
        public decimal? PowerLimitWatts { get; set; }
        public decimal? ShortPowerLimitWatts { get; set; }
        public bool PowerVerified { get; set; }
        public bool CanChange { get; set; }
        public string Detail { get; set; }
        public string CapturedAt { get; set; }

        public int? LinkedPreset
        {
            get
            {
                if (!CpuReadable || !PowerVerified || !Cooling.HasValue || !Cpu.HasValue)
                    return null;
                return Cpu == (Cooling == PresetPolicy.Performance ? 1 : 0) && PresetPolicy.IsCooling(Cooling.Value) ? Cooling : null;
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

        public bool Matches(PresetState state)
        {
            return state != null && state.CpuReadable && state.PowerVerified && state.Cooling == Cooling && state.Cpu == Cpu;
        }
    }

    public sealed class PresetResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public PresetState Actual { get; set; }
    }

    public static class PresetPolicy
    {
        public const int Quiet = 163;
        public const int Balanced = 160;
        public const int Performance = 161;
        public static readonly System.Collections.ObjectModel.ReadOnlyCollection<int> CoolingModes =
            Array.AsReadOnly(new[] { Quiet, Balanced, Performance });

        public static bool IsCooling(int id)
        {
            return id == Quiet || id == Balanced || id == Performance;
        }

        public static string Name(int id)
        {
            switch (id)
            {
                case Quiet: return "Quiet";
                case Balanced: return "Balanced";
                case Performance: return "Performance";
                default: return "Unknown";
            }
        }

        public static PresetTarget Resolve(PresetRequest request, PresetState current)
        {
            if (request == null || current == null || !current.CanChange || !current.Cooling.HasValue || !current.CpuReadable)
                throw new InvalidOperationException("Fresh CPU and cooling readback is required");
            var target = new PresetTarget();
            if (request.Kind == "preset" && IsCooling(request.Value))
            {
                target.Cooling = request.Value;
                target.Cpu = request.Value == PresetPolicy.Performance ? 1 : 0;
            }
            else if (request.Kind == "cooling" && IsCooling(request.Value))
            {
                target.Cooling = request.Value;
                if (request.Value == PresetPolicy.Performance && !current.Cpu.HasValue)
                    throw new InvalidOperationException("Select a main preset to leave an unknown CPU configuration");
                target.Cpu = request.Value == PresetPolicy.Performance ? current.Cpu.Value : 0;
            }
            else if (request.Kind == "cpu" && (request.Value >= 0 && request.Value <= 2))
            {
                target.Cooling = current.Cooling.Value;
                target.Cpu = request.Value;
            }
            else
                throw new InvalidOperationException("Unsupported preset request");
            if (!IsCooling(target.Cooling) || (target.Cpu < 0 || target.Cpu > 2) || (target.Cpu > 0 && target.Cooling != PresetPolicy.Performance))
                throw new InvalidOperationException("OC01 and OC02 require Performance cooling");
            return target;
        }
    }

    internal static class CoolingPowerPolicy
    {
        // Independently observed after original AWCC transitions on the audited
        // R16/14900KF: cooling changes PL1 even when the CPU OC profile is Off.
        internal static bool Matches(int? cooling, decimal? pl1, decimal? pl2)
        {
            decimal? expected =
                cooling == PresetPolicy.Quiet ? (decimal?)170 :
                cooling == PresetPolicy.Balanced ? (decimal?)180 :
                cooling == PresetPolicy.Performance ? (decimal?)253 : null;
            return expected.HasValue && pl1 == expected && pl2 == 253;
        }
    }

    internal interface IPresetBackend
    {
        PresetState Read();
        void ApplyAwcc(int preset);
        void ApplyCooling(int cooling);
        void ApplyOc02();
        void Pause();
    }

    internal static class PresetTransaction
    {
        // Main presets map to Off or OC01; OC02 is manual-only.
        // Confirm CPU Off before independent cooling changes. Main presets
        // use AWCC's own full-preset workflow.
        internal static PresetResult Execute(PresetRequest request, IPresetBackend backend)
        {
            PresetTarget target = null;
            try
            {
                var before = backend.Read();
                target = PresetPolicy.Resolve(request, before);
                if (target.Matches(before))
                    return new PresetResult
                    {
                        Success = true,
                        Actual = before,
                        Message = "CPU and cooling already verified"
                    };
                if (target.Cpu == 2)
                {
                    // Cooling-only requests preserve an existing OC02 without activating it.
                    if (before.Cpu != 2)
                    {
                        var fresh = backend.Read();
                        if (!fresh.CpuReadable || fresh.Cooling != PresetPolicy.Performance)
                            throw new InvalidOperationException("OC02 requires fresh Performance cooling readback");
                        backend.ApplyOc02();
                    }
                }
                else if (target.Cpu == 1)
                {
                    // Establish adequate cooling before asking AWCC to enable OC.
                    if (before.Cooling != PresetPolicy.Performance)
                        backend.ApplyCooling(PresetPolicy.Performance);
                    backend.ApplyAwcc(PresetPolicy.Performance);
                }
                else if (request.Kind == "preset" || target.Cooling != PresetPolicy.Performance || before.Cpu != 0)
                {
                    backend.ApplyAwcc(target.Cooling == PresetPolicy.Performance ? PresetPolicy.Balanced : target.Cooling);
                }

                var actual = WaitFor(backend, s => s.CpuReadable && s.Cpu == target.Cpu);
                if (actual == null)
                    throw new InvalidOperationException("CPU target could not be verified");
                if (actual.Cooling != target.Cooling)
                {
                    if (target.Cooling != PresetPolicy.Performance && actual.Cpu != 0)
                        throw new InvalidOperationException("CPU Off must be verified before reducing cooling");
                    backend.ApplyCooling(target.Cooling);
                }

                actual = WaitFor(backend, target.Matches);
                if (actual == null)
                    throw new InvalidOperationException("CPU or cooling readback did not settle at the requested target");
                return new PresetResult
                {
                    Success = true,
                    Actual = actual,
                    Message = "Verified: " + PresetPolicy.Name(target.Cooling) + " cooling · " + (target.Cpu == 2 ? "OC02" : target.Cpu == 1 ? "OC01" : "OC Off")
                };
            }
            catch (Exception error)
            {
                PresetState actual = null;
                try
                {
                    actual = backend.Read();
                    if (target != null && (!actual.CpuReadable || actual.Cpu != 0) && actual.Cooling.HasValue && actual.Cooling != PresetPolicy.Performance)
                    {
                        backend.ApplyCooling(PresetPolicy.Performance);
                        actual = backend.Read();
                    }
                }
                catch
                {
                }

                return new PresetResult
                {
                    Success = false,
                    Actual = actual,
                    Message = "Not verified: " + error.Message
                };
            }
        }

        static PresetState WaitFor(IPresetBackend backend, Func<PresetState, bool> matches)
        {
            int stable = 0;
            for (int i = 0; i < 20; i++)
            {
                var state = backend.Read();
                stable = matches(state) ? stable + 1 : 0;
                if (stable >= 4)
                    return state;
                backend.Pause();
            }

            return null;
        }
    }
}
