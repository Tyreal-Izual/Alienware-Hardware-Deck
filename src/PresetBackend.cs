using System;
using System.Collections.Generic;
using System.Threading;

namespace HardwareDeck
{
    internal sealed class LocalPresetBackend : IPresetBackend, IDisposable
    {
        readonly CpuPresetReader cpu = new CpuPresetReader();
        readonly AwccReader thermal = new AwccReader(new Dictionary<string, string>());
        readonly AwccPresetBridge bridge;
        internal LocalPresetBackend(IntPtr caller, int callerProcess)
        {
            bridge = new AwccPresetBridge(caller, callerProcess);
            thermal.Read();
        }

        public PresetState Read()
        {
            return PresetStateFactory.Create(cpu.Read(true), thermal.ReadMode());
        }

        public void ApplyAwcc(int preset)
        {
            bridge.Apply(preset, delegate
            {
                if (preset == PresetPolicy.Performance && thermal.ReadMode().CurrentId != PresetPolicy.Performance)
                    ApplyCooling(PresetPolicy.Performance);
            });
        }

        public void ApplyCooling(int preset)
        {
            AwccPresetBridge.ThrowIfStopping();
            var result = thermal.ApplyMode(preset);
            if (!result.Success)
                throw new InvalidOperationException(result.Message);
        }

        public void ApplyOc02()
        {
            Action requirePerformance = delegate
            {
                AwccPresetBridge.ThrowIfStopping();
                var state = Read();
                if (!state.CpuReadable || state.Cooling != PresetPolicy.Performance)
                    throw new InvalidOperationException("OC02 requires fresh Performance cooling readback");
            };
            requirePerformance();
            bridge.ApplyOc02(requirePerformance, delegate
            {
                // Entering Custom also applies its saved thermal policy. Reassert
                // and verify P before saving the explicitly requested CPU OC2.
                ApplyCooling(PresetPolicy.Performance);
                int stable = 0;
                for (int i = 0; i < 20; i++)
                {
                    var state = Read();
                    stable = state.Cooling == PresetPolicy.Performance && state.CpuReadable && state.PowerVerified ? stable + 1 : 0;
                    if (stable >= 4)
                        return;
                    Pause();
                }

                throw new InvalidOperationException("Performance cooling did not settle before OC02");
            });
        }

        public void Pause()
        {
            Thread.Sleep(300);
        }

        public void Dispose()
        {
            bridge.Dispose();
            cpu.Dispose();
            thermal.Dispose();
        }
    }

    internal static class PresetStateFactory
    {
        internal static PresetState Create(CpuPresetReading cpu, ThermalModeState thermal)
        {
            string unavailable = AwccPresetBridge.AvailabilityIssue;
            bool power = cpu.Readable && CoolingPowerPolicy.Matches(thermal.CurrentId, cpu.PowerLimitWatts, cpu.ShortPowerLimitWatts);
            string detail;
            if (!cpu.Readable)
                detail = cpu.Detail;
            else if (!thermal.CanChange)
                detail = "Cooling readback unavailable · " + thermal.Detail;
            else if (unavailable != null)
                detail = unavailable;
            else if (!power)
                detail = "Cooling power limits do not match the audited AWCC profile";
            else
                detail = cpu.Detail + " · Cooling power limits verified";

            return new PresetState
            {
                Cooling = thermal.CurrentId,
                Cpu = cpu.Level,
                CpuReadable = cpu.Readable,
                CapturedAt = cpu.CapturedAt,
                PowerLimitWatts = cpu.PowerLimitWatts,
                ShortPowerLimitWatts = cpu.ShortPowerLimitWatts,
                PowerVerified = power,
                CanChange = cpu.Readable && thermal.CanChange && unavailable == null,
                Detail = detail
            };
        }
    }
}
