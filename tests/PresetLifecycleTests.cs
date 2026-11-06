using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using HardwareDeck;

public static class PresetLifecycleTests
{
    static int checks;
    static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
    sealed class ClosedWriter : TextWriter
    {
        public int Attempts;
        public bool Disposed;
        public override Encoding Encoding {get{return Encoding.UTF8;}}
        public override void WriteLine(string value){Attempts++;if(Disposed)throw new ObjectDisposedException("result pipe");throw new IOException("Pipe closed");}
    }
    sealed class Tuning : IXtuPresetReadOnly
    {
        public int Refreshes;
        public bool Fail;
        public List<XtuControlValue> Values=ValuesFor(false,180);
        public void RefreshActiveValuesFromHardwareAccess(){Refreshes++;if(Fail)throw new IOException("Unavailable");}
        public List<XtuControlValue> GetAllControls(){return Values;}
    }
    static List<XtuControlValue> ValuesFor(bool oc,decimal power)
    {
        var values=CpuPresetReader.Controls.ToDictionary(id=>id,id=>57m);
        foreach(uint id in new uint[]{29,30,120,121})values[id]=oc?61:60;
        values[34]=0;values[88]=0;values[47]=253;values[48]=power;values[102]=oc?316:307;
        for(uint id=1500;id<=1515;id++){values[id]=44;values[id+3000]=44;}
        return values.Select(p=>new XtuControlValue{Id=p.Key,ActiveValue=p.Value}).ToList();
    }
    static OemCpuProfile Profile(bool oc){return new OemCpuProfile{Ratio=oc?61:60,Ecore=44,Current=oc?316:307,Power=oc?253:180,ShortPower=253};}
    public static void Run()
    {
        Check(PresetWorker.ReadInput(new StringReader("{}\r\nignored"))=="{}","Worker reads one Windows-terminated request");
        Check(PresetWorker.ReadInput(new StringReader(new string('x',4096)+"\r\n")).Length==4096,"A 4096-character request fits with CRLF");
        Check(PresetWorker.ReadInput(new StringReader(new string('x',4097)))==null,"Oversized worker input is bounded before deserialization");
        Check(PresetWorker.ReadInput(new StringReader(""))==null,"Closed input must not start a worker operation");
        var writer=new ClosedWriter();PresetWorker.WriteResult(writer,new PresetResult{Success=true});
        Check(writer.Attempts==1,"Closing the dashboard result pipe must not crash its worker");
        writer.Disposed=true;PresetWorker.WriteResult(writer,new PresetResult{Success=true});
        Check(writer.Attempts==2,"Disposed result streams are harmless on shutdown");
        using(var output=new StringWriter()){
            PresetWorker.WriteResult(output,new PresetResult{Success=true,Message="Verified · OC01"});
            Check(output.ToString().Contains("Verified · OC01"),"Normal result text still serializes");
        }
        using(var release=new ManualResetEvent(false))using(var done=new ManualResetEvent(false)){
            bool timedOut=false;
            var result=PresetWorker.RunBackground(delegate{release.WaitOne();done.Set();return new PresetResult{Success=true};},0,()=>timedOut=true);
            Check(timedOut && !result.Success,"A blocked operation reports timeout");
            release.Set();Check(done.WaitOne(5000),"Late test operation finishes without touching hardware");
            Check(!result.Success && result.Message.Contains("timed out"),"Late success cannot overwrite the timeout result");
        }
        Check(PresetWorker.RunBackground(()=>new PresetResult{Success=true},5000,()=>{}).Success,"A timely operation returns its result");
        Check(!PresetWorker.RunBackground(()=>{throw new InvalidOperationException("Failed");},5000,()=>{}).Success,"Operation exceptions become failure results");

        var now=new DateTime(2026,11,6,12,0,0,DateTimeKind.Utc);var tuning=new Tuning();int opens=0;
        using(var reader=new CpuPresetReader(()=>{opens++;return tuning;},()=>now,true,Profile(false),Profile(true),new OemCpuProfile{Ratio=61,Ecore=45,Current=316,Power=253,ShortPower=253})){
            var first=reader.ReadForCooling(160);
            Check(first.Readable && first.Level==0 && first.PowerLimitWatts==180,"Read a verified stock CPU baseline");
            now=now.AddSeconds(1);
            Check(ReferenceEquals(first,reader.ReadForCooling(160)) && tuning.Refreshes==1,"Unchanged cooling uses the ordinary read cache");
            tuning.Values=ValuesFor(true,253);
            var changed=reader.ReadForCooling(161);
            Check(changed.Level==1 && changed.PowerLimitWatts==253 && tuning.Refreshes==2,"A cooling change cannot reuse CPU values from the previous mode");
            Check(opens==1,"Successful reads reuse the connection");
            foreach(var value in tuning.Values)if((value.Id>=1500 && value.Id<=1515)||(value.Id>=4500 && value.Id<=4515))value.ActiveValue=45;
            Check(reader.Read(true).Level==2,"CPU readback classifies all OC02 E-core controls");
            tuning.Values.Single(v=>v.Id==4500).ActiveValue=44;
            Check(reader.Read(true).Level==null,"Partial OC02 application is reported as unknown");
            tuning.Fail=true;now=now.AddSeconds(4);
            Check(!reader.ReadForCooling(161).Readable,"Read failure never keeps a verified CPU claim");
            now=now.AddSeconds(1);int calls=tuning.Refreshes;
            Check(!reader.ReadForCooling(161).Readable && tuning.Refreshes==calls,"Unchanged-mode failures back off");
            tuning.Fail=false;tuning.Values=ValuesFor(false,170);
            Check(reader.ReadForCooling(163).Level==0 && opens==2,"A new cooling mode requests fresh values after recovery");
            tuning.Values=new List<XtuControlValue>{null};
            Check(!reader.Read(true).Readable,"Malformed null controls cannot be verified");
            tuning.Values=ValuesFor(false,170);tuning.Values.Add(tuning.Values[0]);
            Check(!reader.Read(true).Readable,"Duplicate control IDs cannot be verified");
            tuning.Values=ValuesFor(false,170);tuning.Values.RemoveAt(0);
            Check(!reader.Read(true).Readable,"Missing required control IDs cannot be verified");
            reader.Dispose();calls=opens;
            Check(!reader.Read(true).Readable && opens==calls,"A disposed reader never reconnects");
        }
        using(var reader=new CpuPresetReader(()=>{throw new Exception("Must not connect");},()=>now,false,Profile(false),Profile(true),new OemCpuProfile{Ratio=61,Ecore=45,Current=316,Power=253,ShortPower=253}))
            Check(!reader.Read().Readable,"Standard permissions never open a tuning connection");
        Console.WriteLine("Preset lifecycle/readback checks passed: "+checks);
    }
}
