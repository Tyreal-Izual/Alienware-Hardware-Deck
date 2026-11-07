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
    public sealed class HistoryPoint { public DateTime Time; public double? Cpu, Gpu, Download, Upload; }
    public sealed class DeckWindow
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
        T Find<T>(string name) where T : class
        {
            object control;
            if (!controls.TryGetValue(name, out control)) controls[name] = control = window.FindName(name);
            return control as T;
        }
        static Brush Brush(string hex)
        {
            Brush brush;
            if (!brushes.TryGetValue(hex, out brush)) {
                var solid = (SolidColorBrush)new BrushConverter().ConvertFromString(hex);
                solid.Freeze(); brushes[hex] = brush = solid;
            }
            return brush;
        }
        static string Display(double? value, string unit, string format) { return value.HasValue ? value.Value.ToString(format, CultureInfo.InvariantCulture) + unit : "—"; }
        void Text(string name, string value) { var block = Find<TextBlock>(name); block.Text = value; block.ToolTip = value; }
        public DeckWindow(string root, string preview, bool stay, string windowStatePath=null)
        {
            this.root = root; this.preview = preview; this.stay=stay;
            using (var reader = System.Xml.XmlReader.Create(System.IO.Path.Combine(root, "View.xaml")))
                window = (Window)XamlReader.Load(reader);
            appearance=new WindowAppearance(window);
            placement=new WindowPlacementSession(window,string.IsNullOrEmpty(preview)?
                (windowStatePath??WindowPreferencesFile.DefaultPath):null);
            placement.Changed+=(s,e)=>UpdateWindowButtons();
            Find<Button>("AdminButton").Click += (s,e) => Elevate();
            Find<Button>("ScreenButton").Click += (s,e) => placement.NextDisplay();
            Find<Button>("PinButton").Click += (s,e) => placement.ToggleTopmost();
            Find<Button>("FullButton").Click += (s,e) => placement.ToggleFullscreen();
            foreach(int id in new[]{163,160,161}){
                int target=id;
                Find<Button>("Mode"+ModeName(target)).Click+=(s,e)=>RequestMode(target);
                Find<Button>("Cooling"+ModeName(target)).Click+=(s,e)=>RequestPreset(new PresetRequest{Kind="cooling",Value=target});
            }
            Find<Button>("CpuOff").Click+=(s,e)=>RequestPreset(new PresetRequest{Kind="cpu",Value=0});
            Find<Button>("CpuOC01").Click+=(s,e)=>RequestPreset(new PresetRequest{Kind="cpu",Value=1});
            Find<Button>("CpuOC02").Click+=(s,e)=>RequestPreset(new PresetRequest{Kind="cpu",Value=2});
            Find<ComboBox>("Interval").SelectionChanged += (s,e) => {
                interval = new[] {1000,2000,5000}[Math.Max(0, Math.Min(2, Find<ComboBox>("Interval").SelectedIndex))];
                UpdateStatus();
            };
            Find<ScrollViewer>("Viewport").SizeChanged += (s,e) => FitDashboard();
            Find<Grid>("Dashboard").LayoutUpdated += FitHistoryHeight;
            window.KeyDown += (s,e) => { if (e.Key == System.Windows.Input.Key.F11) placement.ToggleFullscreen(); else if (e.Key == System.Windows.Input.Key.Escape && placement.IsFullscreen) placement.ToggleFullscreen(); };
            window.Closed += (s,e) => {
                cancellation.Cancel();clock.Stop();Find<Grid>("Dashboard").LayoutUpdated-=FitHistoryHeight;
                if(samplingTask==null)ReleaseSamplingResources();
                else samplingTask.ContinueWith(t=>ReleaseSamplingResources(),TaskScheduler.Default);
            };
            Find<Canvas>("CpuChart").SizeChanged += (s,e) => DrawCharts();
            Find<Canvas>("GpuChart").SizeChanged += (s,e) => DrawCharts();
            Find<Canvas>("DownloadChart").SizeChanged += (s,e) => DrawCharts();
            Find<Canvas>("UploadChart").SizeChanged += (s,e) => DrawCharts();
            Find<Canvas>("CpuChart").MouseMove += (s,e) => InspectChart(Find<Canvas>("CpuChart"),true,e);
            Find<Canvas>("GpuChart").MouseMove += (s,e) => InspectChart(Find<Canvas>("GpuChart"),false,e);
            Find<Canvas>("DownloadChart").MouseMove += (s,e) => InspectChart(Find<Canvas>("DownloadChart"),true,e,true);
            Find<Canvas>("UploadChart").MouseMove += (s,e) => InspectChart(Find<Canvas>("UploadChart"),false,e,true);
            if (Collector.IsAdmin) { Find<Button>("AdminButton").Content = "Sensors enabled"; Find<Button>("AdminButton").IsEnabled = false; }
            Text("Clock", DateTime.Now.ToString("HH:mm:ss"));
            clock.Interval = TimeSpan.FromSeconds(1);
            clock.Tick += (s,e) => {
                Text("Clock", DateTime.Now.ToString("HH:mm:ss") + (window.Topmost ? "  ·  Pinned" : ""));
                UpdateStatus();
            };
            window.Loaded += (s,e) => { if(samplingTask==null){clock.Start();samplingTask=Task.Run((Action)CollectLoop);} };
        }
        public void Run() { new Application().Run(window); }
        void ReleaseSamplingResources() { cancellation.Dispose(); }
        static string ModeName(int id) { return id==163?"Quiet":id==160?"Balanced":id==161?"Performance":null; }
        bool HasFreshSample()
        {
            double age=(DateTime.UtcNow-lastUpdate).TotalSeconds;
            return latest!=null && collectionError==null && age>=0 && age<=Math.Max(5,interval/1000.0*3);
        }
        void RequestMode(int id)
        {
            RequestPreset(new PresetRequest{Kind="preset",Value=id});
        }
        bool PresetFresh()
        {
            DateTime captured;
            return HasFreshSample() && latest.Preset!=null && DateTime.TryParse(latest.Preset.CapturedAt,null,DateTimeStyles.RoundtripKind,out captured) &&
                (DateTime.UtcNow-captured.ToUniversalTime()).TotalSeconds>=0 && (DateTime.UtcNow-captured.ToUniversalTime()).TotalSeconds<=Math.Max(8,interval/1000.0*3);
        }
        void RequestPreset(PresetRequest request)
        {
            if(modeBusy || !PresetFresh())return;
            var state=latest.Preset;
            PresetTarget target;
            try{target=PresetPolicy.Resolve(request,state);}catch{return;}
            if(latest.ThermalMode==null || latest.ThermalMode.Presets==null || !latest.ThermalMode.Presets.Any(p=>p.Id==target.Cooling))return;
            if(target.Matches(state))return;
            modeBusy=true;pendingRequest=request;
            presetNotice=null;
            Text("ModeNotice","Applying in background · Verifying CPU and cooling...");UpdateModeButtons();
            // Offscreen fixture/preview windows never launch a hardware worker.
            if(!window.IsVisible || !string.IsNullOrEmpty(preview))return;
            var handle=new System.Windows.Interop.WindowInteropHelper(window).Handle;
            Task.Run(()=>{
                PresetResult result;
                try{result=PresetWorker.Execute(request,handle);}catch(Exception e){result=new PresetResult{Message="Preset switch failed: "+e.Message};}
                PostIfOpen(()=>CompletePreset(result));
            });
        }
        void PostIfOpen(Action action)
        {
            if(cancellation.IsCancellationRequested || window.Dispatcher.HasShutdownStarted || window.Dispatcher.HasShutdownFinished)return;
            try{window.Dispatcher.BeginInvoke(new Action(()=>{if(!cancellation.IsCancellationRequested)action();}));}
            catch(InvalidOperationException){ } // The dispatcher may stop after the check.
        }
        void UpdateModeButtons()
        {
            var mode=latest==null?null:latest.ThermalMode;
            var state=latest==null?null:latest.Preset;
            bool fresh=PresetFresh();
            bool enabled=fresh && state.CanChange && mode!=null && mode.CanChange && !modeBusy;
            bool conflict=fresh && state.Cpu>0 && state.Cooling!=161;
            Text("ModeCurrent",!fresh || !state.CpuReadable || !state.PowerVerified?"Unverified":conflict?"Conflict":state.LinkedPreset.HasValue?"Linked":"Independent");
            Find<TextBlock>("ModeCurrent").ToolTip=state==null?"Waiting for CPU and cooling readback":state.Detail;
            foreach(int id in new[]{163,160,161}){
                string name=ModeName(id);
                bool supported=mode!=null && mode.Presets!=null && mode.Presets.Any(p=>p.Id==id);
                SetPresetButton("Mode"+name,name,enabled && supported,fresh && state.LinkedPreset==id,"preset",id,
                    "Apply the full AWCC "+name+" preset");
                SetPresetButton("Cooling"+name,name.Substring(0,1),enabled && supported && (id!=161 || state.Cpu.HasValue),fresh && state.Cooling==id,"cooling",id,
                    name+" cooling"+(id==161?" · Preserve CPU Off / OC01 / OC02":" · CPU OC switches Off first"));
            }
            SetPresetButton("CpuOff","Off",enabled && state.Cooling.HasValue && PresetPolicy.IsCooling(state.Cooling.Value),fresh && state.CpuReadable && state.Cpu==0,"cpu",0,"OC Off · Retain cooling and its BIOS power policy");
            SetPresetButton("CpuOC01","OC01",enabled && state.Cooling==161,fresh && state.CpuReadable && state.Cpu==1,"cpu",1,
                "CPU OC01 requires Performance cooling");
            SetPresetButton("CpuOC02","OC02",enabled && state.Cooling==161,fresh && state.CpuReadable && state.Cpu==2,"cpu",2,
                "CPU OC02 · Manual only · Requires Performance cooling");
            if(!modeBusy)Text("ModeNotice",state!=null && !state.CanChange?state.Detail:!fresh?"Waiting for fresh CPU and cooling readback":conflict?"CPU OC requires Performance cooling · Select a main preset":
                !state.CanChange || !state.Cpu.HasValue || !state.PowerVerified?state.Detail:presetNotice??"OC and cooling verified");
        }
        void SetPresetButton(string control,string label,bool enabled,bool current,string kind,int value,string hint)
        {
            var button=Find<Button>(control);
            bool pending=modeBusy && pendingRequest!=null && pendingRequest.Kind==kind && pendingRequest.Value==value;
            button.Tag=current?"Current":pending?"Pending":null;button.Content=label+(pending?"…":"");button.IsEnabled=enabled;
            button.ToolTip=modeBusy?"Waiting for CPU and cooling verification":hint;
            System.Windows.Automation.AutomationProperties.SetName(button,hint);
            System.Windows.Automation.AutomationProperties.SetItemStatus(button,current?"Current":pending?"Switching":enabled?"Available":"Unavailable");
        }
        void CompletePreset(PresetResult result)
        {
            CpuPresetReader.InvalidateAll();
            modeBusy=false;pendingRequest=null;
            presetNotice=result.Message;
            if(latest!=null){
                latest.Preset=result.Actual??new PresetState{CapturedAt=DateTime.UtcNow.ToString("o"),Detail=result.Message};
            }
            Text("ModeNotice",result.Message);UpdateModeButtons();UpdateCpuPower();
        }
        void FitDashboard()
        {
            var viewport = Find<ScrollViewer>("Viewport");
            bool compact=viewport.ActualHeight<900;
            var dashboard=Find<Grid>("Dashboard");
            dashboard.MinHeight = Math.Max(0, viewport.ActualHeight - (compact?24:32));
            if(compactLayout==compact)return;
            compactLayout=compact;
            dashboard.Margin=new Thickness(20,compact?12:16,20,compact?12:16);
            Find<Grid>("Header").Margin=new Thickness(0,0,0,compact?10:14);
            foreach(var name in new[]{"CpuCard","GpuCard","CpuHistoryCard","GpuHistoryCard","DownloadHistoryCard","UploadHistoryCard"})
                Find<Border>(name).Padding=new Thickness(compact?14:18,compact?8:14,compact?14:18,compact?8:14);
            Find<Border>("CoolingCard").Padding=new Thickness(14,compact?8:12,14,compact?8:12);
            foreach(var name in new[]{"CpuName","GpuName"})Find<TextBlock>(name).Margin=new Thickness(0,compact?3:5,0,compact?6:10);
            foreach(var name in new[]{"CpuLoad","CpuTemp","GpuLoad","GpuTemp"})Find<TextBlock>(name).FontSize=compact?44:48;
            foreach(var name in new[]{"CpuHint","GpuHint"})Find<TextBlock>(name).Margin=new Thickness(0,compact?5:10,0,0);
            foreach(var name in new[]{"CpuBar","GpuBar"})Find<ProgressBar>(name).Margin=new Thickness(0,compact?5:8,0,compact?8:12);
            foreach(var tile in fanTiles.Values.Concat(temperatureTiles.Values))tile.SetDensity(compact);
        }
        void FitHistoryHeight(object sender, EventArgs e)
        {
            var dashboard=Find<Grid>("Dashboard");
            double occupied=dashboard.RowDefinitions.Take(3).Sum(row=>row.ActualHeight);
            if(occupied<=0)return;
            double available=Find<ScrollViewer>("Viewport").ActualHeight-dashboard.Margin.Top-dashboard.Margin.Bottom-occupied-12;
            var panel=Find<Grid>("HistoryPanel");
            panel.MinHeight=compactLayout==true?158:174;
            double height=Math.Max(panel.MinHeight,Math.Min(compactLayout==true?158:220,Math.Floor(available/2)));
            if(Math.Abs(panel.Height-height)>0.5)panel.Height=height;
            var networkPanel=Find<Grid>("NetworkHistoryPanel");
            networkPanel.MinHeight=panel.MinHeight;
            if(Math.Abs(networkPanel.Height-height)>0.5)networkPanel.Height=height;
        }
        void UpdateWindowButtons()
        {
            Find<Button>("PinButton").Content=window.Topmost?"Pin: on":"Pin: off";
            Find<Button>("FullButton").Content=placement.IsFullscreen?"Exit fullscreen":"Fullscreen";
            appearance.Refresh();
        }
        static string PowerDisplay(double? current,double? limit,string currentFormat)
        {
            return Display(current,"",currentFormat)+" / "+Display(limit,"","0.##")+" W";
        }
        void UpdateCpuPower()
        {
            var cpu=latest==null?null:latest.Cpu;
            var preset=latest==null?null:latest.Preset;
            bool readable=PresetFresh() && preset.CpuReadable;
            double? limit=readable && preset.PowerLimitWatts>0?(double?)preset.PowerLimitWatts.Value:null;
            Text("CpuPower",PowerDisplay(cpu==null?null:cpu.PowerWatts,limit,"0.0"));
            Find<TextBlock>("CpuPower").ToolTip=(cpu==null?"CPU package power":cpu.PowerSource)+
                "\nLimit: current sustained package power limit (PL1), read from Intel XTU"+
                (readable && preset.ShortPowerLimitWatts>0?"\nShort boost limit (PL2): "+preset.ShortPowerLimitWatts.Value.ToString("0.##",CultureInfo.InvariantCulture)+" W":"")+
                (limit.HasValue?"":"\nLimit unavailable: waiting for fresh CPU readback");
        }
        void UpdateStatus()
        {
            UpdateModeButtons();UpdateCpuPower();
            if(collectionError!=null){
                Text("LiveState", "COLLECTION STOPPED");
                Find<Ellipse>("LiveDot").Fill = Brush("#E4B77A");
                Find<TextBlock>("LiveState").Foreground = Brush("#E4B77A");
                Text("Footer", "Collection unavailable · " + collectionError);
                return;
            }
            if (latest == null) return;
            double age = (DateTime.UtcNow - lastUpdate).TotalSeconds;
            bool stale = age > Math.Max(5, interval / 1000.0 * 3);
            Text("LiveState", stale ? "STALE DATA" : "LIVE");
            Find<Ellipse>("LiveDot").Fill = Brush(stale ? "#E4B77A" : "#50D7B2");
            Find<TextBlock>("LiveState").Foreground = Brush(stale ? "#E4B77A" : "#50D7B2");
            Text("Footer", stale ? "Last sample " + (int)age + " s ago · Waiting for sensors" :
                interval / 1000 + " s refresh · " + latest.SampleMilliseconds.ToString("0", CultureInfo.InvariantCulture) + " ms sample · Local only");
        }
        void CollectLoop()
        {
            try {
                using (var collector = new Collector(root))
                {
                    while (!cancellation.IsCancellationRequested)
                    {
                        var timer = Stopwatch.StartNew();
                        var snapshot = collector.Sample();
                        if (cancellation.IsCancellationRequested) break;
                        PostIfOpen(()=>Update(snapshot));
                        if (cancellation.Token.WaitHandle.WaitOne(Math.Max(100,interval-(int)timer.ElapsedMilliseconds))) break;
                    }
                }
            } catch(Exception ex) {
                PostIfOpen(()=> {
                        collectionError=ex.GetType().Name;
                        if(modeBusy)Text("ModeNotice","Preset switch could not be verified. Restart HardwareDeck to reconnect.");
                        UpdateStatus();
                    });
            }
        }
        void Update(Snapshot value)
        {
            DateTime previousPresetTime,incomingPresetTime;
            if(latest!=null && latest.Preset!=null && value.Preset!=null &&
               DateTime.TryParse(latest.Preset.CapturedAt,null,DateTimeStyles.RoundtripKind,out previousPresetTime) &&
               DateTime.TryParse(value.Preset.CapturedAt,null,DateTimeStyles.RoundtripKind,out incomingPresetTime) && incomingPresetTime<previousPresetTime)
                value.Preset=latest.Preset;
            if(!modeBusy && latest!=null && latest.Preset!=null && value.Preset!=null &&
               (latest.Preset.Cpu!=value.Preset.Cpu || latest.Preset.Cooling!=value.Preset.Cooling))presetNotice=null;
            latest=value; collectionError=null;
            DateTime captured;
            lastUpdate=DateTime.TryParse(value.CapturedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out captured)
                ? captured.ToUniversalTime() : DateTime.UtcNow;
            samples++;
            var temp=value.CpuTemperature;
            var gpu=value.Gpu ?? new GpuData();
            Text("MachineName",value.ComputerModel+" · Local hardware monitor");
            Text("CpuName",value.CpuName);
            var cpu=value.Cpu??new CpuTelemetry();
            Text("CpuFrequency",Display(cpu.FrequencyGHz," GHz","0.00"));
            Text("CpuVoltage",cpu.VoltageVolts.HasValue?Display(cpu.VoltageVolts," V","0.000"):"Unavailable");
            Find<TextBlock>("CpuFrequency").ToolTip=cpu.FrequencySource;
            Find<TextBlock>("CpuVoltage").ToolTip=cpu.VoltageSource;
            Text("CpuLoad",Display(value.CpuLoad," %","0"));
            Text("CpuTemp",Display(temp==null?null:temp.Value," °C","0"));
            Text("CpuTempLabel",temp==null?"CPU temperature unavailable":temp.Name);
            Text("CpuHint",value.CpuTemperatureSelection);
            Text("CpuChartTitle",(temp==null?"CPU temperature unavailable":temp.Name)+" · 120 s");
            Find<ProgressBar>("CpuBar").Value=value.CpuLoad??0;
            Text("GpuName",gpu.Name??"NVIDIA GPU");
            Text("GpuLoad",Display(gpu.Load," %","0"));
            Text("GpuTemp",Display(gpu.Temperature," °C","0.0"));
            Text("GpuHint",gpu.Error??"NVIDIA NVML · Fan speed is reported as a percentage");
            Text("GpuFrequency",Display(gpu.ClockMHz," MHz","0"));
            Text("GpuFan",Display(gpu.FanPercent," %","0"));
            Find<ProgressBar>("GpuBar").Value=gpu.Load??0;
            Text("Ram",Display(value.MemoryUsedGiB,"","0.0")+" / "+Display(value.MemoryTotalGiB,"","0.0"));
            Text("Vram",Display(gpu.MemoryUsedMiB.HasValue?gpu.MemoryUsedMiB/1024:null,"","0.0")+" / "+Display(gpu.MemoryTotalMiB.HasValue?gpu.MemoryTotalMiB/1024:null,"","0.0"));
            Text("Power",PowerDisplay(gpu.PowerWatts,gpu.PowerLimitWatts,"0"));
            Text("AwccState",value.AwccStatus);
            Find<TextBlock>("AwccState").Foreground=Brush(value.Sensors.Count>0?"#93A7B8":"#E4B77A");
            var fans=value.Sensors.Where(s=>s.Kind=="fan").ToList();
            Text("FanSummary",fans.Count(f=>f.Value.HasValue)+" / "+fans.Count+" fan RPM channels available");
            var fanKeys = fans.Select(f=>f.Id).Concat(new[]{"gpu.fan"}).ToList();
            if(fans.Count==0)fanKeys.Add("awcc.unavailable");
            ReconcileTiles(Find<UniformGrid>("FanPanel"), fanTiles, fanKeys, false);
            foreach(var fan in fans){
                var associated=value.Sensors.FirstOrDefault(s=>s.Kind=="temperature" && s.Id==fan.FanTemperatureSensorId);
                string relation=fan.FanSensorCode.HasValue?"Firmware association code: "+fan.FanSensorCode:"Firmware association unavailable";
                if(associated!=null)relation+="\nRelated temperature: "+associated.Name+" · "+associated.Id;
                relation+="\nThis does not identify the physical fan position.";
                fanTiles[fan.Id].Update(fan.Name,Display(fan.Value," RPM","0"),fan.Id+"\n"+relation,fan.Value,fan.Maximum,"#50D7B2");
            }
            fanTiles["gpu.fan"].Update("GPU fan",Display(gpu.FanPercent," %","0"),"NVIDIA · Driver-reported percentage",gpu.FanPercent,100,"#8EB8FF");
            if(fans.Count==0)fanTiles["awcc.unavailable"].Update("Chassis fans","—","Waiting for AWCC fan RPM",null,null,"#E4B77A");
            var extraTemperatures=value.Sensors.Where(s=>s.Kind=="temperature" && (temp==null || s.Id!=temp.Id)).ToList();
            ReconcileTiles(Find<UniformGrid>("TemperaturePanel"), temperatureTiles, extraTemperatures.Select(s=>s.Id).ToList(), true);
            foreach(var extra in extraTemperatures)
                temperatureTiles[extra.Id].Update(extra.Name,Display(extra.Value," °C","0"),extra.Id,null,null,"#DCE8F2");

            var network=value.Network??new NetworkData{Status="Unavailable",Detail="Waiting for network data",AdapterNames=new string[0]};
            string networkSource=network.Detail+"\n"+string.Join(", ",network.AdapterNames??new string[0])+"\nRates are bytes per second, including local-network traffic.";
            foreach(var name in new[]{"DownloadChartTitle","UploadChartTitle","DownloadChartCurrent","UploadChartCurrent"})
                Find<TextBlock>(name).ToolTip=networkSource;
            history.Add(new HistoryPoint{Time=lastUpdate,Cpu=temp==null?null:temp.Value,Gpu=gpu.Temperature,
                Download=network.DownloadBytesPerSecond,Upload=network.UploadBytesPerSecond});
            history.RemoveAll(p=>(lastUpdate-p.Time).TotalSeconds>120); DrawCharts();
            UpdateStatus();
            if(!previewSaved && !string.IsNullOrEmpty(preview) && samples>=3)
            {
                window.UpdateLayout();
                var surface=Find<ScrollViewer>("Viewport");
                var dpi=PresentationSource.FromVisual(window).CompositionTarget.TransformToDevice;
                var bitmap=new RenderTargetBitmap((int)Math.Ceiling(surface.ActualWidth*dpi.M11),(int)Math.Ceiling(surface.ActualHeight*dpi.M22),96*dpi.M11,96*dpi.M22,PixelFormats.Pbgra32);
                bitmap.Render(surface);
                var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using(var stream=File.Create(preview)) encoder.Save(stream);
                previewSaved=true;
                if(!stay) window.Close();
            }
        }
        sealed class SensorTile
        {
            public readonly Border Surface;
            readonly TextBlock label, reading;
            readonly ProgressBar bar;
            readonly bool temperatureTile;
            public SensorTile(bool compact)
            {
                temperatureTile=compact;
                label=new TextBlock{Foreground=Brush("#9CAEBB"),FontSize=11,TextTrimming=TextTrimming.CharacterEllipsis};
                reading=new TextBlock{FontSize=compact?14:23,FontWeight=FontWeights.Bold,Margin=new Thickness(0,compact?2:4,0,0),TextTrimming=TextTrimming.CharacterEllipsis};
                bar=new ProgressBar{Maximum=100,Height=3,Margin=new Thickness(0,6,0,0),Background=Brush("#253442"),Visibility=compact?Visibility.Collapsed:Visibility.Hidden};
                var grid=new Grid();grid.ColumnDefinitions.Add(new ColumnDefinition());grid.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
                for(int i=0;i<3;i++)grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
                grid.Children.Add(label);grid.Children.Add(reading);grid.Children.Add(bar);
                Grid.SetRow(reading,1);Grid.SetRow(bar,2);Grid.SetColumnSpan(label,2);Grid.SetColumnSpan(reading,2);Grid.SetColumnSpan(bar,2);
                Surface=new Border{Child=grid,Margin=new Thickness(4,0,4,0),CornerRadius=new CornerRadius(7),Background=Brush(compact?"#162631":"#0D161F")};
                SetDensity(false);
            }
            public void SetDensity(bool dense)
            {
                Surface.Padding=new Thickness(10,temperatureTile?5:dense?5:8,10,temperatureTile?5:dense?5:8);
                reading.FontSize=temperatureTile?14:dense?21:23;
                reading.Margin=new Thickness(0,temperatureTile?(dense?0:2):(dense?2:4),0,0);
                bar.Margin=new Thickness(0,dense?4:6,0,0);
                if(temperatureTile){
                    Grid.SetRow(reading,dense?0:1);Grid.SetColumn(reading,dense?1:0);
                    Grid.SetColumnSpan(label,dense?1:2);Grid.SetColumnSpan(reading,dense?1:2);
                    label.Margin=new Thickness(0,0,dense?5:0,0);label.VerticalAlignment=VerticalAlignment.Center;
                }
            }
            public void Update(string name,string value,string source,double? current,double? maximum,string accent)
            {
                label.Text=name; reading.Text=value; reading.Foreground=Brush(accent); Surface.ToolTip=name+"\n"+source;
                if(bar.Visibility!=Visibility.Collapsed){
                    bar.Visibility=current.HasValue && maximum.HasValue && maximum>0?Visibility.Visible:Visibility.Hidden;
                    bar.Value=bar.Visibility==Visibility.Visible?Math.Max(0,Math.Min(100,current.Value/maximum.Value*100)):0;
                    bar.Foreground=Brush(accent);
                }
            }
        }
        void ReconcileTiles(UniformGrid panel,Dictionary<string,SensorTile> tiles,List<string> keys,bool compact)
        {
            panel.Visibility=keys.Count==0?Visibility.Collapsed:Visibility.Visible;
            if(panel.Children.Count==keys.Count && keys.Select((key,index)=>tiles.ContainsKey(key) && ReferenceEquals(panel.Children[index],tiles[key].Surface)).All(s=>s))return;
            panel.Children.Clear();
            foreach(var removed in tiles.Keys.Except(keys).ToList())tiles.Remove(removed);
            foreach(var key in keys){
                SensorTile tile;
                if(!tiles.TryGetValue(key,out tile)){
                    tiles[key]=tile=new SensorTile(compact);
                    tile.SetDensity(compactLayout==true);
                }
                panel.Children.Add(tile.Surface);
            }
            panel.Rows=Math.Max(1,(int)Math.Ceiling(keys.Count/(compact?9.0:6.0)));
        }
        void DrawCharts()
        {
            if(history.Count==0)return;
            Draw(Find<Canvas>("CpuChart"),true,"#50D7B2");
            Draw(Find<Canvas>("GpuChart"),false,"#8EB8FF");
            Draw(Find<Canvas>("DownloadChart"),true,"#8EB8FF",true);
            Draw(Find<Canvas>("UploadChart"),false,"#50D7B2",true);
        }
        void ChartText(Canvas canvas,string text,double x,double y,string color,double size)
        {
            var label=new TextBlock{Text=text,Foreground=Brush(color),FontSize=size};
            Canvas.SetLeft(label,x);Canvas.SetTop(label,y);canvas.Children.Add(label);
        }
        static double? ChartValue(HistoryPoint point,bool first,bool network)
        {
            return network?(first?point.Download:point.Upload):(first?point.Cpu:point.Gpu);
        }
        void Draw(Canvas canvas,bool first,string color,bool network=false)
        {
            canvas.Children.Clear();
            double width=canvas.ActualWidth,height=canvas.ActualHeight;
            const double left=40;
            const double right=12,top=16,bottom=26;
            double plotWidth=width-left-right,plotHeight=height-top-bottom;
            if(plotWidth<=0 || plotHeight<=0)return;
            var valid=history.Select(p=>ChartValue(p,first,network)).Where(v=>v.HasValue).Select(v=>v.Value).ToList();
            double peak=valid.Count==0?0:valid.Max();
            double maximum=network?NetworkRates.AxisMaximum(peak):Math.Max(120,Math.Ceiling(peak/30)*30);
            double divisor=network?NetworkRates.Divisor(maximum):1;
            ChartText(canvas,network?NetworkRates.Unit(divisor):"°C",0,0,"#93A7B8",10);
            int steps=network?4:(int)(maximum/30);
            for(int step=0;step<=steps;step++){
                double level=maximum*step/steps;
                double y=top+plotHeight*(1-level/maximum);
                canvas.Children.Add(new Line{X1=left,X2=left+plotWidth,Y1=y,Y2=y,Stroke=Brush("#243746"),StrokeThickness=1});
                ChartText(canvas,(level/divisor).ToString(network?"0.##":"0",CultureInfo.InvariantCulture),3,y-7,"#93A7B8",10);
            }
            for(int seconds=120;seconds>=0;seconds-=30){
                double x=left+plotWidth*(1-seconds/120.0);
                canvas.Children.Add(new Line{X1=x,X2=x,Y1=top,Y2=top+plotHeight,Stroke=Brush("#1B2A36"),StrokeThickness=1});
                var label=new TextBlock{Text=seconds==0?"Now":seconds+" s ago",Foreground=Brush("#93A7B8"),FontSize=10};
                label.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));
                Canvas.SetLeft(label,Math.Max(0,Math.Min(width-label.DesiredSize.Width,x-label.DesiredSize.Width/2)));
                Canvas.SetTop(label,top+plotHeight+7);canvas.Children.Add(label);
            }
            var line=new Polyline{Stroke=Brush(color),StrokeThickness=2};
            foreach(var point in history){
                double? value=ChartValue(point,first,network);
                if(!value.HasValue){
                    if(line.Points.Count>1)canvas.Children.Add(line);
                    line=new Polyline{Stroke=Brush(color),StrokeThickness=2};continue;
                }
                double age=(lastUpdate-point.Time).TotalSeconds;
                if(age<0 || age>120)continue;
                line.Points.Add(new Point(left+plotWidth*(1-age/120),top+plotHeight*(1-value.Value/maximum)));
            }
            if(line.Points.Count>1)canvas.Children.Add(line);
            var latestPoint=history.LastOrDefault();
            double? current=latestPoint==null?null:ChartValue(latestPoint,first,network);
            string prefix=(network?(first?"Download":"Upload"):(first?"Cpu":"Gpu"))+"Chart";
            Text(prefix+"Current",network?NetworkRates.Format(current):Display(current," °C","0.0"));
            if(network){
                var info=latest==null?null:latest.Network;
                int count=info==null?0:info.AdapterCount;
                Text(prefix+"Summary",current.HasValue?"Peak "+NetworkRates.Format(peak)+" · "+count+(count==1?" adapter":" adapters")+" · Hover to inspect":
                    (info==null?"Waiting for network data":info.Detail));
                Find<TextBlock>(prefix+"Current").ToolTip=Find<TextBlock>(prefix+"Title").ToolTip;
                Find<TextBlock>(prefix+"Summary").ToolTip=Find<TextBlock>(prefix+"Title").ToolTip;
            }else Text(prefix+"Summary",valid.Count==0?"No temperature samples":
                "Min "+valid.Min().ToString("0.0")+" °C   ·   Max "+peak.ToString("0.0")+" °C   ·   Hover to inspect");
            if(current.HasValue){
                var marker=new Ellipse{Width=6,Height=6,Fill=Brush(color)};
                Canvas.SetLeft(marker,left+plotWidth-3);Canvas.SetTop(marker,top+plotHeight*(1-current.Value/maximum)-3);canvas.Children.Add(marker);
            }
        }
        void InspectChart(Canvas canvas,bool first,System.Windows.Input.MouseEventArgs e,bool network=false)
        {
            if(history.Count==0)return;
            const double left=40;
            double fraction=Math.Max(0,Math.Min(1,(e.GetPosition(canvas).X-left)/Math.Max(1,canvas.ActualWidth-left-12)));
            var target=lastUpdate.AddSeconds(-120*(1-fraction));
            var point=history.OrderBy(p=>Math.Abs((p.Time-target).TotalSeconds)).First();
            // Do not present the oldest point as a sample for an unrecorded part of the chart.
            if(target<history[0].Time.AddMilliseconds(-interval) || Math.Abs((point.Time-target).TotalMilliseconds)>interval*1.5){
                canvas.ToolTip="No sample at this time";return;
            }
            double? value=ChartValue(point,first,network);
            canvas.ToolTip=point.Time.ToLocalTime().ToString("HH:mm:ss")+"  ·  "+(network?NetworkRates.Format(value):Display(value," °C","0.0"));
        }
        void Elevate()
        {
            try {
                placement.SaveNow();
                using(var launched=Process.Start(new ProcessStartInfo(typeof(Program).Assembly.Location){UseShellExecute=true,Verb="runas",WorkingDirectory=root})) { }
                window.Close();
            }catch(System.ComponentModel.Win32Exception){Text("Footer","Administrator access was not granted. Basic metrics remain available.");}
        }
    }
    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            CultureInfo.DefaultThreadCurrentCulture=CultureInfo.GetCultureInfo("en-US");
            CultureInfo.DefaultThreadCurrentUICulture=CultureInfo.GetCultureInfo("en-US");
            string root=AppDomain.CurrentDomain.BaseDirectory;
            bool worker=args.Length==1 && args[0]=="--preset-worker";
            bool probe=args.Length==2 && args[0]=="--probe" && !string.IsNullOrWhiteSpace(args[1]);
            bool preview=(args.Length==2 || (args.Length==3 && args[2]=="--stay")) &&
                args[0]=="--preview" && !string.IsNullOrWhiteSpace(args[1]);
            if(args.Length!=0 && !probe && !preview && !worker)return 2;
            try {
                if(worker)return PresetWorker.Run();
                if(probe)
                {
                    using(var collector=new Collector(root)){
                        collector.Sample();Thread.Sleep(1000);
                        collector.Sample();Thread.Sleep(1000);
                        SnapshotFile.Write(args[1],collector.Sample());
                    }
                    return 0;
                }
                new DeckWindow(root,preview?args[1]:null,preview && args.Length==3).Run();
                return 0;
            }catch(Exception ex){
                try{SnapshotFile.Write(System.IO.Path.Combine(SnapshotFile.DataDirectory,"error.json"),new{Error=ex.ToString(),At=DateTime.UtcNow.ToString("o")});}catch{}
                if(args.Length==0)MessageBox.Show("HardwareDeck could not start.\n"+ex.Message,"HardwareDeck",MessageBoxButton.OK,MessageBoxImage.Error);
                return 1;
            }
        }
    }
}
