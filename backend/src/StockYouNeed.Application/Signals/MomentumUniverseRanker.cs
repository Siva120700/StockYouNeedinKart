using StockYouNeed.Domain;

namespace StockYouNeed.Application.Signals;

/// <summary>Cross-sectional percentile ranks for Jegadeesh–Titman horizons.</summary>
public static class MomentumUniverseRanker
{
    public sealed record HorizonReturns(
        decimal? Mom12_1,
        decimal? Mom6_1,
        decimal? Mom3_1);

    public static Dictionary<Guid, HorizonReturns> BuildHorizonReturns(
        IEnumerable<Guid> instrumentIds,
        IReadOnlyDictionary<Guid, List<MarketBarRow>> barsByInstrument)
    {
        var result = new Dictionary<Guid, HorizonReturns>();
        foreach (var id in instrumentIds)
        {
            if (!barsByInstrument.TryGetValue(id, out var bars) || bars.Count < 5)
                continue;

            var chron = MomentumScoreHelpers.ToChronological(bars);
            result[id] = new HorizonReturns(
                Mom12_1: MomentumScoreHelpers.ReturnBetween(
                    chron,
                    MomentumScoreHelpers.SkipRecentTradingDays,
                    MomentumScoreHelpers.TradingDays12M),
                Mom6_1: MomentumScoreHelpers.ReturnBetween(
                    chron,
                    MomentumScoreHelpers.SkipRecentTradingDays,
                    MomentumScoreHelpers.TradingDays6M),
                Mom3_1: MomentumScoreHelpers.ReturnBetween(
                    chron,
                    MomentumScoreHelpers.SkipRecentTradingDays,
                    MomentumScoreHelpers.TradingDays3M));
        }
        return result;
    }

    public static Dictionary<Guid, decimal> BuildPercentileMap(
        Dictionary<Guid, HorizonReturns> returns,
        Func<HorizonReturns, decimal?> selector)
    {
        var values = returns
            .Where(kv => selector(kv.Value) is decimal)
            .ToDictionary(kv => kv.Key, kv => selector(kv.Value)!.Value);

        if (values.Count == 0)
            return new Dictionary<Guid, decimal>();

        var sorted = values.Values.OrderBy(v => v).ToList();
        return values.ToDictionary(kv => kv.Key, kv => MomentumScoreHelpers.PercentileOfValue(kv.Value, sorted));
    }

    public static Dictionary<Guid, decimal> BuildLiquidityPercentiles(
        IEnumerable<Guid> instrumentIds,
        IReadOnlyDictionary<Guid, List<MarketBarRow>> barsByInstrument,
        int lookback = 20)
    {
        var tradedValues = new Dictionary<Guid, decimal>();
        foreach (var id in instrumentIds)
        {
            if (!barsByInstrument.TryGetValue(id, out var bars) || bars.Count < lookback)
                continue;
            var recent = bars.OrderByDescending(b => b.TradeDate).Take(lookback).ToList();
            var avg = recent.Average(b => (double)(b.Close * b.Volume));
            if (avg > 0)
                tradedValues[id] = (decimal)avg;
        }

        if (tradedValues.Count == 0)
            return new Dictionary<Guid, decimal>();

        var sorted = tradedValues.Values.OrderBy(v => v).ToList();
        return tradedValues.ToDictionary(kv => kv.Key, kv => MomentumScoreHelpers.PercentileOfValue(kv.Value, sorted));
    }
}
