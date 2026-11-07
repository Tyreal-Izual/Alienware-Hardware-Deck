using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace HardwareDeck
{
    public struct PixelBounds
    {
        public int Left, Top, Width, Height;
        public PixelBounds(int left, int top, int width, int height)
        {
            Left = left;
            Top = top;
            Width = width;
            Height = height;
        }
    }

    public sealed class WindowDisplay
    {
        public string MonitorId, DeviceName;
        public bool Primary;
        public PixelBounds Bounds, WorkArea;
        public double Scale = 1;
    }

    public sealed class WindowPreferences
    {
        public int Version { get; set; }
        public string MonitorId { get; set; }
        public string DisplayName { get; set; }
        // Normal-window geometry in DIPs, relative to the monitor's working area.
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public bool Fullscreen { get; set; }
        public bool Maximized { get; set; }
        public bool Topmost { get; set; }

        public WindowPreferences()
        {
            Version = 1;
        }

        public WindowPreferences Copy()
        {
            return (WindowPreferences)MemberwiseClone();
        }

        public WindowPreferences OnDisplay(WindowDisplay display)
        {
            var result = Copy();
            result.MonitorId = display.MonitorId;
            result.DisplayName = display.DeviceName;
            return result;
        }

        static bool Finite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        public bool IsValid()
        {
            return Version == 1 && !string.IsNullOrWhiteSpace(DisplayName) && DisplayName.Length <= 256 && (MonitorId == null || MonitorId.Length <= 2048) && Finite(Left) && Finite(Top) && Math.Abs(Left) <= 1000000 && Math.Abs(Top) <= 1000000 && Finite(Width) && Finite(Height) && Width >= 100 && Height >= 100 && Width <= 32768 && Height <= 32768;
        }

        public static WindowDisplay Match(WindowPreferences saved, IEnumerable<WindowDisplay> displays)
        {
            if (saved == null)
                return null;
            // An existing monitor ID takes precedence over a display number that Windows may reuse.
            if (!string.IsNullOrEmpty(saved.MonitorId))
                return displays.FirstOrDefault(d => string.Equals(d.MonitorId, saved.MonitorId, StringComparison.OrdinalIgnoreCase));
            return displays.FirstOrDefault(d => string.Equals(d.DeviceName, saved.DisplayName, StringComparison.OrdinalIgnoreCase));
        }

        public static WindowDisplay ChooseDisplay(WindowPreferences saved, IList<WindowDisplay> displays, string currentDisplay)
        {
            return Match(saved, displays) ?? (saved == null ? displays.FirstOrDefault(d => d.DeviceName == currentDisplay) : null) ?? displays.FirstOrDefault(d => d.Primary) ?? displays.First();
        }

        public static PixelBounds ResolveBounds(WindowPreferences saved, WindowDisplay display, double minWidth, double minHeight)
        {
            var work = display.WorkArea;
            double scale = display.Scale;
            double width = Math.Min(work.Width, Math.Max(Math.Min(minWidth * scale, work.Width), saved == null ? Math.Min(1600 * scale, work.Width * 0.96) : saved.Width * scale));
            double height = Math.Min(work.Height, Math.Max(Math.Min(minHeight * scale, work.Height), saved == null ? Math.Min(900 * scale, work.Height * 0.96) : saved.Height * scale));
            int pixelWidth = (int)Math.Round(width), pixelHeight = (int)Math.Round(height);
            bool matched = Match(saved, new[] { display }) != null;
            double left = matched ? saved.Left * scale : (work.Width - width) / 2;
            double top = matched ? saved.Top * scale : (work.Height - height) / 2;
            // Clamp against the final integer size; rounding both halves can otherwise add a pixel.
            int pixelLeft = (int)Math.Max(0, Math.Min(work.Width - pixelWidth, Math.Round(left)));
            int pixelTop = (int)Math.Max(0, Math.Min(work.Height - pixelHeight, Math.Round(top)));
            return new PixelBounds(work.Left + pixelLeft, work.Top + pixelTop, pixelWidth, pixelHeight);
        }

        public static WindowPreferences Capture(WindowDisplay display, PixelBounds bounds)
        {
            return new WindowPreferences
            {
                MonitorId = display.MonitorId,
                DisplayName = display.DeviceName,
                Left = (bounds.Left - display.WorkArea.Left) / display.Scale,
                Top = (bounds.Top - display.WorkArea.Top) / display.Scale,
                Width = bounds.Width / display.Scale,
                Height = bounds.Height / display.Scale
            };
        }
    }

    public static class WindowPreferencesFile
    {
        public static string DefaultPath
        {
            get
            {
                return Path.Combine(SnapshotFile.DataDirectory, "window-state.json");
            }
        }

        public static WindowPreferences Load(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path) || new FileInfo(path).Length > 65536)
                    return null;
                var value = new JavaScriptSerializer().Deserialize<WindowPreferences>(File.ReadAllText(path));
                return value != null && value.IsValid() ? value : null;
            }
            catch
            {
                return null;
            }
        }

        public static bool Save(string path, WindowPreferences value)
        {
            if (string.IsNullOrEmpty(path) || value == null || !value.IsValid())
                return false;
            try
            {
                SnapshotFile.Write(path, value);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static string Serialize(WindowPreferences value)
        {
            return new JavaScriptSerializer().Serialize(value);
        }
    }
}
