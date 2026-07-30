using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StockYouNeed.Application.Abstractions;
using StockYouNeed.Application.Options;
using StockYouNeed.Application.Outcomes;
using StockYouNeed.Domain;

namespace StockYouNeed.Application.Services;

/// <summary>
/// Separate liquidity engine: 4H sweep zones + 1H confirm (RVOL percentile, strong close, breakout).
/// Does not touch daily AnalysisRunService / analysis_signals.
/// </summary>
public sealed class LiquidityAnalysisService
{
    private const decimal EqualTolPct = 0.0015m;
    private const decimal ImminentMargin = 0.005m;
    private const decimal RvolFloor = 1.2m;
    private const double RvolPercentileGate = 0.75; // top 25%
    private const decimal StrongClosePct = 0.70m;
    private const decimal TargetMinDistancePct = 0.002m;
    private const int RvolLookback = 20;
    private const int RvolHistoryBars = 50;
    private const int Min1hBars = 45;
    private const int Min4hBars = 8;

    private readonly IAngelMarketDataClient _angel;
    private readonly IInstrumentRepository _instruments;
    private readonly IMarketDataRepository _market;
    private readonly IPortfolioRepository _portfolio;
    private readonly IntradayBarsSyncService _intradaySync;
    private readonly MarketBarsSyncService _barsSync;
    private readonly TokenSyncService _tokenSync;
    private readonly UniverseSeedService _universeSeed;
    private readonly SignalOutcomeService _outcomes;
    private readonly AngelOptions _options;
    private readonly ILogger<LiquidityAnalysisService> _logger;

    public LiquidityAnalysisService(
        IAngelMarketDataClient angel,
        IInstrumentRepository instruments,
        IMarketDataRepository market,
        IPortfolioRepository portfolio,
        IntradayBarsSyncService intradaySync,
        MarketBarsSyncService barsSync,
        TokenSyncService tokenSync,
        UniverseSeedService universeSeed,
        SignalOutcomeService outcomes,
        IOptions<AngelOptions> options,
        ILogger<LiquidityAnalysisService> logger)
    {
        _angel = angel;
        _instruments = instruments;
        _market = market;
        _portfolio = portfolio;
        _intradaySync = intradaySync;
        _barsSync = barsSync;
        _tokenSync = tokenSync;
        _universeSeed = universeSeed;
        _outcomes = outcomes;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AnalysisRunRow> RunAsync(
        Guid userId,
        bool includeNifty50,
        bool includeNifty100,
        bool includeWatchlist,
        string triggeredBy,
        CancellationToken ct = default,
        string ruleset = "classic")
    {
        ruleset = ruleset.Trim().ToLowerInvariant() == "fresh" ? "fresh" : "classic";
        var asOf = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(5.5)).DateTime);
        var runId = await _portfolio.CreateLiquidityAnalysisRunAsync(
            userId, triggeredBy, includeNifty50, includeNifty100, includeWatchlist, asOf, ruleset, ct);

        var stats = new Dictionary<string, object>
        {
            ["scanned"] = 0,
            ["signals"] = 0,
            ["sectorConfirmed"] = 0,
            ["fewIntradayBars"] = 0,
            ["noSetup"] = 0,
            ["intradayBarsUpserted"] = 0,
            ["ruleset"] = ruleset
        };

        try
        {
            var upserted = await _intradaySync.SyncUniverseHourlyAsync(ct);
            stats["intradayBarsUpserted"] = upserted;

            // Same sector direction check as AnalysisRunService / Signals page
            // (sector index breaks last 2 sessions high/low). Always computed; UI filters.
            var sectorBarsCache = new Dictionary<Guid, List<MarketBarRow>>();
            try
            {
                await _universeSeed.SeedAsync(ct);
                if (_options.Enabled)
                {
                    var sectorTokens = await _instruments.GetActiveTokensForSectorsAsync(ct);
                    if (sectorTokens.Count == 0)
                        await _tokenSync.SyncUniverseTokensAsync(ct);
                    await _barsSync.SyncMissingSectorBarsAsync(ct);
                    // Refresh today's sector OHLC so direction check matches live Signals runs.
                    await RefreshSectorDailyBarsFromQuotesAsync(asOf, ct);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Sector prep failed — sectorConfirmed may be false for many liquidity rows.");
            }

            var sectorIds = await _instruments.GetSectorInstrumentIdsAsync(ct);
            foreach (var sectorId in sectorIds)
            {
                var sBars = (await _market.GetBarsForInstrumentAsync(sectorId, 10, ct))
                    .OrderByDescending(b => b.TradeDate)
                    .ToList();
                if (sBars.Count >= 3)
                    sectorBarsCache[sectorId] = sBars;
            }

            _logger.LogInformation(
                "Liquidity sector evidence: {SectorIds} sectors, {WithBars} with bars.",
                sectorIds.Count, sectorBarsCache.Count);

            var tokens = await _instruments.GetActiveTokensForUniversesAsync(ct);
            var watchIds = includeWatchlist
                ? new HashSet<Guid>(await _portfolio.GetWatchlistInstrumentIdsAsync(userId, ct))
                : new HashSet<Guid>();

            var ltpRows = await _market.GetAllLtpAsync(ct);
            var ltpById = ltpRows.ToDictionary(x => x.InstrumentId, x => x.Ltp);

            var scanned = 0;
            var signals = 0;
            var fewBars = 0;
            var noSetup = 0;
            var sectorConfirmedCount = 0;

            foreach (var token in tokens)
            {
                ct.ThrowIfCancellationRequested();
                scanned++;

                var bars1h = (await _market.GetIntradayBarsForInstrumentAsync(
                    token.InstrumentId, IntradayBarsSyncService.Interval1h, 120, ct)).ToList();

                if (bars1h.Count < Min1hBars)
                {
                    fewBars++;
                    continue;
                }

                var bars4h = Aggregate4h(bars1h);
                if (bars4h.Count < Min4hBars)
                {
                    fewBars++;
                    continue;
                }

                var daily = await _market.GetBarsForInstrumentAsync(token.InstrumentId, 10, ct);
                ltpById.TryGetValue(token.InstrumentId, out var ltp);

                var signal = TryEvaluate(
                    userId, runId, asOf, token, bars1h, bars4h, daily.ToList(),
                    ltp > 0 ? ltp : null,
                    ruleset);

                if (signal is null)
                {
                    noSetup++;
                    continue;
                }

                var sectorId = await _instruments.GetSectorIdForInstrumentAsync(token.InstrumentId, ct);
                if (sectorId is not null && sectorBarsCache.TryGetValue(sectorId.Value, out var sectorBars))
                {
                    signal.SectorConfirmed = CheckSectorConfirmation(signal.Side, sectorBars);
                }
                else
                {
                    signal.SectorConfirmed = false;
                }

                if (signal.SectorConfirmed)
                    sectorConfirmedCount++;

                await _portfolio.InsertLiquiditySignalAsync(signal, ct);
                await _outcomes.OpenFromLiquidityAsync(signal, ruleset, ct);
                signals++;
            }

            stats["scanned"] = scanned;
            stats["signals"] = signals;
            stats["sectorConfirmed"] = sectorConfirmedCount;
            stats["fewIntradayBars"] = fewBars;
            stats["noSetup"] = noSetup;
            _ = watchIds; // reserved for future universe narrowing

            await _portfolio.CompleteLiquidityAnalysisRunAsync(runId, "succeeded", null, stats, ct);
            _logger.LogInformation(
                "Liquidity run {RunId} ({Ruleset}): scanned={Scanned}, signals={Signals}, sectorConfirmed={SectorConfirmed}, fewBars={Few}, noSetup={No}",
                runId, ruleset, scanned, signals, sectorConfirmedCount, fewBars, noSetup);

            return new AnalysisRunRow
            {
                Id = runId,
                UserId = userId,
                TriggeredBy = triggeredBy,
                IncludeNifty50 = includeNifty50,
                IncludeNifty100 = includeNifty100,
                IncludeWatchlist = includeWatchlist,
                AsOfDate = asOf,
                Status = "succeeded"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Liquidity analysis run {RunId} failed", runId);
            await _portfolio.CompleteLiquidityAnalysisRunAsync(runId, "failed", ex.Message, stats, ct);
            throw;
        }
    }

    /// <summary>Public for historical backtest replay — classic or fresh ruleset.</summary>
    public static LiquiditySignalRow? TryEvaluate(
        Guid userId,
        Guid runId,
        DateOnly asOf,
        AngelTokenRow token,
        List<MarketIntradayBarRow> bars1hNewestFirst,
        List<Ohlcv> bars4hNewestFirst,
        List<MarketBarRow> dailyNewestFirst,
        decimal? livePrice,
        string ruleset = "classic")
    {
        var fresh = ruleset.Trim().ToLowerInvariant() == "fresh";
        var confirmWindow = fresh ? 4 : 10;

        // Look back a few 4H bars so weekend / late-session still finds recent sweeps.
        var sweep = Detect4hSweep(bars4hNewestFirst, dailyNewestFirst, maxBars: 4);
        if (sweep is null)
            return null;

        for (var i = 0; i < Math.Min(confirmWindow, bars1hNewestFirst.Count - 2); i++)
        {
            var bar = bars1hNewestFirst[i];
            if (bar.BarTime < sweep.BarTime)
                continue;

            var prev1h = bars1hNewestFirst.Skip(i + 1).Take(2).ToList();
            if (prev1h.Count < 2)
                continue;

            var last2High = prev1h.Max(b => b.High);
            var last2Low = prev1h.Min(b => b.Low);
            // Classic: price on confirm bar (LTP only on newest). Fresh: always latest mark.
            var price = fresh
                ? (livePrice is > 0 ? livePrice.Value : bars1hNewestFirst[0].Close)
                : (i == 0 && livePrice is > 0 ? livePrice.Value : bar.Close);

            var buyBreak = bar.High > last2High;
            var sellBreak = bar.Low < last2Low;
            var buyImminent = !buyBreak && price >= last2High * (1m - ImminentMargin);
            var sellImminent = !sellBreak && price <= last2Low * (1m + ImminentMargin);

            string? side = null;
            if (sweep.Side == SignalSides.Buy && (buyBreak || buyImminent))
                side = SignalSides.Buy;
            else if (sweep.Side == SignalSides.Sell && (sellBreak || sellImminent))
                side = SignalSides.Sell;
            if (side is null)
                continue;

            var (rvol, rvolPctile, rvolOk) = ComputeRvol(bars1hNewestFirst, i);
            if (!rvolOk)
                continue;

            if (!IsStrongClose(bar, side))
                continue;

            var entry = side == SignalSides.Buy ? last2High : last2Low;
            var sl = side == SignalSides.Buy
                ? Math.Min(sweep.ZonePrice, sweep.CandleLow) * 0.999m
                : Math.Max(sweep.ZonePrice, sweep.CandleHigh) * 1.001m;

            if (side == SignalSides.Buy && sl >= entry)
                sl = entry * 0.995m;
            if (side == SignalSides.Sell && sl <= entry)
                sl = entry * 1.005m;

            var zones = BuildZones(bars4hNewestFirst, dailyNewestFirst, entry);
            var targets = PickStructureTargets(side, entry, sl, zones);

            if (fresh && targets.Count > 0)
            {
                var t1 = targets[0];
                var mark = price;
                var t1Already = side == SignalSides.Buy ? mark >= t1 : mark <= t1;
                if (t1Already)
                    continue;

                var staleAfterConfirm = false;
                for (var j = 0; j < i; j++)
                {
                    var newer = bars1hNewestFirst[j];
                    if (side == SignalSides.Buy && newer.High >= t1)
                    {
                        staleAfterConfirm = true;
                        break;
                    }
                    if (side == SignalSides.Sell && newer.Low <= t1)
                    {
                        staleAfterConfirm = true;
                        break;
                    }
                }
                if (staleAfterConfirm)
                    continue;
            }

            var nearest = NearestZone(price, zones);

            return new LiquiditySignalRow
            {
                Id = Guid.NewGuid(),
                LiquidityRunId = runId,
                UserId = userId,
                InstrumentId = token.InstrumentId,
                AppSymbol = token.AppSymbol,
                Side = side,
                AsOfDate = asOf,
                EntryPrice = RoundPrice(entry),
                InitialStopLoss = RoundPrice(sl),
                TargetT1 = targets.Count > 0 ? RoundPrice(targets[0]) : null,
                TargetT2 = targets.Count > 1 ? RoundPrice(targets[1]) : null,
                TargetT3 = targets.Count > 2 ? RoundPrice(targets[2]) : null,
                RelativeVolume = RoundPrice(rvol),
                RvolPercentile = Math.Round((decimal)rvolPctile, 4),
                RvolOk = true,
                StrongClose = true,
                SectorConfirmed = false,
                SweepSide = side,
                SweptZoneType = sweep.ZoneType,
                SweptZonePrice = RoundPrice(sweep.ZonePrice),
                NearestZoneType = nearest?.Type,
                NearestZonePrice = nearest is null ? null : RoundPrice(nearest.Price),
                DistancePct = nearest is null || price == 0
                    ? null
                    : Math.Round(Math.Abs(price - nearest.Price) / price, 6),
                ZoneTags = zones.Select(z => z.Type).Distinct().Take(12).ToArray(),
                TimeframeContext = fresh ? "4h_sweep+1h_confirm_fresh" : "4h_sweep+1h_confirm"
            };
        }

        return null;
    }

    /// <summary>
    /// Upsert today's OHLC for sector indexes from Angel FULL quotes
    /// (same idea as AnalysisRunService live equity bars — keeps sector direction check current).
    /// </summary>
    private async Task RefreshSectorDailyBarsFromQuotesAsync(DateOnly asOf, CancellationToken ct)
    {
        var sectorTokens = await _instruments.GetActiveTokensForSectorsAsync(ct);
        if (sectorTokens.Count == 0)
            return;

        await _angel.EnsureSessionAsync(ct);
        var exchangeTokens = sectorTokens
            .GroupBy(t => t.Exchange)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<string>)g.Select(x => x.SymbolToken).Distinct().ToList());

        var quotes = await _angel.GetQuotesAsync(QuoteModes.Full, exchangeTokens, ct);
        var byToken = quotes.ToDictionary(q => (q.Exchange, q.SymbolToken), q => q);
        var updated = 0;

        foreach (var token in sectorTokens)
        {
            if (!byToken.TryGetValue((token.Exchange, token.SymbolToken), out var q))
                continue;
            if (q.Ltp is null || q.Open is null || q.High is null || q.Low is null || q.Close is null)
                continue;

            var open = q.Open.Value;
            var high = q.High.Value;
            var low = q.Low.Value;
            var close = q.Ltp.Value;
            high = Math.Max(high, Math.Max(open, close));
            low = Math.Min(low, Math.Min(open, close));

            await _market.UpsertMarketBarAsync(
                token.InstrumentId, asOf, open, high, low, close, q.TradeVolume ?? 0, ct);
            updated++;
        }

        _logger.LogInformation("Refreshed today's OHLC for {Count} sector indexes.", updated);
    }

    /// <summary>Sector confirmation: sector index must also break last 2 sessions' high/low (no volume required). Same rule as Signals / AnalysisRunService.</summary>
    private static bool CheckSectorConfirmation(string side, List<MarketBarRow> sectorBarsDesc)
    {
        if (sectorBarsDesc.Count < 3)
            return true; // not enough data — pass through

        var latest = sectorBarsDesc[0];
        var prev = sectorBarsDesc.Skip(1).Take(2).ToList();
        var last2High = prev.Max(b => b.High);
        var last2Low = prev.Min(b => b.Low);

        return side == SignalSides.Buy
            ? latest.High > last2High
            : latest.Low < last2Low;
    }

    private static (decimal rvol, double percentile, bool ok) ComputeRvol(
        List<MarketIntradayBarRow> barsNewestFirst, int barIndex = 0)
    {
        if (barsNewestFirst.Count < barIndex + RvolLookback + 5)
            return (0, 0, false);

        double RvolAt(int i)
        {
            var window = barsNewestFirst.Skip(i + 1).Take(RvolLookback).ToList();
            if (window.Count < RvolLookback)
                return 0;
            var avg = window.Average(b => (double)b.Volume);
            if (avg <= 0)
                return 0;
            return barsNewestFirst[i].Volume / avg;
        }

        var current = (decimal)RvolAt(barIndex);
        var history = new List<double>();
        var maxI = Math.Min(RvolHistoryBars, barsNewestFirst.Count - RvolLookback - 1);
        for (var i = 0; i <= maxI; i++)
        {
            var v = RvolAt(i);
            if (v > 0)
                history.Add(v);
        }

        if (history.Count < 10)
            return (current, 0, false);

        history.Sort();
        var rank = history.Count(h => h <= (double)current);
        var pctile = rank / (double)history.Count;
        var ok = current >= RvolFloor && pctile >= RvolPercentileGate;
        return (current, pctile, ok);
    }

    private static bool IsStrongClose(MarketIntradayBarRow bar, string side)
    {
        var range = bar.High - bar.Low;
        if (range <= 0)
            return false;
        var pos = (bar.Close - bar.Low) / range;
        return side == SignalSides.Buy
            ? pos >= StrongClosePct
            : pos <= (1m - StrongClosePct);
    }

    private sealed record SweepResult(
        string Side, string ZoneType, decimal ZonePrice,
        decimal CandleHigh, decimal CandleLow, DateTimeOffset BarTime);

    private static SweepResult? Detect4hSweep(
        List<Ohlcv> bars4h, List<MarketBarRow> daily, int maxBars = 4)
    {
        SweepResult? best = null;
        var limit = Math.Min(maxBars, bars4h.Count);
        for (var ci = 0; ci < limit; ci++)
        {
            var candle = bars4h[ci];
            // Zones from structure before this candle
            var prior = bars4h.Skip(ci + 1).ToList();
            var zones = BuildZones(prior, daily, candle.Close);

            foreach (var z in zones.Where(z => z.IsSupportLike))
            {
                if (candle.Low < z.Price && candle.Close > z.Price)
                {
                    best = Prefer(best, new SweepResult(
                        SignalSides.Buy, z.Type, z.Price, candle.High, candle.Low, candle.BarTime));
                }
            }

            foreach (var z in zones.Where(z => z.IsResistanceLike))
            {
                if (candle.High > z.Price && candle.Close < z.Price)
                {
                    best = Prefer(best, new SweepResult(
                        SignalSides.Sell, z.Type, z.Price, candle.High, candle.Low, candle.BarTime));
                }
            }
        }

        return best;
    }

    private static SweepResult Prefer(SweepResult? current, SweepResult candidate)
    {
        if (current is null)
            return candidate;
        var curPri = ZonePriority(current.ZoneType);
        var newPri = ZonePriority(candidate.ZoneType);
        if (newPri < curPri)
            return candidate;
        if (newPri > curPri)
            return current;
        // Same priority: prefer more recent sweep
        return candidate.BarTime >= current.BarTime ? candidate : current;
    }

    private static int ZonePriority(string type) => type switch
    {
        "equal_low" or "equal_high" => 0,
        "swing_low" or "swing_high" => 1,
        "pdl" or "pdh" => 2,
        "pwl" or "pwh" => 3,
        "round" => 4,
        _ => 5
    };

    private sealed record Zone(string Type, decimal Price, int Priority, bool IsSupportLike, bool IsResistanceLike);

    private static List<Zone> BuildZones(List<Ohlcv> bars4hNewestFirst, List<MarketBarRow> dailyNewestFirst, decimal refPrice)
    {
        var zones = new List<Zone>();

        if (dailyNewestFirst.Count >= 2)
        {
            var prev = dailyNewestFirst[1];
            zones.Add(new Zone("pdh", prev.High, 2, false, true));
            zones.Add(new Zone("pdl", prev.Low, 2, true, false));
        }

        if (dailyNewestFirst.Count >= 6)
        {
            var week = dailyNewestFirst.Skip(1).Take(5).ToList();
            zones.Add(new Zone("pwh", week.Max(b => b.High), 3, false, true));
            zones.Add(new Zone("pwl", week.Min(b => b.Low), 3, true, false));
        }

        var swings = FindSwings(bars4hNewestFirst, lookback: 12);
        foreach (var h in swings.Highs)
            zones.Add(new Zone("swing_high", h, 1, false, true));
        foreach (var l in swings.Lows)
            zones.Add(new Zone("swing_low", l, 1, true, false));

        AddEqualLevels(zones, swings.Highs, "equal_high", support: false);
        AddEqualLevels(zones, swings.Lows, "equal_low", support: true);

        foreach (var step in new[] { 50m, 100m, 500m, 1000m })
        {
            if (refPrice < step * 0.5m)
                continue;
            var nearest = Math.Round(refPrice / step, MidpointRounding.AwayFromZero) * step;
            if (nearest <= 0)
                continue;
            if (Math.Abs(nearest - refPrice) / refPrice <= 0.02m)
            {
                zones.Add(new Zone("round", nearest, 4, true, true));
            }
        }

        return zones
            .GroupBy(z => (z.Type, Math.Round(z.Price, 2)))
            .Select(g => g.OrderBy(z => z.Priority).First())
            .ToList();
    }

    private static void AddEqualLevels(List<Zone> zones, List<decimal> levels, string type, bool support)
    {
        for (var i = 0; i < levels.Count; i++)
        for (var j = i + 1; j < levels.Count; j++)
        {
            var a = levels[i];
            var b = levels[j];
            if (a <= 0 || b <= 0)
                continue;
            if (Math.Abs(a - b) / ((a + b) / 2m) <= EqualTolPct)
            {
                var mid = (a + b) / 2m;
                zones.Add(new Zone(type, mid, 0, support, !support));
            }
        }
    }

    private static (List<decimal> Highs, List<decimal> Lows) FindSwings(List<Ohlcv> newestFirst, int lookback)
    {
        var highs = new List<decimal>();
        var lows = new List<decimal>();
        var n = Math.Min(lookback, newestFirst.Count);
        // Work oldest→newest for fractal clarity
        var chron = newestFirst.Take(n).Reverse().ToList();
        for (var i = 1; i < chron.Count - 1; i++)
        {
            if (chron[i].High > chron[i - 1].High && chron[i].High > chron[i + 1].High)
                highs.Add(chron[i].High);
            if (chron[i].Low < chron[i - 1].Low && chron[i].Low < chron[i + 1].Low)
                lows.Add(chron[i].Low);
        }

        return (highs, lows);
    }

    private static List<decimal> PickStructureTargets(string side, decimal entry, decimal sl, List<Zone> zones)
    {
        IEnumerable<decimal> candidates;
        if (side == SignalSides.Buy)
        {
            candidates = zones
                .Where(z => z.IsResistanceLike && z.Price > entry * (1m + TargetMinDistancePct))
                .OrderBy(z => z.Priority)
                .ThenBy(z => z.Price)
                .Select(z => z.Price);
        }
        else
        {
            candidates = zones
                .Where(z => z.IsSupportLike && z.Price < entry * (1m - TargetMinDistancePct))
                .OrderBy(z => z.Priority)
                .ThenByDescending(z => z.Price)
                .Select(z => z.Price);
        }

        var list = new List<decimal>();
        foreach (var p in candidates)
        {
            if (side == SignalSides.Buy && p <= entry)
                continue;
            if (side == SignalSides.Sell && p >= entry)
                continue;
            if (list.Any(x => Math.Abs(x - p) / entry < TargetMinDistancePct))
                continue;
            list.Add(p);
            if (list.Count >= 3)
                break;
        }

        // Nearest-first for buys (ascending), sells (descending)
        if (side == SignalSides.Buy)
            list = list.OrderBy(x => x).ToList();
        else
            list = list.OrderByDescending(x => x).ToList();

        _ = sl;
        return list;
    }

    private static Zone? NearestZone(decimal price, List<Zone> zones)
    {
        if (zones.Count == 0 || price <= 0)
            return null;
        return zones.OrderBy(z => Math.Abs(z.Price - price)).First();
    }

    /// <summary>Aggregate 1H bars (newest first) into 4H buckets per IST trading day (groups of 4 chronological hours).</summary>
    public static List<Ohlcv> Aggregate4h(List<MarketIntradayBarRow> bars1hNewestFirst)
    {
        var chron = bars1hNewestFirst.OrderBy(b => b.BarTime).ToList();
        var result = new List<Ohlcv>();
        foreach (var dayGroup in chron.GroupBy(b => b.BarTime.ToOffset(TimeSpan.FromHours(5.5)).Date))
        {
            var dayBars = dayGroup.OrderBy(b => b.BarTime).ToList();
            var i = 0;
            while (i < dayBars.Count)
            {
                var take = Math.Min(4, dayBars.Count - i);
                if (take < 2)
                    break;
                var chunk = dayBars.Skip(i).Take(take).ToList();
                result.Add(new Ohlcv(
                    chunk[0].BarTime,
                    chunk[0].Open,
                    chunk.Max(c => c.High),
                    chunk.Min(c => c.Low),
                    chunk[^1].Close,
                    chunk.Sum(c => c.Volume)));
                i += take == 4 ? 4 : take;
                if (take < 4)
                    break;
            }
        }

        return result.OrderByDescending(b => b.BarTime).ToList();
    }

    private static decimal RoundPrice(decimal price) =>
        Math.Round(price, 2, MidpointRounding.AwayFromZero);

    public readonly record struct Ohlcv(
        DateTimeOffset BarTime, decimal Open, decimal High, decimal Low, decimal Close, long Volume);
}
