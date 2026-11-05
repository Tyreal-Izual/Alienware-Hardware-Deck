using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HardwareDeck;

// Render the real window content without showing a window or starting a collector.
// All values are fixtures; no WMI calls or hardware-mode writes are performed.
public static class DisplayTests
{
    static int checks;
    static readonly BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
    }
    static void Call(DeckWindow deck, string name, params object[] args)
    {
        typeof(DeckWindow).GetMethod(name, Private).Invoke(deck, args);
    }
    static Snapshot Fixture()
    {
        var sensors = new List<Reading>();
        for (int i = 1; i <= 9; i++) sensors.Add(new Reading {
            Id = "awcc.temp." + i, Name = "AWCC temp " + i, Kind = "temperature", Value = i == 1 ? 52 : 29 + i, Source = "AWCC WMI"
        });
        for (int i = 0; i < 5; i++) sensors.Add(new Reading {
            Id = "awcc.fan." + (50 + i), Name = "Fan " + (i + 1), Kind = "fan",
            Value = i == 4 ? (double?)null : 1100 + i * 170, Maximum = 4200, FanSensorCode = 1, FanTemperatureSensorId = "awcc.temp.1"
        });
        return new Snapshot {
            CapturedAt = DateTime.UtcNow.ToString("o"), ComputerModel = "Alienware Aurora R16",
            CpuName = "Intel(R) Core(TM) i9-14900KF", CpuLoad = 24,
            Cpu = new CpuTelemetry { FrequencyGHz = 5.12, PowerWatts = 83.6, VoltageVolts = 1.173, VoltageSource = "Intel XTU · AWCC CPU voltage source" },
            MemoryUsedGiB = 21.8, MemoryTotalGiB = 63.7,
            Gpu = new GpuData { Name = "NVIDIA GeForce RTX 4090", Load = 36, Temperature = 43.5, ClockMHz = 2520,
                PowerWatts = 146, PowerLimitWatts = 450, FanPercent = 32, MemoryUsedMiB = 5120, MemoryTotalMiB = 24564 },
            Sensors = sensors, CpuTemperature = sensors[0], CpuTemperatureSelection = "Selected temperature source: awcc.temp.1",
            AwccStatus = "AWCC sensors connected", SampleMilliseconds = 12,
            ThermalMode = new ThermalModeState { CurrentId = 160, CurrentName = "Balanced", CanChange = false,
                Presets = new List<ThermalPreset> { new ThermalPreset { Id = 163, Name = "Quiet" },
                    new ThermalPreset { Id = 160, Name = "Balanced" }, new ThermalPreset { Id = 161, Name = "Performance" } } }
        };
    }
    static void Layout(FrameworkElement surface, double width, double height)
    {
        for (int pass = 0; pass < 3; pass++) {
            surface.Measure(new Size(width, height));
            surface.Arrange(new Rect(0, 0, width, height));
            surface.UpdateLayout();
        }
    }
    static void Render(DeckWindow deck, Window window, string directory, double scale, bool fullscreen)
    {
        var surface = (ScrollViewer)window.Content;
        // Windowed/maximized checks leave room for the taskbar and native frame.
        int pixelHeight = fullscreen ? 1080 : 1000;
        Layout(surface, 1920 / scale, pixelHeight / scale);
        Call(deck, "DrawCharts");
        surface.UpdateLayout();
        string name = "1080p-" + (int)(scale * 100) + (fullscreen ? "-fullscreen" : "-windowed");
        Console.WriteLine(name + ": content=" + surface.ExtentHeight.ToString("0.0") + ", viewport=" + surface.ViewportHeight.ToString("0.0") + ", scroll=" + surface.ScrollableHeight.ToString("0.0"));
        Check(surface.ScrollableHeight < 1, name + " must fit without scrolling");
        foreach (var id in new[] { "CpuLoad", "CpuTemp", "GpuLoad", "GpuTemp", "CpuFrequency", "CpuPower", "CpuVoltage", "Ram", "Vram", "Power", "GpuFrequency", "GpuFan", "Footer", "ModeCurrent" }) {
            var text = (TextBlock)window.FindName(id);
            var measured = new TextBlock { Text = text.Text, FontFamily = text.FontFamily, FontSize = text.FontSize, FontWeight = text.FontWeight };
            measured.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Check(text.ActualWidth + 1 >= measured.DesiredSize.Width, name + " clips " + id);
        }
        var chart = (Canvas)window.FindName("GpuChart");
        Check(chart.ActualHeight >= 90, name + " chart is too short");
        Check(((Grid)window.FindName("HistoryPanel")).ActualHeight <= 220, name + " charts must remain compact in fullscreen");
        var bitmap = new RenderTargetBitmap(1920, pixelHeight, scale * 96, scale * 96, PixelFormats.Pbgra32);
        bitmap.Render(surface);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var output = File.Create(Path.Combine(directory, name + ".png"))) encoder.Save(output);
    }
    [STAThread]
    public static int Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("en-US");
        string root = args[0], output = args[1];
        Directory.CreateDirectory(output);
        var deck = new DeckWindow(root, null, false);
        var window = (Window)typeof(DeckWindow).GetField("window", Private).GetValue(deck);
        Check((int)typeof(DeckWindow).GetField("interval", Private).GetValue(deck) == 1000, "Default sampling must be one second");
        Check(((ComboBox)window.FindName("Interval")).SelectedIndex == 0, "Refresh selector must match sampling");
        var sample = Fixture();
        var history = (List<HistoryPoint>)typeof(DeckWindow).GetField("history", Private).GetValue(deck);
        var now = DateTime.UtcNow;
        for (int i = 119; i > 0; i--) history.Add(new HistoryPoint {
            Time = now.AddSeconds(-i), Cpu = 49 + Math.Sin(i / 9.0) * 8 + Math.Cos(i / 3.0) * 3,
            Gpu = 40 + Math.Sin(i / 13.0) * 4
        });
        Call(deck, "Update", sample);
        var fans = (UniformGrid)window.FindName("FanPanel");
        var firstFan = fans.Children[0];
        Call(deck, "Update", sample);
        Check(ReferenceEquals(firstFan, fans.Children[0]), "Regular refresh must reuse sensor controls");
        foreach (double scale in new[] { 1.0, 1.25, 1.5 }) {
            Render(deck, window, output, scale, true);
            Render(deck, window, output, scale, false);
        }
        var selector = (ComboBox)window.FindName("Interval");
        selector.SelectedIndex = 2;
        Check((int)typeof(DeckWindow).GetField("interval", Private).GetValue(deck) == 5000, "Refresh selector must change the interval");
        selector.SelectedIndex = 0;
        typeof(DeckWindow).GetField("lastUpdate", Private).SetValue(deck, DateTime.UtcNow.AddSeconds(-30));
        Call(deck, "UpdateStatus");
        Check(((TextBlock)window.FindName("LiveState")).Text == "STALE DATA", "Stale readings must be flagged");
        sample.CapturedAt = DateTime.UtcNow.ToString("o"); Call(deck, "Update", sample);
        Check(((TextBlock)window.FindName("LiveState")).Text == "LIVE", "Fresh readings must clear the stale indicator");
        sample.Sensors.Clear(); sample.CpuTemperature = null; sample.CpuTemperatureSelection = "No AWCC temperature source available";
        sample.AwccStatus = "AWCC needs administrator access · Select Enable sensors";
        sample.ThermalMode = new ThermalModeState { CurrentName = "Disconnected", Presets = new List<ThermalPreset>() };
        Call(deck, "Update", sample);
        Layout((FrameworkElement)window.Content, 1280, 720);
        Check(fans.Children.Count == 2, "Missing AWCC must leave GPU and unavailable chassis tiles");
        Check(((UniformGrid)window.FindName("TemperaturePanel")).Visibility == Visibility.Collapsed, "Missing temperatures must not reserve an empty row");
        Check(!((Button)window.FindName("ModeApply")).IsEnabled, "Disconnected control must remain disabled");
        Check(((ScrollViewer)window.Content).ScrollableHeight < 1, "Unavailable-sensor view must fit");
        window.Close();
        Console.WriteLine(checks + " display, scaling and refresh checks passed. Images use sample data.");
        return 0;
    }
}
