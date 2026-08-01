using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StockYouNeed.Application.Abstractions;
using StockYouNeed.Application.Options;
using StockYouNeed.Application.Confluence;
using StockYouNeed.Application.Outcomes;
using StockYouNeed.Application.Signals;
using StockYouNeed.Application.TradeScore;
using StockYouNeed.Domain;

namespace StockYouNeed.Application.Services;

/// <summary>
/// One-symbol historical replay (1 year). Fetches Angel candles in-memory;
/// does not call live AnalysisRunService / LiquidityAnalysisService RunAsync.
/// </summary>
public sealed class BacktestService
{
    private const int DailyTimeStopBars = OutcomeSimulator.DailyTimeStopBars;
    private const int HourlyTimeStopBars = OutcomeSimulator.HourlyTimeStopBars;

    private readonly IAngelMarketDataClient _angel;
    private readonly IInstrumentRepository _instruments;
    private readonly IBacktestRepository _backtest;
    private readonly AngelOptions _options;
    private readonly ILogger<BacktestService> _logger;

    public BacktestService(
        IAngelMarketDataClient angel,
        IInstrumentRepository instruments,
        IBacktestRepository backtest,
        IOptions<AngelOptions> options,
        ILogger<BacktestService> logger)
    {
        _angel = angel;
        _instruments = instruments;
        _backtest = backtest;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<BacktestSymbolSummary> RunHistoricalAsync(
        Guid userId,
        Guid instrumentId,
        string strategy,
        CancellationToken ct = default)
    {
        strategy = strategy.Trim().ToLowerInvariant();
        if (strategy is not ("signals" or "liquidity" or "liquidity_fresh" or "liquidity_v2" or "confluence" or "trade_score" or "breakout"))
            throw new ArgumentException(
                "Strategy must be 'signals', 'liquidity', 'liquidity_fresh', 'liquidity_v2', 'confluence', 'trade_score', or 'breakout'.");

        if (!_options.Enabled)
            throw new InvalidOperationException("Angel is disabled; cannot fetch historical candles.");

        var tokens = await _instruments.GetActiveTokensForUniversesAsync(ct);
        var token = tokens.FirstOrDefault(t => t.InstrumentId == instrumentId)
                    ?? throw new InvalidOperationException("No Angel token for this instrument. Run token sync first.");

        var toIst = DateTime.Now;
        var fromIst = toIst.Date.AddYears(-1).AddDays(-15); // warmup buffer
        var sectorBars = await LoadSectorDailyBarsAsync(token.InstrumentId, fromIst, toIst, ct);

        List<BacktestNoteRow> notes;
        if (strategy == "signals")
            notes = await ReplaySignalsAsync(userId, token, fromIst, toIst, sectorBars, ct);
        else if (strategy == "confluence")
            notes = await ReplayConfluenceAsync(userId, token, fromIst, toIst, sectorBars, ct);
        else if (strategy == "trade_score")
            notes = await ReplayTradeScoreAsync(userId, token, fromIst, toIst, sectorBars, ct);
        else if (strategy == "breakout")
            notes = await ReplayBreakoutAsync(userId, token, fromIst, toIst, sectorBars, ct);
        else
            notes = await ReplayLiquidityAsync(userId, token, fromIst, toIst, sectorBars, ct, strategy);

        await _backtest.DeleteAutoNotesAsync(userId, instrumentId, strategy, ct);
        await _backtest.InsertAutoNotesAsync(notes, ct);

        _logger.LogInformation(
            "Historical backtest {Strategy} {Symbol}: {Count} setups over 1Y (sectorBars={SectorBars})",
            strategy, token.AppSymbol, notes.Count, sectorBars.Count);

        return await _backtest.GetSymbolSummaryAsync(userId, instrumentId, strategy, ct: ct);
    }

    private async Task<List<MarketBarRow>> LoadSectorDailyBarsAsync(
        Guid equityInstrumentId, DateTime fromIst, DateTime toIst, CancellationToken ct)
    {
        var sectorId = await _instruments.GetSectorIdForInstrumentAsync(equityInstrumentId, ct);
        if (sectorId is null)
            return [];

        var sectorTokens = await _instruments.GetActiveTokensForSectorsAsync(ct);
        var sectorToken = sectorTokens.FirstOrDefault(t => t.InstrumentId == sectorId.Value);
        if (sectorToken is null)
            return [];

        try
        {
            var candles = await FetchDailyChunkedAsync(sectorToken, fromIst, toIst, ct);
            return candles
                .GroupBy(c => c.TradeDate)
                .Select(g => g.OrderByDescending(c => c.BarTime ?? DateTimeOffset.MinValue).First())
                .OrderBy(c => c.TradeDate)
                .Select(c => new MarketBarRow
                {
                    InstrumentId = sectorToken.InstrumentId,
                    AppSymbol = sectorToken.AppSymbol,
                    TradeDate = c.TradeDate,
                    Open = c.Open,
                    High = c.High,
                    Low = c.Low,
                    Close = c.Close,
                    Volume = c.Volume,
                })
                .ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Sector daily history unavailable for {Symbol}; sector_confirmed will be false.",
                sectorToken.AppSymbol);
            return [];
        }
    }

    private static bool EvalSectorConfirmed(
        IReadOnlyList<MarketBarRow> sectorBars, string side, DateOnly asOf) =>
        SectorConfirmation.IsConfirmed(side, SectorConfirmation.AsOf(sectorBars, asOf));

    /// <summary>Planned R:R using T1 vs stop. Null when not computable.</summary>
    private static decimal? PlannedRiskReward(decimal entry, decimal sl, decimal? t1)
    {
        if (t1 is null) return null;
        var risk = Math.Abs(entry - sl);
        if (risk <= 0) return null;
        return Math.Abs(t1.Value - entry) / risk;
    }

    private static bool MeetsMinRiskReward(decimal entry, decimal sl, decimal? t1, decimal min = 1m)
    {
        var rr = PlannedRiskReward(entry, sl, t1);
        return rr is decimal v && v >= min;
    }

    /// <summary>Skip opposite-side setup while a prior note is still open within 2 calendar days.</summary>
    private static bool IsFlipBlocked(List<BacktestNoteRow> notes, string side, DateOnly asOf) =>
        OppositeSignalFlipGuard.IsFlipAgainstOpenNotes(side, asOf, notes, out _);

    private async Task<List<BacktestNoteRow>> ReplaySignalsAsync(
        Guid userId, AngelTokenRow token, DateTime fromIst, DateTime toIst,
        IReadOnlyList<MarketBarRow> sectorBars, CancellationToken ct)
    {
        var candles = await FetchDailyChunkedAsync(token, fromIst, toIst, ct);
        var chron = candles
            .GroupBy(c => c.TradeDate)
            .Select(g => g.OrderByDescending(c => c.BarTime ?? DateTimeOffset.MinValue).First())
            .OrderBy(c => c.TradeDate)
            .ToList();

        if (chron.Count < 10)
            throw new InvalidOperationException(
                $"Not enough daily history ({chron.Count} bars). Angel may be rate-limiting — wait 1–2 minutes and retry with strategy=Signals.");

        var bars = chron.Select(c => new MarketBarRow
        {
            InstrumentId = token.InstrumentId,
            AppSymbol = token.AppSymbol,
            TradeDate = c.TradeDate,
            Open = c.Open,
            High = c.High,
            Low = c.Low,
            Close = c.Close,
            Volume = c.Volume
        }).ToList();

        var notes = new List<BacktestNoteRow>();
        var runId = Guid.Empty;

        // Start after warmup so Evaluate has ≥5 prior bars
        for (var i = 8; i < bars.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var window = bars.Take(i + 1).Reverse().ToList(); // newest first
            var asOf = bars[i].TradeDate;
            var signal = BreakoutSignalEvaluator.Evaluate(userId, runId, asOf, window, livePrice: null);
            if (signal is null)
                continue;

            if (!MeetsMinRiskReward(signal.EntryPrice, signal.InitialStopLoss, signal.TargetT1))
                continue;

            if (IsFlipBlocked(notes, signal.Side, asOf))
                continue;

            var forward = bars.Skip(i + 1).Take(DailyTimeStopBars).ToList();
            var outcome = SimulateOutcome(
                signal.Side, signal.EntryPrice, signal.InitialStopLoss,
                signal.TargetT1, signal.TargetT2, signal.TargetT3,
                forward.Select(b => (b.High, b.Low, b.Close, (DateOnly?)b.TradeDate, (DateTimeOffset?)null)).ToList());

            notes.Add(ToNote(userId, token, "signals", signal.Side, asOf,
                signal.EntryPrice, signal.InitialStopLoss,
                signal.TargetT1, signal.TargetT2, signal.TargetT3, outcome,
                EvalSectorConfirmed(sectorBars, signal.Side, asOf)));
        }

        return notes;
    }

    private async Task<List<BacktestNoteRow>> ReplayLiquidityAsync(
        Guid userId, AngelTokenRow token, DateTime fromIst, DateTime toIst,
        IReadOnlyList<MarketBarRow> sectorBars, CancellationToken ct,
        string strategy = "liquidity")
    {
        var ruleset = strategy switch
        {
            "liquidity_fresh" => "fresh",
            "liquidity_v2" => "v2",
            _ => "classic"
        };
        var hourly = await FetchHourlyChunkedAsync(token, fromIst, toIst, ct);
        var dailyCandles = await FetchDailyChunkedAsync(token, fromIst, toIst, ct);

        var bars1hChron = hourly
            .Where(c => c.BarTime is not null)
            .OrderBy(c => c.BarTime)
            .Select(c => new MarketIntradayBarRow
            {
                InstrumentId = token.InstrumentId,
                AppSymbol = token.AppSymbol,
                Interval = IntradayBarsSyncService.Interval1h,
                BarTime = c.BarTime!.Value,
                Open = c.Open,
                High = c.High,
                Low = c.Low,
                Close = c.Close,
                Volume = c.Volume
            })
            .ToList();

        var dailyChron = dailyCandles
            .GroupBy(c => c.TradeDate)
            .Select(g => g.First())
            .OrderBy(c => c.TradeDate)
            .Select(c => new MarketBarRow
            {
                InstrumentId = token.InstrumentId,
                AppSymbol = token.AppSymbol,
                TradeDate = c.TradeDate,
                Open = c.Open,
                High = c.High,
                Low = c.Low,
                Close = c.Close,
                Volume = c.Volume
            })
            .ToList();

        if (bars1hChron.Count < 60)
            throw new InvalidOperationException(
                $"Not enough 1H history ({bars1hChron.Count} bars). Angel may be rate-limiting — wait 2 minutes and retry with strategy=Signals.");

        var notes = new List<BacktestNoteRow>();
        var runId = Guid.Empty;
        var step = 2; // evaluate every 2 hours to keep runtime reasonable
        var minIdx = 50;
        var dailyTake = ruleset == "v2" ? 80 : 15;

        for (var i = minIdx; i < bars1hChron.Count; i += step)
        {
            ct.ThrowIfCancellationRequested();
            var asOfBar = bars1hChron[i];
            var bars1hNewest = bars1hChron.Take(i + 1).Reverse().ToList();
            var bars4h = LiquidityAnalysisService.Aggregate4h(bars1hNewest);
            if (bars4h.Count < 8)
                continue;

            var asOfDate = DateOnly.FromDateTime(asOfBar.BarTime.ToOffset(TimeSpan.FromHours(5.5)).DateTime);
            var dailyNewest = dailyChron
                .Where(d => d.TradeDate <= asOfDate)
                .OrderByDescending(d => d.TradeDate)
                .Take(dailyTake)
                .ToList();

            LiquiditySignalRow? signal;
            if (ruleset == "v2")
            {
                signal = LiquidityV2Evaluator.TryEvaluate(
                    userId, runId, asOfDate, token, bars1hNewest, bars4h, dailyNewest,
                    livePrice: null,
                    sectorConfirmed: false,
                    niftyDailyNewestFirst: null,
                    options: new LiquidityV2Evaluator.Options());
                if (signal is not null)
                    signal.SectorConfirmed = EvalSectorConfirmed(sectorBars, signal.Side, asOfDate);
            }
            else
            {
                signal = LiquidityAnalysisService.TryEvaluate(
                    userId, runId, asOfDate, token, bars1hNewest, bars4h, dailyNewest, livePrice: null, ruleset);
            }

            if (signal is null)
                continue;

            if (!MeetsMinRiskReward(signal.EntryPrice, signal.InitialStopLoss, signal.TargetT1))
                continue;

            // Fresh ruleset only: drop if as-of bar already tagged T1.
            if (ruleset == "fresh" && SignalBarAlreadyHitTarget(signal.Side, signal.TargetT1, asOfBar))
                continue;

            // De-dupe: skip if same side within 6 hours of last note
            if (notes.Count > 0)
            {
                var last = notes[^1];
                var lastTime = last.SignalDate.ToDateTime(TimeOnly.MinValue);
                var curTime = asOfBar.BarTime.DateTime;
                if (last.Side == signal.Side && (curTime - lastTime).TotalHours < 6)
                    continue;
            }

            if (IsFlipBlocked(notes, signal.Side, asOfDate))
                continue;

            var forward = bars1hChron.Skip(i + 1).Take(HourlyTimeStopBars)
                .Select(b => (b.High, b.Low, b.Close,
                    (DateOnly?)DateOnly.FromDateTime(b.BarTime.ToOffset(TimeSpan.FromHours(5.5)).DateTime),
                    (DateTimeOffset?)b.BarTime))
                .ToList();

            var outcome = SimulateOutcome(
                signal.Side, signal.EntryPrice, signal.InitialStopLoss,
                signal.TargetT1, signal.TargetT2, signal.TargetT3, forward);

            notes.Add(ToNote(userId, token, strategy, signal.Side, asOfDate,
                signal.EntryPrice, signal.InitialStopLoss,
                signal.TargetT1, signal.TargetT2, signal.TargetT3, outcome,
                EvalSectorConfirmed(sectorBars, signal.Side, asOfDate)));
        }

        return notes;
    }

    private async Task<List<BacktestNoteRow>> ReplayConfluenceAsync(
        Guid userId, AngelTokenRow token, DateTime fromIst, DateTime toIst,
        IReadOnlyList<MarketBarRow> sectorBars, CancellationToken ct)
    {
        var hourly = await FetchHourlyChunkedAsync(token, fromIst, toIst, ct);
        var dailyCandles = await FetchDailyChunkedAsync(token, fromIst, toIst, ct);

        var bars1hChron = hourly
            .Where(c => c.BarTime is not null)
            .OrderBy(c => c.BarTime)
            .Select(c => new MarketIntradayBarRow
            {
                InstrumentId = token.InstrumentId,
                AppSymbol = token.AppSymbol,
                Interval = IntradayBarsSyncService.Interval1h,
                BarTime = c.BarTime!.Value,
                Open = c.Open, High = c.High, Low = c.Low, Close = c.Close, Volume = c.Volume
            }).ToList();

        var dailyChron = dailyCandles
            .GroupBy(c => c.TradeDate).Select(g => g.First()).OrderBy(c => c.TradeDate)
            .Select(c => new MarketBarRow
            {
                InstrumentId = token.InstrumentId, AppSymbol = token.AppSymbol,
                TradeDate = c.TradeDate, Open = c.Open, High = c.High, Low = c.Low,
                Close = c.Close, Volume = c.Volume
            }).ToList();

        if (bars1hChron.Count < 60 || dailyChron.Count < 10)
            throw new InvalidOperationException(
                $"Not enough history for confluence (1H={bars1hChron.Count}, daily={dailyChron.Count}).");

        var dailySignals = new List<(DateOnly Date, AnalysisSignalRow Signal)>();
        var runId = Guid.Empty;
        for (var i = 8; i < dailyChron.Count; i++)
        {
            var window = dailyChron.Take(i + 1).Reverse().ToList();
            var sig = BreakoutSignalEvaluator.Evaluate(userId, runId, dailyChron[i].TradeDate, window, null);
            if (sig is not null) dailySignals.Add((dailyChron[i].TradeDate, sig));
        }

        var notes = new List<BacktestNoteRow>();
        for (var i = 50; i < bars1hChron.Count; i += 2)
        {
            ct.ThrowIfCancellationRequested();
            var asOfBar = bars1hChron[i];
            var bars1hNewest = bars1hChron.Take(i + 1).Reverse().ToList();
            var bars4h = LiquidityAnalysisService.Aggregate4h(bars1hNewest);
            if (bars4h.Count < 8) continue;

            var asOfDate = DateOnly.FromDateTime(asOfBar.BarTime.ToOffset(TimeSpan.FromHours(5.5)).DateTime);
            var dailyNewest = dailyChron.Where(d => d.TradeDate <= asOfDate)
                .OrderByDescending(d => d.TradeDate).Take(15).ToList();

            var liq = LiquidityAnalysisService.TryEvaluate(
                userId, runId, asOfDate, token, bars1hNewest, bars4h, dailyNewest, null, "fresh");
            if (liq is null) continue;

            var sigMatch = dailySignals
                .Where(ds => string.Equals(ds.Signal.Side, liq.Side, StringComparison.OrdinalIgnoreCase)
                    && ConfluenceLevelComposer.DatesAlign(asOfDate, ds.Date)
                    && ConfluenceLevelComposer.PricesAlign(liq.EntryPrice, ds.Signal.EntryPrice, liq.EntryPrice))
                .Select(ds => ds.Signal).FirstOrDefault();
            if (sigMatch is null) continue;

            if (!ConfluenceLevelComposer.TryCompose(
                liq.Side, sigMatch.EntryPrice, sigMatch.InitialStopLoss,
                liq.EntryPrice, liq.InitialStopLoss, out var entry, out var sl))
                continue;

            if (!MeetsMinRiskReward(entry, sl, liq.TargetT1)) continue;
            if (rulesetFreshSkip(liq, asOfBar)) continue;
            if (dedupeRecent(notes, liq.Side, asOfBar)) continue;
            if (IsFlipBlocked(notes, liq.Side, asOfDate)) continue;

            var forward = forwardHourly(bars1hChron, i);
            var outcome = SimulateOutcome(liq.Side, entry, sl, liq.TargetT1, liq.TargetT2, liq.TargetT3, forward);
            notes.Add(ToNote(userId, token, "confluence", liq.Side, asOfDate, entry, sl,
                liq.TargetT1, liq.TargetT2, liq.TargetT3, outcome,
                EvalSectorConfirmed(sectorBars, liq.Side, asOfDate)));
        }
        return notes;
    }

    private async Task<List<BacktestNoteRow>> ReplayBreakoutAsync(
        Guid userId, AngelTokenRow token, DateTime fromIst, DateTime toIst,
        IReadOnlyList<MarketBarRow> sectorBars, CancellationToken ct)
    {
        var dailyCandles = await FetchDailyChunkedAsync(token, fromIst, toIst, ct);
        var dailyChron = dailyCandles
            .GroupBy(c => c.TradeDate).Select(g => g.First()).OrderBy(c => c.TradeDate)
            .Select(c => new MarketBarRow
            {
                InstrumentId = token.InstrumentId, AppSymbol = token.AppSymbol,
                TradeDate = c.TradeDate, Open = c.Open, High = c.High, Low = c.Low,
                Close = c.Close, Volume = c.Volume
            }).ToList();

        if (dailyChron.Count < 30)
            throw new InvalidOperationException($"Not enough daily history ({dailyChron.Count} bars).");

        var notes = new List<BacktestNoteRow>();
        for (var i = 25; i < dailyChron.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var window = dailyChron.Take(i + 1).Reverse().ToList();
            var result = BreakoutConfirmationEvaluator.Evaluate(window);
            if (result is not { Confirmed: true }) continue;

            var asOf = dailyChron[i].TradeDate;
            var entry = result.Close;
            var level = result.BreakoutLevel;
            var sl = result.Side == SignalSides.Buy
                ? (level < entry ? level : entry * 0.98m)
                : (level > entry ? level : entry * 1.02m);

            var risk = Math.Abs(entry - sl);
            var t1 = result.Side == SignalSides.Buy ? entry + risk * 2m : entry - risk * 2m;
            var t2 = result.Side == SignalSides.Buy ? entry + risk * 3m : entry - risk * 3m;
            var t3 = result.Side == SignalSides.Buy ? entry + risk * 4m : entry - risk * 4m;

            if (!MeetsMinRiskReward(entry, sl, t1)) continue;

            if (IsFlipBlocked(notes, result.Side, asOf))
                continue;

            var forward = dailyChron.Skip(i + 1).Take(DailyTimeStopBars)
                .Select(b => (b.High, b.Low, b.Close, (DateOnly?)b.TradeDate, (DateTimeOffset?)null)).ToList();
            var outcome = SimulateOutcome(result.Side, entry, sl, t1, t2, t3, forward);
            notes.Add(ToNote(userId, token, "breakout", result.Side, asOf, entry, sl, t1, t2, t3, outcome,
                EvalSectorConfirmed(sectorBars, result.Side, asOf)));
        }
        return notes;
    }

    private static bool rulesetFreshSkip(LiquiditySignalRow liq, MarketIntradayBarRow asOfBar) =>
        SignalBarAlreadyHitTarget(liq.Side, liq.TargetT1, asOfBar);

    private static bool dedupeRecent(List<BacktestNoteRow> notes, string side, MarketIntradayBarRow asOfBar)
    {
        if (notes.Count == 0) return false;
        var last = notes[^1];
        var curTime = asOfBar.BarTime.DateTime;
        var lastTime = last.SignalDate.ToDateTime(TimeOnly.MinValue);
        return last.Side == side && (curTime - lastTime).TotalHours < 6;
    }

    private static List<(decimal High, decimal Low, decimal Close, DateOnly? Date, DateTimeOffset? Time)> forwardHourly(
        List<MarketIntradayBarRow> bars, int i) =>
        bars.Skip(i + 1).Take(HourlyTimeStopBars)
            .Select(b => (b.High, b.Low, b.Close,
                (DateOnly?)DateOnly.FromDateTime(b.BarTime.ToOffset(TimeSpan.FromHours(5.5)).DateTime),
                (DateTimeOffset?)b.BarTime)).ToList();

    private async Task<List<BacktestNoteRow>> ReplayTradeScoreAsync(
        Guid userId, AngelTokenRow token, DateTime fromIst, DateTime toIst,
        IReadOnlyList<MarketBarRow> sectorBars, CancellationToken ct)
    {
        var hourly = await FetchHourlyChunkedAsync(token, fromIst, toIst, ct);
        var dailyCandles = await FetchDailyChunkedAsync(token, fromIst, toIst, ct);

        var bars1hChron = hourly
            .Where(c => c.BarTime is not null)
            .OrderBy(c => c.BarTime)
            .Select(c => new MarketIntradayBarRow
            {
                InstrumentId = token.InstrumentId,
                AppSymbol = token.AppSymbol,
                Interval = IntradayBarsSyncService.Interval1h,
                BarTime = c.BarTime!.Value,
                Open = c.Open,
                High = c.High,
                Low = c.Low,
                Close = c.Close,
                Volume = c.Volume
            })
            .ToList();

        var dailyChron = dailyCandles
            .GroupBy(c => c.TradeDate)
            .Select(g => g.First())
            .OrderBy(c => c.TradeDate)
            .Select(c => new MarketBarRow
            {
                InstrumentId = token.InstrumentId,
                AppSymbol = token.AppSymbol,
                TradeDate = c.TradeDate,
                Open = c.Open,
                High = c.High,
                Low = c.Low,
                Close = c.Close,
                Volume = c.Volume
            })
            .ToList();

        if (bars1hChron.Count < 60 || dailyChron.Count < 10)
            throw new InvalidOperationException(
                $"Not enough history for trade score (1H={bars1hChron.Count}, daily={dailyChron.Count}). Retry after rate limit clears.");

        var dailySignals = new List<(DateOnly Date, AnalysisSignalRow Signal)>();
        var runId = Guid.Empty;
        for (var i = 8; i < dailyChron.Count; i++)
        {
            var window = dailyChron.Take(i + 1).Reverse().ToList();
            var asOf = dailyChron[i].TradeDate;
            var sig = BreakoutSignalEvaluator.Evaluate(userId, runId, asOf, window, livePrice: null);
            if (sig is not null)
                dailySignals.Add((asOf, sig));
        }

        var notes = new List<BacktestNoteRow>();
        const int step = 2;
        const int minIdx = 50;

        for (var i = minIdx; i < bars1hChron.Count; i += step)
        {
            ct.ThrowIfCancellationRequested();
            var asOfBar = bars1hChron[i];
            var bars1hNewest = bars1hChron.Take(i + 1).Reverse().ToList();
            var bars4h = LiquidityAnalysisService.Aggregate4h(bars1hNewest);
            if (bars4h.Count < 8)
                continue;

            var asOfDate = DateOnly.FromDateTime(asOfBar.BarTime.ToOffset(TimeSpan.FromHours(5.5)).DateTime);
            var dailyNewest = dailyChron
                .Where(d => d.TradeDate <= asOfDate)
                .OrderByDescending(d => d.TradeDate)
                .Take(15)
                .ToList();

            var liq = LiquidityAnalysisService.TryEvaluate(
                userId, runId, asOfDate, token, bars1hNewest, bars4h, dailyNewest, livePrice: null, "fresh");
            if (liq is null)
                continue;

            var sigMatch = dailySignals
                .Where(ds =>
                    string.Equals(ds.Signal.Side, liq.Side, StringComparison.OrdinalIgnoreCase)
                    && TradeScoreLevelComposer.DatesAlign(asOfDate, ds.Date)
                    && TradeScoreLevelComposer.PricesAlign(liq.EntryPrice, ds.Signal.EntryPrice, liq.EntryPrice))
                .Select(ds => ds.Signal)
                .FirstOrDefault();

            if (sigMatch is null)
                continue;

            var dailyWindow = dailyChron.Where(d => d.TradeDate <= asOfDate).OrderByDescending(d => d.TradeDate).ToList();
            var breakoutConfirm = dailyWindow.Count >= 21
                ? BreakoutConfirmationEvaluator.Evaluate(dailyWindow)
                : null;
            if (breakoutConfirm is not { Confirmed: true }
                || !string.Equals(breakoutConfirm.Side, liq.Side, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!TradeScoreLevelComposer.TryCompose(
                liq.Side, sigMatch.EntryPrice, sigMatch.InitialStopLoss,
                liq.EntryPrice, liq.InitialStopLoss,
                out var entry, out var sl))
                continue;

            if (!MeetsMinRiskReward(entry, sl, liq.TargetT1))
                continue;

            if (SignalBarAlreadyHitTarget(liq.Side, liq.TargetT1, asOfBar))
                continue;

            if (notes.Count > 0)
            {
                var last = notes[^1];
                var lastTime = last.SignalDate.ToDateTime(TimeOnly.MinValue);
                var curTime = asOfBar.BarTime.DateTime;
                if (last.Side == liq.Side && (curTime - lastTime).TotalHours < 6)
                    continue;
            }

            if (IsFlipBlocked(notes, liq.Side, asOfDate))
                continue;

            var forward = bars1hChron.Skip(i + 1).Take(HourlyTimeStopBars)
                .Select(b => (b.High, b.Low, b.Close,
                    (DateOnly?)DateOnly.FromDateTime(b.BarTime.ToOffset(TimeSpan.FromHours(5.5)).DateTime),
                    (DateTimeOffset?)b.BarTime))
                .ToList();

            var outcome = SimulateOutcome(
                liq.Side, entry, sl,
                liq.TargetT1, liq.TargetT2, liq.TargetT3, forward);

            notes.Add(ToNote(userId, token, "trade_score", liq.Side, asOfDate,
                entry, sl, liq.TargetT1, liq.TargetT2, liq.TargetT3, outcome,
                EvalSectorConfirmed(sectorBars, liq.Side, asOfDate)));
        }

        return notes;
    }

    private static BacktestNoteRow ToNote(
        Guid userId, AngelTokenRow token, string strategy, string side, DateOnly signalDate,
        decimal entry, decimal sl, decimal? t1, decimal? t2, decimal? t3,
        OutcomeSimulator.SimulatedOutcome outcome,
        bool sectorConfirmed = false)
    {
        return new BacktestNoteRow
        {
            UserId = userId,
            InstrumentId = token.InstrumentId,
            AppSymbol = token.AppSymbol,
            Strategy = strategy,
            Side = side,
            SignalDate = signalDate,
            EntryPrice = entry,
            InitialStopLoss = sl,
            TargetT1 = t1,
            TargetT2 = t2,
            TargetT3 = t3,
            Result = outcome.Result,
            TargetLevel = outcome.TargetLevel,
            TargetHitPct = outcome.TargetHitPct,
            ExitPrice = outcome.ExitPrice,
            ExitDate = outcome.ExitDate,
            PnlPct = outcome.PnlPct,
            RMultiple = outcome.RMultiple,
            Notes = "auto:1y",
            Source = "auto",
            SectorConfirmed = sectorConfirmed,
        };
    }

    /// <summary>True when planned T1 was already traded on the signal bar (not actionable).</summary>
    private static bool SignalBarAlreadyHitTarget(
        string side, decimal? t1, MarketIntradayBarRow bar)
    {
        if (t1 is null) return false;
        return side == SignalSides.Buy
            ? bar.High >= t1 || bar.Close >= t1
            : bar.Low <= t1 || bar.Close <= t1;
    }

    private static OutcomeSimulator.SimulatedOutcome SimulateOutcome(
        string side,
        decimal entry,
        decimal sl,
        decimal? t1,
        decimal? t2,
        decimal? t3,
        List<(decimal High, decimal Low, decimal Close, DateOnly? Date, DateTimeOffset? Time)> forward)
        => OutcomeSimulator.Simulate(side, entry, sl, t1, t2, t3, forward);

    private async Task<List<AngelCandle>> FetchDailyChunkedAsync(
        AngelTokenRow token, DateTime fromIst, DateTime toIst, CancellationToken ct)
        => await FetchCandleRangeAsync(
            token,
            fromIst,
            toIst,
            AngelHistoricalLimits.OneDay,
            _angel.GetDailyCandlesAsync,
            groupByTradeDate: true,
            ct);

    private async Task<List<AngelCandle>> FetchHourlyChunkedAsync(
        AngelTokenRow token, DateTime fromIst, DateTime toIst, CancellationToken ct)
    {
        var candles = await FetchCandleRangeAsync(
            token,
            fromIst,
            toIst,
            AngelHistoricalLimits.OneHour,
            _angel.GetHourlyCandlesAsync,
            groupByTradeDate: false,
            ct);

        if (candles.Count < 60)
        {
            throw new InvalidOperationException(
                $"Not enough 1H history ({candles.Count} bars). Angel may be rate-limiting — wait 2 minutes and retry, or use Signals strategy.");
        }

        return candles;
    }

    /// <summary>
    /// Fetches history in as few Angel requests as allowed (e.g. 1Y daily = 1 call, 1Y hourly = 1 call).
    /// </summary>
    private async Task<List<AngelCandle>> FetchCandleRangeAsync(
        AngelTokenRow token,
        DateTime fromIst,
        DateTime toIst,
        int maxDaysPerRequest,
        Func<string, string, DateTime, DateTime, CancellationToken, Task<IReadOnlyList<AngelCandle>>> fetch,
        bool groupByTradeDate,
        CancellationToken ct)
    {
        await _angel.EnsureSessionAsync(ct);
        var all = new List<AngelCandle>();
        var cursor = fromIst;

        while (cursor < toIst)
        {
            ct.ThrowIfCancellationRequested();
            var chunkEnd = cursor.AddDays(maxDaysPerRequest);
            if (chunkEnd > toIst)
                chunkEnd = toIst;

            var chunk = await FetchCandlesWithRetryAsync(
                () => fetch(token.Exchange, token.SymbolToken, cursor, chunkEnd, ct),
                ct);

            if (chunk.Count == 0 && all.Count == 0 && cursor == fromIst)
            {
                throw new InvalidOperationException(
                    "Angel returned no candles (rate limit or bad token). Wait 1–2 minutes and retry.");
            }

            all.AddRange(chunk);

            if (chunkEnd >= toIst)
                break;

            // Next window starts 1 minute after previous todate (Angel format is inclusive range).
            cursor = chunkEnd.AddMinutes(1);
            await Task.Delay(1200, ct);
        }

        if (groupByTradeDate)
        {
            return all
                .GroupBy(c => c.TradeDate)
                .Select(g => g.First())
                .OrderBy(c => c.TradeDate)
                .ToList();
        }

        return all
            .Where(c => c.BarTime is not null)
            .GroupBy(c => c.BarTime!.Value)
            .Select(g => g.First())
            .OrderBy(c => c.BarTime)
            .ToList();
    }

    private async Task<IReadOnlyList<AngelCandle>> FetchCandlesWithRetryAsync(
        Func<Task<IReadOnlyList<AngelCandle>>> fetch,
        CancellationToken ct)
    {
        const int maxAttempts = 4;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var candles = await fetch();
                if (candles.Count > 0 || attempt == maxAttempts)
                    return candles;
            }
            catch (InvalidOperationException ex) when (
                attempt < maxAttempts &&
                (ex.Message.Contains("403", StringComparison.OrdinalIgnoreCase)
                 || ex.Message.Contains("rate", StringComparison.OrdinalIgnoreCase)
                 || ex.Message.Contains("Too many", StringComparison.OrdinalIgnoreCase)))
            {
                _logger.LogWarning(ex, "Angel rate limit on candle fetch (attempt {Attempt})", attempt);
            }

            await Task.Delay(3000 * attempt, ct);
        }

        return Array.Empty<AngelCandle>();
    }
}
