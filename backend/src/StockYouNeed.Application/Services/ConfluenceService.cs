using StockYouNeed.Application.Abstractions;
using StockYouNeed.Domain;

namespace StockYouNeed.Application.Services;

public sealed class ConfluenceService
{
  private readonly IPortfolioRepository _portfolio;

  public ConfluenceService(IPortfolioRepository portfolio) => _portfolio = portfolio;

  /// <summary>Live overlap from latest Signals + Liquidity Fresh runs.</summary>
  public async Task<IReadOnlyList<ConfluenceSignalRow>> GetSignalsAsync(
    Guid userId, CancellationToken ct = default)
  {
    var signals = await _portfolio.GetSignalsAsync(userId, runId: null, ct);
    var liquidity = await _portfolio.GetLiquiditySignalsAsync(userId, runId: null, "fresh", ct);

    var rows = new List<ConfluenceSignalRow>();
    foreach (var liq in liquidity)
    {
      var match = signals.FirstOrDefault(s =>
        s.InstrumentId == liq.InstrumentId
        && string.Equals(s.Side, liq.Side, StringComparison.OrdinalIgnoreCase)
        && DatesAlign(s.AsOfDate, liq.AsOfDate)
        && PricesAlign(liq.EntryPrice, s.EntryPrice, liq.EntryPrice));

      if (match is null)
        continue;

      if (!TryCombineLevels(
        liq.Side, liq.EntryPrice, liq.InitialStopLoss,
        match.EntryPrice, match.InitialStopLoss,
        out var entry, out var sl))
        continue;

      rows.Add(new ConfluenceSignalRow
      {
        Id = Guid.NewGuid(),
        UserId = userId,
        InstrumentId = liq.InstrumentId,
        AppSymbol = liq.AppSymbol,
        InstrumentName = liq.InstrumentName,
        Side = liq.Side,
        AsOfDate = liq.AsOfDate,
        EntryPrice = entry,
        InitialStopLoss = sl,
        TargetT1 = liq.TargetT1,
        TargetT2 = liq.TargetT2,
        TargetT3 = liq.TargetT3,
        AnalysisSignalId = match.Id,
        LiquiditySignalId = liq.Id,
        SignalsEntry = match.EntryPrice,
        LiquidityEntry = liq.EntryPrice,
        SignalsStopLoss = match.InitialStopLoss,
        LiquidityStopLoss = liq.InitialStopLoss,
        SectorConfirmed = match.SectorConfirmed && liq.SectorConfirmed,
        FreshCross = match.FreshCross,
        RelativeVolume = liq.RelativeVolume,
        RvolPercentile = liq.RvolPercentile,
        StrongClose = liq.StrongClose,
        SweptZoneType = liq.SweptZoneType,
        TimeframeContext = "signals+liquidity_fresh",
      });
    }

    return rows
      .OrderByDescending(r => r.AsOfDate)
      .ThenBy(r => r.AppSymbol)
      .ToList();
  }

  private static bool PricesAlign(decimal a, decimal b, decimal reference) =>
    ConfluenceSignalHelper.PricesAlign(a, b, reference);

  private static bool DatesAlign(DateOnly a, DateOnly b) =>
    ConfluenceSignalHelper.DatesAlign(a, b);

  private static bool TryCombineLevels(
    string side, decimal liqEntry, decimal liqSl, decimal sigEntry, decimal sigSl,
    out decimal entry, out decimal sl) =>
    ConfluenceSignalHelper.TryCombineLevels(side, liqEntry, liqSl, sigEntry, sigSl, out entry, out sl);
}
