using System;
using System.Collections.Generic;
using System.Linq;
using HardwareDeck;

public static class PresetTests
{
    static int checks;
    static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
    static PresetState State(int fan,int? cpu){return new PresetState{Cooling=fan,Cpu=cpu,CpuReadable=true,PowerVerified=true,CanChange=true,CapturedAt=DateTime.UtcNow.ToString("o")};}
    static PresetRequest Request(string kind,int value){return new PresetRequest{Kind=kind,Value=value};}
    sealed class Fake : IPresetBackend
    {
        public PresetState State;
        public bool FailCpu,OverrideCooling,FailPower;
        public List<string> Calls=new List<string>();
        public PresetState Read(){if(OverrideCooling)State.Cooling=163;if(FailPower)State.PowerVerified=false;return State;}
        public void ApplyAwcc(int mode){Calls.Add("preset:"+mode);State.Cooling=mode;if(!FailCpu)State.Cpu=mode==161?1:0;}
        public void ApplyOc02(){Calls.Add("cpu:2");if(!FailCpu)State.Cpu=2;}
        public void ApplyCooling(int mode){Calls.Add("cooling:"+mode);State.Cooling=mode;}
        public void Pause(){}
    }
    public static void Run()
    {
        foreach(int mode in new[]{163,160,161}){
            var target=PresetPolicy.Resolve(Request("preset",mode),State(160,0));
            Check(target.Cooling==mode && target.Cpu==(mode==161?1:0),"Main preset mapping");
        }
        foreach(int mode in new[]{160,163}){
            bool denied=false;try{PresetPolicy.Resolve(Request("cpu",1),State(mode,0));}catch(InvalidOperationException){denied=true;}
            Check(denied,"OC01 must be rejected below Performance cooling");
            Check(PresetPolicy.Resolve(Request("cooling",mode),State(161,1)).Cpu==0,"Reducing cooling must force CPU Off");
        }
        Check(PresetPolicy.Resolve(Request("cooling",161),State(160,0)).Cpu==0,"Performance cooling must preserve CPU Off");
        foreach(var request in new[]{Request("cpu",3),Request("preset",0),Request("unknown",161)}){
            var fake=new Fake{State=State(160,0)};var result=PresetTransaction.Execute(request,fake);
            Check(!result.Success && fake.Calls.Count==0,"Invalid requests cannot perform writes");
        }
        foreach(int mode in new[]{160,163}){
            var guarded=new Fake{State=State(mode,0)};
            Check(!PresetTransaction.Execute(Request("cpu",2),guarded).Success && guarded.Calls.Count==0,"OC02 cannot write below Performance cooling");
            Check(PresetPolicy.Resolve(Request("cooling",mode),State(161,2)).Cpu==0,"Leaving P disables OC02");
        }
        foreach(int mode in new[]{163,160,161}){
            var target=PresetPolicy.Resolve(Request("preset",mode),State(161,2));
            Check(target.Cpu==(mode==161?1:0),"Main presets never inherit OC02");
        }
        var ocTwo=new Fake{State=State(161,1)};
        Check(PresetTransaction.Execute(Request("cpu",2),ocTwo).Success && ocTwo.Calls.SequenceEqual(new[]{"cpu:2"}),"OC02 is an explicit independent action");
        Check(ocTwo.State.LinkedPreset==null,"OC02 does not light the linked Performance preset");
        ocTwo.Calls.Clear();
        Check(PresetTransaction.Execute(Request("cooling",161),ocTwo).Success && ocTwo.Calls.Count==0,"P cooling preserves OC02 without reapplying it");
        Check(PresetTransaction.Execute(Request("preset",161),ocTwo).Success && ocTwo.State.Cpu==1,"Main Performance restores OC01 from OC02");
        var ocFailure=new Fake{State=State(161,1),FailCpu=true};
        Check(!PresetTransaction.Execute(Request("cpu",2),ocFailure).Success,"Unchanged OC01 cannot verify as OC02");
        var noRead=new Fake{State=State(160,0)};noRead.State.CpuReadable=false;
        Check(!PresetTransaction.Execute(Request("preset",161),noRead).Success && noRead.Calls.Count==0,"Missing CPU verification rejects before writing");
        var same=new Fake{State=State(161,1)};
        Check(PresetTransaction.Execute(Request("preset",161),same).Success && same.Calls.Count==0,"Already verified targets avoid redundant work");
        var performance=new Fake{State=State(160,0)};
        Check(PresetTransaction.Execute(Request("preset",161),performance).Success,"Full Performance target verifies");
        Check(performance.Calls.SequenceEqual(new[]{"cooling:161","preset:161"}),"Adequate cooling is established before OC");
        var independent=new Fake{State=State(160,0)};
        Check(PresetTransaction.Execute(Request("cooling",161),independent).Success && independent.State.Cpu==0,"Independent Performance cooling keeps stock CPU");
        Check(independent.Calls.SequenceEqual(new[]{"cooling:161"}),"No redundant AWCC preset application for cooling-only change");
        var stock=new Fake{State=State(161,1)};
        Check(PresetTransaction.Execute(Request("cpu",0),stock).Success,"CPU Off with Performance cooling verifies");
        Check(stock.Calls.SequenceEqual(new[]{"preset:160","cooling:161"}) && stock.State.LinkedPreset==null,"Independent CPU Off retains Performance cooling without false linked highlight");
        var fail=new Fake{State=State(161,1),FailCpu=true};
        var failure=PresetTransaction.Execute(Request("cooling",163),fail);
        Check(!failure.Success && fail.State.Cooling==161,"Failed CPU downgrade must retain/restore Performance cooling");
        var overrideMode=new Fake{State=State(160,0),OverrideCooling=true};
        Check(!PresetTransaction.Execute(Request("preset",161),overrideMode).Success,"External cooling override must not verify a full preset");
        var wrongPower=new Fake{State=State(160,0),FailPower=true};
        Check(!PresetTransaction.Execute(Request("preset",161),wrongPower).Success,"Matching OC and fan IDs cannot hide wrong cooling power limits");
        Check(CoolingPowerPolicy.Matches(163,170,253),"Quiet uses the measured 170 W BIOS policy");
        Check(CoolingPowerPolicy.Matches(160,180,253),"Balanced uses the measured 180 W BIOS policy");
        Check(CoolingPowerPolicy.Matches(161,253,253),"Performance cooling uses 253 W even with OC Off");
        Check(!CoolingPowerPolicy.Matches(163,180,253),"Quiet must not be verified against the Off file's 180 W field");
        Check(!CoolingPowerPolicy.Matches(161,180,253),"Performance cooling with the wrong PL1 must fail verification");
        var expected=new OemCpuProfile{Ratio=61,Power=253,ShortPower=253,Current=316,Ecore=44};
        var readings=CpuPresetReader.Controls.ToDictionary(id=>id,id=>57m);
        foreach(uint id in new uint[]{29,30,120,121})readings[id]=61;
        readings[47]=253;readings[48]=253;readings[102]=316;
        readings[34]=0;readings[88]=0;
        for(uint id=1500;id<=1515;id++){readings[id]=44;readings[id+3000]=44;}
        Check(expected.Matches(readings),"CPU verifier recognizes all critical OC01 targets");
        Check(CpuPresetReader.ValidActiveValues(readings),"Positive active settings are usable for classification");
        var emptyRead=CpuPresetReader.Controls.ToDictionary(id=>id,id=>0m);
        Check(!CpuPresetReader.ValidActiveValues(emptyRead),"A zero-filled getter response must not enable hardware controls");
        Check(typeof(IXtuPresetReadOnly).GetMethods().Select(m=>m.Name).OrderBy(x=>x).SequenceEqual(new[]{"GetAllControls","RefreshActiveValuesFromHardwareAccess"}),"The readback client must not expose tuning setters");
        readings[34]=-20;Check(!expected.Matches(readings),"A custom voltage offset must not be presented as the OEM preset");readings[34]=0;
        readings[1500]=45;Check(!expected.Matches(readings),"OC02 E-core settings must not be reported as OC01");readings[1500]=44;
        readings[4500]=45;Check(!expected.Matches(readings),"Mixed E-core count limits cannot verify as OC01");readings[4500]=44;
        var expectedTwo=new OemCpuProfile{Ratio=61,Power=253,ShortPower=253,Current=316,Ecore=45};
        for(uint id=1500;id<=1515;id++){readings[id]=45;readings[id+3000]=45;}
        Check(expectedTwo.Matches(readings) && !expected.Matches(readings),"Both E-core groups distinguish OC02 from OC01");
        for(uint id=1500;id<=1515;id++){readings[id]=44;readings[id+3000]=44;}
        readings[48]=180;Check(expected.Matches(readings),"OC identity is separate from the cooling-owned power limit");
        var json=new System.Web.Script.Serialization.JavaScriptSerializer();
        var encoded=json.Serialize(new PresetResult{Success=true,Actual=State(161,1),Message="Verified · OC01"});
        var decoded=json.Deserialize<PresetResult>(encoded);
        Check(decoded.Success && decoded.Actual.LinkedPreset==161 && decoded.Message=="Verified · OC01","Worker result serialization preserves verified state and text");
        Console.WriteLine("Preset checks passed: "+checks);
    }
}
