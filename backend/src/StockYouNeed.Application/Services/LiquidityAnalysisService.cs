using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StockYouNeed.Application.Abstractions;
using StockYouNeed.Application.Options;
using StockYouNeed.Application.Outcomes;
using StockYouNeed.Application.Signals;
using StockYouNeed.Domain;

namespace StockYouNeed.Application.Services;

/// <summary>
/// Separate liquidity engine: 4H sweep zones + 1H confirm (RVOL, strong close, breakout).
/// Does not touch daily AnalysisRunService / analysis_signals.
/// </summary>
public sealed class LiquidityAnalysisService
{
    // Level-1 (safest) relaxations: slightly more setups, small accuracy trade-off.
    private const decimal ImminentMargin = 0.01m;
    private const decimal RvolFloor = 1.0m;
    private const decimal StrongClosePct = 0.60m;
    private const int SweepMaxBars = 8;
    private const int ClassicConfirmWindow = 15;
    private const int FreshConfirmWindow = 8;
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
        string ruleset = "classic",
        bool requireRetest = false,
        bool requireRelativeStrength = false)
    {
        ruleset = NormalizeRuleset(ruleset);
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
            ["ruleset"] = ruleset,
            ["requireRetest"] = requireRetest,
            ["requireRelativeStrength"] = requireRelativeStrength
        };

        try
        {
            if (_options.Enabled)
                await _tokenSync.EnsureUniverseTokensMappedAsync(ct);

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

            // Optional Nifty 50 daily bars for V2 relative-strength filter.
            List<MarketBarRow>? niftyDaily = null;
            if (ruleset == "v2")
            {
                niftyDaily = await LoadNiftyDailyBarsAsync(ct);
                stats["niftyDailyBars"] = niftyDaily?.Count ?? 0;
            }

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
            var skippedFlip = 0;
            var sectorConfirmedCount = 0;
            var openOutcomes = await _outcomes.GetOpenAsync(userId, ct);
            var dailyBarLimit = ruleset == "v2" ? 80 : 10;
            var v2Options = new LiquidityV2Evaluator.Options(
                requireRetest, requireRelativeStrength, ActionableOnly: true);
            var v2Diag = new LiquidityV2Evaluator.Diagnostics();

            foreach (var token in tokens)
            {
                ct.ThrowIfCancellationRequested();
                scanned++;
                if (ruleset == "v2")
                    v2Diag.Pass("scanned");

                var bars1h = (await _market.GetIntradayBarsForInstrumentAsync(
                    token.InstrumentId, IntradayBarsSyncService.Interval1h, 120, ct)).ToList();

                if (bars1h.Count < Min1hBars)
                {
                    fewBars++;
                    if (ruleset == "v2")
                        v2Diag.Reject("few_intraday_bars");
                    continue;
                }

                var bars4h = Aggregate4h(bars1h);
                if (bars4h.Count < Min4hBars)
                {
                    fewBars++;
                    if (ruleset == "v2")
                        v2Diag.Reject("few_intraday_bars");
                    continue;
                }

                var daily = (await _market.GetBarsForInstrumentAsync(token.InstrumentId, dailyBarLimit, ct))
                    .OrderByDescending(b => b.TradeDate)
                    .ToList();
                ltpById.TryGetValue(token.InstrumentId, out var ltp);

                var sectorId = await _instruments.GetSectorIdForInstrumentAsync(token.InstrumentId, ct);

                LiquiditySignalRow? signal;
                if (ruleset == "v2")
                {
                    signal = LiquidityV2Evaluator.TryEvaluate(
                        userId, runId, asOf, token, bars1h, bars4h, daily,
                        ltp > 0 ? ltp : null,
                        sectorConfirmed: false,
                        niftyDaily,
                        v2Options,
                        v2Diag);

                    if (signal is not null)
                    {
                        if (sectorId is not null && sectorBarsCache.TryGetValue(sectorId.Value, out var sBarsForSide))
                            signal.SectorConfirmed = CheckSectorConfirmation(signal.Side, sBarsForSide);
                        else
                            signal.SectorConfirmed = false;

                        RescoreV2Sector(signal);
                        if (signal.QualityScore < LiquidityV2Evaluator.MinQualityScore)
                        {
                            v2Diag.Reject("bar_score_below_floor_after_sector");
                            signal = null;
                        }
                    }
                }
                else
                {
                    signal = TryEvaluate(
                        userId, runId, asOf, token, bars1h, bars4h, daily,
                        ltp > 0 ? ltp : null,
                        ruleset,
                        actionableOnly: true);
                }

                if (signal is null)
                {
                    noSetup++;
                    continue;
                }

                if (OppositeSignalFlipGuard.IsFlipAgainstOpen(
                        token.InstrumentId, signal.Side, asOf, openOutcomes, out var flipReason))
                {
                    skippedFlip++;
                    if (ruleset == "v2")
                        v2Diag.Reject("flip_guard");
                    _logger.LogInformation(
                        "Liquidity ({Ruleset}) skip {Symbol}: {Reason}",
                        ruleset, token.AppSymbol, flipReason);
                    continue;
                }

                if (ruleset != "v2")
                {
                    if (sectorId is not null && sectorBarsCache.TryGetValue(sectorId.Value, out var sectorBars2))
                        signal.SectorConfirmed = CheckSectorConfirmation(signal.Side, sectorBars2);
                    else
                        signal.SectorConfirmed = false;
                }

                if (signal.SectorConfirmed)
                    sectorConfirmedCount++;

                await _portfolio.InsertLiquiditySignalAsync(signal, ct);
                await _outcomes.OpenFromLiquidityAsync(signal, ruleset, ct);
                signals++;
                if (ruleset == "v2")
                {
                    v2Diag.Pass("saved");
                    if (!string.IsNullOrWhiteSpace(signal.EventType))
                        v2Diag.SavedEvent(signal.EventType);
                }
            }

            stats["scanned"] = scanned;
            stats["signals"] = signals;
            stats["sectorConfirmed"] = sectorConfirmedCount;
            stats["fewIntradayBars"] = fewBars;
            stats["noSetup"] = noSetup;
            stats["skippedFlip"] = skippedFlip;
            _ = watchIds; // reserved for future universe narrowing

            if (ruleset == "v2")
            {
                foreach (var (stage, count) in v2Diag.Funnel)
                    stats["v2Funnel_" + stage] = count;
                foreach (var (gate, count) in v2Diag.Counts)
                    stats["v2Reject_" + gate] = count;
                foreach (var (evt, count) in v2Diag.EventCandidates)
                    stats["v2EventCand_" + evt] = count;
                foreach (var (evt, count) in v2Diag.EventSaved)
                    stats["v2EventSaved_" + evt] = count;
                _logger.LogInformation("Liquidity V2 funnel: {Funnel}", v2Diag.DescribeFunnel());
                _logger.LogInformation("Liquidity V2 events: {Events}", v2Diag.DescribeEvents());
                _logger.LogInformation("Liquidity V2 rejections: {Rejections}", v2Diag.DescribeRejects());
            }

            await _portfolio.CompleteLiquidityAnalysisRunAsync(runId, "succeeded", null, stats, ct);
            _logger.LogInformation(
                "Liquidity run {RunId} ({Ruleset}): scanned={Scanned}, signals={Signals}, sectorConfirmed={SectorConfirmed}, fewBars={Few}, noSetup={No}, skippedFlip={Flip}",
                runId, ruleset, scanned, signals, sectorConfirmedCount, fewBars, noSetup, skippedFlip);

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

    private static string NormalizeRuleset(string? ruleset)
    {
        var s = (ruleset ?? "classic").Trim().ToLowerInvariant();
        return s switch
        {
            "fresh" => "fresh",
            "v2" => "v2",
            _ => "classic"
        };
    }

    private async Task<List<MarketBarRow>?> LoadNiftyDailyBarsAsync(CancellationToken ct)
    {
        foreach (var symbol in new[] { "NIFTY", "NIFTY 50", "NIFTY50" })
        {
            var inst = await _instruments.FindBySymbolAsync(symbol, ct);
            if (inst is null) continue;
            var bars = (await _market.GetBarsForInstrumentAsync(inst.Id, 80, ct))
                .OrderByDescending(b => b.TradeDate)
                .ToList();
            if (bars.Count >= 2)
                return bars;
        }

        _logger.LogInformation(
            "Liquidity V2: no Nifty daily bars found (symbols NIFTY/NIFTY50). Relative-strength filter will reject when enabled.");
        return null;
    }

    /// <summary>
    /// After sector confirmation is known, adjust V2 quality score so sector +10 is accurate.
    /// </summary>
    private static void RescoreV2Sector(LiquiditySignalRow signal)
    {
        var reasons = signal.ScoreReasons?.ToList() ?? new List<string>();
        var hasSector = reasons.Any(r => r.Contains("sector", StringComparison.OrdinalIgnoreCase));
        if (signal.SectorConfirmed && !hasSector)
        {
            signal.QualityScore += 10;
            reasons.Add("sector +10");
        }
        else if (!signal.SectorConfirmed && hasSector)
        {
            signal.QualityScore = Math.Max(0, signal.QualityScore - 10);
            reasons.RemoveAll(r => r.Contains("sector", StringComparison.OrdinalIgnoreCase));
        }

        signal.ScoreReasons = reasons.ToArray();
        signal.ConfidenceRating = signal.QualityScore >= 92 ? "A+"
            : signal.QualityScore >= 84 ? "A"
            : signal.QualityScore >= 72 ? "B"
            : signal.QualityScore >= 58 ? "C"
            : "D";
    }

    /// <summary>
    /// Live single-stock liquidity: sync 1H bars for this token, build zones, evaluate fresh + classic.
    /// Does not insert into liquidity_signals (ephemeral for Analyze Stock).
    /// </summary>
    public async Task<LiquidityInstrumentEval> EvaluateForInstrumentAsync(
        Guid userId,
        Guid instrumentId,
        CancellationToken ct = default)
    {
        var token = (await _instruments.GetActiveTokensForUniversesAsync(ct))
            .FirstOrDefault(t => t.InstrumentId == instrumentId);
        if (token is null)
        {
            return new LiquidityInstrumentEval
            {
                Status = "no_token",
                Detail = "No Angel token mapped for this equity.",
            };
        }

        var barsUpserted = 0;
        try
        {
            if (_options.Enabled)
                barsUpserted = await _intradaySync.SyncInstrumentHourlyAsync(token, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Single-stock 1H sync failed for {Symbol}", token.AppSymbol);
        }

        var bars1h = (await _market.GetIntradayBarsForInstrumentAsync(
            instrumentId, IntradayBarsSyncService.Interval1h, 120, ct)).ToList();
        if (bars1h.Count < Min1hBars)
        {
            return new LiquidityInstrumentEval
            {
                Status = "few_bars",
                Detail =
                    $"Need ≥{Min1hBars} hourly bars (have {bars1h.Count}). Sync may still be catching up.",
                BarsUpserted = barsUpserted,
            };
        }

        var bars4h = Aggregate4h(bars1h);
        if (bars4h.Count < Min4hBars)
        {
            return new LiquidityInstrumentEval
            {
                Status = "few_bars",
                Detail =
                    $"Need ≥{Min4hBars} 4H bars after aggregation (have {bars4h.Count}).",
                BarsUpserted = barsUpserted,
            };
        }

        var daily = (await _market.GetBarsForInstrumentAsync(instrumentId, 10, ct))
            .OrderByDescending(b => b.TradeDate)
            .ToList();
        var ltp = (await _market.GetAllLtpAsync(ct))
            .FirstOrDefault(x => x.InstrumentId == instrumentId)?.Ltp;
        var mark = ltp is > 0 ? ltp.Value : bars1h[0].Close;
        var asOf = DateOnly.FromDateTime(
            DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(5.5)).DateTime);
        var runId = Guid.Empty;

        var zonesInternal = BuildZones(bars4h, daily, mark);
        var zoneLevels = zonesInternal
            .OrderByDescending(z => z.Price)
            .Select(z => new LiquidityZoneLevel
            {
                Type = z.Type,
                Price = RoundPrice(z.Price),
                Kind = z.IsSupportLike && z.IsResistanceLike
                    ? "both"
                    : z.IsSupportLike
                        ? "support"
                        : "resistance",
            })
            .GroupBy(z => (z.Type, z.Price))
            .Select(g => g.First())
            .Take(20)
            .ToList();

        var fresh = TryEvaluate(
            userId, runId, asOf, token, bars1h, bars4h, daily,
            ltp is > 0 ? ltp : null, "fresh");
        var classic = TryEvaluate(
            userId, runId, asOf, token, bars1h, bars4h, daily,
            ltp is > 0 ? ltp : null, "classic");

        await ApplySectorConfirmAsync(fresh, instrumentId, ct);
        await ApplySectorConfirmAsync(classic, instrumentId, ct);

        var evalDetailParts = new List<string>();
        var openOutcomes = await _outcomes.GetOpenAsync(userId, ct);
        if (fresh is not null
            && OppositeSignalFlipGuard.IsFlipAgainstOpen(
                instrumentId, fresh.Side, asOf, openOutcomes, out var freshFlip))
        {
            evalDetailParts.Add($"Fresh skipped: {freshFlip}");
            fresh = null;
        }
        if (classic is not null
            && OppositeSignalFlipGuard.IsFlipAgainstOpen(
                instrumentId, classic.Side, asOf, openOutcomes, out var classicFlip))
        {
            evalDetailParts.Add($"Classic skipped: {classicFlip}");
            classic = null;
        }

        var sweep = Detect4hSweep(bars4h, daily, maxBars: SweepMaxBars);
        var nearest = NearestZone(mark, zonesInternal);
        var eval = new LiquidityInstrumentEval
        {
            Fresh = fresh,
            Classic = classic,
            Zones = zoneLevels,
            BarsUpserted = barsUpserted,
            SweepSide = fresh?.SweepSide ?? classic?.SweepSide ?? sweep?.Side,
            SweptZoneType = fresh?.SweptZoneType ?? classic?.SweptZoneType ?? sweep?.ZoneType,
            SweptZonePrice = fresh?.SweptZonePrice ?? classic?.SweptZonePrice
                ?? (sweep is null ? null : RoundPrice(sweep.ZonePrice)),
            NearestZoneType = fresh?.NearestZoneType ?? classic?.NearestZoneType ?? nearest?.Type,
            NearestZonePrice = fresh?.NearestZonePrice ?? classic?.NearestZonePrice
                ?? (nearest is null ? null : RoundPrice(nearest.Price)),
            DistancePct = fresh?.DistancePct ?? classic?.DistancePct
                ?? (nearest is null || mark == 0
                    ? null
                    : Math.Round(Math.Abs(mark - nearest.Price) / mark, 6)),
        };

        if (fresh is not null || classic is not null)
        {
            eval.Status = "evaluated";
            if (evalDetailParts.Count > 0)
                eval.Detail = string.Join(" ", evalDetailParts);
            return eval;
        }

        eval.Status = "no_setup";
        var baseDetail = sweep is null
            ? "Zones computed; no 4H sweep + 1H confirm setup right now."
            : $"Recent {sweep.Side} sweep of {sweep.ZoneType} @ {RoundPrice(sweep.ZonePrice)} — waiting 1H confirm / RVOL / strong close.";
        if (nearest is not null && mark > 0)
        {
            var dist = Math.Round(Math.Abs(mark - nearest.Price) / mark * 100m, 2);
            baseDetail += $" Nearest {nearest.Type} @ {RoundPrice(nearest.Price)} ({dist}% away).";
        }
        if (evalDetailParts.Count > 0)
            baseDetail = string.Join(" ", evalDetailParts) + " " + baseDetail;
        eval.Detail = baseDetail;

        return eval;
    }

    private async Task ApplySectorConfirmAsync(
        LiquiditySignalRow? signal, Guid instrumentId, CancellationToken ct)
    {
        if (signal is null)
            return;
        var sectorId = await _instruments.GetSectorIdForInstrumentAsync(instrumentId, ct);
        if (sectorId is null)
        {
            signal.SectorConfirmed = false;
            return;
        }

        var sectorBars = (await _market.GetBarsForInstrumentAsync(sectorId.Value, 10, ct))
            .OrderByDescending(b => b.TradeDate)
            .ToList();
        signal.SectorConfirmed = sectorBars.Count >= 3
            && CheckSectorConfirmation(signal.Side, sectorBars);
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
        string ruleset = "classic",
        bool actionableOnly = false)
    {
        var fresh = ruleset.Trim().ToLowerInvariant() == "fresh";
        var confirmWindow = fresh ? FreshConfirmWindow : ClassicConfirmWindow;

        // Look back several 4H bars so setups that confirm 2–4 days after the sweep still qualify.
        var sweep = Detect4hSweep(bars4hNewestFirst, dailyNewestFirst, maxBars: SweepMaxBars);
        if (sweep is null)
            return null;

        for (var i = 0; i < Math.Min(confirmWindow, bars1hNewestFirst.Count - 2); i++)
        {
            // Live: ignore stale confirms from many bars ago.
            if (actionableOnly && i > 3)
                break;

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
            var buyImminent = !buyBreak && price >= last2High * (1m - ImminentMargin) && price < last2High;
            var sellImminent = !sellBreak && price <= last2Low * (1m + ImminentMargin) && price > last2Low;

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

            if ((fresh || actionableOnly) && targets.Count > 0)
            {
                var t1 = targets[0];
                var mark = actionableOnly
                    ? (livePrice is > 0 ? livePrice.Value : bars1hNewestFirst[0].Close)
                    : price;
                var t1Already = side == SignalSides.Buy ? mark >= t1 : mark <= t1;
                if (t1Already)
                    continue;

                if (actionableOnly && !IsLiveEntryStillOpen(side, entry, t1, mark))
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
    /// True when live mark is still a takeable entry: T1 not tagged and price not extended past entry.
    /// Buy: mark &lt; T1 and mark within ~1.5% above entry (or still below entry).
    /// </summary>
    internal static bool IsLiveEntryStillOpen(string side, decimal entry, decimal t1, decimal mark)
    {
        if (entry <= 0)
            return false;

        const decimal nearEntryPct = 0.025m;
        if (side == SignalSides.Buy)
        {
            if (mark >= t1)
                return false;
            return mark <= entry * (1m + nearEntryPct);
        }

        if (mark <= t1)
            return false;
        return mark >= entry * (1m - nearEntryPct);
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

    internal static (decimal rvol, double percentile, bool ok) ComputeRvol(
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
            return (current, 0, current >= RvolFloor);

        history.Sort();
        var rank = history.Count(h => h <= (double)current);
        var pctile = rank / (double)history.Count;
        // Percentile kept for display/scoring only — Level-1 gate is RVOL floor alone.
        var ok = current >= RvolFloor;
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

    /// <summary>Zone detection knobs. Classic/fresh use <see cref="Classic"/>; V2 passes looser equal-tol / round steps.</summary>
    internal sealed record ZoneOptions(
        decimal EqualTolPct = 0.0015m,
        int SwingLookback = 8,
        decimal[]? RoundSteps = null)
    {
        public static ZoneOptions Classic { get; } = new();
        public static ZoneOptions V2 { get; } = new(
            EqualTolPct: 0.0025m,
            SwingLookback: 8,
            RoundSteps: new[] { 25m, 50m, 100m, 250m, 500m, 1000m });
    }

    internal sealed record SweepResult(
        string Side, string ZoneType, decimal ZonePrice,
        decimal CandleHigh, decimal CandleLow, DateTimeOffset BarTime);

    internal static SweepResult? Detect4hSweep(
        List<Ohlcv> bars4h, List<MarketBarRow> daily, int maxBars = 8, ZoneOptions? zoneOptions = null)
    {
        zoneOptions ??= ZoneOptions.Classic;
        SweepResult? best = null;
        var limit = Math.Min(maxBars, bars4h.Count);
        for (var ci = 0; ci < limit; ci++)
        {
            var candle = bars4h[ci];
            // Zones from structure before this candle
            var prior = bars4h.Skip(ci + 1).ToList();
            var zones = BuildZones(prior, daily, candle.Close, zoneOptions);

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

    internal static SweepResult Prefer(SweepResult? current, SweepResult candidate)
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

    internal static int ZonePriority(string type) => type switch
    {
        "equal_low" or "equal_high" => 0,
        "swing_low" or "swing_high" => 1,
        "pdl" or "pdh" => 2,
        "pwl" or "pwh" => 3,
        "round" => 4,
        "cluster" => 2,
        "internal_high_4h" or "internal_low_4h" => 6,
        "internal_high_1h" or "internal_low_1h" => 7,
        _ => 5
    };

    internal sealed record Zone(string Type, decimal Price, int Priority, bool IsSupportLike, bool IsResistanceLike);

    internal sealed record ZoneCluster(
        string Side, decimal MidPrice, decimal Low, decimal High, int MemberCount, string[] MemberTypes);

    /// <summary>
    /// V2 liquidity event: actionable reclaim/event time may differ from the original sweep candle.
    /// </summary>
    internal sealed record LiquidityEvent(
        string EventType,
        string Side,
        string ZoneType,
        decimal ZonePrice,
        decimal CandleHigh,
        decimal CandleLow,
        DateTimeOffset EventTime,
        DateTimeOffset SweepTime,
        int SweepCount,
        int ClusterSize,
        string[] ZoneTags,
        decimal Depth);

    internal static List<Zone> BuildZones(
        List<Ohlcv> bars4hNewestFirst,
        List<MarketBarRow> dailyNewestFirst,
        decimal refPrice,
        ZoneOptions? zoneOptions = null,
        DateOnly? asOfDate = null)
    {
        zoneOptions ??= ZoneOptions.Classic;
        var equalTol = zoneOptions.EqualTolPct;
        var roundSteps = zoneOptions.RoundSteps ?? new[] { 50m, 100m, 500m, 1000m };
        var zones = new List<Zone>();

        // Classic path keeps legacy indexing: [0]=latest/[1]=prior session.
        // V2 as-of path uses only bars strictly before the event candle's IST date.
        if (asOfDate is DateOnly asOf)
        {
            var daily = dailyNewestFirst
                .Where(d => d.TradeDate < asOf)
                .OrderByDescending(d => d.TradeDate)
                .ToList();
            if (daily.Count >= 1)
            {
                var prev = daily[0];
                zones.Add(new Zone("pdh", prev.High, 2, false, true));
                zones.Add(new Zone("pdl", prev.Low, 2, true, false));
            }
            if (daily.Count >= 5)
            {
                var week = daily.Take(5).ToList();
                zones.Add(new Zone("pwh", week.Max(b => b.High), 3, false, true));
                zones.Add(new Zone("pwl", week.Min(b => b.Low), 3, true, false));
            }
        }
        else
        {
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
        }

        var swings = FindSwings(bars4hNewestFirst, lookback: zoneOptions.SwingLookback);
        foreach (var h in swings.Highs)
            zones.Add(new Zone("swing_high", h, 1, false, true));
        foreach (var l in swings.Lows)
            zones.Add(new Zone("swing_low", l, 1, true, false));

        AddEqualLevels(zones, swings.Highs, "equal_high", support: false, equalTol);
        AddEqualLevels(zones, swings.Lows, "equal_low", support: true, equalTol);

        foreach (var step in roundSteps)
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

    /// <summary>V2-only: major/external zones plus deduped internal 4H/1H swings.</summary>
    internal static List<Zone> BuildV2Zones(
        List<Ohlcv> bars4hNewestFirst,
        List<MarketIntradayBarRow> bars1hNewestFirst,
        List<MarketBarRow> dailyNewestFirst,
        decimal refPrice,
        DateOnly asOfDate)
    {
        var opts = ZoneOptions.V2;
        var zones = BuildZones(bars4hNewestFirst, dailyNewestFirst, refPrice, opts, asOfDate);

        // Internal 4H fractals on a deeper window, excluding levels already near external/major.
        var deep4h = FindSwings(bars4hNewestFirst, lookback: 24);
        AddInternalIfNovel(zones, deep4h.Highs, "internal_high_4h", support: false, opts.EqualTolPct);
        AddInternalIfNovel(zones, deep4h.Lows, "internal_low_4h", support: true, opts.EqualTolPct);

        // Internal 1H fractals (~3 sessions).
        var bars1hOhlcv = bars1hNewestFirst
            .Take(24)
            .Select(b => new Ohlcv(b.BarTime, b.Open, b.High, b.Low, b.Close, b.Volume))
            .ToList();
        var deep1h = FindSwings(bars1hOhlcv, lookback: 24);
        AddInternalIfNovel(zones, deep1h.Highs, "internal_high_1h", support: false, opts.EqualTolPct);
        AddInternalIfNovel(zones, deep1h.Lows, "internal_low_1h", support: true, opts.EqualTolPct);

        return zones
            .GroupBy(z => (z.Type, Math.Round(z.Price, 2)))
            .Select(g => g.OrderBy(z => z.Priority).First())
            .ToList();
    }

    private static void AddInternalIfNovel(
        List<Zone> zones, List<decimal> levels, string type, bool support, decimal equalTolPct)
    {
        foreach (var level in levels.Where(p => p > 0))
        {
            var nearExisting = zones.Any(z =>
                z.Price > 0 && Math.Abs(z.Price - level) / ((z.Price + level) / 2m) <= equalTolPct);
            if (nearExisting)
                continue;
            zones.Add(new Zone(type, level, ZonePriority(type), support, !support));
        }
    }

    /// <summary>Same-side zone clusters (midpoint + outer band). Support and resistance separately.</summary>
    internal static List<ZoneCluster> BuildClusters(List<Zone> zones, decimal clusterTolPct = 0.004m)
    {
        var clusters = new List<ZoneCluster>();
        foreach (var support in new[] { true, false })
        {
            var pool = zones
                .Where(z => support ? z.IsSupportLike : z.IsResistanceLike)
                .OrderBy(z => z.Price)
                .ToList();
            if (pool.Count < 2)
                continue;

            var used = new bool[pool.Count];
            for (var i = 0; i < pool.Count; i++)
            {
                if (used[i]) continue;
                var members = new List<Zone> { pool[i] };
                used[i] = true;
                for (var j = i + 1; j < pool.Count; j++)
                {
                    if (used[j]) continue;
                    var mid = (pool[i].Price + pool[j].Price) / 2m;
                    if (mid <= 0) continue;
                    if (Math.Abs(pool[i].Price - pool[j].Price) / mid <= clusterTolPct)
                    {
                        // Also require near any current member of the growing cluster.
                        var near = members.Any(m =>
                            Math.Abs(m.Price - pool[j].Price) / ((m.Price + pool[j].Price) / 2m) <= clusterTolPct);
                        if (!near) continue;
                        members.Add(pool[j]);
                        used[j] = true;
                    }
                }

                if (members.Count < 2)
                    continue;

                var lo = members.Min(m => m.Price);
                var hi = members.Max(m => m.Price);
                clusters.Add(new ZoneCluster(
                    support ? SignalSides.Buy : SignalSides.Sell,
                    (lo + hi) / 2m,
                    lo,
                    hi,
                    members.Count,
                    members.Select(m => m.Type).Distinct().ToArray()));
            }
        }

        return clusters;
    }

    private static void AddEqualLevels(
        List<Zone> zones, List<decimal> levels, string type, bool support, decimal equalTolPct)
    {
        for (var i = 0; i < levels.Count; i++)
        for (var j = i + 1; j < levels.Count; j++)
        {
            var a = levels[i];
            var b = levels[j];
            if (a <= 0 || b <= 0)
                continue;
            if (Math.Abs(a - b) / ((a + b) / 2m) <= equalTolPct)
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

    internal static List<decimal> PickStructureTargets(string side, decimal entry, decimal sl, List<Zone> zones)
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

    internal static Zone? NearestZone(decimal price, List<Zone> zones)
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

    internal static decimal RoundPrice(decimal price) =>
        Math.Round(price, 2, MidpointRounding.AwayFromZero);

    public readonly record struct Ohlcv(
        DateTimeOffset BarTime, decimal Open, decimal High, decimal Low, decimal Close, long Volume);
}
