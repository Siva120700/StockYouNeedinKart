using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StockYouNeed.Application.Abstractions;
using StockYouNeed.Application.Options;
using StockYouNeed.Domain;

namespace StockYouNeed.Application.Services;

/// <summary>
/// One-symbol historical replay (1 year). Fetches Angel candles in-memory;
/// does not call live AnalysisRunService / LiquidityAnalysisService RunAsync.
/// </summary>
public sealed class BacktestService
{
    private const int DailyTimeStopBars = 20;
    private const int HourlyTimeStopBars = 40;

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
        if (strategy is not ("signals" or "liquidity" or "liquidity_fresh"))
            throw new ArgumentException("Strategy must be 'signals', 'liquidity', or 'liquidity_fresh'.");

        if (!_options.Enabled)
            throw new InvalidOperationException("Angel is disabled; cannot fetch historical candles.");

        var tokens = await _instruments.GetActiveTokensForUniversesAsync(ct);
        var token = tokens.FirstOrDefault(t => t.InstrumentId == instrumentId)
                    ?? throw new InvalidOperationException("No Angel token for this instrument. Run token sync first.");

        var toIst = DateTime.Now;
        var fromIst = toIst.Date.AddYears(-1).AddDays(-15); // warmup buffer

        List<BacktestNoteRow> notes;
        if (strategy == "signals")
            notes = await ReplaySignalsAsync(userId, token, fromIst, toIst, ct);
        else
            notes = await ReplayLiquidityAsync(userId, token, fromIst, toIst, ct, strategy);

        await _backtest.DeleteAutoNotesAsync(userId, instrumentId, strategy, ct);
        await _backtest.InsertAutoNotesAsync(notes, ct);

        _logger.LogInformation(
            "Historical backtest {Strategy} {Symbol}: {Count} setups over 1Y",
            strategy, token.AppSymbol, notes.Count);

        return await _backtest.GetSymbolSummaryAsync(userId, instrumentId, strategy, ct: ct);
    }

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

    private async Task<List<BacktestNoteRow>> ReplaySignalsAsync(
        Guid userId, AngelTokenRow token, DateTime fromIst, DateTime toIst, CancellationToken ct)
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

            var forward = bars.Skip(i + 1).Take(DailyTimeStopBars).ToList();
            var outcome = SimulateOutcome(
                signal.Side, signal.EntryPrice, signal.InitialStopLoss,
                signal.TargetT1, signal.TargetT2, signal.TargetT3,
                forward.Select(b => (b.High, b.Low, b.Close, (DateOnly?)b.TradeDate, (DateTimeOffset?)null)).ToList());

            notes.Add(ToNote(userId, token, "signals", signal.Side, asOf,
                signal.EntryPrice, signal.InitialStopLoss,
                signal.TargetT1, signal.TargetT2, signal.TargetT3, outcome));
        }

        return notes;
    }

    private async Task<List<BacktestNoteRow>> ReplayLiquidityAsync(
        Guid userId, AngelTokenRow token, DateTime fromIst, DateTime toIst, CancellationToken ct,
        string strategy = "liquidity")
    {
        var ruleset = strategy == "liquidity_fresh" ? "fresh" : "classic";
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

            var signal = LiquidityAnalysisService.TryEvaluate(
                userId, runId, asOfDate, token, bars1hNewest, bars4h, dailyNewest, livePrice: null, ruleset);
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
                signal.TargetT1, signal.TargetT2, signal.TargetT3, outcome));
        }

        return notes;
    }

    private static BacktestNoteRow ToNote(
        Guid userId, AngelTokenRow token, string strategy, string side, DateOnly signalDate,
        decimal entry, decimal sl, decimal? t1, decimal? t2, decimal? t3, Outcome outcome)
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
            Source = "auto"
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

    private sealed record Outcome(
        string Result, string? TargetLevel, decimal? TargetHitPct,
        decimal? ExitPrice, DateOnly? ExitDate, decimal? PnlPct, decimal? RMultiple);

    /// <summary>Walk forward bars; if SL and target hit same bar, count SL (conservative).</summary>
    private static Outcome SimulateOutcome(
        string side,
        decimal entry,
        decimal sl,
        decimal? t1,
        decimal? t2,
        decimal? t3,
        List<(decimal High, decimal Low, decimal Close, DateOnly? Date, DateTimeOffset? Time)> forward)
    {
        var risk = Math.Abs(entry - sl);
        if (risk <= 0)
            risk = entry * 0.01m;

        decimal FavorPct(decimal price) =>
            side == SignalSides.Buy
                ? (price - entry) / entry * 100m
                : (entry - price) / entry * 100m;

        decimal RMult(decimal price) =>
            side == SignalSides.Buy
                ? (price - entry) / risk
                : (entry - price) / risk;

        decimal TargetPctOf(decimal target, decimal mfePrice)
        {
            var goal = Math.Abs(target - entry);
            if (goal <= 0) return 0;
            var move = side == SignalSides.Buy
                ? Math.Max(0, mfePrice - entry)
                : Math.Max(0, entry - mfePrice);
            return Math.Round(Math.Min(100m, move / goal * 100m), 2);
        }

        decimal mfe = entry;
        decimal mae = entry;

        for (var i = 0; i < forward.Count; i++)
        {
            var (high, low, close, date, _) = forward[i];
            if (side == SignalSides.Buy)
            {
                if (high > mfe) mfe = high;
                if (low < mae) mae = low;
            }
            else
            {
                if (low < mfe) mfe = low;
                if (high > mae) mae = high;
            }

            var hitSl = side == SignalSides.Buy ? low <= sl : high >= sl;
            string? hitLevel = null;
            decimal? hitPrice = null;
            if (t3 is decimal v3 && (side == SignalSides.Buy ? high >= v3 : low <= v3))
            {
                hitLevel = "t3";
                hitPrice = v3;
            }
            else if (t2 is decimal v2 && (side == SignalSides.Buy ? high >= v2 : low <= v2))
            {
                hitLevel = "t2";
                hitPrice = v2;
            }
            else if (t1 is decimal v1 && (side == SignalSides.Buy ? high >= v1 : low <= v1))
            {
                hitLevel = "t1";
                hitPrice = v1;
            }

            if (hitSl && hitLevel is not null)
            {
                // Same bar: conservative SL
                return new Outcome("sl", null, TargetPctOf(t1 ?? entry, mfe), sl, date,
                    Math.Round(FavorPct(sl), 4), Math.Round(RMult(sl), 4));
            }

            if (hitSl)
            {
                return new Outcome("sl", null, 0m, sl, date,
                    Math.Round(FavorPct(sl), 4), Math.Round(RMult(sl), 4));
            }

            if (hitLevel is not null && hitPrice is decimal tp)
            {
                return new Outcome("target", hitLevel, 100m, tp, date,
                    Math.Round(FavorPct(tp), 4), Math.Round(RMult(tp), 4));
            }
        }

        // Time stop — use last close / MFE for target %
        if (forward.Count == 0)
        {
            return new Outcome("time_stop", null, 0m, entry, null, 0m, 0m);
        }

        var last = forward[^1];
        var exit = last.Close;
        var tHit = t1 is decimal tt ? TargetPctOf(tt, mfe) : 0m;
        return new Outcome("time_stop", null, tHit, exit, last.Date,
            Math.Round(FavorPct(exit), 4), Math.Round(RMult(exit), 4));
    }

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
