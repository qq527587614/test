using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using StockAnalysisSystem_UI_WinUI.Services;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace StockAnalysisSystem_UI_WinUI.Pages;

public sealed partial class BacktestReportPage : Page
{
    public BacktestReportPage()
    {
        InitializeComponent();
        Loaded += (_, _) => Refresh();
    }

    private void Refresh()
    {
        var state = AppServices.Provider.GetRequiredService<BacktestSessionState>();
        var r = state.LastResult;
        if (r == null)
        {
            TxtMetrics.Text = "暂无结果。请先在 Runner 页运行一次回测。";
            return;
        }

        var m = r.Metrics;
        TxtMetrics.Text =
            $"TradingDays: {m.TradingDays}\n" +
            $"TotalReturn: {m.TotalReturn:P2}\n" +
            $"AnnualReturn: {m.AnnualReturn:P2}\n" +
            $"MaxDrawdown: {m.MaxDrawdown:P2}\n" +
            $"Sharpe: {m.SharpeRatio:F2}  Sortino: {m.SortinoRatio:F2}  Calmar: {m.CalmarRatio:F2}\n" +
            $"Trades: {m.TradeCount}  WinRate: {m.WinRate:P1}\n";

        RenderCharts(r);
        RenderTrades(r);
    }

    private void RenderTrades(StockAnalysisSystem.Core.Backtest.V2.BacktestResultV2 r)
    {
        TradesList.Items.Clear();

        if (r.Trades.Count == 0)
        {
            TradesList.Items.Add(new TextBlock { Text = "无成交明细（Trades=0）。如果策略只买不卖，请确认是否启用了到期清算/卖出规则。" });
            return;
        }

        foreach (var t in r.Trades.OrderByDescending(x => x.BuyDate))
        {
            var text =
                $"{t.StockCode} {t.StockName} | {t.StrategyName}\n" +
                $"Buy:  {t.BuyDate:yyyy-MM-dd}  Px={t.BuyPrice:F2}  Shares={t.Shares}\n" +
                $"Sell: {t.SellDate:yyyy-MM-dd}  Px={t.SellPrice:F2}  PnL={t.ProfitLoss:F2} ({t.ProfitLossPercent:P2})  Hold={t.HoldingDays}d\n" +
                $"Reason: {t.SellReason}";

            TradesList.Items.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap });
        }
    }

    private void RenderCharts(StockAnalysisSystem.Core.Backtest.V2.BacktestResultV2 r)
    {
        var dateIndex = r.EquityCurve
            .Select((p, idx) => (date: p.Date.Date, idx))
            .ToDictionary(x => x.date, x => x.idx);

        var markers = new List<(int index, Brush brush)>();
        foreach (var t in r.Trades)
        {
            if (dateIndex.TryGetValue(t.BuyDate.Date, out var bi))
                markers.Add((bi, new SolidColorBrush(Microsoft.UI.Colors.LimeGreen)));
            if (dateIndex.TryGetValue(t.SellDate.Date, out var si))
                markers.Add((si, new SolidColorBrush(Microsoft.UI.Colors.OrangeRed)));
        }

        RenderLineChart(
            EquityCanvas,
            r.EquityCurve.Select(p => (p.Date, (double)p.Equity)).ToList(),
            lineColor: (Brush)App.Current.Resources["SystemControlForegroundAccentBrush"],
            markers: markers);

        RenderLineChart(
            DrawdownCanvas,
            r.DrawdownCurve.Select(p => (p.Date, (double)(-p.Drawdown * 100m))).ToList(),
            lineColor: (Brush)App.Current.Resources["SystemControlForegroundBaseHighBrush"]);
    }

    private async void ExportEquityPng_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var picker = new FileSavePicker();
        picker.FileTypeChoices.Add("PNG Image", new[] { ".png" });
        picker.SuggestedFileName = "equity";
        picker.DefaultFileExtension = ".png";

        var hwnd = AppServices.MainWindow == null
            ? IntPtr.Zero
            : WinRT.Interop.WindowNative.GetWindowHandle(AppServices.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSaveFileAsync();
        if (file == null)
            return;

        await SaveElementAsPngAsync(EquityCanvas, file);
    }

    private async void ExportCsv_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var state = AppServices.Provider.GetRequiredService<BacktestSessionState>();
        var r = state.LastResult;
        if (r == null)
            return;

        var picker = new FileSavePicker();
        picker.FileTypeChoices.Add("CSV", new[] { ".csv" });
        picker.SuggestedFileName = "backtest";
        picker.DefaultFileExtension = ".csv";

        var hwnd = AppServices.MainWindow == null
            ? IntPtr.Zero
            : WinRT.Interop.WindowNative.GetWindowHandle(AppServices.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSaveFileAsync();
        if (file == null)
            return;

        var sb = new StringBuilder();
        sb.AppendLine("Date,Equity,Cash,PositionValue,Drawdown");
        var ddByDate = r.DrawdownCurve.ToDictionary(x => x.Date.Date, x => x.Drawdown);
        foreach (var p in r.EquityCurve)
        {
            ddByDate.TryGetValue(p.Date.Date, out var dd);
            sb.AppendLine(
                $"{p.Date:yyyy-MM-dd}," +
                $"{p.Equity.ToString(CultureInfo.InvariantCulture)}," +
                $"{p.Cash.ToString(CultureInfo.InvariantCulture)}," +
                $"{p.PositionValue.ToString(CultureInfo.InvariantCulture)}," +
                $"{dd.ToString(CultureInfo.InvariantCulture)}");
        }

        await FileIO.WriteTextAsync(file, sb.ToString());
    }

    private async void ExportTradesCsv_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var state = AppServices.Provider.GetRequiredService<BacktestSessionState>();
        var r = state.LastResult;
        if (r == null)
            return;

        var picker = new FileSavePicker();
        picker.FileTypeChoices.Add("CSV", new[] { ".csv" });
        picker.SuggestedFileName = "trades";
        picker.DefaultFileExtension = ".csv";

        var hwnd = AppServices.MainWindow == null
            ? IntPtr.Zero
            : WinRT.Interop.WindowNative.GetWindowHandle(AppServices.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSaveFileAsync();
        if (file == null)
            return;

        var sb = new StringBuilder();
        sb.AppendLine("StockId,StockCode,StockName,StrategyName,BuyDate,BuyPrice,Shares,SellDate,SellPrice,Commission,ProfitLoss,ProfitLossPercent,HoldingDays,SellReason");
        foreach (var t in r.Trades.OrderBy(x => x.BuyDate))
        {
            sb.AppendLine(
                $"{Esc(t.StockId)}," +
                $"{Esc(t.StockCode)}," +
                $"{Esc(t.StockName)}," +
                $"{Esc(t.StrategyName)}," +
                $"{t.BuyDate:yyyy-MM-dd}," +
                $"{t.BuyPrice.ToString(CultureInfo.InvariantCulture)}," +
                $"{t.Shares}," +
                $"{t.SellDate:yyyy-MM-dd}," +
                $"{t.SellPrice.ToString(CultureInfo.InvariantCulture)}," +
                $"{t.Commission.ToString(CultureInfo.InvariantCulture)}," +
                $"{t.ProfitLoss.ToString(CultureInfo.InvariantCulture)}," +
                $"{t.ProfitLossPercent.ToString(CultureInfo.InvariantCulture)}," +
                $"{t.HoldingDays}," +
                $"{Esc(t.SellReason)}");
        }

        await FileIO.WriteTextAsync(file, sb.ToString());
    }

    private static string Esc(string? s)
    {
        s ??= "";
        if (s.Contains('"'))
            s = s.Replace("\"", "\"\"");
        if (s.Contains(',') || s.Contains('\n') || s.Contains('\r') || s.Contains('"'))
            return $"\"{s}\"";
        return s;
    }

    private static void RenderLineChart(
        Canvas canvas,
        List<(DateTime date, double value)> series,
        Brush lineColor,
        List<(int index, Brush brush)>? markers = null)
    {
        canvas.Children.Clear();
        if (series.Count < 2)
            return;

        var w = Math.Max(1, canvas.ActualWidth);
        var h = Math.Max(1, canvas.ActualHeight);

        // WinUI 初次加载时 ActualWidth 可能为 0，兜底用 Height/Width 估算
        if (w <= 1) w = 800;
        if (h <= 1) h = canvas.Height > 0 ? canvas.Height : 260;

        var min = series.Min(x => x.value);
        var max = series.Max(x => x.value);
        if (Math.Abs(max - min) < 1e-9)
        {
            max = min + 1;
        }

        var poly = new Polyline
        {
            Stroke = lineColor,
            StrokeThickness = 2
        };

        for (int i = 0; i < series.Count; i++)
        {
            var x = (double)i / (series.Count - 1) * (w - 2) + 1;
            var yNorm = (series[i].value - min) / (max - min);
            var y = (1 - yNorm) * (h - 2) + 1;
            poly.Points.Add(new Windows.Foundation.Point(x, y));
        }

        canvas.Children.Add(poly);

        if (markers != null && markers.Count > 0)
        {
            foreach (var (index, brush) in markers)
            {
                if (index < 0 || index >= series.Count)
                    continue;
                var x = (double)index / (series.Count - 1) * (w - 2) + 1;
                var yNorm = (series[index].value - min) / (max - min);
                var y = (1 - yNorm) * (h - 2) + 1;

                var dot = new Ellipse
                {
                    Width = 6,
                    Height = 6,
                    Fill = brush,
                    Stroke = brush,
                    StrokeThickness = 1
                };

                Canvas.SetLeft(dot, x - 3);
                Canvas.SetTop(dot, y - 3);
                canvas.Children.Add(dot);
            }
        }
    }

    private static async Task SaveElementAsPngAsync(FrameworkElement element, StorageFile file)
    {
        var rtb = new RenderTargetBitmap();
        await rtb.RenderAsync(element);
        var buffer = await rtb.GetPixelsAsync();

        using IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            (uint)rtb.PixelWidth,
            (uint)rtb.PixelHeight,
            96,
            96,
            buffer.ToArray());
        await encoder.FlushAsync();
    }
}

