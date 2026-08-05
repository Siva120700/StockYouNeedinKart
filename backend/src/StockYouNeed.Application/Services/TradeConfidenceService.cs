using Microsoft.Extensions.Logging;
using StockYouNeed.Application.Abstractions;
using StockYouNeed.Application.Outcomes;
using StockYouNeed.Application.Signals;
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

    /// <summary>
    /// Calculate Trade Score for one stock using current daily bars and the live
    /// per-stock liquidity result. This is ephemeral and does not create a run.
    /// </summary>
    public async Task<(TradeConfidenceScoreRow Score, AnalysisSignalRow? Signal)>
        EvaluateForInstrumentAsync(
            Guid userId,
            Instrument instrument,
            LiquiditySignalRow? liveLiquidity,
            CancellationToken ct = default)
    {
        var bars = (await _market.GetBarsForInstrumentAsync(instrument.Id, 60, ct))
            .OrderByDescending(b => b.TradeDate)
            .ToList();
        var ltp = (await _market.GetAllLtpAsync(ct))
            .FirstOrDefault(x => x.InstrumentId == instrument.Id)?.Ltp;
        var asOf = DateOnly.FromDateTime(
            DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(5.5)).DateTime);

        AnalysisSignalRow? signal = null;
        if (bars.Count >= 5)
        {
            signal = BreakoutSignalEvaluator.Evaluate(
                userId, Guid.Empty, asOf, bars, ltp is > 0 ? ltp : null,
                actionableOnly: true);
            if (signal is not null)
                signal.InstrumentName = instrument.Name;
        }

        string? flipReason = null;
        if (signal is not null)
        {
            var openOutcomes = await _outcomes.GetOpenAsync(userId, ct);
            if (OppositeSignalFlipGuard.IsFlipAgainstOpen(
                    instrument.Id, signal.Side, asOf, openOutcomes, out flipReason))
            {
                signal = null;
            }
        }

        var breakout = bars.Count >= 21
            ? BreakoutConfirmationEvaluator.Evaluate(bars)
            : null;
        var liquiditySideAligned = signal is not null
            && liveLiquidity is not null
            && string.Equals(
                liveLiquidity.Side, signal.Side, StringComparison.OrdinalIgnoreCase);
        var liquidityDateAligned = liquiditySideAligned
            && TradeScoreLevelComposer.DatesAlign(
                liveLiquidity!.AsOfDate, signal!.AsOfDate);
        var liquidityPriceAligned = liquidityDateAligned
            && TradeScoreLevelComposer.PricesAlign(
                signal!.EntryPrice, liveLiquidity!.EntryPrice, signal.EntryPrice);
        var liquidityAligned = liquidityPriceAligned;
        var breakoutConfirmed = signal is not null
            && breakout is { Confirmed: true }
            && string.Equals(
                breakout.Side, signal.Side, StringComparison.OrdinalIgnoreCase);

        var breakdown = TradeConfidenceScorer.Score(
            signal is not null, liquidityAligned, breakoutConfirmed);
        var reasons = new List<string>();
        var breakoutLabel = breakout is null
            ? null
            : BreakoutConfirmationEvaluator.PatternLabel(breakout.PatternType);
        if (breakout is not null && breakoutLabel == "—")
            breakoutLabel = breakout.PatternType.Replace('_', ' ');

        if (signal is null)
            reasons.Add(flipReason is null
                ? "Daily signal absent (+0/20)"
                : $"Daily signal skipped — {flipReason} (+0/20)");
        else
            reasons.Add("Daily signal present (+20/20)");

        if (liveLiquidity is null)
            reasons.Add("Liquidity Fresh setup absent (+0/20)");
        else if (!liquiditySideAligned)
            reasons.Add("Liquidity side conflicts with daily signal (+0/20)");
        else if (!liquidityDateAligned)
            reasons.Add("Liquidity setup is not date-aligned (+0/20)");
        else if (!liquidityPriceAligned)
            reasons.Add("Liquidity entry differs by more than 0.2% (+0/20)");
        else
            reasons.Add("Liquidity Fresh side/date/entry aligned (+20/20)");

        if (breakout is null)
            reasons.Add("No recognized breakout pattern (+0/30)");
        else if (!breakout.Confirmed)
            reasons.Add($"{breakoutLabel} not confirmed (+0/30)");
        else if (!breakoutConfirmed)
            reasons.Add("Breakout direction conflicts with daily signal (+0/30)");
        else
            reasons.Add($"{breakoutLabel} confirmed (+30/30)");

        reasons.Add("Futures layer not evaluated (excluded from scale)");
        reasons.Add("Option-chain layer not evaluated (excluded from scale)");
        reasons.Add(
            $"Total {breakdown.RawScore}/{breakdown.AvailableWeight} available points " +
            $"= {breakdown.TotalScore}/100 — {TradeConfidenceScorer.RatingLabel(breakdown.Rating)}");

        decimal entry = 0;
        decimal sl = 0;
        if (signal is not null)
        {
            if (!TradeScoreLevelComposer.TryCompose(
                    signal.Side, signal.EntryPrice, signal.InitialStopLoss,
                    liquidityAligned ? liveLiquidity?.EntryPrice : null,
                    liquidityAligned ? liveLiquidity?.InitialStopLoss : null,
                    out entry, out sl))
            {
                entry = signal.EntryPrice;
                sl = signal.InitialStopLoss;
            }
        }

        return (new TradeConfidenceScoreRow
        {
            Id = Guid.NewGuid(),
            RunId = Guid.Empty,
            UserId = userId,
            InstrumentId = instrument.Id,
            AppSymbol = instrument.Symbol,
            InstrumentName = instrument.Name,
            Side = signal?.Side ?? "",
            AsOfDate = asOf,
            ConfidenceScore = breakdown.TotalScore,
            Rating = breakdown.Rating,
            SignalsScore = breakdown.SignalsScore,
            LiquidityScore = breakdown.LiquidityScore,
            BreakoutScore = breakdown.BreakoutScore,
            FuturesScore = breakdown.FuturesScore,
            OptionsScore = breakdown.OptionsScore,
            Reasons = reasons.Distinct().ToArray(),
            EntryPrice = entry,
            InitialStopLoss = sl,
            TargetT1 = liquidityAligned
                ? liveLiquidity?.TargetT1 ?? signal?.TargetT1
                : signal?.TargetT1,
            TargetT2 = liquidityAligned
                ? liveLiquidity?.TargetT2 ?? signal?.TargetT2
                : signal?.TargetT2,
            TargetT3 = liquidityAligned
                ? liveLiquidity?.TargetT3 ?? signal?.TargetT3
                : signal?.TargetT3,
            AnalysisSignalId = signal?.Id,
            LiquiditySignalId = liquidityAligned ? liveLiquidity?.Id : null,
            BreakoutConfirmed = breakoutConfirmed,
            BreakoutAdx = breakout?.PatternDepthPct,
        }, signal);
    }

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
            var openOutcomes = await _outcomes.GetOpenAsync(userId, ct);
            var scored = 0;
            var skippedFlip = 0;

            foreach (var sig in signals)
            {
                ct.ThrowIfCancellationRequested();

                // Ignore the same outcome row that belongs to this signal itself.
                var flipPeers = openOutcomes.Where(o =>
                    o.AnalysisSignalId != sig.Id
                    && !(o.Strategy == "signals"
                         && o.InstrumentId == sig.InstrumentId
                         && o.SignalDate == sig.AsOfDate
                         && string.Equals(o.Side, sig.Side, StringComparison.OrdinalIgnoreCase)));

                if (OppositeSignalFlipGuard.IsFlipAgainstOpen(
                        sig.InstrumentId, sig.Side, sig.AsOfDate, flipPeers, out var flipReason))
                {
                    skippedFlip++;
                    _logger.LogInformation(
                        "Trade Score skip {Symbol}: {Reason}", sig.AppSymbol, flipReason);
                    continue;
                }

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
            _logger.LogInformation(
                "Trade confidence run {RunId}: {Scored} scored rows, skippedFlip={Flip}",
                runId, scored, skippedFlip);

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
