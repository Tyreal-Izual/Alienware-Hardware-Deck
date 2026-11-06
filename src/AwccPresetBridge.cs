using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Automation;

namespace HardwareDeck
{
    internal sealed class AwccPresetBridge : IDisposable
    {
        internal static readonly string Executable=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),@"Alienware\Alienware Command Center\AWCC\AWCC.exe");
        internal static Process OwnedFrontend;
        static readonly object ownedLock=new object();
        static bool stopping;
        static readonly object availabilityLock=new object();
        static DateTime nextAvailabilityCheck;
        static string availabilityIssue;
        internal static string AvailabilityIssue {
            get { lock(availabilityLock){
                if(DateTime.UtcNow>=nextAvailabilityCheck){
                    try{availabilityIssue=!File.Exists(Executable)?"AWCC is not installed":FileVersionInfo.GetVersionInfo(Executable).FileVersion!="6.14.105.0"?
                        "This AWCC version needs a background-control compatibility check":null;}
                    catch{availabilityIssue="AWCC installation could not be verified";}
                    nextAvailabilityCheck=DateTime.UtcNow.AddSeconds(30);
                }
                return availabilityIssue;
            }}
        }
        IntPtr desktop;
        IntPtr job;
        bool hidden;
        Process frontend;
        IntPtr window;
        readonly IntPtr caller;
        readonly int callerProcess;
        internal AwccPresetBridge(IntPtr caller,int callerProcess){this.caller=caller;this.callerProcess=callerProcess;}

        internal void Apply(int preset,Action beforeInvoke)
        {
            if(!PresetPolicy.IsCooling(preset))throw new InvalidOperationException("Unsupported AWCC preset");
            EnsureFrontend();
            string name=PresetPolicy.Name(preset);
            var timer=Stopwatch.StartNew();
            AutomationElement list=null;
            while(timer.ElapsedMilliseconds<40000){
                ThrowIfStopping();
                if(frontend.HasExited)throw new InvalidOperationException("AWCC exited before its preset controls became available");
                window=FindWindow(frontend.Id,desktop);
                if(window!=IntPtr.Zero){
                    try {
                        var root=AutomationElement.FromHandle(window);
                        var lists=root.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.AutomationIdProperty,"PredefinedProfilesList"));
                        if(lists.Count==1){list=lists[0];break;}
                        // Never navigate an existing user-owned AWCC page: it may
                        // contain unsaved edits. Only our isolated frontend is navigated.
                        if(!hidden)throw new InvalidOperationException("AWCC needs its Dashboard or Performance page open");
                        var dashboard=root.FindFirst(TreeScope.Descendants,new PropertyCondition(AutomationElement.AutomationIdProperty,"DASHBOARD"));
                        object selection;
                        if(dashboard!=null && dashboard.TryGetCurrentPattern(SelectionItemPattern.Pattern,out selection))((SelectionItemPattern)selection).Select();
                    }catch(ElementNotAvailableException){} // Splash-to-main-window replacement.
                }
                Thread.Sleep(300);
            }
            if(list==null)throw new InvalidOperationException("AWCC preset controls did not become available in the background");
            if(!hidden){
                object wp;
                var root=AutomationElement.FromHandle(window);
                if(!IsIconic(window)){
                    if(!root.TryGetCurrentPattern(WindowPattern.Pattern,out wp))throw new InvalidOperationException("AWCC cannot be minimized safely");
                    ((WindowPattern)wp).SetWindowVisualState(WindowVisualState.Minimized);
                }
                if(!IsIconic(window))throw new InvalidOperationException("AWCC must stay minimized");
            }
            var items=list.FindAll(TreeScope.Children,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.ListItem));
            var targets=items.Cast<AutomationElement>().Where(x=>x.Current.Name==name).ToArray();
            if(targets.Length!=1 || !targets[0].Current.IsEnabled)throw new InvalidOperationException(name+" is unavailable in AWCC");
            object selected,invoke;
            if(!targets[0].TryGetCurrentPattern(SelectionItemPattern.Pattern,out selected))throw new InvalidOperationException("AWCC selection readback is unavailable");
            ThrowIfStopping();
            if(beforeInvoke!=null)beforeInvoke();
            if(((SelectionItemPattern)selected).Current.IsSelected)return; // Hardware verifier still decides success.
            if(!targets[0].TryGetCurrentPattern(InvokePattern.Pattern,out invoke))throw new InvalidOperationException("AWCC's preset click action is unavailable");
            ThrowIfStopping();
            try{((InvokePattern)invoke).Invoke();}
            finally{RestoreCallerFocus();}
            if(!hidden && !IsIconic(window))throw new InvalidOperationException("AWCC unexpectedly restored its window");
        }
        void EnsureFrontend()
        {
            ThrowIfStopping();
            if(frontend!=null && !frontend.HasExited)return;
            if(!File.Exists(Executable))throw new InvalidOperationException("AWCC is not installed");
            if(FileVersionInfo.GetVersionInfo(Executable).FileVersion!="6.14.105.0")throw new InvalidOperationException("This AWCC version needs a background-control compatibility check");
            var candidates=new List<Process>();
            foreach(var process in Process.GetProcessesByName("AWCC")){
                try{
                    if(string.Equals(process.MainModule.FileName,Executable,StringComparison.OrdinalIgnoreCase))candidates.Add(process);
                    else process.Dispose();
                }catch{process.Dispose();throw new InvalidOperationException("Cannot verify the running AWCC process");}
            }
            if(candidates.Count>1){foreach(var p in candidates)p.Dispose();throw new InvalidOperationException("Multiple AWCC frontends are running");}
            if(candidates.Count==1){
                frontend=candidates[0];
                if(FindWindow(frontend.Id,IntPtr.Zero)==IntPtr.Zero)throw new InvalidOperationException("AWCC is starting or is on another desktop; retry shortly");
                return;
            }
            // Windows desktop isolation prevents splash, main, and modal windows
            // from appearing on the user's input desktop, including cold startup.
            // This does not change the input desktop and grants no extra privileges.
            ThrowIfStopping();
            string name="HardwareDeck-AWCC-"+Guid.NewGuid().ToString("N");
            desktop=CreateDesktop(name,IntPtr.Zero,IntPtr.Zero,0,0xC3,IntPtr.Zero);
            if(desktop==IntPtr.Zero)throw new Win32Exception(Marshal.GetLastWin32Error(),"Cannot create AWCC background desktop");
            if(!SetThreadDesktop(desktop))throw new Win32Exception(Marshal.GetLastWin32Error(),"Cannot bind the AWCC worker desktop");
            hidden=true;
            job=CreateJobObject(IntPtr.Zero,null);
            var limits=new JobLimits();limits.Basic.LimitFlags=0x2000; // KILL_ON_JOB_CLOSE
            if(job==IntPtr.Zero || !SetInformationJobObject(job,9,ref limits,Marshal.SizeOf(typeof(JobLimits))))
                throw new Win32Exception(Marshal.GetLastWin32Error(),"Cannot isolate the temporary AWCC process lifetime");
            var start=new StartupInfo{cb=Marshal.SizeOf(typeof(StartupInfo)),lpDesktop=name};
            ProcessInformation info;
            ThrowIfStopping();
            if(!CreateProcess(Executable,null,IntPtr.Zero,IntPtr.Zero,false,4,IntPtr.Zero,Path.GetDirectoryName(Executable),ref start,out info))
                throw new Win32Exception(Marshal.GetLastWin32Error(),"AWCC background startup failed");
            try{
                frontend=Process.GetProcessById(info.dwProcessId);
                if(frontend.Handle==IntPtr.Zero)throw new InvalidOperationException("Cannot retain the temporary frontend process handle");
                lock(ownedLock){ThrowIfStopping();OwnedFrontend=frontend;}
                if(!AssignProcessToJobObject(job,info.hProcess))throw new Win32Exception(Marshal.GetLastWin32Error(),"Cannot track the temporary AWCC process");
                ThrowIfStopping();
                if(ResumeThread(info.hThread)==uint.MaxValue)throw new Win32Exception(Marshal.GetLastWin32Error(),"Cannot start the AWCC background frontend");
            }catch{
                // Even failures before managed Process/job ownership must terminate
                // the suspended process through the exact handle we created.
                TerminateProcess(info.hProcess,1);throw;
            }finally{CloseHandle(info.hThread);CloseHandle(info.hProcess);}
        }
        void RestoreCallerFocus()
        {
            if(hidden || caller==IntPtr.Zero || !IsWindowVisible(caller) || IsIconic(caller))return;
            uint owner;GetWindowThreadProcessId(GetForegroundWindow(),out owner);
            uint callerOwner;GetWindowThreadProcessId(caller,out callerOwner);
            if(callerOwner==callerProcess && frontend!=null && owner==frontend.Id){try{AutomationElement.FromHandle(caller).SetFocus();}catch{}}
        }
        internal static void StopOwnedFrontend()
        {
            Process owned;
            lock(ownedLock){stopping=true;owned=OwnedFrontend;OwnedFrontend=null;}
            if(owned!=null){try{if(!owned.HasExited)owned.Kill();}catch{} }
        }
        internal static void ThrowIfStopping(){lock(ownedLock){if(stopping)throw new OperationCanceledException("AWCC background operation stopped");}}
        public void Dispose()
        {
            RestoreCallerFocus();
            // Only the transient frontend started by this worker is closed. The
            // Dell services and any existing user-owned frontend are retained.
            StopOwnedFrontend();if(frontend!=null)frontend.Dispose();
            if(job!=IntPtr.Zero){CloseHandle(job);job=IntPtr.Zero;}
            if(desktop!=IntPtr.Zero){CloseDesktop(desktop);desktop=IntPtr.Zero;}
        }
        static IntPtr FindWindow(int pid,IntPtr desktop)
        {
            var matches=new List<IntPtr>();
            EnumWindow callback=delegate(IntPtr handle,IntPtr unused){
                uint owner;GetWindowThreadProcessId(handle,out owner);
                if(owner==pid){var text=new StringBuilder(256);GetWindowText(handle,text,text.Capacity);if(text.ToString()=="Alienware Command Center")matches.Add(handle);}
                return true;
            };
            if(desktop==IntPtr.Zero)EnumWindows(callback,IntPtr.Zero);else EnumDesktopWindows(desktop,callback,IntPtr.Zero);
            return matches.Count==1?matches[0]:IntPtr.Zero;
        }
        delegate bool EnumWindow(IntPtr handle,IntPtr parameter);
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct StartupInfo {
            public int cb;public string lpReserved,lpDesktop,lpTitle;public int dwX,dwY,dwXSize,dwYSize,dwXCountChars,dwYCountChars,dwFillAttribute,dwFlags;
            public short wShowWindow,cbReserved2;public IntPtr lpReserved2,hStdInput,hStdOutput,hStdError;
        }
        [StructLayout(LayoutKind.Sequential)] struct ProcessInformation{public IntPtr hProcess,hThread;public int dwProcessId,dwThreadId;}
        [StructLayout(LayoutKind.Sequential)] struct BasicLimits{public long ProcessTime,JobTime;public uint LimitFlags;public UIntPtr MinWorking,MaxWorking;public uint ActiveLimit;public UIntPtr Affinity;public uint Priority,Scheduling;}
        [StructLayout(LayoutKind.Sequential)] struct IoCounters{public ulong ReadOperations,WriteOperations,OtherOperations,ReadBytes,WriteBytes,OtherBytes;}
        [StructLayout(LayoutKind.Sequential)] struct JobLimits{public BasicLimits Basic;public IoCounters Io;public UIntPtr ProcessMemory,JobMemory,PeakProcessMemory,PeakJobMemory;}
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr CreateJobObject(IntPtr security,string name);
        [DllImport("kernel32.dll",SetLastError=true)]static extern bool SetInformationJobObject(IntPtr job,int info,ref JobLimits limits,int size);
        [DllImport("kernel32.dll",SetLastError=true)]static extern bool AssignProcessToJobObject(IntPtr job,IntPtr process);
        [DllImport("kernel32.dll",SetLastError=true)]static extern uint ResumeThread(IntPtr thread);
        [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr CreateDesktop(string name,IntPtr device,IntPtr devmode,int flags,uint access,IntPtr security);
        [DllImport("user32.dll",SetLastError=true)] static extern bool SetThreadDesktop(IntPtr desktop);
        [DllImport("user32.dll")] static extern bool CloseDesktop(IntPtr desktop);
        [DllImport("user32.dll")] static extern bool EnumDesktopWindows(IntPtr desktop,EnumWindow callback,IntPtr parameter);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindow callback,IntPtr parameter);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr window,StringBuilder text,int length);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool CreateProcess(string file,StringBuilder command,IntPtr processSecurity,IntPtr threadSecurity,bool inherit,int flags,IntPtr environment,string directory,ref StartupInfo start,out ProcessInformation info);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll",SetLastError=true)] static extern bool TerminateProcess(IntPtr process,uint exitCode);
    }
    internal sealed class LocalPresetBackend : IPresetBackend,IDisposable
    {
        readonly CpuPresetReader cpu=new CpuPresetReader();
        readonly AwccReader thermal=new AwccReader(new Dictionary<string,string>());
        readonly AwccPresetBridge bridge;
        internal LocalPresetBackend(IntPtr caller,int callerProcess){bridge=new AwccPresetBridge(caller,callerProcess);thermal.Read();}
        public PresetState Read(){return PresetStateFactory.Create(cpu.Read(true),thermal.ReadMode());}
        public void ApplyAwcc(int preset){bridge.Apply(preset,delegate{if(preset==161 && thermal.ReadMode().CurrentId!=161)ApplyCooling(161);});}
        public void ApplyCooling(int preset){AwccPresetBridge.ThrowIfStopping();var r=thermal.ApplyMode(preset);if(!r.Success)throw new InvalidOperationException(r.Message);}
        public void Pause(){Thread.Sleep(300);}
        public void Dispose(){bridge.Dispose();cpu.Dispose();thermal.Dispose();}
    }
    internal static class PresetStateFactory
    {
        internal static PresetState Create(CpuPresetReading cpu,ThermalModeState thermal)
        {
            string unavailable=AwccPresetBridge.AvailabilityIssue;
            bool power=cpu.Readable && CoolingPowerPolicy.Matches(thermal.CurrentId,cpu.PowerLimitWatts,cpu.ShortPowerLimitWatts);
            return new PresetState{Cooling=thermal.CurrentId,Cpu=cpu.Level,CpuReadable=cpu.Readable,CapturedAt=cpu.CapturedAt,
                PowerLimitWatts=cpu.PowerLimitWatts,ShortPowerLimitWatts=cpu.ShortPowerLimitWatts,PowerVerified=power,
                CanChange=cpu.Readable && thermal.CanChange && unavailable==null,
                Detail=!cpu.Readable?cpu.Detail:!thermal.CanChange?"Cooling readback unavailable · "+thermal.Detail:unavailable??
                    (!power?"Cooling power limits do not match the audited AWCC profile":cpu.Detail+" · Cooling power limits verified")};
        }
    }
    internal sealed class PresetWorkerRequest
    {
        public PresetRequest Request{get;set;}
        public long CallerWindow{get;set;}
        public int CallerProcess{get;set;}
    }
    internal static class PresetWorker
    {
        internal static string ReadInput(TextReader reader)
        {
            var input=new StringBuilder();
            for(int count=0;count<=4096;count++){
                int c=reader.Read();
                if(c<0 || c=='\n' || c=='\r'){
                    if(c=='\r' && reader.Peek()=='\n')reader.Read();
                    return input.Length==0?null:input.ToString();
                }
                if(count==4096)return null;
                input.Append((char)c);
            }
            return null;
        }
        internal static void WriteResult(TextWriter writer,PresetResult result)
        {
            try{writer.WriteLine(new JavaScriptSerializer().Serialize(result));writer.Flush();}
            catch(IOException){ } // Closing the dashboard closes the result pipe.
            catch(ObjectDisposedException){ }
        }
        internal static PresetResult RunBackground(Func<PresetResult> operation,int timeout,Action onTimeout)
        {
            PresetResult workerResult=null;
            var thread=new Thread(delegate(){
                try{workerResult=operation();}
                catch(Exception e){workerResult=new PresetResult{Message=e.Message};}
            }){IsBackground=true};
            thread.SetApartmentState(ApartmentState.MTA);thread.Start();
            if(thread.Join(timeout))return workerResult??new PresetResult{Message="No preset result received"};
            onTimeout();
            // Never share this timeout object with the still-finishing worker.
            return new PresetResult{Message="AWCC background operation timed out; verify current CPU and cooling before retrying"};
        }
        internal static int Run()
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput(),new UTF8Encoding(false)){AutoFlush=true});
            Console.SetIn(new StreamReader(Console.OpenStandardInput(),Encoding.UTF8));
            PresetResult result=null;
            string input;
            try{input=ReadInput(Console.In);}catch(IOException){return 2;}
            if(input==null)return 2;
            PresetWorkerRequest request;
            try{request=new JavaScriptSerializer().Deserialize<PresetWorkerRequest>(input);if(request==null || request.Request==null)throw new InvalidOperationException();}
            catch{WriteResult(Console.Out,new PresetResult{Message="Invalid preset worker request"});return 2;}
            bool acquired=false;
            using(var gate=new Mutex(false,@"Local\HardwareDeck.AwccPresetSwitch")){
                try{try{acquired=gate.WaitOne(0);}catch(AbandonedMutexException){acquired=true;}
                    if(!acquired)throw new InvalidOperationException("Another preset change is in progress");
                    result=RunBackground(delegate{
                        using(var backend=new LocalPresetBackend(new IntPtr(request.CallerWindow),request.CallerProcess))return PresetTransaction.Execute(request.Request,backend);
                    },85000,AwccPresetBridge.StopOwnedFrontend);
                }catch(Exception e){result=new PresetResult{Message=e.Message};}
                finally{if(acquired)gate.ReleaseMutex();}
            }
            WriteResult(Console.Out,result);return result!=null && result.Success?0:1;
        }
        internal static PresetResult Execute(PresetRequest request,IntPtr caller)
        {
            using(var process=new Process()){
                process.StartInfo=new ProcessStartInfo{FileName=System.Reflection.Assembly.GetExecutingAssembly().Location,Arguments="--preset-worker",
                    UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,
                    StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
                process.Start();
                var output=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();
                int callerProcess;using(var current=Process.GetCurrentProcess())callerProcess=current.Id;
                process.StandardInput.WriteLine(new JavaScriptSerializer().Serialize(new PresetWorkerRequest{Request=request,CallerWindow=caller.ToInt64(),CallerProcess=callerProcess}));process.StandardInput.Close();
                if(!process.WaitForExit(95000)){try{process.Kill();}catch{}return new PresetResult{Message="Preset worker did not complete; current state needs verification"};}
                try{return new JavaScriptSerializer().Deserialize<PresetResult>(output.Result)??new PresetResult{Message="No preset result received"};}
                catch{return new PresetResult{Message="Preset worker could not report a verified result"};}
            }
        }
    }
}
