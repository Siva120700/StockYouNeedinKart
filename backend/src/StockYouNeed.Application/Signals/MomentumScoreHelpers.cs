using StockYouNeed.Domain;

namespace StockYouNeed.Application.Signals;

/// <summary>Shared bar/return helpers for momentum scoring.</summary>
public static class MomentumScoreHelpers
{
    public const int MomentumBarDays = 280;
    public const int SkipRecentTradingDays = 21;
    public const int TradingDays3M = 63;
    public const int TradingDays6M = 126;
    public const int TradingDays12M = 252;

    public static List<MarketBarRow> ToChronological(IReadOnlyList<MarketBarRow> newestFirst)
        => newestFirst.OrderBy(b => b.TradeDate).ToList();

    /// <summary>Map intraday bars to daily-like rows for RSI/EMA (BarTime → TradeDate).</summary>
    public static List<MarketBarRow> ToChronological(IReadOnlyList<MarketIntradayBarRow> newestFirst)
    {
        return newestFirst
            .OrderBy(b => b.BarTime)
            .Select(b => new MarketBarRow
            {
                InstrumentId = b.InstrumentId,
                AppSymbol = b.AppSymbol,
                TradeDate = DateOnly.FromDateTime(b.BarTime.ToOffset(TimeSpan.FromHours(5.5)).DateTime),
                Open = b.Open,
                High = b.High,
                Low = b.Low,
                Close = b.Close,
                Volume = b.Volume,
            })
            .ToList();
    }

    /// <summary>Return from <paramref name="startBarsAgo"/> to <paramref name="endBarsAgo"/> (bars ago from latest).</summary>
    public static decimal? ReturnBetween(IReadOnlyList<MarketBarRow> chron, int endBarsAgo, int startBarsAgo)
    {
        if (chron.Count <= startBarsAgo || endBarsAgo >= startBarsAgo)
            return null;

        var endIdx = chron.Count - 1 - endBarsAgo;
        var startIdx = chron.Count - 1 - startBarsAgo;
        if (endIdx < 0 || startIdx < 0)
            return null;

        var pEnd = chron[endIdx].Close;
        var pStart = chron[startIdx].Close;
        if (pStart <= 0)
            return null;

        return (pEnd - pStart) / pStart;
    }

    public static decimal? CloseBarsAgo(IReadOnlyList<MarketBarRow> chron, int barsAgo)
    {
        var idx = chron.Count - 1 - barsAgo;
        if (idx < 0 || idx >= chron.Count)
            return null;
        return chron[idx].Close;
    }

    public static decimal PercentileOfValue(decimal value, IReadOnlyList<decimal> sortedAsc)
    {
        if (sortedAsc.Count == 0)
            return 50m;
        var rank = sortedAsc.Count(v => v <= value);
        return Math.Round(100m * rank / sortedAsc.Count, 2);
    }

    public static decimal AlignPercentileForSide(decimal percentile, bool isBuy)
        => isBuy ? percentile : 100m - percentile;

    public static decimal ClampScore(decimal score) =>
        Math.Round(Math.Clamp(score, 0m, 10m), 1);
}
