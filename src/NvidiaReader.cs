using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace HardwareDeck
{
    // One NVML connection for the life of the collector; no helper processes.
    public sealed class NvidiaReader : IDisposable
    {
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
        static extern IntPtr LoadLibraryEx(string file, IntPtr reserved, uint flags);
        [DllImport("kernel32.dll", CharSet=CharSet.Ansi, ExactSpelling=true)]
        static extern IntPtr GetProcAddress(IntPtr module,string name);
        [DllImport("kernel32.dll")] static extern bool FreeLibrary(IntPtr module);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Basic();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Handle(uint index,out IntPtr device);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Name(IntPtr device,StringBuilder value,uint length);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Unsigned(IntPtr device,out uint value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Indexed(IntPtr device,uint index,out uint value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Usage(IntPtr device,out Utilization value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Memory(IntPtr device,out MemoryInfo value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Temperature(IntPtr device,ref TemperatureInfo value);
        [StructLayout(LayoutKind.Sequential)] struct Utilization { public uint Gpu,Memory; }
        [StructLayout(LayoutKind.Sequential)] struct MemoryInfo { public ulong Total,Free,Used; }
        [StructLayout(LayoutKind.Sequential)] struct TemperatureInfo { public uint Version,Sensor;public int Temperature; }
        IntPtr library,device;
        bool initialized;
        DateTime nextTry=DateTime.MinValue;
        string name,error;
        Basic shutdown;
        Unsigned fan,power,powerLimit;
        Indexed temperatureLegacy,clock;
        Temperature temperature;
        Usage utilization;
        Memory memory;
        T Bind<T>(string symbol,bool required=false) where T:class
        {
            var address=GetProcAddress(library,symbol);
            if(address==IntPtr.Zero){
                if(required)throw new InvalidOperationException("NVML 缺少 "+symbol);
                return null;
            }
            return Marshal.GetDelegateForFunctionPointer(address,typeof(T)) as T;
        }
        bool Open()
        {
            if(device!=IntPtr.Zero)return true;
            if(DateTime.UtcNow<nextTry)return false;
            nextTry=DateTime.UtcNow.AddSeconds(60);
            try {
                var path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"nvml.dll");
                if(!File.Exists(path))throw new InvalidOperationException("未找到 NVIDIA NVML 驱动库");
                library=LoadLibraryEx(path,IntPtr.Zero,0x00000800);
                if(library==IntPtr.Zero)throw new InvalidOperationException("无法加载 NVIDIA NVML 驱动库");
                shutdown=Bind<Basic>("nvmlShutdown",true);
                int status=Bind<Basic>("nvmlInit_v2",true)();
                if(status!=0)throw new InvalidOperationException("NVML 初始化失败 ("+status+")");
                initialized=true;
                status=Bind<Handle>("nvmlDeviceGetHandleByIndex_v2",true)(0,out device);
                if(status!=0)throw new InvalidOperationException("NVML 未找到可用显卡 ("+status+")");
                var text=new StringBuilder(96);
                if(Bind<Name>("nvmlDeviceGetName",true)(device,text,96)==0)name=text.ToString();
                temperature=Bind<Temperature>("nvmlDeviceGetTemperatureV");
                temperatureLegacy=Bind<Indexed>("nvmlDeviceGetTemperature");
                utilization=Bind<Usage>("nvmlDeviceGetUtilizationRates");
                memory=Bind<Memory>("nvmlDeviceGetMemoryInfo");
                fan=Bind<Unsigned>("nvmlDeviceGetFanSpeed");
                power=Bind<Unsigned>("nvmlDeviceGetPowerUsage");
                powerLimit=Bind<Unsigned>("nvmlDeviceGetPowerManagementLimit");
                clock=Bind<Indexed>("nvmlDeviceGetClockInfo");
                error=null;return true;
            }catch(Exception ex){error=ex.Message;Close();return false;}
        }
        double? ReadUnsigned(Unsigned getter,double divisor)
        {
            uint value;
            return getter!=null && getter(device,out value)==0 && value!=uint.MaxValue
                ? (double?)value/divisor : null;
        }
        public GpuData Read()
        {
            if(!Open())return new GpuData{Error=error??"NVIDIA NVML 暂不可用"};
            var result=new GpuData{Name=name};
            var thermal=new TemperatureInfo{Version=(uint)(Marshal.SizeOf(typeof(TemperatureInfo)) | (1<<24)),Sensor=0};
            uint value;
            if(temperature!=null && temperature(device,ref thermal)==0)
                result.Temperature=Values.Range(thermal.Temperature,0,150);
            else if(temperatureLegacy!=null && temperatureLegacy(device,0,out value)==0)
                result.Temperature=Values.Range(value,0,150);
            Utilization use;
            if(utilization!=null && utilization(device,out use)==0)result.Load=Values.Range(use.Gpu,0,100);
            MemoryInfo mem;
            if(memory!=null && memory(device,out mem)==0 && mem.Total>0 && mem.Used<=mem.Total){
                result.MemoryUsedMiB=mem.Used/1048576.0;result.MemoryTotalMiB=mem.Total/1048576.0;
            }
            // NVML describes this as intended fan speed; it is not a tachometer RPM.
            result.FanPercent=ReadUnsigned(fan,1);
            result.PowerWatts=ReadUnsigned(power,1000);
            result.PowerLimitWatts=ReadUnsigned(powerLimit,1000);
            if(clock!=null && clock(device,0,out value)==0)result.ClockMHz=value;
            if(!result.Temperature.HasValue && !result.Load.HasValue && !result.MemoryTotalMiB.HasValue){
                result.Error="NVML 驱动暂无有效读数";error=result.Error;Close();nextTry=DateTime.UtcNow.AddSeconds(30);
            }
            return result;
        }
        void Close()
        {
            device=IntPtr.Zero;
            if(initialized && shutdown!=null)shutdown();
            initialized=false;
            if(library!=IntPtr.Zero)FreeLibrary(library);
            library=IntPtr.Zero;
        }
        public void Dispose(){Close();}
    }
}
