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
using Forms = System.Windows.Forms;

namespace HardwareDeck
{
    public sealed class HistoryPoint { public DateTime Time; public double? Cpu, Gpu; }
    public sealed class DeckWindow
    {
        readonly Window window;
        readonly string root, preview;
        readonly bool stay;
        bool previewSaved;
        readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        readonly List<HistoryPoint> history = new List<HistoryPoint>();
        DateTime lastUpdate = DateTime.MinValue;
        Snapshot latest;
        volatile int interval = 1000;
        bool full;
        WindowState previousState;
        Rect previousBounds;
        ResizeMode previousResizeMode;
        int samples;
        int requestedMode=-1;
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
        public DeckWindow(string root, string preview, bool stay)
        {
            this.root = root; this.preview = preview; this.stay=stay;
            using (var reader = System.Xml.XmlReader.Create(System.IO.Path.Combine(root, "View.xaml")))
                window = (Window)XamlReader.Load(reader);
            Find<Button>("AdminButton").Click += (s,e) => Elevate();
            Find<Button>("ScreenButton").Click += (s,e) => NextScreen();
            Find<Button>("PinButton").Click += (s,e) => { window.Topmost = !window.Topmost; Find<Button>("PinButton").Content = window.Topmost ? "Pin: on" : "Pin: off"; };
            Find<Button>("FullButton").Click += (s,e) => ToggleFull();
            Find<Button>("ModeApply").Click += (s,e) => {
                var chosen=Find<ComboBox>("ModeSelect").SelectedItem as ThermalPreset;
                if(chosen==null || modeBusy || latest==null || latest.ThermalMode==null || !latest.ThermalMode.CanChange)return;
                modeBusy=true;Find<Button>("ModeApply").IsEnabled=false;
                Text("ModeNotice","Applying preset and verifying firmware readback...");
                Interlocked.Exchange(ref requestedMode,chosen.Id);
            };
            Find<ComboBox>("Interval").SelectionChanged += (s,e) => {
                interval = new[] {1000,2000,5000}[Math.Max(0, Math.Min(2, Find<ComboBox>("Interval").SelectedIndex))];
                UpdateStatus();
            };
            Find<ScrollViewer>("Viewport").SizeChanged += (s,e) => FitDashboard();
            window.SourceInitialized += (s,e) => FitWindow();
            window.KeyDown += (s,e) => { if (e.Key == System.Windows.Input.Key.F11) ToggleFull(); else if (e.Key == System.Windows.Input.Key.Escape && full) ToggleFull(); };
            window.Closed += (s,e) => { cancellation.Cancel(); clock.Stop(); };
            Find<Canvas>("CpuChart").SizeChanged += (s,e) => DrawCharts();
            Find<Canvas>("GpuChart").SizeChanged += (s,e) => DrawCharts();
            Find<Canvas>("CpuChart").MouseMove += (s,e) => InspectChart(Find<Canvas>("CpuChart"),true,e);
            Find<Canvas>("GpuChart").MouseMove += (s,e) => InspectChart(Find<Canvas>("GpuChart"),false,e);
            if (Collector.IsAdmin) { Find<Button>("AdminButton").Content = "Sensors enabled"; Find<Button>("AdminButton").IsEnabled = false; }
            Text("Clock", DateTime.Now.ToString("HH:mm:ss"));
            clock.Interval = TimeSpan.FromSeconds(1);
            clock.Tick += (s,e) => {
                Text("Clock", DateTime.Now.ToString("HH:mm:ss") + (window.Topmost ? "  ·  Pinned" : ""));
                UpdateStatus();
            };
            window.Loaded += (s,e) => { clock.Start(); Task.Run((Action)CollectLoop); };
        }
        public void Run() { new Application().Run(window); }
        void FitDashboard()
        {
            var viewport = Find<ScrollViewer>("Viewport");
            bool compact=viewport.ActualHeight<760;
            var dashboard=Find<Grid>("Dashboard");
            dashboard.MinHeight = Math.Max(0, viewport.ActualHeight - (compact?24:32));
            Find<Grid>("HistoryPanel").Height=compact?158:220;
            if(compactLayout==compact)return;
            compactLayout=compact;
            dashboard.Margin=new Thickness(20,compact?12:16,20,compact?12:16);
            Find<Grid>("Header").Margin=new Thickness(0,0,0,compact?10:14);
            foreach(var name in new[]{"CpuCard","GpuCard","CpuHistoryCard","GpuHistoryCard"})
                Find<Border>(name).Padding=new Thickness(compact?14:18,compact?8:14,compact?14:18,compact?8:14);
            Find<Border>("CoolingCard").Padding=new Thickness(14,compact?8:12,14,compact?8:12);
            foreach(var name in new[]{"CpuName","GpuName"})Find<TextBlock>(name).Margin=new Thickness(0,compact?3:5,0,compact?6:10);
            foreach(var name in new[]{"CpuLoad","CpuTemp","GpuLoad","GpuTemp"})Find<TextBlock>(name).FontSize=compact?44:48;
            foreach(var name in new[]{"CpuHint","GpuHint"})Find<TextBlock>(name).Margin=new Thickness(0,compact?5:10,0,0);
            foreach(var name in new[]{"CpuBar","GpuBar"})Find<ProgressBar>(name).Margin=new Thickness(0,compact?5:8,0,compact?8:12);
            foreach(var tile in fanTiles.Values.Concat(temperatureTiles.Values))tile.SetDensity(compact);
        }
        void FitWindow()
        {
            var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            var area = Forms.Screen.FromHandle(handle).WorkingArea;
            var source = PresentationSource.FromVisual(window);
            var scale = source == null ? Matrix.Identity : source.CompositionTarget.TransformToDevice;
            window.Width = Math.Min(window.Width, area.Width / scale.M11 * 0.96);
            window.Height = Math.Min(window.Height, area.Height / scale.M22 * 0.96);
        }
        void UpdateStatus()
        {
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
                        int mode=Interlocked.Exchange(ref requestedMode,-1);
                        if(mode>=0){
                            var result=collector.SetThermalMode(mode);
                            window.Dispatcher.BeginInvoke(new Action(()=>{modeBusy=false;Text("ModeNotice",result.Message);}));
                        }
                        var snapshot = collector.Sample();
                        if (cancellation.IsCancellationRequested) break;
                        window.Dispatcher.BeginInvoke(new Action(() => Update(snapshot)));
                        if (cancellation.Token.WaitHandle.WaitOne(Math.Max(100,interval-(int)timer.ElapsedMilliseconds))) break;
                    }
                }
            } catch(Exception ex) {
                if (!cancellation.IsCancellationRequested)
                    window.Dispatcher.BeginInvoke(new Action(()=> {
                        collectionError=ex.GetType().Name;
                        UpdateStatus();
                    }));
            }
        }
        void Update(Snapshot value)
        {
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
            Text("CpuPower",Display(cpu.PowerWatts," W","0.0"));
            Text("CpuVoltage",cpu.VoltageVolts.HasValue?Display(cpu.VoltageVolts," V","0.000"):"Unavailable");
            Find<TextBlock>("CpuFrequency").ToolTip=cpu.FrequencySource;
            Find<TextBlock>("CpuPower").ToolTip=cpu.PowerSource;
            Find<TextBlock>("CpuVoltage").ToolTip=cpu.VoltageSource;
            var mode=value.ThermalMode??new ThermalModeState{Presets=new List<ThermalPreset>(),CurrentName="Disconnected"};
            Text("ModeCurrent","Current: "+mode.CurrentName);
            var select=Find<ComboBox>("ModeSelect");
            if(!select.Items.Cast<ThermalPreset>().Select(p=>p.Id).SequenceEqual(mode.Presets.Select(p=>p.Id))){
                var chosen=select.SelectedItem as ThermalPreset;
                select.ItemsSource=mode.Presets;
                select.SelectedItem=mode.Presets.FirstOrDefault(p=>chosen!=null && p.Id==chosen.Id)??
                    mode.Presets.FirstOrDefault(p=>p.Id==mode.CurrentId)??mode.Presets.FirstOrDefault();
            }
            select.IsEnabled=mode.CanChange&&!modeBusy;
            Find<Button>("ModeApply").IsEnabled=mode.CanChange&&!modeBusy;
            Find<TextBlock>("ModeCurrent").ToolTip=mode.Detail;
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
            Text("Power",Display(gpu.PowerWatts,"","0")+" / "+Display(gpu.PowerLimitWatts,"","0"));
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
                temperatureTiles[extra.Id].Update(extra.Name,Display(extra.Value," °C","0"),extra.Id,null,null,"#B7C9D5");

            history.Add(new HistoryPoint{Time=lastUpdate,Cpu=temp==null?null:temp.Value,Gpu=gpu.Temperature});
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
                reading=new TextBlock{FontSize=compact?14:23,Margin=new Thickness(0,compact?2:4,0,0),TextTrimming=TextTrimming.CharacterEllipsis};
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
        }
        void ChartText(Canvas canvas,string text,double x,double y,string color,double size)
        {
            var label=new TextBlock{Text=text,Foreground=Brush(color),FontSize=size};
            Canvas.SetLeft(label,x);Canvas.SetTop(label,y);canvas.Children.Add(label);
        }
        void Draw(Canvas canvas,bool cpu,string color)
        {
            canvas.Children.Clear();
            double width=canvas.ActualWidth,height=canvas.ActualHeight;
            const double left=40,right=12,top=16,bottom=26;
            double plotWidth=width-left-right,plotHeight=height-top-bottom;
            if(plotWidth<=0 || plotHeight<=0)return;
            var valid=history.Select(p=>cpu?p.Cpu:p.Gpu).Where(v=>v.HasValue).Select(v=>v.Value).ToList();
            double maximum=Math.Max(120,valid.Count==0?120:Math.Ceiling(valid.Max()/30)*30);
            ChartText(canvas,"°C",0,0,"#93A7B8",10);
            for(int degree=0;degree<=maximum;degree+=30){
                double y=top+plotHeight*(1-degree/maximum);
                canvas.Children.Add(new Line{X1=left,X2=left+plotWidth,Y1=y,Y2=y,Stroke=Brush("#243746"),StrokeThickness=1});
                ChartText(canvas,degree.ToString(),3,y-7,"#93A7B8",10);
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
                double? value=cpu?point.Cpu:point.Gpu;
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
            double? current=latestPoint==null?null:(cpu?latestPoint.Cpu:latestPoint.Gpu);
            Text(cpu?"CpuChartCurrent":"GpuChartCurrent",Display(current," °C","0.0"));
            Text(cpu?"CpuChartSummary":"GpuChartSummary",valid.Count==0?"No temperature samples":
                "Min "+valid.Min().ToString("0.0")+" °C   ·   Max "+valid.Max().ToString("0.0")+" °C   ·   Hover to inspect");
            if(current.HasValue){
                var marker=new Ellipse{Width=6,Height=6,Fill=Brush(color)};
                Canvas.SetLeft(marker,left+plotWidth-3);Canvas.SetTop(marker,top+plotHeight*(1-current.Value/maximum)-3);canvas.Children.Add(marker);
            }
        }
        void InspectChart(Canvas canvas,bool cpu,System.Windows.Input.MouseEventArgs e)
        {
            if(history.Count==0)return;
            double fraction=Math.Max(0,Math.Min(1,(e.GetPosition(canvas).X-40)/Math.Max(1,canvas.ActualWidth-52)));
            var target=lastUpdate.AddSeconds(-120*(1-fraction));
            var point=history.OrderBy(p=>Math.Abs((p.Time-target).TotalSeconds)).First();
            // Do not present the oldest point as a sample for an unrecorded part of the chart.
            if(target<history[0].Time.AddMilliseconds(-interval) || Math.Abs((point.Time-target).TotalMilliseconds)>interval*1.5){
                canvas.ToolTip="No sample at this time";return;
            }
            canvas.ToolTip=point.Time.ToLocalTime().ToString("HH:mm:ss")+"  ·  "+Display(cpu?point.Cpu:point.Gpu," °C","0.0");
        }
        void Elevate()
        {
            try {
                using(var launched=Process.Start(new ProcessStartInfo(typeof(Program).Assembly.Location){UseShellExecute=true,Verb="runas",WorkingDirectory=root})) { }
                window.Close();
            }catch(System.ComponentModel.Win32Exception){Text("Footer","Administrator access was not granted. Basic metrics remain available.");}
        }
        void ToggleFull()
        {
            if(!full){
                previousState=window.WindowState;
                previousBounds=window.WindowState==WindowState.Normal?new Rect(window.Left,window.Top,window.Width,window.Height):window.RestoreBounds;
                previousResizeMode=window.ResizeMode;
                var handle=new System.Windows.Interop.WindowInteropHelper(window).Handle;
                var bounds=Forms.Screen.FromHandle(handle).Bounds;
                window.WindowState=WindowState.Normal;window.WindowStyle=WindowStyle.None;window.ResizeMode=ResizeMode.NoResize;
                SetWindowPos(handle,IntPtr.Zero,bounds.Left,bounds.Top,bounds.Width,bounds.Height,0x0004);
                full=true;
            }
            else {
                window.WindowState=WindowState.Normal;window.WindowStyle=WindowStyle.SingleBorderWindow;window.ResizeMode=previousResizeMode;
                window.Left=previousBounds.Left;window.Top=previousBounds.Top;window.Width=previousBounds.Width;window.Height=previousBounds.Height;
                window.WindowState=previousState;full=false;
            }
            Find<Button>("FullButton").Content=full?"Exit fullscreen":"Fullscreen";
        }
        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError=true)]
        static extern bool SetWindowPos(IntPtr handle,IntPtr after,int x,int y,int width,int height,uint flags);
        void NextScreen()
        {
            var screens=Forms.Screen.AllScreens;
            var helper=new System.Windows.Interop.WindowInteropHelper(window);
            var current=Forms.Screen.FromHandle(helper.Handle);
            int index=Array.FindIndex(screens,s=>s.DeviceName==current.DeviceName);
            var bounds=screens[(index+1)%screens.Length].WorkingArea;
            bool wasFull=full;bool wasMaximized=(full?previousState:window.WindowState)==WindowState.Maximized;if(wasFull)ToggleFull();
            window.WindowState=WindowState.Normal;
            int inset=Math.Max(12,Math.Min(bounds.Width,bounds.Height)/50);
            SetWindowPos(helper.Handle,IntPtr.Zero,bounds.Left+inset,bounds.Top+inset,
                bounds.Width-inset*2,bounds.Height-inset*2,0x0004);

            if(wasMaximized)window.WindowState=WindowState.Maximized;
            if(wasFull)ToggleFull();
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
            bool probe=args.Length==2 && args[0]=="--probe" && !string.IsNullOrWhiteSpace(args[1]);
            bool preview=(args.Length==2 || (args.Length==3 && args[2]=="--stay")) &&
                args[0]=="--preview" && !string.IsNullOrWhiteSpace(args[1]);
            if(args.Length!=0 && !probe && !preview)return 2;
            try {
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
