using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace HardwareDeck
{
    public sealed partial class DeckWindow
    {
        readonly Window window;
        readonly WindowAppearance appearance;
        readonly WindowPlacementSession placement;
        readonly string root, preview;
        readonly bool stay;
        bool previewSaved;
        readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        Task samplingTask;
        readonly List<HistoryPoint> history = new List<HistoryPoint>();
        DateTime lastUpdate = DateTime.MinValue;
        Snapshot latest;
        volatile int interval = 1000;
        int samples;
        PresetRequest pendingRequest;
        string presetNotice;
        bool modeBusy;
        string collectionError;
        bool? compactLayout;
        readonly DispatcherTimer clock = new DispatcherTimer();
        readonly Dictionary<string, object> controls = new Dictionary<string, object>();
        static readonly Dictionary<string, Brush> brushes = new Dictionary<string, Brush>();
        readonly Dictionary<string, SensorTile> fanTiles = new Dictionary<string, SensorTile>();
        readonly Dictionary<string, SensorTile> temperatureTiles = new Dictionary<string, SensorTile>();
        T Find<T>(string name)
            where T : class
        {
            object control;
            if (!controls.TryGetValue(name, out control))
                controls[name] = control = window.FindName(name);
            return control as T;
        }

        static Brush Brush(string hex)
        {
            Brush brush;
            if (!brushes.TryGetValue(hex, out brush))
            {
                var solid = (SolidColorBrush)new BrushConverter().ConvertFromString(hex);
                solid.Freeze();
                brushes[hex] = brush = solid;
            }

            return brush;
        }

        static string Display(double? value, string unit, string format)
        {
            return value.HasValue ? value.Value.ToString(format, CultureInfo.InvariantCulture) + unit : "—";
        }

        void Text(string name, string value)
        {
            var block = Find<TextBlock>(name);
            block.Text = value;
            block.ToolTip = value;
        }

        public DeckWindow(string root, string preview, bool stay, string windowStatePath = null)
        {
            this.root = root;
            this.preview = preview;
            this.stay = stay;
            using (var reader = System.Xml.XmlReader.Create(System.IO.Path.Combine(root, "View.xaml")))
                window = (Window)XamlReader.Load(reader);
            appearance = new WindowAppearance(window);
            placement = new WindowPlacementSession(window, string.IsNullOrEmpty(preview) ? (windowStatePath ?? WindowPreferencesFile.DefaultPath) : null);
            placement.Changed += (s, e) => UpdateWindowButtons();
            Find<Button>("AdminButton").Click += (s, e) => Elevate();
            Find<Button>("ScreenButton").Click += (s, e) => placement.NextDisplay();
            Find<Button>("PinButton").Click += (s, e) => placement.ToggleTopmost();
            Find<Button>("FullButton").Click += (s, e) => placement.ToggleFullscreen();
            foreach (int id in PresetPolicy.CoolingModes)
            {
                int target = id;
                Find<Button>("Mode" + PresetPolicy.Name(target)).Click += (s, e) => RequestPreset(new PresetRequest { Kind = "preset", Value = target });
                Find<Button>("Cooling" + PresetPolicy.Name(target)).Click += (s, e) => RequestPreset(new PresetRequest { Kind = "cooling", Value = target });
            }

            Find<Button>("CpuOff").Click += (s, e) => RequestPreset(new PresetRequest { Kind = "cpu", Value = 0 });
            Find<Button>("CpuOC01").Click += (s, e) => RequestPreset(new PresetRequest { Kind = "cpu", Value = 1 });
            Find<Button>("CpuOC02").Click += (s, e) => RequestPreset(new PresetRequest { Kind = "cpu", Value = 2 });
            Find<ComboBox>("Interval").SelectionChanged += (s, e) =>
            {
                interval = new[]
                {
                    1000,
                    2000,
                    5000
                }[Math.Max(0, Math.Min(2, Find<ComboBox>("Interval").SelectedIndex))];
                UpdateStatus();
            };
            Find<ScrollViewer>("Viewport").SizeChanged += (s, e) => FitDashboard();
            Find<Grid>("Dashboard").LayoutUpdated += FitHistoryHeight;
            window.KeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.F11)
                    placement.ToggleFullscreen();
                else if (e.Key == System.Windows.Input.Key.Escape && placement.IsFullscreen)
                    placement.ToggleFullscreen();
            };
            window.Closed += (s, e) =>
            {
                cancellation.Cancel();
                clock.Stop();
                Find<Grid>("Dashboard").LayoutUpdated -= FitHistoryHeight;
                if (samplingTask == null)
                    ReleaseSamplingResources();
                else
                    samplingTask.ContinueWith(t => ReleaseSamplingResources(), TaskScheduler.Default);
            };
            Find<Canvas>("CpuChart").SizeChanged += (s, e) => DrawCharts();
            Find<Canvas>("GpuChart").SizeChanged += (s, e) => DrawCharts();
            Find<Canvas>("DownloadChart").SizeChanged += (s, e) => DrawCharts();
            Find<Canvas>("UploadChart").SizeChanged += (s, e) => DrawCharts();
            Find<Canvas>("CpuChart").MouseMove += (s, e) => InspectChart(Find<Canvas>("CpuChart"), true, e);
            Find<Canvas>("GpuChart").MouseMove += (s, e) => InspectChart(Find<Canvas>("GpuChart"), false, e);
            Find<Canvas>("DownloadChart").MouseMove += (s, e) => InspectChart(Find<Canvas>("DownloadChart"), true, e, true);
            Find<Canvas>("UploadChart").MouseMove += (s, e) => InspectChart(Find<Canvas>("UploadChart"), false, e, true);
            if (Collector.IsAdmin)
            {
                Find<Button>("AdminButton").Content = "Sensors enabled";
                Find<Button>("AdminButton").IsEnabled = false;
            }

            Text("Clock", DateTime.Now.ToString("HH:mm:ss"));
            clock.Interval = TimeSpan.FromSeconds(1);
            clock.Tick += (s, e) =>
            {
                Text("Clock", DateTime.Now.ToString("HH:mm:ss") + (window.Topmost ? "  ·  Pinned" : ""));
                UpdateStatus();
            };
            window.Loaded += (s, e) =>
            {
                if (samplingTask == null)
                {
                    clock.Start();
                    samplingTask = Task.Run((Action)CollectLoop);
                }
            };
        }

        public void Run()
        {
            new Application().Run(window);
        }

        void ReleaseSamplingResources()
        {
            cancellation.Dispose();
        }

        bool HasFreshSample()
        {
            double age = (DateTime.UtcNow - lastUpdate).TotalSeconds;
            return latest != null && collectionError == null && age >= 0 && age <= Math.Max(5, interval / 1000.0 * 3);
        }

        void PostIfOpen(Action action)
        {
            if (cancellation.IsCancellationRequested || window.Dispatcher.HasShutdownStarted || window.Dispatcher.HasShutdownFinished)
                return;
            try
            {
                window.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!cancellation.IsCancellationRequested)
                        action();
                }));
            }
            catch (InvalidOperationException)
            {
            } // The dispatcher may stop after the check.
        }

        void FitDashboard()
        {
            var viewport = Find<ScrollViewer>("Viewport");
            bool compact = viewport.ActualHeight < 900;
            var dashboard = Find<Grid>("Dashboard");
            dashboard.MinHeight = Math.Max(0, viewport.ActualHeight - (compact ? 24 : 32));
            if (compactLayout == compact)
                return;
            compactLayout = compact;
            dashboard.Margin = new Thickness(20, compact ? 12 : 16, 20, compact ? 12 : 16);
            Find<Grid>("Header").Margin = new Thickness(0, 0, 0, compact ? 10 : 14);
            foreach (var name in new[]
            {
                "CpuCard",
                "GpuCard",
                "CpuHistoryCard",
                "GpuHistoryCard",
                "DownloadHistoryCard",
                "UploadHistoryCard"
            }

            )
                Find<Border>(name).Padding = new Thickness(compact ? 14 : 18, compact ? 8 : 14, compact ? 14 : 18, compact ? 8 : 14);
            Find<Border>("CoolingCard").Padding = new Thickness(14, compact ? 8 : 12, 14, compact ? 8 : 12);
            foreach (var name in new[]
            {
                "CpuName",
                "GpuName"
            }

            )
                Find<TextBlock>(name).Margin = new Thickness(0, compact ? 3 : 5, 0, compact ? 6 : 10);
            foreach (var name in new[]
            {
                "CpuLoad",
                "CpuTemp",
                "GpuLoad",
                "GpuTemp"
            }

            )
                Find<TextBlock>(name).FontSize = compact ? 44 : 48;
            foreach (var name in new[]
            {
                "CpuHint",
                "GpuHint"
            }

            )
                Find<TextBlock>(name).Margin = new Thickness(0, compact ? 5 : 10, 0, 0);
            foreach (var name in new[]
            {
                "CpuBar",
                "GpuBar"
            }

            )
                Find<ProgressBar>(name).Margin = new Thickness(0, compact ? 5 : 8, 0, compact ? 8 : 12);
            foreach (var tile in fanTiles.Values.Concat(temperatureTiles.Values))
                tile.SetDensity(compact);
        }

        void FitHistoryHeight(object sender, EventArgs e)
        {
            var dashboard = Find<Grid>("Dashboard");
            double occupied = dashboard.RowDefinitions.Take(3).Sum(row => row.ActualHeight);
            if (occupied <= 0)
                return;
            double available = Find<ScrollViewer>("Viewport").ActualHeight - dashboard.Margin.Top - dashboard.Margin.Bottom - occupied - 12;
            var panel = Find<Grid>("HistoryPanel");
            panel.MinHeight = compactLayout == true ? 158 : 174;
            double height = Math.Max(panel.MinHeight, Math.Min(compactLayout == true ? 158 : 220, Math.Floor(available / 2)));
            if (Math.Abs(panel.Height - height) > 0.5)
                panel.Height = height;
            var networkPanel = Find<Grid>("NetworkHistoryPanel");
            networkPanel.MinHeight = panel.MinHeight;
            if (Math.Abs(networkPanel.Height - height) > 0.5)
                networkPanel.Height = height;
        }

        void UpdateWindowButtons()
        {
            Find<Button>("PinButton").Content = window.Topmost ? "Pin: on" : "Pin: off";
            Find<Button>("FullButton").Content = placement.IsFullscreen ? "Exit fullscreen" : "Fullscreen";
            appearance.Refresh();
        }

        static string PowerDisplay(double? current, double? limit, string currentFormat)
        {
            return Display(current, "", currentFormat) + " / " + Display(limit, "", "0.##") + " W";
        }

        void UpdateCpuPower()
        {
            var cpu = latest == null ? null : latest.Cpu;
            var preset = latest == null ? null : latest.Preset;
            bool readable = PresetFresh() && preset.CpuReadable;
            double? limit = readable && preset.PowerLimitWatts > 0 ? (double?)preset.PowerLimitWatts.Value : null;
            Text("CpuPower", PowerDisplay(cpu == null ? null : cpu.PowerWatts, limit, "0.0"));
            Find<TextBlock>("CpuPower").ToolTip = (cpu == null ? "CPU package power" : cpu.PowerSource) + "\nLimit: current sustained package power limit (PL1), read from Intel XTU" + (readable && preset.ShortPowerLimitWatts > 0 ? "\nShort boost limit (PL2): " + preset.ShortPowerLimitWatts.Value.ToString("0.##", CultureInfo.InvariantCulture) + " W" : "") + (limit.HasValue ? "" : "\nLimit unavailable: waiting for fresh CPU readback");
        }

        void UpdateStatus()
        {
            UpdateModeButtons();
            UpdateCpuPower();
            if (collectionError != null)
            {
                Text("LiveState", "COLLECTION STOPPED");
                Find<Ellipse>("LiveDot").Fill = Brush("#E4B77A");
                Find<TextBlock>("LiveState").Foreground = Brush("#E4B77A");
                Text("Footer", "Collection unavailable · " + collectionError);
                return;
            }

            if (latest == null)
                return;
            double age = (DateTime.UtcNow - lastUpdate).TotalSeconds;
            bool stale = age > Math.Max(5, interval / 1000.0 * 3);
            Text("LiveState", stale ? "STALE DATA" : "LIVE");
            Find<Ellipse>("LiveDot").Fill = Brush(stale ? "#E4B77A" : "#50D7B2");
            Find<TextBlock>("LiveState").Foreground = Brush(stale ? "#E4B77A" : "#50D7B2");
            Text("Footer", stale ? "Last sample " + (int)age + " s ago · Waiting for sensors" : interval / 1000 + " s refresh · " + latest.SampleMilliseconds.ToString("0", CultureInfo.InvariantCulture) + " ms sample · Local only");
        }

        void CollectLoop()
        {
            try
            {
                using (var collector = new Collector(root))
                {
                    while (!cancellation.IsCancellationRequested)
                    {
                        var timer = Stopwatch.StartNew();
                        var snapshot = collector.Sample();
                        if (cancellation.IsCancellationRequested)
                            break;
                        PostIfOpen(() => Update(snapshot));
                        if (cancellation.Token.WaitHandle.WaitOne(Math.Max(100, interval - (int)timer.ElapsedMilliseconds)))
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                PostIfOpen(() =>
                {
                    collectionError = ex.GetType().Name;
                    if (modeBusy)
                        Text("ModeNotice", "Preset switch could not be verified. Restart HardwareDeck to reconnect.");
                    UpdateStatus();
                });
            }
        }

        void Update(Snapshot value)
        {
            DateTime previousPresetTime, incomingPresetTime;
            if (latest != null && latest.Preset != null && value.Preset != null && DateTime.TryParse(latest.Preset.CapturedAt, null, DateTimeStyles.RoundtripKind, out previousPresetTime) && DateTime.TryParse(value.Preset.CapturedAt, null, DateTimeStyles.RoundtripKind, out incomingPresetTime) && incomingPresetTime < previousPresetTime)
                value.Preset = latest.Preset;
            if (!modeBusy && latest != null && latest.Preset != null && value.Preset != null && (latest.Preset.Cpu != value.Preset.Cpu || latest.Preset.Cooling != value.Preset.Cooling))
                presetNotice = null;
            latest = value;
            collectionError = null;
            DateTime captured;
            lastUpdate = DateTime.TryParse(value.CapturedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out captured) ? captured.ToUniversalTime() : DateTime.UtcNow;
            samples++;
            var temp = value.CpuTemperature;
            var gpu = value.Gpu ?? new GpuData();
            Text("MachineName", value.ComputerModel + " · Local hardware monitor");
            Text("CpuName", value.CpuName);
            var cpu = value.Cpu ?? new CpuTelemetry();
            Text("CpuFrequency", Display(cpu.FrequencyGHz, " GHz", "0.00"));
            Text("CpuVoltage", cpu.VoltageVolts.HasValue ? Display(cpu.VoltageVolts, " V", "0.000") : "Unavailable");
            Find<TextBlock>("CpuFrequency").ToolTip = cpu.FrequencySource;
            Find<TextBlock>("CpuVoltage").ToolTip = cpu.VoltageSource;
            Text("CpuLoad", Display(value.CpuLoad, " %", "0"));
            Text("CpuTemp", Display(temp == null ? null : temp.Value, " °C", "0"));
            Text("CpuTempLabel", temp == null ? "CPU temperature unavailable" : temp.Name);
            Text("CpuHint", value.CpuTemperatureSelection);
            Text("CpuChartTitle", (temp == null ? "CPU temperature unavailable" : temp.Name) + " · 120 s");
            Find<ProgressBar>("CpuBar").Value = value.CpuLoad ?? 0;
            Text("GpuName", gpu.Name ?? "NVIDIA GPU");
            Text("GpuLoad", Display(gpu.Load, " %", "0"));
            Text("GpuTemp", Display(gpu.Temperature, " °C", "0.0"));
            Text("GpuHint", gpu.Error ?? "NVIDIA NVML · Fan speed is reported as a percentage");
            Text("GpuFrequency", Display(gpu.ClockMHz, " MHz", "0"));
            Text("GpuFan", Display(gpu.FanPercent, " %", "0"));
            Find<ProgressBar>("GpuBar").Value = gpu.Load ?? 0;
            Text("Ram", Display(value.MemoryUsedGiB, "", "0.0") + " / " + Display(value.MemoryTotalGiB, "", "0.0"));
            Text("Vram", Display(gpu.MemoryUsedMiB.HasValue ? gpu.MemoryUsedMiB / 1024 : null, "", "0.0") + " / " + Display(gpu.MemoryTotalMiB.HasValue ? gpu.MemoryTotalMiB / 1024 : null, "", "0.0"));
            Text("Power", PowerDisplay(gpu.PowerWatts, gpu.PowerLimitWatts, "0"));
            Text("AwccState", value.AwccStatus);
            Find<TextBlock>("AwccState").Foreground = Brush(value.Sensors.Count > 0 ? "#93A7B8" : "#E4B77A");
            var fans = value.Sensors.Where(s => s.Kind == "fan").ToList();
            Text("FanSummary", fans.Count(f => f.Value.HasValue) + " / " + fans.Count + " fan RPM channels available");
            var fanKeys = fans.Select(f => f.Id).Concat(new[] { "gpu.fan" }).ToList();
            if (fans.Count == 0)
                fanKeys.Add("awcc.unavailable");
            ReconcileTiles(Find<UniformGrid>("FanPanel"), fanTiles, fanKeys, false);
            foreach (var fan in fans)
            {
                var associated = value.Sensors.FirstOrDefault(s => s.Kind == "temperature" && s.Id == fan.FanTemperatureSensorId);
                string relation = fan.FanSensorCode.HasValue ? "Firmware association code: " + fan.FanSensorCode : "Firmware association unavailable";
                if (associated != null)
                    relation += "\nRelated temperature: " + associated.Name + " · " + associated.Id;
                relation += "\nThis does not identify the physical fan position.";
                fanTiles[fan.Id].Update(fan.Name, Display(fan.Value, " RPM", "0"), fan.Id + "\n" + relation, fan.Value, fan.Maximum, "#50D7B2");
            }

            fanTiles["gpu.fan"].Update("GPU fan", Display(gpu.FanPercent, " %", "0"), "NVIDIA · Driver-reported percentage", gpu.FanPercent, 100, "#8EB8FF");
            if (fans.Count == 0)
                fanTiles["awcc.unavailable"].Update("Chassis fans", "—", "Waiting for AWCC fan RPM", null, null, "#E4B77A");
            var extraTemperatures = value.Sensors.Where(s => s.Kind == "temperature" && (temp == null || s.Id != temp.Id)).ToList();
            ReconcileTiles(Find<UniformGrid>("TemperaturePanel"), temperatureTiles, extraTemperatures.Select(s => s.Id).ToList(), true);
            foreach (var extra in extraTemperatures)
                temperatureTiles[extra.Id].Update(extra.Name, Display(extra.Value, " °C", "0"), extra.Id, null, null, "#DCE8F2");
            var network = value.Network ?? new NetworkData
            {
                Status = "Unavailable",
                Detail = "Waiting for network data",
                AdapterNames = new string[0]
            };
            string networkSource = network.Detail + "\n" + string.Join(", ", network.AdapterNames ?? new string[0]) + "\nRates are bytes per second, including local-network traffic.";
            foreach (var name in new[]
            {
                "DownloadChartTitle",
                "UploadChartTitle",
                "DownloadChartCurrent",
                "UploadChartCurrent"
            }

            )
                Find<TextBlock>(name).ToolTip = networkSource;
            history.Add(new HistoryPoint { Time = lastUpdate, Cpu = temp == null ? null : temp.Value, Gpu = gpu.Temperature, Download = network.DownloadBytesPerSecond, Upload = network.UploadBytesPerSecond });
            history.RemoveAll(p => (lastUpdate - p.Time).TotalSeconds > 120);
            DrawCharts();
            UpdateStatus();
            if (!previewSaved && !string.IsNullOrEmpty(preview) && samples >= 3)
            {
                window.UpdateLayout();
                var surface = Find<ScrollViewer>("Viewport");
                var dpi = PresentationSource.FromVisual(window).CompositionTarget.TransformToDevice;
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(surface.ActualWidth * dpi.M11), (int)Math.Ceiling(surface.ActualHeight * dpi.M22), 96 * dpi.M11, 96 * dpi.M22, PixelFormats.Pbgra32);
                bitmap.Render(surface);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var stream = File.Create(preview))
                    encoder.Save(stream);
                previewSaved = true;
                if (!stay)
                    window.Close();
            }
        }

        void Elevate()
        {
            try
            {
                placement.SaveNow();
                using (var launched = Process.Start(new ProcessStartInfo(typeof(Program).Assembly.Location) { UseShellExecute = true, Verb = "runas", WorkingDirectory = root }))
                {
                }

                window.Close();
            }
            catch (System.ComponentModel.Win32Exception)
            {
                Text("Footer", "Administrator access was not granted. Basic metrics remain available.");
            }
        }
    }
}
