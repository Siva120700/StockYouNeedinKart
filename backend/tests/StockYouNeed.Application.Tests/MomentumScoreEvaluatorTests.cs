using StockYouNeed.Application.Signals;
using StockYouNeed.Domain;
using Xunit;

namespace StockYouNeed.Application.Tests;

public sealed class MomentumScoreEvaluatorTests
{
    [Fact]
    public void PercentileOfValue_RanksCorrectly()
    {
        var sorted = new List<decimal> { 1m, 2m, 3m, 4m, 5m };
        Assert.Equal(20m, MomentumScoreHelpers.PercentileOfValue(1m, sorted));
        Assert.Equal(100m, MomentumScoreHelpers.PercentileOfValue(5m, sorted));
        Assert.Equal(60m, MomentumScoreHelpers.PercentileOfValue(3m, sorted));
    }

    [Fact]
    public void AlignPercentileForSide_InvertsForSell()
    {
        Assert.Equal(80m, MomentumScoreHelpers.AlignPercentileForSide(80m, isBuy: true));
        Assert.Equal(20m, MomentumScoreHelpers.AlignPercentileForSide(80m, isBuy: false));
    }

    [Fact]
    public void V2_TierLabels_MatchBands()
    {
        Assert.Equal("Strong", MomentumScoreV2Evaluator.TierLabel(8.5m));
        Assert.Equal("Good", MomentumScoreV2Evaluator.TierLabel(6.2m));
        Assert.Equal("Average", MomentumScoreV2Evaluator.TierLabel(4.8m));
        Assert.Equal("Weak", MomentumScoreV2Evaluator.TierLabel(3m));
    }

    [Fact]
    public void V2_ScoresAlignedBuyMoveHigher()
    {
        var bars = BuildTrendingBars(start: 100m, step: 0.8m, count: 60, volume: 2_000_000);
        var score = MomentumScoreV2Evaluator.Score(SignalSides.Buy, bars, null);
        Assert.NotNull(score);
        Assert.True(score > 0m);
    }

    [Fact]
    public void V2_RvolBoostsScore()
    {
        var bars = BuildTrendingBars(start: 100m, step: 0.2m, count: 30, volume: 500_000);
        bars[0].Volume = 5_000_000;
        var score = MomentumScoreV2Evaluator.Score(SignalSides.Buy, bars, null);
        Assert.NotNull(score);
        Assert.True(score >= 2m);
    }

    [Fact]
    public void V3_RequiresLongHistory()
    {
        var bars = BuildTrendingBars(start: 100m, step: 0.2m, count: 40, volume: 500_000);
        var pct = new Dictionary<Guid, decimal> { [Guid.NewGuid()] = 50m };
        var id = pct.Keys.First();
        var score = MomentumScoreV3Evaluator.Score(
            SignalSides.Buy, id, bars, null, pct, pct, pct, pct);
        Assert.Null(score);
    }

    private static List<MarketBarRow> BuildTrendingBars(
        decimal start, decimal step, int count, long volume)
    {
        var list = new List<MarketBarRow>();
        var price = start;
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-count));
        for (var i = 0; i < count; i++)
        {
            list.Add(new MarketBarRow
            {
                TradeDate = date.AddDays(i),
                Open = price,
                High = price + 1m,
                Low = price - 0.5m,
                Close = price,
                Volume = volume + i * 1000,
            });
            price += step;
        }
        return list.OrderByDescending(b => b.TradeDate).ToList();
    }
}
