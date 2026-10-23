using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using PzlEv.Modules.Runs.Services;
using PzlEv.Shared.Utils.Dictionaries;

namespace PzlEv.Modules.Runs.Views;

/// <summary>
/// Wykres przyrostów (okno niemodalne z okna „Zmiany ACWP”): w każdym okresie słupek każdego rodzaju kosztu (lewa oś – koszt
/// zaksięgowany w okresie, skok – obramowanie w kolorze ostrzeżenia), nad nimi linia kosztu skumulowanego rodzajów wykresu
/// (prawa oś). Rysowany na nowo przy zmianie rozmiaru okna.
/// </summary>
public partial class AcwpChartWindow : Window
{
    private const double PadLeft = 80;
    private const double PadRight = 90;
    private const double PadTop = 12;
    private const double PadBottom = 28;
    private const int Ticks = 5;

    private readonly AcwpChart _chart;

    public AcwpChartWindow(string title, string note, AcwpChart chart)
    {
        InitializeComponent();
        _chart = chart;
        Title = title;
        Heading.Text = title;
        Note.Text = note;
        Empty.Visibility = chart.Series.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var series in chart.Series)
            Legend.Children.Add(LegendItem(new Rectangle { Width = 12, Height = 12, Fill = KindBrush(series.Kind) }, $"ACWP {series.Kind}"));
        if (chart.Series.Count > 0)
            Legend.Children.Add(LegendItem(new Rectangle { Width = 18, Height = 2, Fill = Brush("Ink") }, "Skumulowany (prawa oś)"));
    }

    private void OnPlotSizeChanged(object sender, SizeChangedEventArgs e) => Draw();

    private void Draw()
    {
        Plot.Children.Clear();
        var periods = _chart.Series.FirstOrDefault()?.Increments ?? [];
        var width = Plot.ActualWidth - PadLeft - PadRight;
        var height = Plot.ActualHeight - PadTop - PadBottom;
        if (periods.Count == 0 || width < 50 || height < 50)
            return;

        var (barMin, barMax) = Range(_chart.Series.SelectMany(s => s.Increments).Select(i => i.Amount));
        var (lineMin, lineMax) = Range(_chart.Cumulative);
        double Y(decimal value, decimal min, decimal max) => PadTop + height - (double)((value - min) / (max - min)) * height;

        // Osie: siatka i opisy lewej osi (przyrost), opisy prawej osi (skumulowany).
        for (var t = 0; t <= Ticks; t++)
        {
            var y = PadTop + height - height * t / Ticks;
            Plot.Children.Add(new Line { X1 = PadLeft, X2 = PadLeft + width, Y1 = y, Y2 = y, Stroke = Brush("Line"), StrokeThickness = 1 });
            AddText(Format(barMin + (barMax - barMin) * t / Ticks), 0, y - 8, PadLeft - 6, TextAlignment.Right);
            AddText(Format(lineMin + (lineMax - lineMin) * t / Ticks), PadLeft + width + 6, y - 8, PadRight - 6, TextAlignment.Left);
        }
        if (barMin < 0)
        {
            var zero = Y(0, barMin, barMax);
            Plot.Children.Add(new Line { X1 = PadLeft, X2 = PadLeft + width, Y1 = zero, Y2 = zero, Stroke = Brush("Muted"), StrokeThickness = 1 });
        }

        // Słupki rodzajów w okresie.
        var group = width / periods.Count;
        var bar = group * 0.7 / _chart.Series.Count;
        for (var p = 0; p < periods.Count; p++)
        {
            var x0 = PadLeft + group * p + group * 0.15;
            for (var k = 0; k < _chart.Series.Count; k++)
            {
                var series = _chart.Series[k];
                var increment = series.Increments[p];
                var y = Y(increment.Amount, barMin, barMax);
                var zero = Y(0, barMin, barMax);
                var rect = new Rectangle
                {
                    Width = Math.Max(1, bar - 2),
                    Height = Math.Max(1, Math.Abs(zero - y)),
                    Fill = KindBrush(series.Kind),
                    Stroke = increment.IsJump ? Brush("Warn") : null,
                    StrokeThickness = increment.IsJump ? 2 : 0,
                    ToolTip = $"ACWP {series.Kind} {increment.Label}: {increment.Amount:#,0.00} (średni przyrost {AcwpChanges.IncrementPeriods} poprzednich okresów "
                              + $"{increment.Average:#,0.00})" + (increment.IsJump ? " – skok" : ""),
                };
                Canvas.SetLeft(rect, x0 + bar * k + 1);
                Canvas.SetTop(rect, Math.Min(y, zero));
                Plot.Children.Add(rect);
            }
            AddText(periods[p].Label, PadLeft + group * p, PadTop + height + 6, group, TextAlignment.Center);
        }

        // Linia kosztu skumulowanego (suma rodzajów wykresu) nad słupkami.
        var line = new Polyline { Stroke = Brush("Ink"), StrokeThickness = 2 };
        for (var p = 0; p < periods.Count; p++)
        {
            var point = new Point(PadLeft + group * (p + 0.5), Y(_chart.Cumulative[p], lineMin, lineMax));
            line.Points.Add(point);
            var dot = new Ellipse { Width = 8, Height = 8, Fill = Brush("Ink"), ToolTip = $"Skumulowany {periods[p].Label}: {_chart.Cumulative[p]:#,0.00}" };
            Canvas.SetLeft(dot, point.X - 4);
            Canvas.SetTop(dot, point.Y - 4);
            Canvas.SetZIndex(dot, 2);
            Plot.Children.Add(dot);
        }
        Canvas.SetZIndex(line, 1);
        Plot.Children.Add(line);
    }

    // Zakres osi z zerem; bez rozpiętości – zakres 0..1.
    private static (decimal Min, decimal Max) Range(IEnumerable<decimal> values)
    {
        var list = values.ToList();
        var min = Math.Min(0, list.DefaultIfEmpty(0).Min());
        var max = Math.Max(0, list.DefaultIfEmpty(0).Max());
        return max == min ? (min, min + 1) : (min, max);
    }

    private void AddText(string text, double x, double y, double width, TextAlignment alignment)
    {
        var block = new TextBlock { Text = text, Width = width, TextAlignment = alignment, FontSize = 11, Foreground = Brush("Muted") };
        Canvas.SetLeft(block, x);
        Canvas.SetTop(block, y);
        Plot.Children.Add(block);
    }

    private static StackPanel LegendItem(Shape mark, string text)
    {
        mark.VerticalAlignment = VerticalAlignment.Center;
        mark.Margin = new Thickness(0, 0, 6, 0);
        return new StackPanel
        {
            Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 16, 0),
            Children = { mark, new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center } },
        };
    }

    private static string Format(decimal value) => value.ToString("#,0", CultureInfo.CurrentCulture);

    // Kolory rodzajów z palety aplikacji (Theme.xaml).
    private Brush KindBrush(string kind) => Brush(kind switch
    {
        GlobalDictionaries.LaborKind => "Accent",
        GlobalDictionaries.MaterialKind => "Info",
        _ => "Stale",
    });

    private Brush Brush(string key) => (Brush)FindResource(key);
}
