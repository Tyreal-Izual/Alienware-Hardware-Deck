using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace HardwareDeck
{
    public sealed partial class DeckWindow
    {
        void UpdateCpuLimits()
        {
            var state = latest == null ? null : latest.Preset;
            var limits = PresetFresh() && state.CpuReadable ? state.Limits : null;
            bool groups = limits != null && limits.HasCoreGroups;
            SetLimitChoices("Icc", limits == null ? null : (decimal?)limits.IccMaxAmps, 307, 316, " A", false);
            SetLimitChoices("Favored", groups ? limits.UniformRatio(true) : null, 60, 61, "×", groups);
            SetLimitChoices("OtherP", groups ? limits.UniformRatio(false) : null, 57, 58, "×", groups);
            bool ecores = limits != null && limits.HasEcoreLimits;
            SetLimitChoices("Ecore", ecores ? limits.UniformEcoreRatio() : null, 44, 45, "×", ecores);
            Text("FavoredCoreLabel", groups ? "Favored " + string.Join("/", limits.Pcores.Where(c => c.Favored == true)
                .OrderBy(c => c.Index).Select(c => "P" + c.Index)) : "Favored P");

            Find<FrameworkElement>("IccLimits").ToolTip =
                "Read only · Processor Core IccMax (XTU control 102)\nConfigured current ceiling, not measured current draw.\n307 A: OEM Off · 316 A: firmware startup / OC01 / OC02.";
            string detail = "Read only · Per-core ratio ceilings, not current clock speeds.";
            if (limits != null && limits.Pcores != null)
                detail += "\n" + string.Join(" · ", limits.Pcores.OrderBy(c => c.Index)
                    .Select(c => "P" + c.Index + (c.Favored == true ? " ★" : "") + ": " +
                        c.Ratio.ToString("0.##", CultureInfo.InvariantCulture) + "×"));
            if (limits != null && limits.ActivePcoreRatios != null)
                detail += "\nActive P-core count limits: " + string.Join(" · ", limits.ActivePcoreRatios
                    .Select((value, index) => (index + 1) + ": " + value.ToString("0.##", CultureInfo.InvariantCulture) + "×"));
            detail += groups
                ? "\n★ = XTU IsFavoredCore flag. Both per-core and active-count limits constrain clocks."
                : "\nFavored-core grouping unavailable; grouping is not inferred from higher ratios.";
            if (!PresetFresh()) detail += "\nWaiting for fresh CPU readback.";
            Find<FrameworkElement>("FavoredLimits").ToolTip = detail;
            Find<FrameworkElement>("OtherPLimits").ToolTip = detail;

            string edetail = "Read only · E-core ratio ceilings, not current clock speeds.\n44×: firmware startup / OEM Off / OC01 · 45×: OC02.";
            if (ecores)
            {
                edetail += "\nIndividual E-core limits:" + RatioRows(limits.EcoreRatios, "E", 0);
                edetail += "\nActive E-core count limits:" + RatioRows(limits.ActiveEcoreRatios, "", 1);
                edetail += "\nA reference is highlighted only when all 32 limits match it.";
            }
            else edetail += "\nWaiting for complete, fresh E-core readback.";
            Find<FrameworkElement>("EcoreLimits").ToolTip = edetail;
        }

        static string RatioRows(decimal[] values, string prefix, int start)
        {
            return string.Concat(Enumerable.Range(0, (values.Length + 7) / 8).Select(row => "\n" +
                string.Join(" · ", values.Skip(row * 8).Take(8).Select((value, index) =>
                    prefix + (start + row * 8 + index) + ": " + value.ToString("0.##", CultureInfo.InvariantCulture) + "×"))));
        }

        void SetLimitChoices(string prefix, decimal? current, decimal first, decimal second, string unit, bool mixed)
        {
            foreach (var choice in new[] { new { Suffix = "First", Value = first }, new { Suffix = "Second", Value = second } })
            {
                var badge = Find<TextBlock>(prefix + choice.Suffix);
                bool selected = current == choice.Value;
                badge.Tag = selected ? "Current" : null;
                System.Windows.Automation.AutomationProperties.SetName(badge,
                    prefix + " limit " + choice.Value.ToString("0.##", CultureInfo.InvariantCulture) + unit);
                System.Windows.Automation.AutomationProperties.SetItemStatus(badge, selected ? "Current" : "Reference");
            }
            var actual = Find<TextBlock>(prefix + "Actual");
            actual.Visibility = current == first || current == second ? Visibility.Collapsed : Visibility.Visible;
            actual.Text = current.HasValue ? "Other: " + current.Value.ToString("0.##", CultureInfo.InvariantCulture) + unit :
                mixed ? "Mixed" : "—";
            actual.ToolTip = actual.Text;
        }
    }
}
