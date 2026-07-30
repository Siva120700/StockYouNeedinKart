using StockYouNeed.Application.Abstractions;
using StockYouNeed.Application.Outcomes;
using StockYouNeed.Domain;

namespace StockYouNeed.Application.Confluence;

/// <summary>Live Signals + Liquidity Fresh overlap — independent of Trade Score and Breakout.</summary>
public sealed class ConfluenceService
{
    private readonly IPortfolioRepository _portfolio;
    private readonly SignalOutcomeService _outcomes;

    public ConfluenceService(IPortfolioRepository portfolio, SignalOutcomeService outcomes)
    {
        _portfolio = portfolio;
        _outcomes = outcomes;
    }

    public async Task<IReadOnlyList<ConfluenceSignalRow>> GetSignalsAsync(
        Guid userId, CancellationToken ct = default)
    {
        var signals = await _portfolio.GetSignalsAsync(userId, null, ct);
        var liquidity = await _portfolio.GetLiquiditySignalsAsync(userId, null, "fresh", ct);
        var rows = new List<ConfluenceSignalRow>();

        foreach (var liq in liquidity)
        {
            var sig = signals.FirstOrDefault(s =>
                s.InstrumentId == liq.InstrumentId
                && string.Equals(s.Side, liq.Side, StringComparison.OrdinalIgnoreCase)
                && ConfluenceLevelComposer.DatesAlign(s.AsOfDate, liq.AsOfDate)
                && ConfluenceLevelComposer.PricesAlign(liq.EntryPrice, s.EntryPrice, liq.EntryPrice));

            if (sig is null)
                continue;

            if (!ConfluenceLevelComposer.TryCompose(
                liq.Side, sig.EntryPrice, sig.InitialStopLoss,
                liq.EntryPrice, liq.InitialStopLoss,
                out var entry, out var sl))
                continue;

            var row = new ConfluenceSignalRow
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
                AnalysisSignalId = sig.Id,
                LiquiditySignalId = liq.Id,
                SignalsEntry = sig.EntryPrice,
                LiquidityEntry = liq.EntryPrice,
                SignalsStopLoss = sig.InitialStopLoss,
                LiquidityStopLoss = liq.InitialStopLoss,
                SectorConfirmed = sig.SectorConfirmed && liq.SectorConfirmed,
                FreshCross = sig.FreshCross,
            };
            rows.Add(row);
            await _outcomes.OpenFromConfluenceAsync(row, ct);
        }

        return rows.OrderByDescending(r => r.AsOfDate).ThenBy(r => r.AppSymbol).ToList();
    }
}
