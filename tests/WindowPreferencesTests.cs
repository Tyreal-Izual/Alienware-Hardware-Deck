using System;
using System.IO;
using System.Collections.Generic;
using HardwareDeck;

public static class WindowPreferencesTests
{
    static int count;
    static void Check(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
        count++;
    }

    static bool Same(PixelBounds a, PixelBounds b)
    {
        return a.Left == b.Left && a.Top == b.Top && a.Width == b.Width && a.Height == b.Height;
    }

    static bool Visible(PixelBounds a, PixelBounds work)
    {
        return a.Left >= work.Left && a.Top >= work.Top && a.Left + a.Width <= work.Left + work.Width && a.Top + a.Height <= work.Top + work.Height;
    }

    public static void Run()
    {
        var primary = new WindowDisplay
        {
            MonitorId = "monitor-main",
            DeviceName = "DISPLAY1",
            Primary = true,
            Bounds = new PixelBounds(0, 0, 2560, 1440),
            WorkArea = new PixelBounds(0, 40, 2560, 1400),
            Scale = 1
        };
        var secondary = new WindowDisplay
        {
            MonitorId = "monitor-22N1",
            DeviceName = "DISPLAY2",
            Bounds = new PixelBounds(-1920, 0, 1920, 1080),
            WorkArea = new PixelBounds(-1920, 0, 1920, 1040),
            Scale = 1.5
        };
        var screens = new List<WindowDisplay>
        {
            primary,
            secondary
        };
        var original = new PixelBounds(-1830, 30, 1560, 975);
        var saved = WindowPreferences.Capture(secondary, original);
        saved.Fullscreen = true;
        saved.Topmost = true;
        saved.Maximized = true;
        Check(saved.IsValid(), "Captured window geometry must be valid");
        Check(saved.Width == 1040 && saved.Height == 650 && saved.Left == 60 && saved.Top == 20, "Store monitor-relative dimensions in DIPs");
        Check(Same(WindowPreferences.ResolveBounds(saved, secondary, 1040, 640), original), "Restore exact pixel bounds on an unchanged display");
        Check(WindowPreferences.ChooseDisplay(saved, screens, "DISPLAY1") == secondary, "Restore to the saved secondary monitor");
        secondary.DeviceName = "DISPLAY3";
        primary.DeviceName = "DISPLAY2";
        Check(WindowPreferences.ChooseDisplay(saved, screens, null) == secondary, "Monitor identity must survive display-number reordering");
        var copied = saved.Copy();
        copied.Topmost = false;
        Check(saved.Topmost && !copied.Topmost, "Preference edits must not mutate saved geometry/state");
        var moved = saved.OnDisplay(primary);
        Check(WindowPreferences.ChooseDisplay(moved, screens, null) == primary, "Leaving fullscreen after an OS monitor move must target the current display");
        Check(Same(WindowPreferences.ResolveBounds(moved, primary, 1040, 640), new PixelBounds(60, 60, 1040, 650)), "Rebase remembered normal geometry across monitors with different DPI");
        Check(saved.MonitorId == secondary.MonitorId && saved.Width == 1040, "Rebasing the active placement must not overwrite the disconnected-monitor preference");
        secondary.Scale = 1;
        var resized = WindowPreferences.ResolveBounds(saved, secondary, 1040, 640);
        Check(resized.Width == 1040 && resized.Height == 650, "Restore logical window size at a different DPI");
        Check(resized.Left == -1860 && resized.Top == 20, "Restore logical offsets at a different DPI");
        secondary.Bounds = new PixelBounds(-3200, -1080, 1920, 1080);
        secondary.WorkArea = new PixelBounds(-3200, -1040, 1920, 1040);
        resized = WindowPreferences.ResolveBounds(saved, secondary, 1040, 640);
        Check(resized.Left == -3140 && resized.Top == -1020, "Track rearranged monitors and top/left taskbars without coordinate creep");
        var fallback = WindowPreferences.ChooseDisplay(saved, new[] { primary }, null);
        Check(fallback == primary, "Use an available primary display when the saved monitor is disconnected");
        Check(Visible(WindowPreferences.ResolveBounds(saved, fallback, 1040, 640), primary.WorkArea), "Disconnected-display fallback must remain fully visible");
        Check(WindowPreferences.Match(saved, new[] { primary }) == null, "Do not mistake a reused display number for the missing monitor");
        var legacy = saved.Copy();
        legacy.MonitorId = null;
        Check(WindowPreferences.Match(legacy, new[] { primary }) == primary, "Use the device name when monitor identity is unavailable");
        Check(WindowPreferences.ChooseDisplay(null, screens, "DISPLAY3") == secondary, "First launch can use the current display");
        Check(Visible(WindowPreferences.ResolveBounds(null, primary, 1040, 640), primary.WorkArea), "First-launch defaults must fit the working area");
        var outside = saved.Copy();
        outside.Left = -500;
        outside.Top = 100000;
        outside.Width = 10000;
        outside.Height = 10000;
        Check(Visible(WindowPreferences.ResolveBounds(outside, secondary, 1040, 640), secondary.WorkArea), "Clamp offscreen positions and oversized windows");
        var oddDisplay = new WindowDisplay
        {
            MonitorId = saved.MonitorId,
            DeviceName = "DISPLAY3",
            Scale = 1.25,
            Bounds = new PixelBounds(0, 0, 1919, 1079),
            WorkArea = new PixelBounds(0, 0, 1919, 1079)
        };
        var fractional = saved.Copy();
        fractional.Width = 1041.2;
        fractional.Height = 641.2;
        fractional.Left = 100000;
        fractional.Top = 100000;
        Check(Visible(WindowPreferences.ResolveBounds(fractional, oddDisplay, 1040, 640), oddDisplay.WorkArea), "Fractional DPI rounding must keep the final pixel inside the working area");
        var small = new WindowDisplay
        {
            MonitorId = saved.MonitorId,
            DeviceName = "DISPLAY3",
            Scale = 1.5,
            Bounds = new PixelBounds(0, 0, 800, 480),
            WorkArea = new PixelBounds(0, 0, 800, 440)
        };
        var fitted = WindowPreferences.ResolveBounds(saved, small, 1040, 640);
        Check(fitted.Width == 800 && fitted.Height == 440 && Visible(fitted, small.WorkArea), "Small displays must override application minimum size safely");
        var path = Path.GetTempFileName();
        try
        {
            Check(WindowPreferencesFile.Load(path) == null, "An empty settings file must use defaults");
            Check(WindowPreferencesFile.Save(path, saved), "Save a valid window state");
            var restored = WindowPreferencesFile.Load(path);
            Check(restored != null && restored.MonitorId == saved.MonitorId && restored.Fullscreen && restored.Maximized && restored.Topmost, "Persist monitor, fullscreen, maximized and topmost state");
            Check(restored.Width == saved.Width && restored.Height == saved.Height && restored.Left == saved.Left && restored.Top == saved.Top, "Persist normal-window geometry separately from fullscreen state");
            restored.Fullscreen = false;
            restored.Topmost = false;
            Check(WindowPreferencesFile.Save(path, restored) && !WindowPreferencesFile.Load(path).Fullscreen, "Replace a prior state atomically");
            string before = File.ReadAllText(path);
            var invalid = saved.Copy();
            invalid.Width = double.NaN;
            Check(!WindowPreferencesFile.Save(path, invalid) && File.ReadAllText(path) == before, "Invalid geometry must not overwrite a valid state");
            invalid = saved.Copy();
            invalid.Version = 99;
            File.WriteAllText(path, WindowPreferencesFile.Serialize(invalid));
            Check(WindowPreferencesFile.Load(path) == null, "Unknown schema versions must use defaults");
            File.WriteAllText(path, "{invalid json");
            Check(WindowPreferencesFile.Load(path) == null, "Corrupt settings must not prevent startup");
            Check(!WindowPreferencesFile.Save(Path.Combine(path, "state.json"), saved), "An unwritable settings path must fail without crashing");
            Check(WindowPreferencesFile.Load(null) == null && !WindowPreferencesFile.Save(null, saved), "Preview mode can disable persistence");
        }
        finally
        {
            File.Delete(path);
        }

        Check(WindowPreferencesFile.Load(path) == null, "Missing settings must use defaults");
        Console.WriteLine(count + " window persistence, monitor selection and DPI checks passed.");
    }
}
