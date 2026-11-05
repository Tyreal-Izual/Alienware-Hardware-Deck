using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Web.Script.Serialization;

namespace HardwareDeck
{
    public sealed class Reading
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Kind { get; set; }
        public double? Value { get; set; }
        public double? Maximum { get; set; }
        public string Source { get; set; }
        public int? FanSensorCode { get; set; }
        public string FanTemperatureSensorId { get; set; }
        public string FanAssociationStatus { get; set; }
    }
    public sealed class GpuData
    {
        public string Name { get; set; }
        public double? Temperature { get; set; }
        public double? FanPercent { get; set; }
        public double? Load { get; set; }
        public double? MemoryUsedMiB { get; set; }
        public double? MemoryTotalMiB { get; set; }
        public double? PowerWatts { get; set; }
        public double? PowerLimitWatts { get; set; }
        public double? ClockMHz { get; set; }
        public string Error { get; set; }
    }
    public sealed class Snapshot
    {
        public int SchemaVersion { get; set; }
        public string CapturedAt { get; set; }
        public string ComputerModel { get; set; }
        public string CpuName { get; set; }
        public bool Administrator { get; set; }
        public double? CpuLoad { get; set; }
        public CpuTelemetry Cpu { get; set; }
        public ThermalModeState ThermalMode { get; set; }
        public double? MemoryUsedGiB { get; set; }
        public double? MemoryTotalGiB { get; set; }
        public GpuData Gpu { get; set; }
        public List<Reading> Sensors { get; set; }
        public Reading CpuTemperature { get; set; }
        public string CpuTemperatureSensorId { get; set; }
        public bool CpuTemperatureSourceExplicit { get; set; }
        public string CpuTemperatureSelection { get; set; }
        public string AwccStatus { get; set; }
        public double SampleMilliseconds { get; set; }
    }
    public static class Values
    {
        public static double? Range(double? value, double minimum, double maximum)
        {
            if (!value.HasValue || double.IsNaN(value.Value) || double.IsInfinity(value.Value)
                || value < minimum || value > maximum) return null;
            return value;
        }
    }
    public sealed class SystemReader
    {
        [StructLayout(LayoutKind.Sequential)]
        struct FileTime { public uint Low, High; public ulong Ticks { get { return ((ulong)High << 32) | Low; } } }
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        sealed class MemoryStatus
        {
            public uint Length = (uint)Marshal.SizeOf(typeof(MemoryStatus));
            public uint Load;
            public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatus status);
        ulong? idlePrevious, totalPrevious;
        public void Read(Snapshot snapshot)
        {
            FileTime idle, kernel, user;
            if (GetSystemTimes(out idle, out kernel, out user))
            {
                ulong total = kernel.Ticks + user.Ticks;
                if (totalPrevious.HasValue && total > totalPrevious.Value && idle.Ticks >= idlePrevious.Value)
                {
                    double delta = total - totalPrevious.Value;
                    snapshot.CpuLoad = Values.Range(100.0 * (delta - (idle.Ticks - idlePrevious.Value)) / delta, 0, 100);
                }
                idlePrevious = idle.Ticks; totalPrevious = total;
            }
            var memory = new MemoryStatus();
            if (GlobalMemoryStatusEx(memory))
            {
                snapshot.MemoryTotalGiB = memory.TotalPhysical / 1073741824.0;
                snapshot.MemoryUsedGiB = (memory.TotalPhysical - memory.AvailablePhysical) / 1073741824.0;
            }
        }
    }
    public sealed class AwccReader : IDisposable
    {
        ManagementObject device;
        ManagementBaseObject queryTemplate, fanSensorTemplate;
        readonly Dictionary<int,int?> fanSensorCodes=new Dictionary<int,int?>();
        public const string FanSensorMethod="GetFanSensors";
        public static uint FanSensorArgument(int fanId)
        {
            if(fanId<1 || fanId>255)throw new ArgumentOutOfRangeException("fanId");
            return (uint)(2 | (fanId<<8));
        }
        public static string MatchFanTemperature(int? code,IEnumerable<int> knownTemperatures)
        {
            if(!code.HasValue)return null;
            int id=code.Value;
            if(id>0x100 && id<0x110)id-=0x100;
            if(id<1 || id>15 || !knownTemperatures.Contains(id))return null;
            return "awcc.temp."+id.ToString(CultureInfo.InvariantCulture);
        }
        int? ReadFanSensorCode(int fanId)
        {
            if(fanSensorTemplate==null)return null;
            try {
                using(var input=(ManagementBaseObject)fanSensorTemplate.Clone()){
                    input["arg2"]=FanSensorArgument(fanId);
                    using(var output=device.InvokeMethod(FanSensorMethod,input,new InvokeMethodOptions{Timeout=TimeSpan.FromSeconds(2)})){
                        if(output==null || output["argr"]==null)return null;
                        long raw=Convert.ToInt64(output["argr"],CultureInfo.InvariantCulture);
                        return raw>=0 && raw<int.MaxValue?(int?)raw:null;
                    }
                }
            }catch{return null;}
        }
        readonly List<int> temperatureIds = new List<int>(), fanIds = new List<int>();
        readonly Dictionary<int, double?> fanMaximums = new Dictionary<int, double?>();
        List<ThermalPreset> presets=new List<ThermalPreset>();
        readonly Dictionary<string, string> labels;
        DateTime nextProbe = DateTime.MinValue;
        public string Status { get; private set; }
        public AwccReader(Dictionary<string, string> labels) { this.labels = labels; Status = "等待 AWCC 传感器"; }
        // Read operations are separate from the explicitly requested, allowlisted mode setter.
        public static uint QueryArgument(int command, int index)
        {
            if (command != 3 && command != 4 && command != 5 && command != 9 && command != 11)
                throw new ArgumentOutOfRangeException("command", "Only sensor information queries are allowed.");
            if (index < 0 || index > 255) throw new ArgumentOutOfRangeException("index");
            return (uint)(command | (index << 8));
        }
        int Query(int command, int index)
        {
            using (var input = (ManagementBaseObject)queryTemplate.Clone())
            {
                input["arg2"] = QueryArgument(command, index);
                using (var output = device.InvokeMethod("Thermal_Information", input,
                    new InvokeMethodOptions { Timeout = TimeSpan.FromSeconds(2) }))
                {
                    if (output == null || output["argr"] == null) return -1;
                    return unchecked((int)Convert.ToUInt32(output["argr"], CultureInfo.InvariantCulture));
                }
            }
        }
        void Probe()
        {
            if (device != null || DateTime.UtcNow < nextProbe) return;
            nextProbe = DateTime.UtcNow.AddSeconds(60);
            temperatureIds.Clear(); fanIds.Clear(); fanMaximums.Clear(); fanSensorCodes.Clear();
            try
            {
                var scope = new ManagementScope(@"\\.\root\wmi", new ConnectionOptions { EnablePrivileges = true });
                using (var search = new ManagementObjectSearcher(scope,
                    new ObjectQuery("SELECT * FROM AWCCWmiMethodFunction"),
                    new EnumerationOptions { Timeout = TimeSpan.FromSeconds(3) }))
                using (var instances = search.Get())
                {
                    foreach (ManagementObject item in instances) { device = item; break; }
                }
                if (device == null) { Status = "这台电脑没有可用的 AWCC 传感器接口"; return; }
                queryTemplate = device.GetMethodParameters("Thermal_Information");
                // Upstream routes association opcode 2 to GetFanSensors, not Thermal_Information.
                try{fanSensorTemplate=device.GetMethodParameters(FanSensorMethod);}catch{fanSensorTemplate=null;}
                var seen = new HashSet<int>();
                for (int index = 0; index < 64; index++)
                {
                    int id = Query(3, index);
                    if (id <= 0 || !seen.Add(id)) break;
                    if (id > 0x100 && id < 0x110) temperatureIds.Add(id & 255);
                    else if (id < 0x90) fanIds.Add(id);
                }
                presets=ModeCatalog.Load(seen);
                foreach (int id in fanIds){
                    fanMaximums[id] = Values.Range(Query(9, id), 1, 30000);
                    fanSensorCodes[id]=ReadFanSensorCode(id);
                }
                Status = (fanIds.Count + temperatureIds.Count > 0) ? "AWCC · 只读传感器已连接" : "AWCC 未返回兼容的传感器";
            }
            catch (UnauthorizedAccessException) { Reset("AWCC 需要管理员权限 · 点击“启用传感器”"); }
            catch (ManagementException ex) { Reset(ex.ErrorCode == ManagementStatus.AccessDenied ? "AWCC 需要管理员权限 · 点击“启用传感器”" : "AWCC 暂不可用 · " + ex.ErrorCode); }
            catch (Exception ex) { Reset("AWCC 暂不可用 · " + ex.GetType().Name); }
        }
        void Reset(string message)
        {
            if (queryTemplate != null) queryTemplate.Dispose();
            queryTemplate = null;
            if(fanSensorTemplate!=null)fanSensorTemplate.Dispose();
            fanSensorTemplate=null;
            if (device != null) device.Dispose();
            device = null; Status = message;
        }
        string Label(string id, string fallback) { string value; return labels.TryGetValue(id, out value) ? value : fallback; }
        public List<Reading> Read()
        {
            Probe();
            var readings = new List<Reading>();
            if (device == null) return readings;
            try
            {
                for (int i = 0; i < temperatureIds.Count; i++)
                {
                    int id = temperatureIds[i]; string key = "awcc.temp." + id;
                    readings.Add(new Reading { Id = key, Name = Label(key, "AWCC 温度 " + (i + 1)),
                        Kind = "temperature", Value = Values.Range(Query(4, id), 0, 150), Source = "AWCC WMI" });
                }
                for (int i = 0; i < fanIds.Count; i++)
                {
                    int id = fanIds[i]; string key = "awcc.fan." + id;
                    readings.Add(new Reading { Id = key, Name = Label(key, "风扇 " + (i + 1)),
                        Kind = "fan", Value = Values.Range(Query(5, id), 0, 30000),
                        Maximum = fanMaximums[id], Source = "AWCC WMI",
                        FanSensorCode=fanSensorCodes[id],
                        FanTemperatureSensorId=MatchFanTemperature(fanSensorCodes[id],temperatureIds),
                        FanAssociationStatus=!fanSensorCodes[id].HasValue?"query-unavailable":
                            MatchFanTemperature(fanSensorCodes[id],temperatureIds)!=null?"temperature-id-match":"unmatched-code" });
                }
            }
            catch (Exception ex) { Reset("AWCC 读数暂不可用 · " + ex.GetType().Name); }
            return readings;
        }
        public ThermalModeState ReadMode()
        {
            int? current=null;
            if(device!=null){
                try{int raw=Query(11,0);if(raw>=0 && raw<=255)current=raw;}catch{}
            }
            var preset=presets.FirstOrDefault(p=>p.Id==current);
            return new ThermalModeState{CurrentId=current,
                CurrentName=preset!=null?preset.Name:current==0?"Custom · 自定义":current.HasValue?"未知模式 "+current:"未连接",
                Presets=presets.ToList(),CanChange=device!=null && Collector.IsAdmin && presets.Count>0 && current.HasValue,
                Detail="仅切换固件散热预设；AWCC 游戏/自动配置仍可能覆盖它"};
        }
        public ModeResult ApplyMode(int mode)
        {
            var result=new ModeResult{Requested=mode};
            try{
                if(device==null || !Collector.IsAdmin)throw new InvalidOperationException("需要已连接的 AWCC 传感器和管理员权限");
                uint argument=ModeCatalog.ControlArgument(mode,presets);
                var before=ReadMode();
                if(!before.CurrentId.HasValue)throw new InvalidOperationException("无法读取当前模式，未执行修改");
                if(before.CurrentId==mode)return new ModeResult{Success=true,Requested=mode,Actual=mode,Message="已经是所选散热模式"};
                using(var input=device.GetMethodParameters("Thermal_Control")){
                    input["arg2"]=argument;
                    using(var output=device.InvokeMethod("Thermal_Control",input,new InvokeMethodOptions{Timeout=TimeSpan.FromSeconds(2)})){
                        if(output==null)throw new InvalidOperationException("固件没有确认模式请求");
                    }
                }
                for(int attempt=0;attempt<4;attempt++){
                    Thread.Sleep(150);
                    result.Actual=ReadMode().CurrentId;
                    if(result.Actual==mode){result.Success=true;result.Message="已切换并读回确认："+presets.First(p=>p.Id==mode).Name;return result;}
                }
                result.Message="读回模式与请求不一致；可能被 AWCC 自动配置覆盖。";
            }catch(Exception ex){result.Message="未完成切换："+ex.Message;result.Actual=ReadMode().CurrentId;}
            return result;
        }
        public void Dispose() { if (queryTemplate != null) queryTemplate.Dispose(); if (fanSensorTemplate != null) fanSensorTemplate.Dispose(); if (device != null) device.Dispose(); }
    }
    public sealed class Collector : IDisposable
    {
        readonly SystemReader system = new SystemReader();
        readonly CpuTelemetryReader cpu=new CpuTelemetryReader();
        readonly NvidiaReader nvidia = new NvidiaReader();
        readonly AwccReader awcc;
        readonly SensorConfiguration configuration;
        readonly string cpuName, computerModel;
        static readonly bool administrator=ReadAdministratorStatus();
        static bool ReadAdministratorStatus()
        {
            using(var identity=WindowsIdentity.GetCurrent())
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        public static bool IsAdmin { get { return administrator; } }
        public Collector(string root)
        {
            configuration=SensorConfiguration.Load(Path.Combine(root,"sensor-labels.json"));
            awcc = new AwccReader(configuration.Labels);
            cpuName = Info("Win32_Processor", "Name");
            computerModel = Info("Win32_ComputerSystem", "Model");
        }
        static string Info(string type, string property)
        {
            try {
                using (var search = new ManagementObjectSearcher("SELECT " + property + " FROM " + type))
                using (var rows = search.Get())
                    foreach (ManagementObject item in rows) { using (item) return Convert.ToString(item[property]).Trim(); }
            } catch { }
            return "Unknown";
        }
        public Snapshot Sample()
        {
            var timer = Stopwatch.StartNew();
            var result = new Snapshot { SchemaVersion = 1, CapturedAt = DateTime.UtcNow.ToString("o"),
                CpuName = cpuName, ComputerModel = computerModel, Administrator = IsAdmin };
            system.Read(result);
            result.Cpu=cpu.Read();
            result.Gpu = nvidia.Read();
            result.Sensors = awcc.Read();
            result.CpuTemperature=configuration.SelectCpuTemperature(result.Sensors);
            result.CpuTemperatureSensorId=configuration.CpuTemperatureSensorId ??
                (result.CpuTemperature==null?null:result.CpuTemperature.Id);
            result.CpuTemperatureSourceExplicit=configuration.CpuTemperatureSensorId!=null;
            result.CpuTemperatureSelection=configuration.DescribeSelection(result.CpuTemperature);
            result.AwccStatus = awcc.Status;
            result.ThermalMode=awcc.ReadMode();
            result.SampleMilliseconds = timer.Elapsed.TotalMilliseconds;
            result.CapturedAt = DateTime.UtcNow.ToString("o");
            return result;
        }
        public ModeResult SetThermalMode(int mode) { return awcc.ApplyMode(mode); }
        public void Dispose() { awcc.Dispose(); nvidia.Dispose(); cpu.Dispose(); }
    }
    public static class SnapshotFile
    {
        static readonly int processId=ReadProcessId();
        static int ReadProcessId(){using(var process=Process.GetCurrentProcess())return process.Id;}
        public static string DataDirectory { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HardwareDeck"); } }
        public static void Write(string path, object data)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temporary = path + ".tmp." + processId;
            File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(data), new System.Text.UTF8Encoding(false));
            try { if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
