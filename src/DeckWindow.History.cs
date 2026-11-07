using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;

namespace HardwareDeck
{
    public sealed class HistoryPoint
    {
        public DateTime Time;
        public double? Cpu, Gpu, Download, Upload;
    }

    public sealed partial class DeckWindow
    {
        void DrawCharts()
        {
            if (history.Count == 0)
                return;
            Draw(Find<Canvas>("CpuChart"), true, "#50D7B2");
            Draw(Find<Canvas>("GpuChart"), false, "#8EB8FF");
            Draw(Find<Canvas>("DownloadChart"), true, "#8EB8FF", true);
            Draw(Find<Canvas>("UploadChart"), false, "#50D7B2", true);
        }

        void ChartText(Canvas canvas, string text, double x, double y, string color, double size)
        {
            var label = new TextBlock
            {
                Text = text,
                Foreground = Brush(color),
                FontSize = size
            };
            Canvas.SetLeft(label, x);
            Canvas.SetTop(label, y);
            canvas.Children.Add(label);
        }

        static double? ChartValue(HistoryPoint point, bool first, bool network)
        {
            return network ? (first ? point.Download : point.Upload) : (first ? point.Cpu : point.Gpu);
        }

        void Draw(Canvas canvas, bool first, string color, bool network = false)
        {
            canvas.Children.Clear();
            double width = canvas.ActualWidth, height = canvas.ActualHeight;
            const double left = 40;
            const double right = 12, top = 16, bottom = 26;
            double plotWidth = width - left - right, plotHeight = height - top - bottom;
            if (plotWidth <= 0 || plotHeight <= 0)
                return;
            var valid = history.Select(p => ChartValue(p, first, network)).Where(v => v.HasValue).Select(v => v.Value).ToList();
            double peak = valid.Count == 0 ? 0 : valid.Max();
            double maximum = network ? NetworkRates.AxisMaximum(peak) : Math.Max(120, Math.Ceiling(peak / 30) * 30);
            double divisor = network ? NetworkRates.Divisor(maximum) : 1;
            ChartText(canvas, network ? NetworkRates.Unit(divisor) : "°C", 0, 0, "#93A7B8", 10);
            int steps = network ? 4 : (int)(maximum / 30);
            for (int step = 0; step <= steps; step++)
            {
                double level = maximum * step / steps;
                double y = top + plotHeight * (1 - level / maximum);
                canvas.Children.Add(new Line { X1 = left, X2 = left + plotWidth, Y1 = y, Y2 = y, Stroke = Brush("#243746"), StrokeThickness = 1 });
                ChartText(canvas, (level / divisor).ToString(network ? "0.##" : "0", CultureInfo.InvariantCulture), 3, y - 7, "#93A7B8", 10);
            }

            for (int seconds = 120; seconds >= 0; seconds -= 30)
            {
                double x = left + plotWidth * (1 - seconds / 120.0);
                canvas.Children.Add(new Line { X1 = x, X2 = x, Y1 = top, Y2 = top + plotHeight, Stroke = Brush("#1B2A36"), StrokeThickness = 1 });
                var label = new TextBlock
                {
                    Text = seconds == 0 ? "Now" : seconds + " s ago",
                    Foreground = Brush("#93A7B8"),
                    FontSize = 10
                };
                label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(label, Math.Max(0, Math.Min(width - label.DesiredSize.Width, x - label.DesiredSize.Width / 2)));
                Canvas.SetTop(label, top + plotHeight + 7);
                canvas.Children.Add(label);
            }

            var line = new Polyline
            {
                Stroke = Brush(color),
                StrokeThickness = 2
            };
            foreach (var point in history)
            {
                double? value = ChartValue(point, first, network);
                if (!value.HasValue)
                {
                    if (line.Points.Count > 1)
                        canvas.Children.Add(line);
                    line = new Polyline
                    {
                        Stroke = Brush(color),
                        StrokeThickness = 2
                    };
                    continue;
                }

                double age = (lastUpdate - point.Time).TotalSeconds;
                if (age < 0 || age > 120)
                    continue;
                line.Points.Add(new Point(left + plotWidth * (1 - age / 120), top + plotHeight * (1 - value.Value / maximum)));
            }

            if (line.Points.Count > 1)
                canvas.Children.Add(line);
            var latestPoint = history.LastOrDefault();
            double? current = latestPoint == null ? null : ChartValue(latestPoint, first, network);
            string prefix = (network ? (first ? "Download" : "Upload") : (first ? "Cpu" : "Gpu")) + "Chart";
            Text(prefix + "Current", network ? NetworkRates.Format(current) : Display(current, " °C", "0.0"));
            if (network)
            {
                var info = latest == null ? null : latest.Network;
                int count = info == null ? 0 : info.AdapterCount;
                Text(prefix + "Summary", current.HasValue ? "Peak " + NetworkRates.Format(peak) + " · " + count + (count == 1 ? " adapter" : " adapters") + " · Hover to inspect" : (info == null ? "Waiting for network data" : info.Detail));
                Find<TextBlock>(prefix + "Current").ToolTip = Find<TextBlock>(prefix + "Title").ToolTip;
                Find<TextBlock>(prefix + "Summary").ToolTip = Find<TextBlock>(prefix + "Title").ToolTip;
            }
            else
                Text(prefix + "Summary", valid.Count == 0 ? "No temperature samples" : "Min " + valid.Min().ToString("0.0") + " °C   ·   Max " + peak.ToString("0.0") + " °C   ·   Hover to inspect");
            if (current.HasValue)
            {
                var marker = new Ellipse
                {
                    Width = 6,
                    Height = 6,
                    Fill = Brush(color)
                };
                Canvas.SetLeft(marker, left + plotWidth - 3);
                Canvas.SetTop(marker, top + plotHeight * (1 - current.Value / maximum) - 3);
                canvas.Children.Add(marker);
            }
        }

        void InspectChart(Canvas canvas, bool first, System.Windows.Input.MouseEventArgs e, bool network = false)
        {
            if (history.Count == 0)
                return;
            const double left = 40;
            double fraction = Math.Max(0, Math.Min(1, (e.GetPosition(canvas).X - left) / Math.Max(1, canvas.ActualWidth - left - 12)));
            var target = lastUpdate.AddSeconds(-120 * (1 - fraction));
            var point = history.OrderBy(p => Math.Abs((p.Time - target).TotalSeconds)).First();
            // Do not present the oldest point as a sample for an unrecorded part of the chart.
            if (target < history[0].Time.AddMilliseconds(-interval) || Math.Abs((point.Time - target).TotalMilliseconds) > interval * 1.5)
            {
                canvas.ToolTip = "No sample at this time";
                return;
            }

            double? value = ChartValue(point, first, network);
            canvas.ToolTip = point.Time.ToLocalTime().ToString("HH:mm:ss") + "  ·  " + (network ? NetworkRates.Format(value) : Display(value, " °C", "0.0"));
        }
    }
}
