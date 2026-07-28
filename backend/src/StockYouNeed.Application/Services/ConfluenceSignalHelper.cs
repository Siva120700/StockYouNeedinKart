using StockYouNeed.Domain;

namespace StockYouNeed.Application.Services;

/// <summary>
/// Signals + Liquidity Fresh overlap: same side, entries within 0.2%, SL = tighter of the two stops.
/// </summary>
public static class ConfluenceSignalHelper
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

  /// <summary>Tighter stop to entry (smaller risk distance).</summary>
  public static decimal NearerStopLoss(string side, decimal slA, decimal slB) =>
    side == SignalSides.Buy ? Math.Max(slA, slB) : Math.Min(slA, slB);

  public static bool TryCombineLevels(
    string side,
    decimal liquidityEntry,
    decimal liquiditySl,
    decimal signalsEntry,
    decimal signalsSl,
    out decimal combinedEntry,
    out decimal combinedSl)
  {
    combinedEntry = liquidityEntry;
    if (!PricesAlign(liquidityEntry, signalsEntry, liquidityEntry))
    {
      combinedSl = 0;
      return false;
    }

    combinedSl = NearerStopLoss(side, signalsSl, liquiditySl);

    if (side == SignalSides.Buy && combinedSl >= combinedEntry)
      combinedSl = combinedEntry * (1m - PriceTolerancePct);
    else if (side == SignalSides.Sell && combinedSl <= combinedEntry)
      combinedSl = combinedEntry * (1m + PriceTolerancePct);

    return true;
  }
}
