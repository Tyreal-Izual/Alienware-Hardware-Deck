using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Web.Script.Serialization;
using HardwareDeck;

public static class CpuLimitsTests
{
    static int checks;
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
    }

    static XtuControlValue ReadWire(string member)
    {
        string xml = "<XtuTuningControl xmlns=\"http://schemas.datacontract.org/2004/07/IronCity.Core\">" +
            "<ActiveValue>61</ActiveValue><Id>120</Id>" + member + "</XtuTuningControl>";
        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml)))
            return (XtuControlValue)new DataContractSerializer(typeof(XtuControlValue)).ReadObject(stream);
    }

    public static void Run()
    {
        var absent = ReadWire("");
        var yes = ReadWire("<IsFavoredCore>true</IsFavoredCore>");
        var no = ReadWire("<IsFavoredCore>false</IsFavoredCore>");
        Check(!absent.HasFavoredCoreMetadata, "Missing wire metadata is not an explicit false flag");
        Check(yes.HasFavoredCoreMetadata && yes.IsFavoredCore, "Deserialize the service's favored-core flag");
        Check(no.HasFavoredCoreMetadata && !no.IsFavoredCore, "Deserialize explicit non-favored metadata");

        var controls = PresetLifecycleTests.StartupValues().ToDictionary(x => x.Id);
        var ungrouped = CpuLimitReading.FromControls(controls);
        Check(ungrouped.IccMaxAmps == 316 && !ungrouped.HasCoreGroups,
            "IccMax is readable without inventing favored groups from high ratios");
        foreach (var pair in controls.Where(x => x.Key >= 116 && x.Key <= 123))
            pair.Value.IsFavoredCore = pair.Key == 120 || pair.Key == 121;
        var limits = CpuLimitReading.FromControls(controls);
        Check(limits.HasCoreGroups && limits.Pcores.Where(c => c.Favored == true).Select(c => c.Index).SequenceEqual(new[] { 4, 5 }),
            "Favored indices come from explicit metadata");
        Check(limits.UniformRatio(true) == 61 && limits.UniformRatio(false) == 58,
            "Current startup per-core limits remain independent of OEM profile matching");
        Check(limits.ActivePcoreRatios.SequenceEqual(new decimal[] { 60, 60, 57, 57, 57, 57, 57, 57 }),
            "Keep active-core-count ceilings distinct from individual-core ceilings");
        Check(limits.HasEcoreLimits && limits.UniformEcoreRatio() == 44 &&
            limits.EcoreRatios.Length == 16 && limits.ActiveEcoreRatios.Length == 16,
            "Startup E-core limits include all individual and active-count entries");
        var ecoreIds = Enumerable.Range(1500, 16).Concat(Enumerable.Range(4500, 16)).Select(id => (uint)id).ToArray();
        foreach (uint id in ecoreIds) controls[id].ActiveValue = 45;
        Check(CpuLimitReading.FromControls(controls).UniformEcoreRatio() == 45, "OC02 E-core limits read back as 45");
        bool everyEcoreChecked = true;
        foreach (uint id in ecoreIds)
        {
            controls[id].ActiveValue = 44;
            everyEcoreChecked &= CpuLimitReading.FromControls(controls).UniformEcoreRatio() == null;
            controls[id].ActiveValue = 45;
        }
        Check(everyEcoreChecked, "Changing any one of the 32 E-core limits invalidates a uniform highlight");
        foreach (uint id in ecoreIds) controls[id].ActiveValue = 46;
        Check(CpuLimitReading.FromControls(controls).UniformEcoreRatio() == 46, "Custom E-core values remain visible");
        var invalidEcores = CpuLimitReading.FromControls(controls);
        invalidEcores.EcoreRatios = null;
        Check(!invalidEcores.HasEcoreLimits && invalidEcores.UniformEcoreRatio() == null, "Missing E-core data is unavailable, not mixed");
        invalidEcores = CpuLimitReading.FromControls(controls);
        invalidEcores.ActiveEcoreRatios = new decimal[15];
        Check(!invalidEcores.HasEcoreLimits, "Partial active-count limits cannot produce a highlight");
        invalidEcores = CpuLimitReading.FromControls(controls);
        invalidEcores.ActiveEcoreRatios[15] = 0;
        Check(!invalidEcores.HasEcoreLimits, "Invalid E-core limits cannot produce a highlight");

        controls[120].ActiveValue = 62;
        Check(CpuLimitReading.FromControls(controls).UniformRatio(true) == null,
            "A mixed favored group cannot highlight one uniform value");
        controls[120].ActiveValue = 61;
        foreach (var pair in controls.Where(x => x.Key >= 116 && x.Key <= 123))
            pair.Value.IsFavoredCore = pair.Key == 116 || pair.Key == 117;
        limits = CpuLimitReading.FromControls(controls);
        Check(limits.UniformRatio(true) == 58 && limits.UniformRatio(false) == null,
            "Core grouping follows metadata even when higher ratios belong to other cores");
        controls[119] = new XtuControlValue { Id = 119, ActiveValue = 58 };
        Check(!CpuLimitReading.FromControls(controls).HasCoreGroups, "Partial metadata must not produce confident group labels");
        foreach (var pair in controls.Where(x => x.Key >= 116 && x.Key <= 123)) pair.Value.IsFavoredCore = false;
        Check(!CpuLimitReading.FromControls(controls).HasCoreGroups, "No reported favored group is not guessed from values");

        var json = new JavaScriptSerializer();
        var roundTrip = json.Deserialize<PresetState>(json.Serialize(new PresetState { Limits = ungrouped }));
        Check(roundTrip.Limits.IccMaxAmps == 316 && !roundTrip.Limits.HasCoreGroups && roundTrip.Limits.UniformEcoreRatio() == 44,
            "Worker JSON preserves P/E-core limits and unknown metadata");
        Console.WriteLine("CPU limit display data checks passed: " + checks);
    }
}
