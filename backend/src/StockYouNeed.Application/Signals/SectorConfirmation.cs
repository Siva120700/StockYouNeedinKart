using StockYouNeed.Domain;

namespace StockYouNeed.Application.Signals;

/// <summary>
/// Sector confirmation: linked sector index must also break the prior 2 sessions' high/low
/// (same side as the equity setup). Used by live engines, backtest, and accuracy filters.
/// </summary>
public static class SectorConfirmation
{
    /// <param name="sectorBarsNewestFirst">Sector daily bars with TradeDate ≤ as-of, newest first.</param>
    public static bool IsConfirmed(string side, IReadOnlyList<MarketBarRow> sectorBarsNewestFirst)
    {
        if (sectorBarsNewestFirst.Count < 3)
            return false;

        var latest = sectorBarsNewestFirst[0];
        var prev = sectorBarsNewestFirst.Skip(1).Take(2).ToList();
        var last2High = prev.Max(b => b.High);
        var last2Low = prev.Min(b => b.Low);

        return side == SignalSides.Buy
            ? latest.High > last2High
            : latest.Low < last2Low;
    }

    public static IReadOnlyList<MarketBarRow> AsOf(
        IReadOnlyList<MarketBarRow> sectorBarsChronOrAny, DateOnly asOf)
    {
        return sectorBarsChronOrAny
            .Where(b => b.TradeDate <= asOf)
            .GroupBy(b => b.TradeDate)
            .Select(g => g.First())
            .OrderByDescending(b => b.TradeDate)
            .Take(10)
            .ToList();
    }
}
