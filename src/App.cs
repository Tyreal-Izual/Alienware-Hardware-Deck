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
        volatile int interval = 2000;
        bool full;
        WindowState previousState;
        Rect previousBounds;
        int samples;
        int requestedMode=-1;
        bool modeBusy;
        readonly DispatcherTimer clock = new DispatcherTimer();
        T Find<T>(string name) where T : class { return window.FindName(name) as T; }
        static Brush Brush(string hex) { return (SolidColorBrush)new BrushConverter().ConvertFromString(hex); }
        static string Display(double? value, string unit, string format) { return value.HasValue ? value.Value.ToString(format, CultureInfo.InvariantCulture) + unit : "—"; }
        void Text(string name, string value) { Find<TextBlock>(name).Text = value; }
        public DeckWindow(string root, string preview, bool stay)
        {
            this.root = root; this.preview = preview; this.stay=stay;
            using (var reader = System.Xml.XmlReader.Create(System.IO.Path.Combine(root, "View.xaml")))
                window = (Window)XamlReader.Load(reader);
            Find<Button>("AdminButton").Click += (s,e) => Elevate();
            Find<Button>("ScreenButton").Click += (s,e) => NextScreen();
            Find<Button>("PinButton").Click += (s,e) => { window.Topmost = !window.Topmost; Find<Button>("PinButton").Content = window.Topmost ? "置顶：开" : "置顶：关"; };
            Find<Button>("FullButton").Click += (s,e) => ToggleFull();
            Find<Button>("ModeApply").Click += (s,e) => {
                var chosen=Find<ComboBox>("ModeSelect").SelectedItem as ThermalPreset;
                if(chosen==null || modeBusy || latest==null || latest.ThermalMode==null || !latest.ThermalMode.CanChange)return;
                modeBusy=true;Find<Button>("ModeApply").IsEnabled=false;
                Text("ModeNotice","正在切换并等待固件读回…");
                Interlocked.Exchange(ref requestedMode,chosen.Id);
            };
            Find<ComboBox>("Interval").SelectionChanged += (s,e) => { interval = new[] {1000,2000,5000}[Math.Max(0,Find<ComboBox>("Interval").SelectedIndex)]; };
            window.KeyDown += (s,e) => { if (e.Key == System.Windows.Input.Key.F11) ToggleFull(); else if (e.Key == System.Windows.Input.Key.Escape && full) ToggleFull(); };
            window.Closed += (s,e) => { cancellation.Cancel(); clock.Stop(); };
            Find<Canvas>("CpuChart").SizeChanged += (s,e) => DrawCharts();
            Find<Canvas>("GpuChart").SizeChanged += (s,e) => DrawCharts();
            Find<Canvas>("CpuChart").MouseMove += (s,e) => InspectChart(Find<Canvas>("CpuChart"),true,e);
            Find<Canvas>("GpuChart").MouseMove += (s,e) => InspectChart(Find<Canvas>("GpuChart"),false,e);
            if (Collector.IsAdmin) { Find<Button>("AdminButton").Content = "传感器权限已启用"; Find<Button>("AdminButton").IsEnabled = false; }
            clock.Interval = TimeSpan.FromSeconds(1);
            clock.Tick += (s,e) => {
                Text("Clock", DateTime.Now.ToString("HH:mm:ss") + (window.Topmost ? "  ·  置顶" : ""));
                if (lastUpdate != DateTime.MinValue && (DateTime.UtcNow-lastUpdate).TotalMilliseconds > Math.Max(8000,interval*3))
                    Text("Footer","采集延迟 · 上次更新 "+(int)(DateTime.UtcNow-lastUpdate).TotalSeconds+" 秒前");
            };
            window.Loaded += (s,e) => { clock.Start(); Task.Run((Action)CollectLoop); };
        }
        public void Run() { new Application().Run(window); }
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
                    window.Dispatcher.BeginInvoke(new Action(()=>Text("Footer","采集暂不可用 · "+ex.GetType().Name)));
            }
        }
        void Update(Snapshot value)
        {
            latest=value; lastUpdate=DateTime.UtcNow; samples++;
            var temp=value.CpuTemperature;
            var gpu=value.Gpu ?? new GpuData();
            Text("MachineName",value.ComputerModel+"  ·  本机实时监测");
            Text("CpuName",value.CpuName);
            var cpu=value.Cpu??new CpuTelemetry();
            Text("CpuFrequency",Display(cpu.FrequencyGHz," GHz","0.00"));
            Text("CpuPower",Display(cpu.PowerWatts," W","0.0"));
            Text("CpuVoltage",cpu.VoltageVolts.HasValue?Display(cpu.VoltageVolts," V","0.000"):"未提供");
            Find<TextBlock>("CpuFrequency").ToolTip=cpu.FrequencySource;
            Find<TextBlock>("CpuPower").ToolTip=cpu.PowerSource;
            Find<TextBlock>("CpuVoltage").ToolTip=cpu.VoltageSource;
            var mode=value.ThermalMode??new ThermalModeState{Presets=new List<ThermalPreset>(),CurrentName="未连接"};
            Text("ModeCurrent","当前："+mode.CurrentName);
            var select=Find<ComboBox>("ModeSelect");
            if(select.Items.Count!=mode.Presets.Count){
                select.ItemsSource=mode.Presets;
                select.SelectedItem=mode.Presets.FirstOrDefault(p=>p.Id==mode.CurrentId)??mode.Presets.FirstOrDefault();
            }
            select.IsEnabled=mode.CanChange&&!modeBusy;
            Find<Button>("ModeApply").IsEnabled=mode.CanChange&&!modeBusy;
            Find<TextBlock>("ModeCurrent").ToolTip=mode.Detail;
            Text("CpuLoad",Display(value.CpuLoad," %","0"));
            Text("CpuTemp",Display(temp==null?null:temp.Value," °C","0"));
            Text("CpuTempLabel",temp==null?"CPU 温度源不可用":temp.Name);
            Text("CpuHint",value.CpuTemperatureSelection);
            Text("CpuChartTitle",(temp==null?"CPU 温度源不可用":temp.Name)+" · 最近 120 秒");
            Find<ProgressBar>("CpuBar").Value=value.CpuLoad??0;
            Text("GpuName",gpu.Name??"NVIDIA GPU");
            Text("GpuLoad",Display(gpu.Load," %","0"));
            Text("GpuTemp",Display(gpu.Temperature," °C","0.0"));
            Text("GpuHint",gpu.Error??("NVIDIA NVML · "+Display(gpu.ClockMHz," MHz","0")+" · 风扇 "+Display(gpu.FanPercent," %","0")));
            Find<ProgressBar>("GpuBar").Value=gpu.Load??0;
            Text("Ram",Display(value.MemoryUsedGiB,"","0.0")+" / "+Display(value.MemoryTotalGiB," GB","0.0"));
            Text("Vram",Display(gpu.MemoryUsedMiB.HasValue?gpu.MemoryUsedMiB/1024:null,"","0.0")+" / "+Display(gpu.MemoryTotalMiB.HasValue?gpu.MemoryTotalMiB/1024:null," GB","0.0"));
            Text("Power",Display(gpu.PowerWatts," W","0")+" / "+Display(gpu.PowerLimitWatts," W","0"));
            Text("AwccState",value.AwccStatus);
            Find<TextBlock>("AwccState").Foreground=Brush(value.Sensors.Count>0?"#93A7B8":"#E4B77A");
            var fans=value.Sensors.Where(s=>s.Kind=="fan").ToList();
            Text("FanSummary",fans.Count(f=>f.Value.HasValue)+" / "+fans.Count+" 路 RPM 可读 · GPU 单独显示百分比");
            var panel=Find<WrapPanel>("FanPanel");panel.Children.Clear();
            foreach(var fan in fans){
                var associated=value.Sensors.FirstOrDefault(s=>s.Kind=="temperature" && s.Id==fan.FanTemperatureSensorId);
                string relation=fan.FanSensorCode.HasValue?"固件关联码："+fan.FanSensorCode:"固件关联查询未提供数据";
                if(associated!=null)relation+="\n匹配温度："+associated.Name+" · "+associated.Id;
                relation+="\n关联关系不等于风扇物理安装位置";
                panel.Children.Add(FanCard(fan.Name,Display(fan.Value," RPM","0"),fan.Id+"\n"+relation,fan.Value,fan.Maximum,"#50D7B2"));
            }
            panel.Children.Add(FanCard("GPU 风扇",Display(gpu.FanPercent," %","0"),"NVIDIA · 驱动报告百分比",gpu.FanPercent,100,"#8EB8FF"));
            if(fans.Count==0) panel.Children.Add(FanCard("机箱 / 散热风扇","—","等待 AWCC RPM 数据",null,null,"#E4B77A"));
            var temperatures=Find<WrapPanel>("TemperaturePanel");temperatures.Children.Clear();
            foreach(var extra in value.Sensors.Where(s=>s.Kind=="temperature" && (temp==null || s.Id!=temp.Id)))
                temperatures.Children.Add(new Border{Width=123,Padding=new Thickness(7),Margin=new Thickness(3),CornerRadius=new CornerRadius(5),Background=Brush("#162631"),ToolTip=extra.Id,
                    Child=new TextBlock{Text=extra.Name+"  "+Display(extra.Value,"°","0"),FontSize=10,Foreground=Brush("#B7C9D5")}});

            history.Add(new HistoryPoint{Time=lastUpdate,Cpu=temp==null?null:temp.Value,Gpu=gpu.Temperature});
            history.RemoveAll(p=>(lastUpdate-p.Time).TotalSeconds>120); DrawCharts();
            Text("Footer","本机监测 · 采样 "+value.SampleMilliseconds.ToString("0")+" ms · "+interval/1000+" 秒刷新 · 内存采集，不定时写盘");
            if(!previewSaved && !string.IsNullOrEmpty(preview) && samples>=3)
            {
                window.UpdateLayout();
                var bitmap=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);
                bitmap.Render(window);
                var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using(var stream=File.Create(preview)) encoder.Save(stream);
                previewSaved=true;
                if(!stay) window.Close();
            }
        }
        Border FanCard(string name,string value,string source,double? current,double? maximum,string accent)
        {
            var stack=new StackPanel();
            stack.Children.Add(new TextBlock{Text=name,Foreground=Brush("#93A7B8"),FontSize=12});
            stack.Children.Add(new TextBlock{Text=value,FontSize=23,Foreground=Brush(accent),Margin=new Thickness(0,6,0,5)});
            if(current.HasValue && maximum.HasValue && maximum>0)
                stack.Children.Add(new ProgressBar{Value=Math.Min(100,current.Value/maximum.Value*100),Maximum=100,Height=3,Margin=new Thickness(0,3,0,7),Foreground=Brush(accent),Background=Brush("#253442")});

            return new Border{Child=stack,ToolTip=source,Width=164,Padding=new Thickness(11),Margin=new Thickness(4),CornerRadius=new CornerRadius(8),Background=Brush("#0D161F")};
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
                var label=new TextBlock{Text=seconds==0?"现在":seconds+"秒前",Foreground=Brush("#93A7B8"),FontSize=10};
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
            Text(cpu?"CpuChartSummary":"GpuChartSummary",valid.Count==0?"暂无有效温度读数":
                "最低 "+valid.Min().ToString("0.0")+" °C   ·   最高 "+valid.Max().ToString("0.0")+" °C   ·   悬停查看采样值");
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
                canvas.ToolTip="该时间没有采样数据";return;
            }
            canvas.ToolTip=point.Time.ToLocalTime().ToString("HH:mm:ss")+"  ·  "+Display(cpu?point.Cpu:point.Gpu," °C","0.0");
        }
        void Elevate()
        {
            try {
                using(var launched=Process.Start(new ProcessStartInfo(typeof(Program).Assembly.Location){UseShellExecute=true,Verb="runas",WorkingDirectory=root})) { }
                window.Close();
            }catch(System.ComponentModel.Win32Exception){Text("Footer","未授予管理员权限；基础指标仍可查看。");}
        }
        void ToggleFull()
        {
            if(!full){previousState=window.WindowState;previousBounds=new Rect(window.Left,window.Top,window.Width,window.Height);window.WindowState=WindowState.Normal;window.WindowStyle=WindowStyle.None;window.WindowState=WindowState.Maximized;full=true;}
            else {window.WindowState=WindowState.Normal;window.WindowStyle=WindowStyle.SingleBorderWindow;window.Left=previousBounds.Left;window.Top=previousBounds.Top;window.Width=previousBounds.Width;window.Height=previousBounds.Height;window.WindowState=previousState;full=false;}
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
            bool wasFull=full;if(wasFull)ToggleFull();
            window.WindowState=WindowState.Normal;
            SetWindowPos(helper.Handle,IntPtr.Zero,bounds.Left+30,bounds.Top+30,
                Math.Min(1400,bounds.Width-60),Math.Min(1000,bounds.Height-60),0x0004);

            if(wasFull)ToggleFull();
        }
    }
    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
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
                if(args.Length==0)MessageBox.Show("HardwareDeck 未能启动。\n"+ex.Message,"HardwareDeck",MessageBoxButton.OK,MessageBoxImage.Error);
                return 1;
            }
        }
    }
}
