using StockYouNeed.Application.Signals;
using StockYouNeed.Domain;
using Xunit;

namespace StockYouNeed.Application.Tests;

public class SpikeScanEvaluatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 18, 10, 20, 0, TimeSpan.Zero);

    [Fact]
    public void Evaluate_FlagsUpBarWithVolumeSurge()
    {
        var bars = QuietThen(open: 100, close: 101.2m, high: 101.3m, low: 99.9m, volume: 40_000);
        var hit = SpikeScanEvaluator.Evaluate(bars, Now);

        Assert.NotNull(hit);
        Assert.Equal("buy", hit!.Side);
        Assert.True(hit.ChangePct >= SpikeScanEvaluator.MinAbsChangePct);
        Assert.True(hit.RelativeVolume >= SpikeScanEvaluator.MinRvol);
        Assert.True(hit.EntryPrice > hit.InitialStopLoss);
    }

    [Fact]
    public void Evaluate_FlagsDownBar()
    {
        var bars = QuietThen(open: 100, close: 98.8m, high: 100.1m, low: 98.7m, volume: 40_000);
        var hit = SpikeScanEvaluator.Evaluate(bars, Now);

        Assert.NotNull(hit);
        Assert.Equal("sell", hit!.Side);
        Assert.True(hit.InitialStopLoss > hit.EntryPrice);
    }

    [Fact]
    public void Evaluate_RejectsQuietVolume()
    {
        var bars = QuietThen(open: 100, close: 101.2m, high: 101.3m, low: 99.9m, volume: 1_000);
        Assert.Null(SpikeScanEvaluator.Evaluate(bars, Now));
    }

    [Fact]
    public void Evaluate_RejectsSmallMove()
    {
        var bars = QuietThen(open: 100, close: 100.1m, high: 100.15m, low: 99.95m, volume: 40_000);
        Assert.Null(SpikeScanEvaluator.Evaluate(bars, Now));
    }

    [Fact]
    public void Evaluate_RejectsDojiEvenWithVolume()
    {
        var bars = QuietThen(open: 100, close: 100.05m, high: 101.2m, low: 98.8m, volume: 40_000);
        Assert.Null(SpikeScanEvaluator.Evaluate(bars, Now));
    }

    private static List<MarketIntradayBarRow> QuietThen(
        decimal open, decimal close, decimal high, decimal low, long volume)
    {
        var id = Guid.NewGuid();
        var t0 = new DateTimeOffset(2026, 8, 18, 10, 0, 0, TimeSpan.FromHours(5.5));
        var rows = new List<MarketIntradayBarRow>
        {
            Bar(id, t0, open, high, low, close, volume),
        };
        for (var i = 1; i <= SpikeScanEvaluator.VolumeLookback; i++)
        {
            rows.Add(Bar(id, t0.AddMinutes(-15 * i), 100, 100.2m, 99.8m, 100.05m, 10_000));
        }
        return rows;
    }

    private static MarketIntradayBarRow Bar(
        Guid id, DateTimeOffset time, decimal open, decimal high, decimal low, decimal close, long volume)
        => new()
        {
            InstrumentId = id,
            AppSymbol = "TEST",
            Interval = "15m",
            BarTime = time,
            Open = open,
            High = high,
            Low = low,
            Close = close,
            Volume = volume,
        };
}
