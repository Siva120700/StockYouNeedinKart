using Microsoft.Extensions.Logging;
using StockYouNeed.Application.Abstractions;
using StockYouNeed.Application.Outcomes;
using StockYouNeed.Application.TradeScore;
using StockYouNeed.Domain;

namespace StockYouNeed.Application.Services;

/// <summary>
/// High-probability trade scoring — separate from Signals/Liquidity live runs.
/// Primary: Signals (40%). Confirmations: Liquidity Fresh (20%), Quality Breakout (20%).
/// Phase 3–4 placeholders: Futures (10%), Options (10%).
/// </summary>
public sealed class TradeConfidenceService
{
    private readonly IPortfolioRepository _portfolio;
    private readonly ITradeScoreRepository _tradeScore;
    private readonly IMarketDataRepository _market;
    private readonly AnalysisRunService _analysis;
    private readonly LiquidityAnalysisService _liquidity;
    private readonly SignalOutcomeService _outcomes;
    private readonly ILogger<TradeConfidenceService> _logger;

    public TradeConfidenceService(
        IPortfolioRepository portfolio,
        ITradeScoreRepository tradeScore,
        IMarketDataRepository market,
        AnalysisRunService analysis,
        LiquidityAnalysisService liquidity,
        SignalOutcomeService outcomes,
        ILogger<TradeConfidenceService> logger)
    {
        _portfolio = portfolio;
        _tradeScore = tradeScore;
        _market = market;
        _analysis = analysis;
        _liquidity = liquidity;
        _outcomes = outcomes;
        _logger = logger;
    }

    public async Task<IReadOnlyList<TradeConfidenceScoreRow>> GetScoresAsync(
        Guid userId, Guid? runId, CancellationToken ct = default)
        => await _tradeScore.GetScoresAsync(userId, runId, ct);

    public async Task<TradeConfidenceRunRow> RunAsync(
        Guid userId,
        bool refreshSignals,
        bool refreshLiquidity,
        CancellationToken ct = default)
    {
        var asOf = DateOnly.FromDateTime(DateTime.Now);
        var runId = await _tradeScore.CreateRunAsync(userId, "manual", asOf, ct);

        try
        {
            if (refreshSignals)
            {
                await _analysis.RunAsync(
                    userId, true, true, true, AnalysisTriggers.ManualRun, includeSectorCheck: false, ct);
            }

            if (refreshLiquidity)
            {
                await _liquidity.RunAsync(
                    userId, true, true, true, "manual", ct, ruleset: "fresh");
            }

            var signals = await _portfolio.GetSignalsAsync(userId, null, ct);
            var liquidity = await _portfolio.GetLiquiditySignalsAsync(userId, null, "fresh", ct);
            var scored = 0;

            foreach (var sig in signals)
            {
                ct.ThrowIfCancellationRequested();

                var liq = liquidity.FirstOrDefault(l =>
                    l.InstrumentId == sig.InstrumentId
                    && string.Equals(l.Side, sig.Side, StringComparison.OrdinalIgnoreCase)
                    && TradeScoreLevelComposer.DatesAlign(l.AsOfDate, sig.AsOfDate));

                var bars = await _market.GetBarsAsync(sig.InstrumentId, 60, ct);
                var barsDesc = bars.OrderByDescending(b => b.TradeDate).ToList();
                var breakout = barsDesc.Count >= 21
                    ? BreakoutConfirmationEvaluator.Evaluate(barsDesc)
                    : null;

                var breakoutConfirmed = breakout is { Confirmed: true }
                    && string.Equals(breakout.Side, sig.Side, StringComparison.OrdinalIgnoreCase);

                if (breakout is not null)
                {
                    await _tradeScore.InsertBreakoutAsync(
                        runId, userId, sig.InstrumentId, sig.Side, sig.AsOfDate,
                        breakout.Confirmed,
                        breakout.Close, breakout.BreakoutLevel, breakout.VolumeRatio,
                        null, null, null, false, ct);
                }

                var liquidityAligned = liq is not null
                    && TradeScoreLevelComposer.PricesAlign(
                        sig.EntryPrice, liq.EntryPrice, sig.EntryPrice);

                var breakdown = TradeConfidenceScorer.Score(
                    hasPrimarySignal: true,
                    liquidityAligned,
                    breakoutConfirmed);

                if (breakdown.TotalScore < 40)
                    continue;

                if (!TradeScoreLevelComposer.TryCompose(
                    sig.Side, sig.EntryPrice, sig.InitialStopLoss,
                    liq?.EntryPrice, liq?.InitialStopLoss,
                    out var entry, out var sl))
                    continue;

                var t1 = liq?.TargetT1 ?? sig.TargetT1;
                var t2 = liq?.TargetT2 ?? sig.TargetT2;
                var t3 = liq?.TargetT3 ?? sig.TargetT3;

                var score = new TradeConfidenceScoreRow
                {
                    Id = Guid.NewGuid(),
                    RunId = runId,
                    UserId = userId,
                    InstrumentId = sig.InstrumentId,
                    AppSymbol = sig.AppSymbol,
                    InstrumentName = sig.InstrumentName,
                    Side = sig.Side,
                    AsOfDate = sig.AsOfDate,
                    ConfidenceScore = breakdown.TotalScore,
                    Rating = breakdown.Rating,
                    SignalsScore = breakdown.SignalsScore,
                    LiquidityScore = breakdown.LiquidityScore,
                    BreakoutScore = breakdown.BreakoutScore,
                    FuturesScore = breakdown.FuturesScore,
                    OptionsScore = breakdown.OptionsScore,
                    Reasons = breakdown.Reasons.ToArray(),
                    EntryPrice = entry,
                    InitialStopLoss = sl,
                    TargetT1 = t1,
                    TargetT2 = t2,
                    TargetT3 = t3,
                    AnalysisSignalId = sig.Id,
                    LiquiditySignalId = liq?.Id,
                    BreakoutConfirmed = breakoutConfirmed,
                    BreakoutAdx = breakout?.PatternDepthPct,
                    BreakoutRsi = null,
                };
                await _tradeScore.InsertScoreAsync(score, ct);
                await _outcomes.OpenFromTradeScoreAsync(score, ct);

                scored++;
            }

            await _tradeScore.CompleteRunAsync(runId, userId, "succeeded", null, ct);
            _logger.LogInformation("Trade confidence run {RunId}: {Scored} scored rows", runId, scored);

            return new TradeConfidenceRunRow
            {
                Id = runId,
                UserId = userId,
                AsOfDate = asOf,
                Status = "succeeded",
            };
        }
        catch (Exception ex)
        {
            await _tradeScore.CompleteRunAsync(runId, userId, "failed", ex.Message, ct);
            throw;
        }
    }
}
