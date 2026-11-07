using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace HardwareDeck
{
    // Owns placement independently of sampling. Nothing is written by the refresh loop.
    public sealed class WindowPlacementSession
    {
        readonly Window window;
        readonly string path;
        readonly double minimumWidth, minimumHeight;
        readonly ResizeMode resizeMode;
        readonly double opacity;
        readonly DispatcherTimer saveTimer = new DispatcherTimer();
        WindowPreferences preferred, normal;
        string lastSaved;
        bool initialized, transition, maximized, maximizedBeforeFullscreen, userMoved;
        HwndSource source;
        IntPtr handle;
        public bool IsFullscreen { get; private set; }

        public event EventHandler Changed;
        public WindowPlacementSession(Window window, string path)
        {
            this.window = window;
            this.path = path;
            minimumWidth = window.MinWidth;
            minimumHeight = window.MinHeight;
            resizeMode = window.ResizeMode;
            opacity = window.Opacity;
            preferred = WindowPreferencesFile.Load(path);
            lastSaved = preferred == null ? null : WindowPreferencesFile.Serialize(preferred);
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.SourceInitialized += Initialize;
            window.ContentRendered += RestoreModes;
            window.LocationChanged += WindowChanged;
            window.SizeChanged += (s, e) => WindowChanged(s, e);
            window.StateChanged += WindowChanged;
            window.Closing += (s, e) => SaveNow();
            window.Closed += (s, e) =>
            {
                initialized = false;
                saveTimer.Stop();
                if (source != null && !source.IsDisposed)
                    source.RemoveHook(WindowMessage);
            };
            saveTimer.Interval = TimeSpan.FromMilliseconds(750);
            saveTimer.Tick += (s, e) => SaveNow();
        }

        void Initialize(object sender, EventArgs e)
        {
            handle = new WindowInteropHelper(window).Handle;
            source = HwndSource.FromHwnd(handle);
            source.AddHook(WindowMessage);
            transition = true;
            window.Opacity = 0;
            var displays = Displays();
            var target = WindowPreferences.ChooseDisplay(preferred, displays, Forms.Screen.FromHandle(handle).DeviceName);
            PlaceNormal(preferred, target);
        }

        void RestoreModes(object sender, EventArgs e)
        {
            if (initialized)
                return;
            // Loaded is too early: WPF can still overwrite native bounds during its first render.
            // Reapply placement after that pass, then reveal the window in its final mode.
            var target = WindowPreferences.ChooseDisplay(preferred, Displays(), Forms.Screen.FromHandle(handle).DeviceName);
            PlaceNormal(preferred, target);
            if (preferred != null)
            {
                window.Topmost = preferred.Topmost;
                maximized = preferred.Maximized;
                if (maximized)
                    window.WindowState = WindowState.Maximized;
                if (preferred.Fullscreen)
                    EnterFullscreen();
            }

            initialized = true;
            transition = false;
            window.Opacity = opacity;
            CaptureNormal();
            Notify();
        }

        void Notify()
        {
            if (Changed != null)
                Changed(this, EventArgs.Empty);
        }

        void WindowChanged(object sender, EventArgs e)
        {
            if (!initialized || transition)
                return;
            if (window.WindowState != WindowState.Minimized && !IsFullscreen)
                maximized = window.WindowState == WindowState.Maximized;
            if (window.WindowState != WindowState.Minimized && normal != null)
            {
                var current = CurrentDisplay();
                if (WindowPreferences.Match(normal, new[] { current }) == null)
                {
                    // Follow OS-initiated monitor moves even while fullscreen/maximized. An automatic
                    // fallback from a disconnected monitor must not erase the preferred monitor.
                    userMoved |= WindowPreferences.Match(normal, Displays()) != null;
                    normal = normal.OnDisplay(current);
                }
            }

            CaptureNormal();
            QueueSave();
        }

        IntPtr WindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message == 0x0232 && initialized && !transition)
            { // WM_EXITSIZEMOVE: deliberate drag/resize.
                userMoved = true;
                CaptureNormal();
                QueueSave();
            }

            return IntPtr.Zero;
        }

        void QueueSave()
        {
            if (!initialized || transition || path == null)
                return;
            saveTimer.Stop();
            saveTimer.Start();
        }

        WindowDisplay CurrentDisplay()
        {
            var screen = Forms.Screen.FromHandle(handle);
            var display = Display(screen);
            display.Scale = WindowScale();
            return display;
        }

        double WindowScale()
        {
            try
            {
                uint dpi = GetDpiForWindow(handle);
                if (dpi >= 48 && dpi <= 768)
                    return dpi / 96.0;
            }
            catch (EntryPointNotFoundException)
            {
            }

            return source == null || source.CompositionTarget == null ? 1 : source.CompositionTarget.TransformToDevice.M11;
        }

        void CaptureNormal()
        {
            if (handle == IntPtr.Zero || transition || IsFullscreen || window.WindowState != WindowState.Normal)
                return;
            var placement = new NativePlacement
            {
                Length = Marshal.SizeOf(typeof(NativePlacement))
            };
            // Native state avoids treating maximize/minimize transition rectangles as normal geometry.
            if (!GetWindowPlacement(handle, ref placement) || placement.ShowCommand != 1)
                return;
            NativeRect rect;
            if (GetWindowRect(handle, out rect) && rect.Right > rect.Left && rect.Bottom > rect.Top)
                normal = WindowPreferences.Capture(CurrentDisplay(), rect.Bounds);
        }

        void Position(PixelBounds bounds, uint extraFlags = 0)
        {
            if (!SetWindowPos(handle, IntPtr.Zero, bounds.Left, bounds.Top, bounds.Width, bounds.Height, 0x0014 | extraFlags))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }

        void PlaceNormal(WindowPreferences settings, WindowDisplay target)
        {
            // First place the HWND on its target monitor, then use that window's actual DPI.
            // GetDpiForMonitor is intentionally avoided for this per-monitor-aware window.
            window.MinWidth = 0;
            window.MinHeight = 0;
            Position(new PixelBounds(target.WorkArea.Left + target.WorkArea.Width / 2 - 50, target.WorkArea.Top + target.WorkArea.Height / 2 - 50, 100, 100), 0x0008);
            target.Scale = WindowScale();
            window.MinWidth = Math.Min(minimumWidth, target.WorkArea.Width / target.Scale);
            window.MinHeight = Math.Min(minimumHeight, target.WorkArea.Height / target.Scale);
            var bounds = WindowPreferences.ResolveBounds(settings, target, minimumWidth, minimumHeight);
            Position(bounds);
            normal = WindowPreferences.Capture(target, bounds);
        }

        void EnterFullscreen()
        {
            var display = CurrentDisplay();
            normal = normal.OnDisplay(display);
            maximizedBeforeFullscreen = window.WindowState == WindowState.Maximized;
            IsFullscreen = true;
            window.WindowState = WindowState.Normal;
            window.WindowStyle = WindowStyle.None;
            window.ResizeMode = ResizeMode.NoResize;
            Position(display.Bounds);
        }

        void LeaveFullscreen()
        {
            normal = normal.OnDisplay(CurrentDisplay());
            IsFullscreen = false;
            window.WindowState = WindowState.Normal;
            window.WindowStyle = WindowStyle.SingleBorderWindow;
            window.ResizeMode = resizeMode;
            var target = WindowPreferences.ChooseDisplay(normal, Displays(), Forms.Screen.FromHandle(handle).DeviceName);
            PlaceNormal(normal, target);
            maximized = maximizedBeforeFullscreen;
            if (maximized)
                window.WindowState = WindowState.Maximized;
        }

        public void ToggleFullscreen()
        {
            if (!initialized)
                return;
            CaptureNormal();
            transition = true;
            try
            {
                if (IsFullscreen)
                    LeaveFullscreen();
                else
                    EnterFullscreen();
            }
            finally
            {
                transition = false;
            }

            Notify();
            QueueSave();
        }

        public void ToggleTopmost()
        {
            window.Topmost = !window.Topmost;
            Notify();
            QueueSave();
        }

        public void NextDisplay()
        {
            if (!initialized)
                return;
            var displays = Displays();
            int index = displays.FindIndex(d => d.DeviceName == Forms.Screen.FromHandle(handle).DeviceName);
            var target = displays[(index + 1) % displays.Count];
            CaptureNormal();
            bool full = IsFullscreen, wasMaximized = full ? maximizedBeforeFullscreen : maximized;
            transition = true;
            try
            {
                if (full)
                    LeaveFullscreen();
                window.WindowState = WindowState.Normal;
                var settings = normal.OnDisplay(target);
                PlaceNormal(settings, target);
                if (wasMaximized)
                    window.WindowState = WindowState.Maximized;
                maximized = wasMaximized;
                if (full)
                    EnterFullscreen();
                userMoved = true;
            }
            finally
            {
                transition = false;
            }

            Notify();
            QueueSave();
        }

        public bool SaveNow()
        {
            saveTimer.Stop();
            if (!initialized || transition || path == null)
                return true;
            CaptureNormal();
            if (normal == null)
                return false;
            var displays = Displays();
            // A disconnected preferred display is retained until the user deliberately repositions.
            bool preserve = preferred != null && !userMoved && WindowPreferences.Match(preferred, displays) == null;
            var value = (preserve ? preferred : normal).Copy();
            if (!preserve && window.WindowState != WindowState.Minimized)
            {
                var current = CurrentDisplay();
                value.MonitorId = current.MonitorId;
                value.DisplayName = current.DeviceName;
            }

            value.Fullscreen = IsFullscreen;
            value.Maximized = IsFullscreen ? maximizedBeforeFullscreen : maximized;
            value.Topmost = window.Topmost;
            var serialized = WindowPreferencesFile.Serialize(value);
            if (serialized == lastSaved)
            {
                preferred = value;
                userMoved = false;
                return true;
            }

            if (!WindowPreferencesFile.Save(path, value))
                return false;
            lastSaved = serialized;
            preferred = value;
            userMoved = false;
            return true;
        }

        static List<WindowDisplay> Displays()
        {
            return Forms.Screen.AllScreens.Select(Display).ToList();
        }

        static WindowDisplay Display(Forms.Screen screen)
        {
            var info = new DisplayDevice
            {
                Size = Marshal.SizeOf(typeof(DisplayDevice))
            };
            string id = EnumDisplayDevices(screen.DeviceName, 0, ref info, 1) ? info.DeviceId : null;
            return new WindowDisplay
            {
                DeviceName = screen.DeviceName,
                MonitorId = id,
                Primary = screen.Primary,
                Bounds = new PixelBounds(screen.Bounds.Left, screen.Bounds.Top, screen.Bounds.Width, screen.Bounds.Height),
                WorkArea = new PixelBounds(screen.WorkingArea.Left, screen.WorkingArea.Top, screen.WorkingArea.Width, screen.WorkingArea.Height)
            };
        }

        [StructLayout(LayoutKind.Sequential)]
        struct NativePoint
        {
            public int X, Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct NativeRect
        {
            public int Left, Top, Right, Bottom;
            public PixelBounds Bounds
            {
                get
                {
                    return new PixelBounds(Left, Top, Right - Left, Bottom - Top);
                }
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        struct NativePlacement
        {
            public int Length, Flags, ShowCommand;
            public NativePoint MinPosition, MaxPosition;
            public NativeRect NormalPosition;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct DisplayDevice
        {
            public int Size;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string Description;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceId;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceKey;
        }

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")]
        static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
        [DllImport("user32.dll")]
        static extern bool GetWindowPlacement(IntPtr hwnd, ref NativePlacement placement);
        [DllImport("user32.dll")]
        static extern uint GetDpiForWindow(IntPtr hwnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern bool EnumDisplayDevices(string device, uint index, ref DisplayDevice info, uint flags);
    }
}
