using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace HardwareDeck
{
    public sealed partial class DeckWindow
    {
        sealed class SensorTile
        {
            public readonly Border Surface;
            readonly TextBlock label, reading;
            readonly ProgressBar bar;
            readonly bool temperatureTile;
            public SensorTile(bool compact)
            {
                temperatureTile = compact;
                label = new TextBlock
                {
                    Foreground = Brush("#9CAEBB"),
                    FontSize = 11,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                reading = new TextBlock
                {
                    FontSize = compact ? 14 : 23,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, compact ? 2 : 4, 0, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                bar = new ProgressBar
                {
                    Maximum = 100,
                    Height = 3,
                    Margin = new Thickness(0, 6, 0, 0),
                    Background = Brush("#253442"),
                    Visibility = compact ? Visibility.Collapsed : Visibility.Hidden
                };
                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition());
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                for (int i = 0; i < 3; i++)
                    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.Children.Add(label);
                grid.Children.Add(reading);
                grid.Children.Add(bar);
                Grid.SetRow(reading, 1);
                Grid.SetRow(bar, 2);
                Grid.SetColumnSpan(label, 2);
                Grid.SetColumnSpan(reading, 2);
                Grid.SetColumnSpan(bar, 2);
                Surface = new Border
                {
                    Child = grid,
                    Margin = new Thickness(4, 0, 4, 0),
                    CornerRadius = new CornerRadius(7),
                    Background = Brush(compact ? "#162631" : "#0D161F")
                };
                SetDensity(false);
            }

            public void SetDensity(bool dense)
            {
                Surface.Padding = new Thickness(10, temperatureTile ? 5 : dense ? 5 : 8, 10, temperatureTile ? 5 : dense ? 5 : 8);
                reading.FontSize = temperatureTile ? 14 : dense ? 21 : 23;
                reading.Margin = new Thickness(0, temperatureTile ? (dense ? 0 : 2) : (dense ? 2 : 4), 0, 0);
                bar.Margin = new Thickness(0, dense ? 4 : 6, 0, 0);
                if (temperatureTile)
                {
                    Grid.SetRow(reading, dense ? 0 : 1);
                    Grid.SetColumn(reading, dense ? 1 : 0);
                    Grid.SetColumnSpan(label, dense ? 1 : 2);
                    Grid.SetColumnSpan(reading, dense ? 1 : 2);
                    label.Margin = new Thickness(0, 0, dense ? 5 : 0, 0);
                    label.VerticalAlignment = VerticalAlignment.Center;
                }
            }

            public void Update(string name, string value, string source, double? current, double? maximum, string accent)
            {
                label.Text = name;
                reading.Text = value;
                reading.Foreground = Brush(accent);
                Surface.ToolTip = name + "\n" + source;
                if (bar.Visibility != Visibility.Collapsed)
                {
                    bar.Visibility = current.HasValue && maximum.HasValue && maximum > 0 ? Visibility.Visible : Visibility.Hidden;
                    bar.Value = bar.Visibility == Visibility.Visible ? Math.Max(0, Math.Min(100, current.Value / maximum.Value * 100)) : 0;
                    bar.Foreground = Brush(accent);
                }
            }
        }

        void ReconcileTiles(UniformGrid panel, Dictionary<string, SensorTile> tiles, List<string> keys, bool compact)
        {
            panel.Visibility = keys.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            if (panel.Children.Count == keys.Count && keys.Select((key, index) => tiles.ContainsKey(key) && ReferenceEquals(panel.Children[index], tiles[key].Surface)).All(s => s))
                return;
            panel.Children.Clear();
            foreach (var removed in tiles.Keys.Except(keys).ToList())
                tiles.Remove(removed);
            foreach (var key in keys)
            {
                SensorTile tile;
                if (!tiles.TryGetValue(key, out tile))
                {
                    tiles[key] = tile = new SensorTile(compact);
                    tile.SetDensity(compactLayout == true);
                }

                panel.Children.Add(tile.Surface);
            }

            panel.Rows = Math.Max(1, (int)Math.Ceiling(keys.Count / (compact ? 9.0 : 6.0)));
        }
    }
}
