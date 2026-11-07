using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Automation;

namespace HardwareDeck
{
    internal sealed class AwccPresetBridge : IDisposable
    {
        internal static readonly string Executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Alienware\Alienware Command Center\AWCC\AWCC.exe");
        internal static Process OwnedFrontend;
        static readonly object ownedLock = new object ();
        static bool stopping;
        static readonly object availabilityLock = new object ();
        static DateTime nextAvailabilityCheck;
        static string availabilityIssue;
        internal static string AvailabilityIssue
        {
            get
            {
                lock (availabilityLock)
                {
                    if (DateTime.UtcNow >= nextAvailabilityCheck)
                    {
                        try
                        {
                            availabilityIssue = !File.Exists(Executable) ? "AWCC is not installed" : FileVersionInfo.GetVersionInfo(Executable).FileVersion != "6.14.105.0" ? "This AWCC version needs a background-control compatibility check" : null;
                        }
                        catch
                        {
                            availabilityIssue = "AWCC installation could not be verified";
                        }

                        nextAvailabilityCheck = DateTime.UtcNow.AddSeconds(30);
                    }

                    return availabilityIssue;
                }
            }
        }

        IntPtr desktop;
        IntPtr job;
        bool hidden;
        Process frontend;
        IntPtr window;
        readonly IntPtr caller;
        readonly int callerProcess;
        internal AwccPresetBridge(IntPtr caller, int callerProcess)
        {
            this.caller = caller;
            this.callerProcess = callerProcess;
        }

        internal void Apply(int preset, Action beforeInvoke)
        {
            if (!PresetPolicy.IsCooling(preset))
                throw new InvalidOperationException("Unsupported AWCC preset");
            ApplyNamed(PresetPolicy.Name(preset), beforeInvoke);
        }

        void ApplyNamed(string name, Action beforeInvoke)
        {
            EnsureFrontend();
            var timer = Stopwatch.StartNew();
            AutomationElement list = null;
            while (timer.ElapsedMilliseconds < 40000)
            {
                ThrowIfStopping();
                if (frontend.HasExited)
                    throw new InvalidOperationException("AWCC exited before its preset controls became available");
                window = FindWindow(frontend.Id, desktop);
                if (window != IntPtr.Zero)
                {
                    try
                    {
                        var root = AutomationElement.FromHandle(window);
                        var lists = root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "PredefinedProfilesList"));
                        if (lists.Count == 1)
                        {
                            list = lists[0];
                            break;
                        }

                        // Never navigate an existing user-owned AWCC page: it may
                        // contain unsaved edits. Only our isolated frontend is navigated.
                        if (!hidden)
                            throw new InvalidOperationException("AWCC needs its Dashboard or Performance page open");
                        var dashboard = root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "DASHBOARD"));
                        object selection;
                        if (dashboard != null && dashboard.TryGetCurrentPattern(SelectionItemPattern.Pattern, out selection))
                            ((SelectionItemPattern)selection).Select();
                    }
                    catch (ElementNotAvailableException)
                    {
                    } // Splash-to-main-window replacement.
                }

                Thread.Sleep(300);
            }

            if (list == null)
                throw new InvalidOperationException("AWCC preset controls did not become available in the background");
            if (!hidden)
            {
                object wp;
                var root = AutomationElement.FromHandle(window);
                if (!IsIconic(window))
                {
                    if (!root.TryGetCurrentPattern(WindowPattern.Pattern, out wp))
                        throw new InvalidOperationException("AWCC cannot be minimized safely");
                    ((WindowPattern)wp).SetWindowVisualState(WindowVisualState.Minimized);
                }

                if (!IsIconic(window))
                    throw new InvalidOperationException("AWCC must stay minimized");
            }

            var items = list.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem));
            var targets = items.Cast<AutomationElement>().Where(x => x.Current.Name == name).ToArray();
            if (targets.Length != 1 || !targets[0].Current.IsEnabled)
                throw new InvalidOperationException(name + " is unavailable in AWCC");
            object selected, invoke;
            if (!targets[0].TryGetCurrentPattern(SelectionItemPattern.Pattern, out selected))
                throw new InvalidOperationException("AWCC selection readback is unavailable");
            ThrowIfStopping();
            if (beforeInvoke != null)
                beforeInvoke();
            if (((SelectionItemPattern)selected).Current.IsSelected)
                return; // Hardware verifier still decides success.
            if (!targets[0].TryGetCurrentPattern(InvokePattern.Pattern, out invoke))
                throw new InvalidOperationException("AWCC's preset click action is unavailable");
            ThrowIfStopping();
            try
            {
                ((InvokePattern)invoke).Invoke();
            }
            finally
            {
                RestoreCallerFocus();
            }

            if (!hidden && !IsIconic(window))
                throw new InvalidOperationException("AWCC unexpectedly restored its window");
        }

        // OC02 is only exposed by AWCC's native Custom CPU editor. The private
        // frontend avoids touching a user's unsaved settings or showing popups.
        internal void ApplyOc02(Action requirePerformance, Action settlePerformance)
        {
            EnsureFrontend();
            if (!hidden)
                throw new InvalidOperationException("Close AWCC before selecting OC02 so its CPU editor can run in the background");
            AutomationElement root = null;
            WaitForUi(delegate
            {
                window = FindWindow(frontend.Id, desktop);
                if (window == IntPtr.Zero)
                    return false;
                root = AutomationElement.FromHandle(window);
                var menu = Find(root, AutomationElement.AutomationIdProperty, "PERFORMANCE");
                if (menu == null)
                    return false;
                ((SelectionItemPattern)menu.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
                return true;
            }, 40000, "AWCC Performance page is unavailable");
            AutomationElement toggle = null;
            ToggleState original = ToggleState.Indeterminate;
            try
            {
                WaitForUi(delegate
                {
                    toggle = Find(root, AutomationElement.AutomationIdProperty, "_performanceThermalToggle");
                    return toggle != null;
                }, 10000, "AWCC performance view is unavailable");
                var view = (TogglePattern)toggle.GetCurrentPattern(TogglePattern.Pattern);
                original = view.Current.ToggleState;
                if (toggle.Current.Name == "PERFORMANCE")
                    view.Toggle();
                else if (toggle.Current.Name != "THERMAL")
                    throw new InvalidOperationException("AWCC performance view changed");
                ApplyNamed("Custom", requirePerformance);
                AutomationElement cpu = null, advanced = null;
                WaitForUi(delegate
                {
                    cpu = root.FindFirst(TreeScope.Descendants, new AndCondition(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Group), new PropertyCondition(AutomationElement.NameProperty, "Cpu Overview")));
                    advanced = cpu == null ? null : Find(cpu, AutomationElement.AutomationIdProperty, "AdvancedViewButton");
                    return advanced != null && advanced.Current.IsEnabled;
                }, 10000, "AWCC CPU editor is unavailable");
                Invoke(advanced);
                settlePerformance();
                AutomationElement combo = null;
                WaitForUi(delegate
                {
                    combo = Find(cpu, AutomationElement.NameProperty, "Custom Presets");
                    return combo != null && combo.Current.IsEnabled;
                }, 5000, "AWCC CPU preset selector is unavailable");
                var expand = (ExpandCollapsePattern)combo.GetCurrentPattern(ExpandCollapsePattern.Pattern);
                expand.Expand();
                AutomationElement choice = null;
                WaitForUi(delegate
                {
                    choice = Find(combo, AutomationElement.NameProperty, "OC2");
                    return choice != null && choice.Current.IsEnabled;
                }, 5000, "AWCC does not expose the OC2 preset");
                ((SelectionItemPattern)choice.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
                expand.Collapse();
                AutomationElement save = null;
                WaitForUi(delegate
                {
                    save = Find(cpu, AutomationElement.AutomationIdProperty, "_saveButton");
                    return save != null && save.Current.Name == "SAVE";
                }, 5000, "AWCC did not accept the predefined OC2 selection");
                requirePerformance();
                // A previously saved OC2 may already be active after entering
                // Custom; final hardware readback still determines success.
                if (save.Current.IsEnabled)
                    Invoke(save);
            }
            finally
            {
                try
                {
                    if (toggle != null && original != ToggleState.Indeterminate)
                    {
                        var p = (TogglePattern)toggle.GetCurrentPattern(TogglePattern.Pattern);
                        if (p.Current.ToggleState != original)
                            p.Toggle();
                    }
                }
                catch
                {
                }
            }
        }

        static AutomationElement Find(AutomationElement root, AutomationProperty property, string value)
        {
            return root.FindFirst(TreeScope.Descendants, new PropertyCondition(property, value));
        }

        static void Invoke(AutomationElement element)
        {
            ThrowIfStopping();
            ((InvokePattern)element.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
        }

        static void WaitForUi(Func<bool> ready, int timeout, string failure)
        {
            var timer = Stopwatch.StartNew();
            do
            {
                ThrowIfStopping();
                try
                {
                    if (ready())
                        return;
                }
                catch (ElementNotAvailableException)
                {
                }

                Thread.Sleep(150);
            }
            while (timer.ElapsedMilliseconds < timeout);
            throw new InvalidOperationException(failure);
        }

        void EnsureFrontend()
        {
            ThrowIfStopping();
            if (frontend != null && !frontend.HasExited)
                return;
            if (!File.Exists(Executable))
                throw new InvalidOperationException("AWCC is not installed");
            if (FileVersionInfo.GetVersionInfo(Executable).FileVersion != "6.14.105.0")
                throw new InvalidOperationException("This AWCC version needs a background-control compatibility check");
            var candidates = new List<Process>();
            foreach (var process in Process.GetProcessesByName("AWCC"))
            {
                try
                {
                    if (string.Equals(process.MainModule.FileName, Executable, StringComparison.OrdinalIgnoreCase))
                        candidates.Add(process);
                    else
                        process.Dispose();
                }
                catch
                {
                    process.Dispose();
                    throw new InvalidOperationException("Cannot verify the running AWCC process");
                }
            }

            if (candidates.Count > 1)
            {
                foreach (var p in candidates)
                    p.Dispose();
                throw new InvalidOperationException("Multiple AWCC frontends are running");
            }

            if (candidates.Count == 1)
            {
                frontend = candidates[0];
                if (FindWindow(frontend.Id, IntPtr.Zero) == IntPtr.Zero)
                    throw new InvalidOperationException("AWCC is starting or is on another desktop; retry shortly");
                return;
            }

            // Windows desktop isolation prevents splash, main, and modal windows
            // from appearing on the user's input desktop, including cold startup.
            // This does not change the input desktop and grants no extra privileges.
            ThrowIfStopping();
            string name = "HardwareDeck-AWCC-" + Guid.NewGuid().ToString("N");
            desktop = CreateDesktop(name, IntPtr.Zero, IntPtr.Zero, 0, 0xC3, IntPtr.Zero);
            if (desktop == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot create AWCC background desktop");
            if (!SetThreadDesktop(desktop))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot bind the AWCC worker desktop");
            hidden = true;
            job = CreateJobObject(IntPtr.Zero, null);
            var limits = new JobLimits();
            limits.Basic.LimitFlags = 0x2000; // KILL_ON_JOB_CLOSE
            if (job == IntPtr.Zero || !SetInformationJobObject(job, 9, ref limits, Marshal.SizeOf(typeof(JobLimits))))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot isolate the temporary AWCC process lifetime");
            var start = new StartupInfo
            {
                cb = Marshal.SizeOf(typeof(StartupInfo)),
                lpDesktop = name
            };
            ProcessInformation info;
            ThrowIfStopping();
            if (!CreateProcess(Executable, null, IntPtr.Zero, IntPtr.Zero, false, 4, IntPtr.Zero, Path.GetDirectoryName(Executable), ref start, out info))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "AWCC background startup failed");
            try
            {
                frontend = Process.GetProcessById(info.dwProcessId);
                if (frontend.Handle == IntPtr.Zero)
                    throw new InvalidOperationException("Cannot retain the temporary frontend process handle");
                lock (ownedLock)
                {
                    ThrowIfStopping();
                    OwnedFrontend = frontend;
                }

                if (!AssignProcessToJobObject(job, info.hProcess))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot track the temporary AWCC process");
                ThrowIfStopping();
                if (ResumeThread(info.hThread) == uint.MaxValue)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot start the AWCC background frontend");
            }
            catch
            {
                // Even failures before managed Process/job ownership must terminate
                // the suspended process through the exact handle we created.
                TerminateProcess(info.hProcess, 1);
                throw;
            }
            finally
            {
                CloseHandle(info.hThread);
                CloseHandle(info.hProcess);
            }
        }

        void RestoreCallerFocus()
        {
            if (hidden || caller == IntPtr.Zero || !IsWindowVisible(caller) || IsIconic(caller))
                return;
            uint owner;
            GetWindowThreadProcessId(GetForegroundWindow(), out owner);
            uint callerOwner;
            GetWindowThreadProcessId(caller, out callerOwner);
            if (callerOwner == callerProcess && frontend != null && owner == frontend.Id)
            {
                try
                {
                    AutomationElement.FromHandle(caller).SetFocus();
                }
                catch
                {
                }
            }
        }

        internal static void StopOwnedFrontend()
        {
            Process owned;
            lock (ownedLock)
            {
                stopping = true;
                owned = OwnedFrontend;
                OwnedFrontend = null;
            }

            if (owned != null)
            {
                try
                {
                    if (!owned.HasExited)
                        owned.Kill();
                }
                catch
                {
                }
            }
        }

        internal static void ThrowIfStopping()
        {
            lock (ownedLock)
            {
                if (stopping)
                    throw new OperationCanceledException("AWCC background operation stopped");
            }
        }

        public void Dispose()
        {
            RestoreCallerFocus();
            // Only the transient frontend started by this worker is closed. The
            // Dell services and any existing user-owned frontend are retained.
            StopOwnedFrontend();
            if (frontend != null)
                frontend.Dispose();
            if (job != IntPtr.Zero)
            {
                CloseHandle(job);
                job = IntPtr.Zero;
            }

            if (desktop != IntPtr.Zero)
            {
                CloseDesktop(desktop);
                desktop = IntPtr.Zero;
            }
        }

        static IntPtr FindWindow(int pid, IntPtr desktop)
        {
            var matches = new List<IntPtr>();
            EnumWindow callback = delegate (IntPtr handle, IntPtr unused)
            {
                uint owner;
                GetWindowThreadProcessId(handle, out owner);
                if (owner == pid)
                {
                    var text = new StringBuilder(256);
                    GetWindowText(handle, text, text.Capacity);
                    if (text.ToString() == "Alienware Command Center")
                        matches.Add(handle);
                }

                return true;
            };
            if (desktop == IntPtr.Zero)
                EnumWindows(callback, IntPtr.Zero);
            else
                EnumDesktopWindows(desktop, callback, IntPtr.Zero);
            return matches.Count == 1 ? matches[0] : IntPtr.Zero;
        }

        delegate bool EnumWindow(IntPtr handle, IntPtr parameter);
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct StartupInfo
        {
            public int cb;
            public string lpReserved, lpDesktop, lpTitle;
            public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
            public short wShowWindow, cbReserved2;
            public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct ProcessInformation
        {
            public IntPtr hProcess, hThread;
            public int dwProcessId, dwThreadId;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct BasicLimits
        {
            public long ProcessTime, JobTime;
            public uint LimitFlags;
            public UIntPtr MinWorking, MaxWorking;
            public uint ActiveLimit;
            public UIntPtr Affinity;
            public uint Priority, Scheduling;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct IoCounters
        {
            public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct JobLimits
        {
            public BasicLimits Basic;
            public IoCounters Io;
            public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr CreateJobObject(IntPtr security, string name);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool SetInformationJobObject(IntPtr job, int info, ref JobLimits limits, int size);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern uint ResumeThread(IntPtr thread);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr CreateDesktop(string name, IntPtr device, IntPtr devmode, int flags, uint access, IntPtr security);
        [DllImport("user32.dll", SetLastError = true)]
        static extern bool SetThreadDesktop(IntPtr desktop);
        [DllImport("user32.dll")]
        static extern bool CloseDesktop(IntPtr desktop);
        [DllImport("user32.dll")]
        static extern bool EnumDesktopWindows(IntPtr desktop, EnumWindow callback, IntPtr parameter);
        [DllImport("user32.dll")]
        static extern bool EnumWindows(EnumWindow callback, IntPtr parameter);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int GetWindowText(IntPtr window, StringBuilder text, int length);
        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
        [DllImport("user32.dll")]
        static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")]
        static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool CreateProcess(string file, StringBuilder command, IntPtr processSecurity, IntPtr threadSecurity, bool inherit, int flags, IntPtr environment, string directory, ref StartupInfo start, out ProcessInformation info);
        [DllImport("kernel32.dll")]
        static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool TerminateProcess(IntPtr process, uint exitCode);
    }
}
