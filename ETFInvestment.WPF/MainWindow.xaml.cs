using ETFInvestment.Core.Models;
using ETFInvestment.WPF.ViewModels;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ETFInvestment.WPF;

public partial class MainWindow : Window
{
    private readonly MarketDashboardViewModel _market = new();
    private MarketSnapshot? _snapshot;
    private EtfDefinition _selected = MarketDashboardViewModel.Etfs[0];
    private static readonly Brush PriceBrush = BrushFrom("#4DC4F6");
    private static readonly Brush Ma20Brush = BrushFrom("#F5BD67");
    private static readonly Brush Ma120Brush = BrushFrom("#C4A7FA");
    private static readonly Brush AxisBrush = BrushFrom("#94A7C3");

    public MainWindow()
    {
        InitializeComponent();
        foreach (var etf in MarketDashboardViewModel.Etfs)
        {
            var item = etf;
            var b = new Button
            {
                Content = new TextBlock { Text = $"{item.Symbol}   {item.Name}", FontSize = 15 },
                HorizontalContentAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 10)
            };
            b.Click += async (_, _) => await SelectEtfAsync(item);
            EtfButtons.Children.Add(b);
        }
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e) => await SelectEtfAsync(_selected);
    private async void Reload_Click(object sender, RoutedEventArgs e) => await SelectEtfAsync(_selected);

    private async Task SelectEtfAsync(EtfDefinition selected)
    {
        _selected = selected;
        TitleText.Text = $"{selected.Symbol}  {selected.Name}";
        StatusText.Text = "讀取本機 market.db…";
        try
        {
            _snapshot = await _market.LoadAsync(selected);
            if (_snapshot.Latest is null)
            {
                QuoteText.Text = "沒有行情資料";
                StatusText.Text = "請先使用 Collector 下載歷史價格。";
            }
            else
            {
                var latest = _snapshot.Latest;
                var change = _snapshot.TwentyDayReturn;
                QuoteText.Text = $"最新收盤 {latest.Close:N2} 元  ·  {latest.Date:yyyy/MM/dd}  · 近20交易日調整價變化：{(change.HasValue ? change.Value.ToString("P2", CultureInfo.CurrentCulture) : "資料不足")}";
                var audit = _snapshot.Audit;
                StatusText.Text = audit.HasBlockingFindings
                    ? $"待確認：{audit.Findings.Count} 項價格稽核發現。{string.Join("；", audit.Findings.Take(2).Select(f => $"{f.Date}: {f.Message}"))}"
                    : $"{_snapshot.AuditLabel}。歷史事件完整性尚未經獨立來源核對；禁止作為正式買點。";
            }
        }
        catch (Exception ex)
        {
            _snapshot = null;
            QuoteText.Text = "無法取得資料";
            StatusText.Text = $"讀取失敗：{ex.Message}";
        }
        DrawChart();
    }

    private void ChartOptions_Changed(object sender, RoutedEventArgs e) => DrawChart();
    private void ChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => DrawChart();

    private void DrawChart()
    {
        if (ChartCanvas is null || ChartEmptyText is null || PeriodCombo is null || PriceModeCombo is null
            || Ma20Check is null || Ma120Check is null || ChartFootnote is null) return;
        ChartCanvas.Children.Clear();
        var snapshot = _snapshot;
        if (snapshot is null || snapshot.Raw.Count < 2 || snapshot.Adjusted.Count != snapshot.Raw.Count)
        {
            ChartEmptyText.Visibility = Visibility.Visible;
            ChartEmptyText.Text = "尚無可繪製行情，或調整事件有待確認";
            ChartFootnote.Text = "";
            return;
        }
        double w = ChartCanvas.ActualWidth, h = ChartCanvas.ActualHeight;
        if (w < 160 || h < 150) return;
        ChartEmptyText.Visibility = Visibility.Collapsed;
        var mode = (PriceModeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        var series = mode == "Raw" ? snapshot.Raw : snapshot.Adjusted;
        var tag = (PeriodCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "6M";
        int days = tag switch { "1M" => 30, "3M" => 92, "6M" => 183, "1Y" => 366, _ => 10000 };
        DateOnly threshold = series[^1].Date.AddDays(-days);
        int start = tag == "ALL" ? 0 : Math.Max(0, FindFirstOnOrAfter(series, threshold));
        if (series.Count - start < 2) start = Math.Max(0, series.Count - 2);

        var ma20 = MovingAverage(series, 20);
        var ma120 = MovingAverage(series, 120);
        bool show20 = Ma20Check.IsChecked == true, show120 = Ma120Check.IsChecked == true;
        var ys = series.Skip(start).Select(p => (double)p.Close).ToList();
        if (show20) ys.AddRange(ma20.Skip(start).Where(x => x.HasValue).Select(x => (double)x!.Value));
        if (show120) ys.AddRange(ma120.Skip(start).Where(x => x.HasValue).Select(x => (double)x!.Value));
        double lo = ys.Min(), hi = ys.Max();
        if (Math.Abs(hi - lo) < 0.00001) { lo -= 1; hi += 1; }
        double pad = (hi - lo) * 0.09; lo -= pad; hi += pad;
        const double left = 72, right = 20, top = 20, bottom = 42;
        double dw = Math.Max(1, w - left - right), dh = Math.Max(1, h - top - bottom);
        double X(int index) => left + (index - start) * dw / Math.Max(1, series.Count - start - 1);
        double Y(double val) => top + (hi - val) * dh / (hi - lo);
        for (int i = 0; i <= 4; i++)
        {
            double val = lo + (hi - lo) * i / 4.0;
            double py = Y(val);
            ChartCanvas.Children.Add(new Line { X1 = left, X2 = left + dw, Y1 = py, Y2 = py,
                Stroke = AxisBrush, StrokeThickness = 0.7, Opacity = 0.25, StrokeDashArray = new DoubleCollection { 3, 5 } });
            AddText(val.ToString("F2", CultureInfo.InvariantCulture), 6, py - 10);
        }
        AddText(series[start].Date.ToString("yyyy/MM/dd"), left, h - bottom + 10);
        AddText(series[^1].Date.ToString("yyyy/MM/dd"), Math.Max(left, w - 112), h - bottom + 10);
        DrawLine(series.Select(p => (decimal?)p.Close).ToArray(), PriceBrush, 2.2);
        if (show20) DrawLine(ma20, Ma20Brush, 1.6);
        if (show120) DrawLine(ma120, Ma120Brush, 1.6);
        ChartFootnote.Text = $"顯示 {series.Count - start} 個交易日｜藍：{(mode == "Raw" ? "原始價格" : "向後調整價格")}｜橘：20日均線｜紫：120日均線。移動平均使用完整快取中的前置資料計算。";
        void DrawLine(IReadOnlyList<decimal?> values, Brush brush, double thickness)
        {
            var poly = new Polyline { Stroke = brush, StrokeThickness = thickness,
                StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            for (int i = start; i < values.Count; i++)
            {
                if (!values[i].HasValue) continue;
                poly.Points.Add(new Point(X(i), Y((double)values[i]!.Value)));
            }
            if (poly.Points.Count > 1) ChartCanvas.Children.Add(poly);
        }
        void AddText(string value, double x, double y)
        {
            var text = new TextBlock { Text = value, FontSize = 11, Foreground = AxisBrush };
            Canvas.SetLeft(text, x); Canvas.SetTop(text, y);
            ChartCanvas.Children.Add(text);
        }
    }

    private static int FindFirstOnOrAfter(IReadOnlyList<EtfPrice> prices, DateOnly d)
    {
        for (int i = 0; i < prices.Count; i++) if (prices[i].Date >= d) return i;
        return prices.Count - 1;
    }
    private static decimal?[] MovingAverage(IReadOnlyList<EtfPrice> values, int period)
    {
        var result = new decimal?[values.Count];
        decimal rolling = 0m;
        for (int i = 0; i < values.Count; i++)
        {
            rolling += values[i].Close;
            if (i >= period) rolling -= values[i - period].Close;
            if (i >= period - 1) result[i] = rolling / period;
        }
        return result;
    }
    private static Brush BrushFrom(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;
}
