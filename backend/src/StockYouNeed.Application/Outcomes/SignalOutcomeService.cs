using Microsoft.Extensions.Logging;
using StockYouNeed.Application.Abstractions;
using StockYouNeed.Application.Outcomes;
using StockYouNeed.Application.Services;
using StockYouNeed.Domain;

namespace StockYouNeed.Application.Outcomes;

/// <summary>
/// Opens live outcome rows when setups are emitted; resolves them against new bars
/// with the same SL/target/time-stop rules as historical backtest.
/// </summary>
public sealed class SignalOutcomeService
{
    private readonly ISignalOutcomeRepository _outcomes;
    private readonly IMarketDataRepository _market;
    private readonly IPortfolioRepository _portfolio;
    private readonly IBreakoutRepository _breakout;
    private readonly ITradeScoreRepository _tradeScore;
    private readonly IntradayBarsSyncService _intradaySync;
    private readonly ILogger<SignalOutcomeService> _logger;

    public SignalOutcomeService(
        ISignalOutcomeRepository outcomes,
        IMarketDataRepository market,
        IPortfolioRepository portfolio,
        IBreakoutRepository breakout,
        ITradeScoreRepository tradeScore,
        IntradayBarsSyncService intradaySync,
        ILogger<SignalOutcomeService> logger)
    {
        _outcomes = outcomes;
        _market = market;
        _portfolio = portfolio;
        _breakout = breakout;
        _tradeScore = tradeScore;
        _intradaySync = intradaySync;
        _logger = logger;
    }

    public Task OpenAsync(SignalOutcomeRow row, CancellationToken ct = default)
        => _outcomes.OpenAsync(row, ct);

    public Task OpenFromSignalAsync(AnalysisSignalRow signal, CancellationToken ct = default)
        => OpenAsync(new SignalOutcomeRow
        {
            Id = Guid.NewGuid(),
            UserId = signal.UserId,
            InstrumentId = signal.InstrumentId,
            Strategy = "signals",
            Side = signal.Side,
            SignalDate = signal.AsOfDate,
            EntryPrice = signal.EntryPrice,
            InitialStopLoss = signal.InitialStopLoss,
            TargetT1 = signal.TargetT1,
            TargetT2 = signal.TargetT2,
            TargetT3 = signal.TargetT3,
            AnalysisSignalId = signal.Id,
            SectorConfirmed = signal.SectorConfirmed,
        }, ct);

    public Task OpenFromLiquidityAsync(LiquiditySignalRow signal, string ruleset, CancellationToken ct = default)
    {
        var r = ruleset.Trim().ToLowerInvariant();
        var strategy = r switch
        {
            "fresh" => "liquidity_fresh",
            "v2" => "liquidity_v2",
            _ => "liquidity"
        };
        return OpenAsync(new SignalOutcomeRow
        {
            Id = Guid.NewGuid(),
            UserId = signal.UserId,
            InstrumentId = signal.InstrumentId,
            Strategy = strategy,
            Side = signal.Side,
            SignalDate = signal.AsOfDate,
            EntryPrice = signal.EntryPrice,
            InitialStopLoss = signal.InitialStopLoss,
            TargetT1 = signal.TargetT1,
            TargetT2 = signal.TargetT2,
            TargetT3 = signal.TargetT3,
            LiquiditySignalId = signal.Id,
            SectorConfirmed = signal.SectorConfirmed,
        }, ct);
    }

    public Task OpenFromMomentumAsync(MomentumSignalRow signal, string ruleset, CancellationToken ct = default)
    {
        var strategy = NormalizeRuleset(ruleset) == "v3" ? "momentum_v3" : "momentum_v2";
        return OpenAsync(new SignalOutcomeRow
        {
            Id = Guid.NewGuid(),
            UserId = signal.UserId,
            InstrumentId = signal.InstrumentId,
            Strategy = strategy,
            Side = signal.Side,
            SignalDate = signal.AsOfDate,
            EntryPrice = signal.EntryPrice,
            InitialStopLoss = signal.InitialStopLoss,
            TargetT1 = signal.TargetT1,
            TargetT2 = signal.TargetT2,
            TargetT3 = signal.TargetT3,
            MomentumSignalId = signal.Id == Guid.Empty ? null : signal.Id,
            SectorConfirmed = signal.SectorConfirmed,
        }, ct);
    }

    public Task OpenFromConfluenceAsync(ConfluenceSignalRow signal, CancellationToken ct = default)
        => OpenAsync(new SignalOutcomeRow
        {
            Id = Guid.NewGuid(),
            UserId = signal.UserId,
            InstrumentId = signal.InstrumentId,
            Strategy = "confluence",
            Side = signal.Side,
            SignalDate = signal.AsOfDate,
            EntryPrice = signal.EntryPrice,
            InitialStopLoss = signal.InitialStopLoss,
            TargetT1 = signal.TargetT1,
            TargetT2 = signal.TargetT2,
            TargetT3 = signal.TargetT3,
            AnalysisSignalId = signal.AnalysisSignalId,
            LiquiditySignalId = signal.LiquiditySignalId,
            SectorConfirmed = signal.SectorConfirmed,
        }, ct);

    public Task OpenFromTradeScoreAsync(TradeConfidenceScoreRow score, CancellationToken ct = default)
        => OpenAsync(new SignalOutcomeRow
        {
            Id = Guid.NewGuid(),
            UserId = score.UserId,
            InstrumentId = score.InstrumentId,
            Strategy = "trade_score",
            Side = score.Side,
            SignalDate = score.AsOfDate,
            EntryPrice = score.EntryPrice,
            InitialStopLoss = score.InitialStopLoss,
            TargetT1 = score.TargetT1,
            TargetT2 = score.TargetT2,
            TargetT3 = score.TargetT3,
            AnalysisSignalId = score.AnalysisSignalId,
            LiquiditySignalId = score.LiquiditySignalId,
            TradeConfidenceScoreId = score.Id,
            // Trade Score does not store sector on the score row; treat as unconfirmed unless both layers were.
            SectorConfirmed = false,
        }, ct);

    public Task OpenFromBreakoutAsync(BreakoutConfirmationRow row, CancellationToken ct = default)
    {
        if (!row.Confirmed || row.ClosePrice is not decimal close || row.Level20d is not decimal level)
            return Task.CompletedTask;

        var entry = close;
        var sl = row.Side == SignalSides.Buy
            ? (level < entry ? level : entry * 0.98m)
            : (level > entry ? level : entry * 1.02m);
        var risk = Math.Abs(entry - sl);
        if (risk <= 0) risk = entry * 0.01m;
        var t1 = row.Side == SignalSides.Buy ? entry + risk * 2m : entry - risk * 2m;
        var t2 = row.Side == SignalSides.Buy ? entry + risk * 3m : entry - risk * 3m;
        var t3 = row.Side == SignalSides.Buy ? entry + risk * 4m : entry - risk * 4m;

        return OpenAsync(new SignalOutcomeRow
        {
            Id = Guid.NewGuid(),
            UserId = row.UserId,
            InstrumentId = row.InstrumentId,
            Strategy = "breakout",
            Side = row.Side,
            SignalDate = row.AsOfDate,
            EntryPrice = entry,
            InitialStopLoss = sl,
            TargetT1 = t1,
            TargetT2 = t2,
            TargetT3 = t3,
            BreakoutConfirmationId = row.Id,
            SectorConfirmed = false,
        }, ct);
    }

    private static string NormalizeRuleset(string? ruleset)
    {
        var s = (ruleset ?? "v2").Trim().ToLowerInvariant();
        return s == "v3" ? "v3" : "v2";
    }

    public Task<IReadOnlyList<SignalOutcomeRow>> GetOutcomesAsync(
        Guid userId, string? strategy, string? result, bool sectorConfirmedOnly = false,
        DateOnly? fromDate = null, DateOnly? toDate = null,
        CancellationToken ct = default)
        => _outcomes.GetOutcomesAsync(
            userId, strategy, result, sectorConfirmedOnly, fromDate, toDate, ct);

    public Task<IReadOnlyList<SignalOutcomeRow>> GetOpenAsync(
        Guid userId, CancellationToken ct = default)
        => _outcomes.GetOpenAsync(userId, ct);

    public Task<IReadOnlyList<SignalOutcomeSummary>> GetSummariesAsync(
        Guid userId, string? strategy, bool sectorConfirmedOnly = false,
        DateOnly? fromDate = null, DateOnly? toDate = null,
        CancellationToken ct = default)
        => _outcomes.GetSummariesAsync(
            userId, strategy, sectorConfirmedOnly, fromDate, toDate, ct);

    /// <summary>
    /// Import currently stored live setups into signal_outcomes (idempotent).
    /// Use when Accuracy is empty but Signals/Liquidity/etc. already have rows.
    /// </summary>
    public async Task<int> BackfillFromLiveAsync(Guid userId, CancellationToken ct = default)
    {
        var before = (await _outcomes.GetOutcomesAsync(userId, null, null, ct: ct)).Count;
        var opened = 0;

        foreach (var sig in await _portfolio.GetSignalsAsync(userId, null, ct))
        {
            await OpenFromSignalAsync(sig, ct);
            opened++;
        }

        foreach (var sig in await _portfolio.GetLiquiditySignalsAsync(userId, null, "classic", ct))
        {
            await OpenFromLiquidityAsync(sig, "classic", ct);
            opened++;
        }

        foreach (var sig in await _portfolio.GetLiquiditySignalsAsync(userId, null, "fresh", ct))
        {
            await OpenFromLiquidityAsync(sig, "fresh", ct);
            opened++;
        }

        var signals = await _portfolio.GetSignalsAsync(userId, null, ct);
        var liquidityV2 = await _portfolio.GetLiquiditySignalsAsync(userId, null, "v2", ct);
        foreach (var liq in liquidityV2)
        {
            var sig = signals.FirstOrDefault(s =>
                s.InstrumentId == liq.InstrumentId
                && string.Equals(s.Side, liq.Side, StringComparison.OrdinalIgnoreCase)
                && Confluence.ConfluenceLevelComposer.DatesAlign(s.AsOfDate, liq.AsOfDate)
                && Confluence.ConfluenceLevelComposer.PricesAlign(liq.EntryPrice, s.EntryPrice, liq.EntryPrice));
            if (sig is null) continue;
            if (!Confluence.ConfluenceLevelComposer.TryCompose(
                liq.Side, sig.EntryPrice, sig.InitialStopLoss,
                liq.EntryPrice, liq.InitialStopLoss,
                out var entry, out var sl))
                continue;

            await OpenFromConfluenceAsync(new ConfluenceSignalRow
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                InstrumentId = liq.InstrumentId,
                Side = liq.Side,
                AsOfDate = liq.AsOfDate,
                EntryPrice = entry,
                InitialStopLoss = sl,
                TargetT1 = liq.TargetT1,
                TargetT2 = liq.TargetT2,
                TargetT3 = liq.TargetT3,
                AnalysisSignalId = sig.Id,
                LiquiditySignalId = liq.Id,
                SectorConfirmed = sig.SectorConfirmed && liq.SectorConfirmed,
            }, ct);
            opened++;
        }

        foreach (var row in await _breakout.GetConfirmationsAsync(userId, null, ct))
        {
            if (!row.Confirmed) continue;
            await OpenFromBreakoutAsync(row, ct);
            opened++;
        }

        foreach (var score in await _tradeScore.GetScoresAsync(userId, null, ct))
        {
            await OpenFromTradeScoreAsync(score, ct);
            opened++;
        }

        foreach (var sig in await _portfolio.GetMomentumSignalsAsync(userId, null, "v2", ct))
        {
            await OpenFromMomentumAsync(sig, "v2", ct);
            opened++;
        }

        foreach (var sig in await _portfolio.GetMomentumSignalsAsync(userId, null, "v3", ct))
        {
            await OpenFromMomentumAsync(sig, "v3", ct);
            opened++;
        }

        var after = (await _outcomes.GetOutcomesAsync(userId, null, null, ct: ct)).Count;
        var created = Math.Max(0, after - before);
        _logger.LogInformation(
            "Outcome backfill: considered={Opened}, newly stored={Created} (duplicates skipped)",
            opened, created);
        return created;
    }

    /// <summary>
    /// Resolve open outcomes. Only applies time_stop once the full horizon of bars is available;
    /// SL/target can close earlier.
    /// </summary>
    public async Task<int> ResolveOpenAsync(Guid userId, CancellationToken ct = default)
    {
        var open = await _outcomes.GetOpenAsync(userId, ct);
        if (open.Count == 0)
            return 0;

        if (open.Any(o => OutcomeSimulator.UsesHourlyBars(o.Strategy)))
        {
            try
            {
                await _intradaySync.SyncUniverseHourlyAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Intraday sync before outcome resolve failed — continuing with DB bars");
            }
        }

        var resolved = 0;
        foreach (var row in open)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (await TryResolveOneAsync(row, ct))
                    resolved++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed resolving outcome {Id} {Symbol} {Strategy}",
                    row.Id, row.AppSymbol, row.Strategy);
            }
        }

        _logger.LogInformation(
            "Signal outcomes resolve: open={Open}, resolved={Resolved}", open.Count, resolved);
        return resolved;
    }

    private async Task<bool> TryResolveOneAsync(SignalOutcomeRow row, CancellationToken ct)
    {
        var horizon = OutcomeSimulator.TimeStopBars(row.Strategy);
        List<(decimal High, decimal Low, decimal Close, DateOnly? Date, DateTimeOffset? Time)> forward;

        if (OutcomeSimulator.UsesHourlyBars(row.Strategy))
        {
            var bars = await _market.GetIntradayBarsForInstrumentAsync(
                row.InstrumentId, IntradayBarsSyncService.Interval1h, horizon + 80, ct);
            forward = bars
                .Where(b => DateOnly.FromDateTime(b.BarTime.ToOffset(TimeSpan.FromHours(5.5)).DateTime) > row.SignalDate)
                .OrderBy(b => b.BarTime)
                .Take(horizon)
                .Select(b => (b.High, b.Low, b.Close,
                    (DateOnly?)DateOnly.FromDateTime(b.BarTime.ToOffset(TimeSpan.FromHours(5.5)).DateTime),
                    (DateTimeOffset?)b.BarTime))
                .ToList();
        }
        else
        {
            var bars = await _market.GetBarsForInstrumentAsync(row.InstrumentId, horizon + 40, ct);
            forward = bars
                .Where(b => b.TradeDate > row.SignalDate)
                .OrderBy(b => b.TradeDate)
                .Take(horizon)
                .Select(b => (b.High, b.Low, b.Close, (DateOnly?)b.TradeDate, (DateTimeOffset?)null))
                .ToList();
        }

        if (forward.Count == 0)
            return false;

        var sim = OutcomeSimulator.Simulate(
            row.Side, row.EntryPrice, row.InitialStopLoss,
            row.TargetT1, row.TargetT2, row.TargetT3, forward);

        // Keep open until full horizon unless SL/target already decided.
        if (sim.Result == "time_stop" && forward.Count < horizon)
            return false;

        row.Result = sim.Result;
        row.TargetLevel = sim.TargetLevel;
        row.TargetHitPct = sim.TargetHitPct;
        row.ExitPrice = sim.ExitPrice;
        row.ExitDate = sim.ExitDate;
        row.PnlPct = sim.PnlPct;
        row.RMultiple = sim.RMultiple;
        await _outcomes.ResolveAsync(row, ct);
        return true;
    }
}
