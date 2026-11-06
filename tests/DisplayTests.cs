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
            Network = new NetworkData { DownloadBytesPerSecond=1250000, UploadBytesPerSecond=42000,
                AdapterCount=1, AdapterNames=new[]{"Ethernet"}, Status="Connected", Detail="Physical adapter traffic · Includes LAN transfers" },
            Sensors = sensors, CpuTemperature = sensors[0], CpuTemperatureSelection = "Selected temperature source: awcc.temp.1",
            AwccStatus = "AWCC sensors connected", SampleMilliseconds = 12,
            Preset = new PresetState { Cooling=160, Cpu=0, CpuReadable=true, PowerVerified=true, CanChange=true, CapturedAt=DateTime.UtcNow.ToString("o"), Detail="Fixture CPU readback verified" },
            ThermalMode = new ThermalModeState { CurrentId = 160, CurrentName = "Balanced", CanChange = true,
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
    static void TestModeButtons(DeckWindow deck,Window window)
    {
        var quiet=(Button)window.FindName("ModeQuiet");var balanced=(Button)window.FindName("ModeBalanced");var performance=(Button)window.FindName("ModePerformance");
        var cpuTwo=(Button)window.FindName("CpuOC02");var cpuOn=(Button)window.FindName("CpuOC01");var cpuOff=(Button)window.FindName("CpuOff");
        var fanP=(Button)window.FindName("CoolingPerformance");
        var buttons=new[]{quiet,balanced,performance};
        var request=typeof(DeckWindow).GetField("pendingRequest",Private);
        var sample=Fixture();Call(deck,"Update",sample);
        Check((string)balanced.Tag=="Current" && quiet.Tag==null && performance.Tag==null,"Only a verified linked combination highlights a main preset");
        Check(buttons.All(b=>b.IsEnabled) && !cpuOn.IsEnabled && !cpuTwo.IsEnabled,"Balanced permits switching but forbids OC");
        cpuTwo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(request.GetValue(deck)==null,"Programmatic OC02 clicks are rejected below P");
        cpuOn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(request.GetValue(deck)==null,"Backend-facing UI guard rejects a programmatic OC click under Balanced cooling");
        balanced.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(request.GetValue(deck)==null,"Already verified targets do not write redundantly");
        performance.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(((PresetRequest)request.GetValue(deck)).Value==161,"Performance queues the complete linked preset");
        Check(buttons.All(b=>!b.IsEnabled) && !cpuOff.IsEnabled && !fanP.IsEnabled,"All controls lock during a request");
        quiet.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(((PresetRequest)request.GetValue(deck)).Value==161,"Rapid clicks cannot replace a pending target");
        Call(deck,"CompletePreset",new PresetResult{Success=false,Message="Mismatch",Actual=Fixture().Preset});
        Check((string)balanced.Tag=="Current" && performance.Tag==null,"A failed request never claims the requested preset is active");
        fanP.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(((PresetRequest)request.GetValue(deck)).Kind=="cooling","Cooling controls use the shared request path");
        var independent=new PresetState{Cooling=161,Cpu=0,CpuReadable=true,PowerVerified=true,CanChange=true,CapturedAt=DateTime.UtcNow.ToString("o")};
        Call(deck,"CompletePreset",new PresetResult{Success=true,Message="Verified independent cooling",Actual=independent});
        Check(buttons.All(b=>b.Tag==null) && (string)fanP.Tag=="Current" && (string)cpuOff.Tag=="Current","Independent combination only highlights its component states");
        Check(cpuOn.IsEnabled && cpuTwo.IsEnabled,"OC becomes available with verified Performance cooling");
        cpuOn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(((PresetRequest)request.GetValue(deck)).Kind=="cpu","CPU control shares serialization and completion handling");
        Call(deck,"CompletePreset",new PresetResult{Success=true,Message="Verified",Actual=new PresetState{Cooling=161,Cpu=1,CpuReadable=true,PowerVerified=true,CanChange=true,CapturedAt=DateTime.UtcNow.ToString("o")}});
        Check((string)performance.Tag=="Current" && (string)cpuOn.Tag=="Current","A verified OC01/Performance combination is linked");
        cpuTwo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(((PresetRequest)request.GetValue(deck)).Kind=="cpu" && ((PresetRequest)request.GetValue(deck)).Value==2,"OC02 queues an independent CPU request");
        Check(!cpuTwo.IsEnabled && (string)cpuTwo.Tag=="Pending","OC02 locks and shows pending until readback completes");
        Call(deck,"CompletePreset",new PresetResult{Success=true,Message="Verified OC02",Actual=new PresetState{Cooling=161,Cpu=2,CpuReadable=true,PowerVerified=true,CanChange=true,CapturedAt=DateTime.UtcNow.ToString("o")}});
        Check((string)cpuTwo.Tag=="Current" && cpuOn.Tag==null && performance.Tag==null,"OC02 highlights independently, never as linked Performance");
        performance.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(((PresetRequest)request.GetValue(deck)).Kind=="preset" && ((PresetRequest)request.GetValue(deck)).Value==161,"Main Performance remains a linked preset request after OC02");
        Call(deck,"CompletePreset",new PresetResult{Success=true,Message="Verified",Actual=new PresetState{Cooling=161,Cpu=1,CpuReadable=true,PowerVerified=true,CanChange=true,CapturedAt=DateTime.UtcNow.ToString("o")}});
        sample=Fixture();sample.Preset.PowerVerified=false;Call(deck,"Update",sample);
        Check(buttons.All(b=>b.Tag==null) && ((TextBlock)window.FindName("ModeCurrent")).Text=="Unverified","Wrong cooling power limits must clear linked-preset claims");
        sample=Fixture();sample.Preset.Cooling=163;sample.ThermalMode.CurrentId=163;Call(deck,"Update",sample);
        Check((string)quiet.Tag=="Current" && !cpuOn.IsEnabled && !cpuTwo.IsEnabled,"Quiet also rejects OC");
        sample=Fixture();sample.Preset.CpuReadable=false;sample.Preset.CanChange=false;Call(deck,"Update",sample);
        Check(buttons.All(b=>!b.IsEnabled && b.Tag==null) && !fanP.IsEnabled,"Unverified CPU state disables writes");
        sample=Fixture();Call(deck,"Update",sample);
        typeof(DeckWindow).GetField("lastUpdate",Private).SetValue(deck,DateTime.UtcNow.AddSeconds(-30));Call(deck,"UpdateStatus");
        Check(buttons.All(b=>!b.IsEnabled && b.Tag==null),"Stale snapshots disable controls");
        performance.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(request.GetValue(deck)==null,"Stale programmatic clicks are rejected");
        Check(typeof(DeckWindow).GetField("samplingTask",Private).GetValue(deck)==null,"Fixture interaction never starts hardware collection");
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
        if(scale==1)Check(surface.ScrollableHeight < 1, name + " must fit all four charts without scrolling at 100% scaling");
        if(scale==1.25 && fullscreen)Check(surface.ScrollableHeight < 1, name + " must fit the linked controls and four charts at 125% scaling");
        Check(surface.ScrollableWidth < 1, name + " must not require horizontal scrolling");
        foreach (var id in new[] { "CpuLoad", "CpuTemp", "GpuLoad", "GpuTemp", "CpuFrequency", "CpuPower", "CpuVoltage", "Ram", "Vram", "Power", "GpuFrequency", "GpuFan", "Footer", "ModeCurrent", "DownloadChartCurrent", "UploadChartCurrent" }) {
            var text = (TextBlock)window.FindName(id);
            var measured = new TextBlock { Text = text.Text, FontFamily = text.FontFamily, FontSize = text.FontSize, FontWeight = text.FontWeight };
            measured.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Check(text.ActualWidth + 1 >= measured.DesiredSize.Width, name + " clips " + id);
        }
        var ocTwo=(Button)window.FindName("CpuOC02");
        var edge=ocTwo.TransformToAncestor(surface).Transform(new Point(ocTwo.ActualWidth,0));
        Check(edge.X<=surface.ActualWidth && ocTwo.ActualWidth>=62,name+" OC02 must fit the control row");
        var chart = (Canvas)window.FindName("GpuChart");
        Check(chart.ActualHeight >= 90, name + " chart is too short");
        Check(((Grid)window.FindName("HistoryPanel")).ActualHeight <= 220, name + " charts must remain compact in fullscreen");
        var temperatures=(Grid)window.FindName("HistoryPanel");
        var network=(Grid)window.FindName("NetworkHistoryPanel");
        Check(Math.Abs(network.ActualHeight-temperatures.ActualHeight)<1 && Math.Abs(network.ActualWidth-temperatures.ActualWidth)<1,
            name + " network and temperature rows must have matching dimensions");
        Check(((Canvas)window.FindName("DownloadChart")).ActualHeight>=90, name + " network plot must remain readable");
        var bitmap = new RenderTargetBitmap(1920, pixelHeight, scale * 96, scale * 96, PixelFormats.Pbgra32);
        bitmap.Render(surface);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var output = File.Create(Path.Combine(directory, name + ".png"))) encoder.Save(output);
        if(surface.ScrollableHeight>=1){
            surface.ScrollToEnd();Layout(surface,1920/scale,pixelHeight/scale);
            var end=network.TransformToAncestor(surface).Transform(new Point(0,network.ActualHeight));
            Check(end.Y<=surface.ActualHeight+1 && end.Y>0,name+" network cards must be reachable by vertical scrolling");
            var scrolled=new RenderTargetBitmap(1920,pixelHeight,scale*96,scale*96,PixelFormats.Pbgra32);scrolled.Render(surface);
            var scrolledEncoder=new PngBitmapEncoder();scrolledEncoder.Frames.Add(BitmapFrame.Create(scrolled));
            using(var output=File.Create(Path.Combine(directory,name+"-network.png")))scrolledEncoder.Save(output);
            surface.ScrollToHome();Layout(surface,1920/scale,pixelHeight/scale);
        }
    }
    [STAThread]
    public static int Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("en-US");
        string root = args[0], output = args[1];
        Directory.CreateDirectory(output);
        var settingsPath=Path.Combine(output,"unshown-window-state.json");
        var deck = new DeckWindow(root, null, false, settingsPath);
        var window = (Window)typeof(DeckWindow).GetField("window", Private).GetValue(deck);
        Check((int)typeof(DeckWindow).GetField("interval", Private).GetValue(deck) == 1000, "Default sampling must be one second");
        Check(((ComboBox)window.FindName("Interval")).SelectedIndex == 0, "Refresh selector must match sampling");
        var sample = Fixture();
        var history = (List<HistoryPoint>)typeof(DeckWindow).GetField("history", Private).GetValue(deck);
        var now = DateTime.UtcNow;
        for (int i = 119; i > 0; i--) history.Add(new HistoryPoint {
            Time = now.AddSeconds(-i), Cpu = 49 + Math.Sin(i / 9.0) * 8 + Math.Cos(i / 3.0) * 3,
            Gpu = 40 + Math.Sin(i / 13.0) * 4,
            Download=800000+350000*Math.Sin(i/10.0)+(i>48 && i<75?1200000:0),
            Upload=35000+22000*Math.Sin(i/14.0)
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
        Check(((TextBlock)window.FindName("DownloadChartCurrent")).Text=="1.3 MB/s", "Display current download in byte-per-second units");
        Check(((TextBlock)window.FindName("UploadChartCurrent")).Text=="42.0 KB/s", "Display upload independently of download");
        // A smaller viewport keeps readable plots and exposes the lower row through scrolling.
        Layout((FrameworkElement)window.Content, 1536, 760);
        Check(((Grid)window.FindName("HistoryPanel")).ActualHeight==((Grid)window.FindName("NetworkHistoryPanel")).ActualHeight,
            "Both history rows must shrink together");
        Check(((Canvas)window.FindName("GpuChart")).ActualHeight >= 90, "Adaptive charts must retain a readable plot");
        TestModeButtons(deck,window);
        sample.CapturedAt=DateTime.UtcNow.ToString("o");Call(deck,"Update",sample);
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
        Check(new[]{"ModeQuiet","ModeBalanced","ModePerformance"}.All(name=>!((Button)window.FindName(name)).IsEnabled), "Disconnected mode controls must remain disabled");
        Check(((ScrollViewer)window.Content).ScrollableWidth < 1, "Unavailable-sensor view must not overflow horizontally");
        sample.Network=new NetworkData{Status="Disconnected",Detail="No active physical network adapter",AdapterNames=new string[0]};
        Call(deck,"Update",sample);
        Check(((TextBlock)window.FindName("DownloadChartCurrent")).Text=="—" && ((TextBlock)window.FindName("UploadChartCurrent")).Text=="—",
            "Disconnected networking must not display stale or zero throughput");
        Check(((TextBlock)window.FindName("DownloadChartSummary")).Text.Contains("No active physical"),"Explain missing network connectivity");
        bool lateUpdate=false;
        Call(deck,"PostIfOpen",new Action(()=>lateUpdate=true));
        ((System.Threading.CancellationTokenSource)typeof(DeckWindow).GetField("cancellation",Private).GetValue(deck)).Cancel();
        var frame=new System.Windows.Threading.DispatcherFrame();
        window.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,new Action(()=>frame.Continue=false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
        Check(!lateUpdate,"A queued result must not update a closing dashboard");
        Call(deck,"PostIfOpen",new Action(()=>lateUpdate=true));
        Check(!lateUpdate,"New updates must be rejected after cancellation");
        window.Close();
        Check(!File.Exists(settingsPath), "Offscreen rendering must not save or replace user window preferences");
        Console.WriteLine(checks + " display, scaling and refresh checks passed. Images use sample data.");
        return 0;
    }
}
