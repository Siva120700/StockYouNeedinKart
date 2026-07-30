using StockYouNeed.Domain;

namespace StockYouNeed.Application.Confluence;

/// <summary>Signals + Liquidity Fresh: nearer SL, 0.2% entry tolerance.</summary>
public static class ConfluenceLevelComposer
{
    public const decimal PriceTolerancePct = 0.002m;
    public const int MaxCalendarDaysApart = 2;

    public static bool PricesAlign(decimal a, decimal b, decimal reference)
    {
        if (reference <= 0) return false;
        return Math.Abs(a - b) / reference <= PriceTolerancePct;
    }

    public static bool DatesAlign(DateOnly a, DateOnly b) =>
        Math.Abs(a.DayNumber - b.DayNumber) <= MaxCalendarDaysApart;

    public static decimal NearerStopLoss(string side, decimal slA, decimal slB) =>
        side == SignalSides.Buy ? Math.Max(slA, slB) : Math.Min(slA, slB);

    public static bool TryCompose(
        string side,
        decimal signalsEntry,
        decimal signalsSl,
        decimal liquidityEntry,
        decimal liquiditySl,
        out decimal entry,
        out decimal sl)
    {
        entry = liquidityEntry;
        if (!PricesAlign(signalsEntry, liquidityEntry, liquidityEntry))
        {
            sl = 0;
            return false;
        }

        sl = NearerStopLoss(side, signalsSl, liquiditySl);

        if (side == SignalSides.Buy && sl >= entry)
            sl = entry * (1m - PriceTolerancePct);
        else if (side == SignalSides.Sell && sl <= entry)
            sl = entry * (1m + PriceTolerancePct);

        return true;
    }
}
