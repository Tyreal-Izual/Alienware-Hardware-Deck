using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.ServiceModel;
using System.Runtime.Serialization;
using System.Threading;
using System.Web.Script.Serialization;

namespace HardwareDeck
{
    [ServiceContract(Name="IXtuTuning", Namespace="http://tempuri.org/")]
    public interface IXtuPresetReadOnly
    {
        [OperationContract] List<XtuControlValue> GetAllControls();
        // The installed service reads hardware and refreshes its cached values;
        // this operation never proposes or applies tuning settings.
        [OperationContract] void RefreshActiveValuesFromHardwareAccess();
    }
    [DataContract(Name="XtuTuningControl",Namespace="http://schemas.datacontract.org/2004/07/IronCity.Core")]
    public sealed class XtuControlValue
    {
        [DataMember] public decimal ActiveValue { get; set; }
        [DataMember] public uint Id { get; set; }
    }
    public sealed class CpuPresetReading
    {
        public int? Level { get; set; }
        public bool Readable { get; set; }
        public string Detail { get; set; }
        public string CapturedAt { get; set; }
        public decimal? PowerLimitWatts { get; set; }
        public decimal? ShortPowerLimitWatts { get; set; }
        [ScriptIgnore] public Dictionary<string,decimal> CheckedValues { get; set; }
    }
    internal sealed class OemCpuProfile
    {
        internal decimal Ratio, Power, ShortPower, Current, Ecore;
        internal static OemCpuProfile Parse(Dictionary<string,object> profile)
        {
            var cpu=(Dictionary<string,object>)profile["cpu"];
            var controls=((object[])profile["xtu"]).Cast<Dictionary<string,object>>().ToDictionary(x=>Convert.ToInt32(x["id"]),x=>Convert.ToDecimal(x["value"],CultureInfo.InvariantCulture));
            return new OemCpuProfile{Ratio=Convert.ToDecimal(cpu["frequency"],CultureInfo.InvariantCulture)*10,
                Power=controls[48],ShortPower=controls[47],Current=controls[102],Ecore=controls[1500]};
        }
        internal bool Matches(IDictionary<uint,decimal> values)
        {
            // BIOS cooling profiles own the live PL1 value. OC fingerprints use
            // clocks, current and voltage; power is checked against cooling below.
            if(values[29]!=Ratio || values[30]!=Ratio || values[102]!=Current || values[34]!=0 || values[88]!=0)return false;
            for(uint id=1500;id<=1515;id++)if(values[id]!=Ecore)return false;
            // The captured R16/14900KF OEM profile uses two favored P cores; read
            // all eight instead of assuming the favored core numbers are constant.
            var cores=Enumerable.Range(116,8).Select(id=>values[(uint)id]).ToArray();
            return cores.Count(v=>v==Ratio)==2 && cores.Count(v=>v==57)==6 &&
                new uint[]{31,32,42,43,96,97}.All(id=>values[id]==57);
        }
    }
    public sealed class CpuPresetReader : IDisposable
    {
        internal static readonly string AwccBase=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),@"Alienware\Alienware Command Center\Subsystems\Performance\SubAgent\SCSubAgent");
        internal static readonly uint[] Controls=new uint[]{29,30,31,32,34,42,43,47,48,88,96,97,102}
            .Concat(Enumerable.Range(116,8).Select(x=>(uint)x)).Concat(Enumerable.Range(1500,16).Select(x=>(uint)x)).ToArray();
        ChannelFactory<IXtuPresetReadOnly> factory;
        IXtuPresetReadOnly channel;
        OemCpuProfile off,on;
        CpuPresetReading last;
        readonly Func<IXtuPresetReadOnly> open;
        readonly Func<DateTime> utcNow;
        readonly bool administrator;
        bool disposed,hasCooling;
        int? lastCooling;
        DateTime nextRead=DateTime.MinValue;
        static int revision;
        int observedRevision=-1;
        internal static void InvalidateAll(){Interlocked.Increment(ref revision);}
        public CpuPresetReader(){open=Open;utcNow=()=>DateTime.UtcNow;administrator=Collector.IsAdmin;}
        internal CpuPresetReader(Func<IXtuPresetReadOnly> open,Func<DateTime> utcNow,bool administrator,OemCpuProfile off,OemCpuProfile on)
        {this.open=open;this.utcNow=utcNow;this.administrator=administrator;this.off=off;this.on=on;}
        IXtuPresetReadOnly Open()
        {
            var timeout=TimeSpan.FromMilliseconds(650);
            var binding=new NetNamedPipeBinding(NetNamedPipeSecurityMode.Transport){
                OpenTimeout=timeout,SendTimeout=timeout,ReceiveTimeout=timeout,CloseTimeout=timeout,MaxReceivedMessageSize=8388608,MaxBufferSize=8388608};
            binding.ReaderQuotas.MaxArrayLength=65536;
            factory=new ChannelFactory<IXtuPresetReadOnly>(binding,new EndpointAddress("net.pipe://localhost/ExtremeTuningUtility/Tuning"));
            return factory.CreateChannel();
        }
        internal CpuPresetReading ReadForCooling(int? cooling)
        {
            bool changed=!hasCooling || lastCooling!=cooling;
            hasCooling=true;lastCooling=cooling;
            return Read(changed);
        }
        public CpuPresetReading Read(bool force=false)
        {
            if(disposed)return new CpuPresetReading{CapturedAt=utcNow().ToString("o"),Detail="CPU readback is closed"};
            int currentRevision=Thread.VolatileRead(ref revision);
            if(currentRevision!=observedRevision){force=true;observedRevision=currentRevision;}
            if(!force && last!=null && utcNow()<nextRead)return last;
            var result=new CpuPresetReading{CapturedAt=utcNow().ToString("o")};
            try {
                if(!administrator)throw new InvalidOperationException("Enable sensors to verify CPU presets");
                if(off==null)LoadProfiles();
                if(channel==null)channel=open();
                channel.RefreshActiveValuesFromHardwareAccess();
                var all=channel.GetAllControls();
                if(all==null || all.Count==0 || all.Any(c=>c==null) || all.Select(c=>c.Id).Distinct().Count()!=all.Count)throw new InvalidOperationException("XTU returned an invalid control list");
                var available=all.ToDictionary(c=>c.Id);
                var values=new Dictionary<uint,decimal>();
                foreach(uint id in Controls){
                    XtuControlValue control;
                    if(!available.TryGetValue(id,out control))throw new InvalidOperationException("Required CPU readback controls are unavailable");
                    if(control==null || control.Id!=id)throw new InvalidOperationException("XTU returned an invalid control identity");
                    values[id]=control.ActiveValue;
                }
                result.CheckedValues=values.ToDictionary(p=>p.Key.ToString(CultureInfo.InvariantCulture),p=>p.Value);
                if(!ValidActiveValues(values))throw new InvalidOperationException("XTU returned unavailable active settings");
                result.PowerLimitWatts=values[48];result.ShortPowerLimitWatts=values[47];
                result.Readable=true;
                result.Level=off.Matches(values)?(int?)0:on.Matches(values)?(int?)1:null;
                result.Detail=result.Level.HasValue?"XTU readback: P/E-core ratios, IccMax and voltage mode/offset match OEM "+(result.Level==0?"Off":"OC01"):
                    "CPU targets do not match OEM Off or OC01; select a main preset to reset them";
                nextRead=utcNow().AddSeconds(3);
            }catch(Exception error){result.Readable=false;result.Level=null;result.Detail="CPU verification unavailable: "+error.Message;Release();nextRead=utcNow().AddSeconds(10);}
            last=result;return result;
        }
        void LoadProfiles()
        {
            var data=new JavaScriptSerializer().DeserializeObject(File.ReadAllText(Path.Combine(AwccBase,@"OCControlService\OCControl.Service.Overclocking.Data\Config.json"))) as Dictionary<string,object>;
            if(data==null || Convert.ToString(data["platform"])!="Alienware Aurora R16" || Convert.ToString(data["processor"])!="Intel(R) Core(TM) i9-14900KF")
                throw new InvalidOperationException("This CPU preset verifier supports the audited R16 / i9-14900KF configuration");
            var profiles=((object[])data["predefined_profiles"]).Cast<Dictionary<string,object>>().ToArray();
            var stock=OemCpuProfile.Parse(profiles.Single(x=>Convert.ToString(x["name"])=="Off"));
            var oc=OemCpuProfile.Parse(profiles.Single(x=>Convert.ToString(x["name"])=="Overclock 01"));
            if(stock.Ratio!=60 || oc.Ratio!=61 || stock.Ecore!=44 || oc.Ecore!=44 || stock.Power!=180 || oc.Power!=253 || stock.ShortPower!=253 || oc.ShortPower!=253)
                throw new InvalidOperationException("OEM profile layout changed; verification needs review");
            off=stock;on=oc;
        }
        internal static bool ValidActiveValues(IDictionary<uint,decimal> values)
        {
            return Controls.All(id=>values.ContainsKey(id) && (id==34 || id==88 || values[id]>0));
        }
        void Release(){var connection=channel as ICommunicationObject;if(connection!=null)connection.Abort();channel=null;if(factory!=null)factory.Abort();factory=null;}
        public void Dispose(){disposed=true;Release();}
    }
}
