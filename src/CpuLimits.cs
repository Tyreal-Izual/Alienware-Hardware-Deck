using System;
using System.Collections.Generic;
using System.Linq;

namespace HardwareDeck
{
    public sealed class PcoreLimit
    {
        public int Index { get; set; }
        public decimal Ratio { get; set; }
        public bool? Favored { get; set; }
    }

    public sealed class CpuLimitReading
    {
        public decimal IccMaxAmps { get; set; }
        public List<PcoreLimit> Pcores { get; set; }
        public decimal[] ActivePcoreRatios { get; set; }
        public decimal[] EcoreRatios { get; set; }
        public decimal[] ActiveEcoreRatios { get; set; }

        public bool HasEcoreLimits
        {
            get
            {
                return EcoreRatios != null && EcoreRatios.Length == 16 && EcoreRatios.All(value => value > 0) &&
                    ActiveEcoreRatios != null && ActiveEcoreRatios.Length == 16 && ActiveEcoreRatios.All(value => value > 0);
            }
        }

        public decimal? UniformEcoreRatio()
        {
            if (!HasEcoreLimits) return null;
            var values = EcoreRatios.Concat(ActiveEcoreRatios).Distinct().ToArray();
            return values.Length == 1 ? (decimal?)values[0] : null;
        }

        public bool HasCoreGroups
        {
            get
            {
                return Pcores != null && Pcores.Count == 8 &&
                    Pcores.All(c => c != null && c.Index >= 0 && c.Index < 8 && c.Ratio > 0 && c.Favored.HasValue) &&
                    Pcores.Select(c => c.Index).Distinct().Count() == 8 &&
                    Pcores.Any(c => c.Favored == true) && Pcores.Any(c => c.Favored == false);
            }
        }

        public decimal? UniformRatio(bool favored)
        {
            if (!HasCoreGroups) return null;
            var values = Pcores.Where(c => c.Favored == favored).Select(c => c.Ratio).Distinct().ToArray();
            return values.Length == 1 ? (decimal?)values[0] : null;
        }

        internal static CpuLimitReading FromControls(IDictionary<uint, XtuControlValue> controls)
        {
            return new CpuLimitReading
            {
                IccMaxAmps = controls[102].ActiveValue,
                Pcores = Enumerable.Range(116, 8).Select(id => new PcoreLimit
                {
                    Index = id - 116,
                    Ratio = controls[(uint)id].ActiveValue,
                    Favored = controls[(uint)id].HasFavoredCoreMetadata ? (bool?)controls[(uint)id].IsFavoredCore : null
                }).ToList(),
                ActivePcoreRatios = new uint[] { 29, 30, 31, 32, 42, 43, 96, 97 }
                    .Select(id => controls[id].ActiveValue).ToArray(),
                EcoreRatios = Enumerable.Range(1500, 16).Select(id => controls[(uint)id].ActiveValue).ToArray(),
                ActiveEcoreRatios = Enumerable.Range(4500, 16).Select(id => controls[(uint)id].ActiveValue).ToArray()
            };
        }
    }
}
