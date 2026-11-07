using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace HardwareDeck
{
    // Style the native frame so Windows still owns dragging, resizing, snapping and caption buttons.
    public sealed class WindowAppearance
    {
        readonly Window window;
        HwndSource source;
        IntPtr handle;
        bool closed, refreshQueued;
        public WindowAppearance(Window window)
        {
            this.window = window;
            using (var stream = typeof(WindowAppearance).Assembly.GetManifestResourceStream("HardwareDeck.AppIcon.ico"))
            {
                if (stream != null)
                {
                    var icon = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    icon.Freeze();
                    window.Icon = icon;
                }
            }

            window.SourceInitialized += (s, e) =>
            {
                handle = new WindowInteropHelper(window).Handle;
                source = HwndSource.FromHwnd(handle);
                if (source != null)
                    source.AddHook(WindowMessage);
                Refresh();
            };
            window.ContentRendered += (s, e) => Refresh();
            window.Activated += (s, e) => Refresh();
            window.Deactivated += (s, e) => Refresh();
            window.StateChanged += (s, e) => Refresh();
            window.Closed += (s, e) =>
            {
                closed = true;
                if (source != null && !source.IsDisposed)
                    source.RemoveHook(WindowMessage);
            };
        }

        static int Color(int red, int green, int blue)
        {
            return red | (green << 8) | (blue << 16);
        }

        int Set(int attribute, int value)
        {
            return DwmSetWindowAttribute(handle, attribute, ref value, sizeof(int));
        }

        public void Refresh()
        {
            if (closed || handle == IntPtr.Zero)
                return;
            bool contrast = SystemParameters.HighContrast;
            int dark = contrast ? 0 : 1;
            // Older Windows 10 releases use attribute 19; unsupported attributes safely return an HRESULT.
            if (Set(20, dark) < 0)
                Set(19, dark);
            int systemDefault = unchecked((int)0xffffffff);
            Set(35, contrast ? systemDefault : Color(11, 17, 23)); // Caption matches the dashboard background.
            Set(36, contrast ? systemDefault : (window.IsActive ? Color(232, 240, 246) : Color(156, 174, 187)));
            Set(34, contrast ? systemDefault : (window.IsActive ? Color(36, 52, 64) : Color(24, 35, 44)));
            Set(33, contrast ? 0 : window.WindowStyle == WindowStyle.None ? 1 : 2); // Rounded when windowed; square when fullscreen.
        }

        IntPtr WindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if ((message == 0x031a || message == 0x031e || message == 0x001a || message == 0x007d) && !refreshQueued && !closed)
            {
                refreshQueued = true;
                window.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    Refresh();
                    refreshQueued = false;
                }));
            }

            return IntPtr.Zero;
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }
}
