using StockYouNeed.Application.TradeScore;
using StockYouNeed.Domain;

namespace StockYouNeed.Application.Services;

/// <summary>
/// Liquidity V2 — scored liquidity-event framework (external / internal / cluster /
/// delayed reclaim / multi-sweep). Does not alter classic or fresh behavior.
/// </summary>
public static class LiquidityV2Evaluator
{
    public const string EventExternalSweep = "external_sweep";
    public const string EventInternalLiquidity = "internal_liquidity";
    public const string EventLiquidityCluster = "liquidity_cluster";
    public const string EventDelayedReclaim = "delayed_reclaim";
    public const string EventMultiSweep = "multi_sweep";

    private const decimal ImminentMargin = 0.01m;
    private const decimal TargetMinDistancePct = 0.002m;
    private const decimal RetestTolPct = 0.003m;
    private const decimal GapRejectPct = 0.02m;
    private const decimal ClusterTolPct = 0.004m;
    private const decimal TouchTolPct = 0.0025m;
    /// <summary>Reject when structural stop is farther than this from entry (no invented mid-price SL).</summary>
    private const decimal MaxStopPct = 0.05m;
    private const int ConfirmWindow = 15;
    private const int SweepLookback4h = 12;
    private const int MultiLookback4h = 12;
    private const int MultiLookback1h = 24;
    private const int MinDailyBars = 35;
    private const int VolSmaPeriod = 20;
    private const int DisplacementLookback = 20;
    private const decimal EmaToleranceAtrMult = 0.25m;
    private const decimal RvolHardFloor = 1.0m;
    public const int MinQualityScore = 50;

    private static readonly TimeSpan Ist = TimeSpan.FromHours(5.5);

    public sealed record Options(
        bool RequireRetest = false,
        bool RequireRelativeStrength = false,
        /// <summary>
        /// Live scanner: keep setups you can still take (near entry, T1 open).
        /// Not the same as strict pre-break imminent-only.
        /// </summary>
        bool ActionableOnly = false);

    public sealed class Diagnostics
    {
        private readonly Dictionary<string, int> _rejects = new();
        private readonly Dictionary<string, int> _funnel = new()
        {
            ["scanned"] = 0,
            ["has_data"] = 0,
            ["has_event"] = 0,
            ["ema_trend_ok"] = 0,
            ["structure_ok"] = 0,
            ["confirm_ok"] = 0,
            ["score_ok"] = 0,
            ["saved"] = 0,
        };
        private readonly Dictionary<string, int> _eventCandidates = new();
        private readonly Dictionary<string, int> _eventSaved = new();

        public IReadOnlyDictionary<string, int> Counts => _rejects;
        public IReadOnlyDictionary<string, int> Funnel => _funnel;
        public IReadOnlyDictionary<string, int> EventCandidates => _eventCandidates;
        public IReadOnlyDictionary<string, int> EventSaved => _eventSaved;

        public void Pass(string stage) =>
            _funnel[stage] = _funnel.TryGetValue(stage, out var n) ? n + 1 : 1;

        public void Reject(string gate) =>
            _rejects[gate] = _rejects.TryGetValue(gate, out var n) ? n + 1 : 1;

        public void Candidate(string eventType) =>
            _eventCandidates[eventType] = _eventCandidates.TryGetValue(eventType, out var n) ? n + 1 : 1;

        public void SavedEvent(string eventType) =>
            _eventSaved[eventType] = _eventSaved.TryGetValue(eventType, out var n) ? n + 1 : 1;

        public string DescribeRejects() =>
            _rejects.Count == 0
                ? "(none)"
                : string.Join(", ", _rejects.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}={kv.Value}"));

        public string DescribeFunnel() =>
            string.Join(" → ", new[]
            {
                "scanned", "has_data", "has_event", "ema_trend_ok",
                "structure_ok", "confirm_ok", "score_ok", "saved"
            }.Select(s => $"{s}={(_funnel.TryGetValue(s, out var n) ? n : 0)}"));

        public string DescribeEvents() =>
            _eventCandidates.Count == 0
                ? "(none)"
                : string.Join(", ", _eventCandidates.OrderByDescending(kv => kv.Value)
                    .Select(kv =>
                    {
                        var saved = _eventSaved.TryGetValue(kv.Key, out var s) ? s : 0;
                        return $"{kv.Key}:cand={kv.Value}/saved={saved}";
                    }));
    }

    public static LiquiditySignalRow? TryEvaluate(
        Guid userId,
        Guid runId,
        DateOnly asOf,
        AngelTokenRow token,
        List<MarketIntradayBarRow> bars1hNewestFirst,
        List<LiquidityAnalysisService.Ohlcv> bars4hNewestFirst,
        List<MarketBarRow> dailyNewestFirst,
        decimal? livePrice,
        bool sectorConfirmed,
        IReadOnlyList<MarketBarRow>? niftyDailyNewestFirst,
        Options? options = null,
        Diagnostics? diag = null)
    {
        options ??= new Options();

        if (bars1hNewestFirst.Count < 45 || bars4hNewestFirst.Count < 8)
        {
            diag?.Reject("few_intraday_bars");
            return null;
        }
        if (dailyNewestFirst.Count < MinDailyBars)
        {
            diag?.Reject("few_daily_bars");
            return null;
        }

        var dailyChron = dailyNewestFirst.OrderBy(b => b.TradeDate).ToList();
        var atr = TechnicalIndicators.Atr(dailyChron, 14);
        var ema20 = TechnicalIndicators.Ema(dailyChron, 20);
        if (atr is null or <= 0 || ema20 is null)
        {
            diag?.Reject("atr_or_ema_null");
            return null;
        }

        diag?.Pass("has_data");

        var markPrice = livePrice is > 0
            ? livePrice.Value
            : bars1hNewestFirst[0].Close;
        var emaTolerance = EmaToleranceAtrMult * atr.Value;

        var events = DetectEvents(bars1hNewestFirst, bars4hNewestFirst, dailyNewestFirst);
        foreach (var e in events)
            diag?.Candidate(e.EventType);

        var evt = PreferEvent(events);
        if (evt is null)
        {
            diag?.Reject("no_liquidity_event");
            return null;
        }

        diag?.Pass("has_event");

        if (!PassesTrendFilter(evt.Side, markPrice, ema20.Value, emaTolerance))
        {
            diag?.Reject("sweep_against_ema20");
            return null;
        }

        diag?.Pass("ema_trend_ok");

        if (!Has4hStructure(bars4hNewestFirst, evt.Side, evt.SweepTime))
        {
            diag?.Reject("no_4h_structure");
            return null;
        }

        diag?.Pass("structure_ok");

        var minDepth = Math.Max(0.2m * atr.Value, 0.0025m * markPrice);
        if (evt.Depth < minDepth)
        {
            diag?.Reject("sweep_too_shallow");
            return null;
        }

        var depthPct = markPrice > 0 ? evt.Depth / markPrice : 0m;
        var sweepStrength = depthPct < 0.003m ? "Weak"
            : depthPct < 0.008m ? "Medium"
            : "Strong";
        if (evt.SweepCount >= 2)
            sweepStrength = $"Multi:{evt.SweepCount}";

        string? barGate = null;
        var reachedConfirm = false;

        for (var i = 0; i < Math.Min(ConfirmWindow, bars1hNewestFirst.Count - 2); i++)
        {
            // Live: ignore stale confirms from many bars ago.
            if (options.ActionableOnly && i > 3)
                break;

            var bar = bars1hNewestFirst[i];
            if (bar.BarTime < evt.EventTime)
                continue;

            var prev1h = bars1hNewestFirst.Skip(i + 1).Take(2).ToList();
            if (prev1h.Count < 2)
                continue;

            var last2High = prev1h.Max(b => b.High);
            var last2Low = prev1h.Min(b => b.Low);
            var price = i == 0 && livePrice is > 0 ? livePrice.Value : bar.Close;

            var buyBreak = bar.High > last2High;
            var sellBreak = bar.Low < last2Low;
            var buyImminent = !buyBreak && price >= last2High * (1m - ImminentMargin) && price < last2High;
            var sellImminent = !sellBreak && price <= last2Low * (1m + ImminentMargin) && price > last2Low;

            string? side = null;
            if (evt.Side == SignalSides.Buy && (buyBreak || buyImminent))
                side = SignalSides.Buy;
            else if (evt.Side == SignalSides.Sell && (sellBreak || sellImminent))
                side = SignalSides.Sell;
            if (side is null)
            {
                barGate ??= "bar_no_break_or_imminent";
                continue;
            }

            if (!PassesTrendFilter(side, price, ema20.Value, emaTolerance))
            {
                barGate = "bar_against_ema20";
                continue;
            }

            var (rvol, rvolPctile, isVolumePeak) = ComputeRvol(bars1hNewestFirst, i);
            if (rvol < RvolHardFloor)
            {
                barGate = "bar_rvol_below_floor";
                continue;
            }

            if (HasGapOnConfirmDay(bar, dailyNewestFirst, bars1hNewestFirst))
            {
                barGate = "bar_gap";
                continue;
            }

            var breakLevel = side == SignalSides.Buy ? last2High : last2Low;

            if (options.RequireRetest)
            {
                if (!HasRetestThenBounce(bars1hNewestFirst, i, side, breakLevel, evt.SweepTime))
                {
                    barGate = "bar_no_retest";
                    continue;
                }
            }

            if (options.RequireRelativeStrength)
            {
                if (!PassesRelativeStrength(side, dailyNewestFirst, niftyDailyNewestFirst, asOf))
                {
                    barGate = "bar_no_relative_strength";
                    continue;
                }
            }

            var entry = breakLevel;
            var asOfBar = DateOnly.FromDateTime(bar.BarTime.ToOffset(Ist).DateTime);
            var zones = LiquidityAnalysisService.BuildV2Zones(
                bars4hNewestFirst, bars1hNewestFirst, dailyNewestFirst, entry, asOfBar);

            // Structural stop from sweep, optionally tightened to a nearer support/resistance zone.
            var sl = PickV2Stop(side, entry, evt, zones);
            var risk = Math.Abs(entry - sl);
            if (risk <= 0)
            {
                barGate = "bar_zero_risk";
                continue;
            }
            if (risk / entry > MaxStopPct)
            {
                barGate = "bar_stop_too_wide";
                continue;
            }

            if (!reachedConfirm)
            {
                reachedConfirm = true;
                diag?.Pass("confirm_ok");
            }

            // Zone / structure levels only — never invent R-multiple targets.
            var (t1, t2, t3) = PickV2Targets(side, entry, zones);

            // Roll past spent T1 so remaining structure targets stay actionable.
            (t1, t2, t3) = RollPastSpentTargets(side, t1, t2, t3, markPrice, bars1hNewestFirst, i);
            // Skip micro targets (<0.4%) — promote to next real level.
            while (t1 is decimal micro
                   && entry > 0
                   && Math.Abs(micro - entry) / entry < 0.004m)
            {
                (t1, t2, t3) = (t2, t3, null);
            }
            if (t1 is null)
            {
                barGate = "bar_no_zone_target";
                continue;
            }

            var rewardToT1 = Math.Abs(t1.Value - entry);

            // Live scanner: still near entry with T1 open (not a move that already ran).
            if (options.ActionableOnly
                && !LiquidityAnalysisService.IsLiveEntryStillOpen(side, entry, t1.Value, markPrice))
            {
                barGate = "bar_entry_already_extended";
                continue;
            }

            var nearest = LiquidityAnalysisService.NearestZone(price, zones);

            var closePos = ClosePositionPct(bar);
            var displaceMult = DisplacementMultiple(bars1hNewestFirst, i);
            var plannedRr = risk > 0 ? rewardToT1 / risk : 0m;
            var trendAligned = side == SignalSides.Buy
                ? price > ema20.Value
                : price < ema20.Value;

            var (score, grade, reasons) = ScoreSignal(
                evt,
                rvol,
                closePos,
                displaceMult,
                isVolumePeak,
                sectorConfirmed,
                trendAligned,
                plannedRr,
                side);

            if (score < MinQualityScore)
            {
                barGate = "bar_score_below_floor";
                continue;
            }

            diag?.Pass("score_ok");

            var closeScore = ClosePositionScore(side, closePos);
            var tags = evt.ZoneTags.Length > 0
                ? evt.ZoneTags.Concat(zones.Select(z => z.Type)).Distinct().Take(16).ToArray()
                : zones.Select(z => z.Type).Distinct().Take(12).ToArray();

            return new LiquiditySignalRow
            {
                Id = Guid.NewGuid(),
                LiquidityRunId = runId,
                UserId = userId,
                InstrumentId = token.InstrumentId,
                AppSymbol = token.AppSymbol,
                Side = side,
                AsOfDate = asOf,
                EntryPrice = LiquidityAnalysisService.RoundPrice(entry),
                InitialStopLoss = LiquidityAnalysisService.RoundPrice(sl),
                TargetT1 = t1 is decimal a ? LiquidityAnalysisService.RoundPrice(a) : null,
                TargetT2 = t2 is decimal b ? LiquidityAnalysisService.RoundPrice(b) : null,
                TargetT3 = t3 is decimal c ? LiquidityAnalysisService.RoundPrice(c) : null,
                RelativeVolume = LiquidityAnalysisService.RoundPrice(rvol),
                RvolPercentile = Math.Round((decimal)rvolPctile, 4),
                RvolOk = rvol >= RvolHardFloor,
                StrongClose = closeScore >= 10,
                SectorConfirmed = sectorConfirmed,
                SweepSide = side,
                SweptZoneType = evt.ZoneType,
                SweptZonePrice = LiquidityAnalysisService.RoundPrice(evt.ZonePrice),
                NearestZoneType = nearest?.Type,
                NearestZonePrice = nearest is null
                    ? null
                    : LiquidityAnalysisService.RoundPrice(nearest.Price),
                DistancePct = nearest is null || price == 0
                    ? null
                    : Math.Round(Math.Abs(price - nearest.Price) / price, 6),
                ZoneTags = tags,
                TimeframeContext = "liq_v2:" + evt.EventType,
                EventType = evt.EventType,
                QualityScore = score,
                ConfidenceRating = grade,
                SweepStrength = sweepStrength,
                Atr14 = atr,
                ScoreReasons = reasons,
            };
        }

        diag?.Reject(barGate ?? "no_confirm_bar_after_event");
        return null;
    }

    internal static List<LiquidityAnalysisService.LiquidityEvent> DetectEvents(
        List<MarketIntradayBarRow> bars1hNewestFirst,
        List<LiquidityAnalysisService.Ohlcv> bars4hNewestFirst,
        List<MarketBarRow> dailyNewestFirst)
    {
        var events = new List<LiquidityAnalysisService.LiquidityEvent>();
        var limit = Math.Min(SweepLookback4h, bars4hNewestFirst.Count);

        for (var ci = 0; ci < limit; ci++)
        {
            var candle = bars4hNewestFirst[ci];
            var prior = bars4hNewestFirst.Skip(ci + 1).ToList();
            var asOf = DateOnly.FromDateTime(candle.BarTime.ToOffset(Ist).DateTime);
            var zones = LiquidityAnalysisService.BuildV2Zones(
                prior, bars1hNewestFirst, dailyNewestFirst, candle.Close, asOf);
            var clusters = LiquidityAnalysisService.BuildClusters(zones, ClusterTolPct);

            // Same-bar reclaim on individual zones.
            foreach (var z in zones)
            {
                if (TrySameBarReclaim(candle, z, out var same))
                {
                    var eventType = IsInternal(z.Type) ? EventInternalLiquidity : EventExternalSweep;
                    events.Add(same with { EventType = eventType });
                }

                if (TryDelayedReclaim(bars4hNewestFirst, ci, z, out var delayed))
                    events.Add(delayed);
            }

            // Cluster sweep/reclaim.
            foreach (var cluster in clusters)
            {
                if (TrySameBarCluster(candle, cluster, out var sameCluster))
                    events.Add(sameCluster);
                if (TryDelayedCluster(bars4hNewestFirst, ci, cluster, out var delayedCluster))
                    events.Add(delayedCluster);
            }

            // Multi-sweep against major/external + clusters.
            foreach (var z in zones.Where(z => !IsInternal(z.Type) || z.Type.Contains("4h")))
            {
                if (TryMultiSweep4h(bars4hNewestFirst, ci, z, out var multi))
                    events.Add(multi);
            }
            foreach (var cluster in clusters)
            {
                var proxy = new LiquidityAnalysisService.Zone(
                    "cluster", cluster.MidPrice, 2,
                    cluster.Side == SignalSides.Buy,
                    cluster.Side == SignalSides.Sell);
                if (TryMultiSweep4h(bars4hNewestFirst, ci, proxy, out var multiCluster, cluster.MemberCount, cluster.MemberTypes))
                    events.Add(multiCluster with { EventType = EventMultiSweep, ZoneType = "cluster" });
            }
        }

        // Internal 1H same-bar / delayed / multi on recent bars.
        var look1h = Math.Min(8, bars1hNewestFirst.Count);
        for (var i = 0; i < look1h; i++)
        {
            var bar = bars1hNewestFirst[i];
            var asOf = DateOnly.FromDateTime(bar.BarTime.ToOffset(Ist).DateTime);
            var prior1h = bars1hNewestFirst.Skip(i + 1).ToList();
            var zones = LiquidityAnalysisService.BuildV2Zones(
                bars4hNewestFirst, prior1h, dailyNewestFirst, bar.Close, asOf);
            foreach (var z in zones.Where(z => z.Type is "internal_high_1h" or "internal_low_1h"))
            {
                var ohlcv = new LiquidityAnalysisService.Ohlcv(
                    bar.BarTime, bar.Open, bar.High, bar.Low, bar.Close, bar.Volume);
                if (TrySameBarReclaim(ohlcv, z, out var same))
                    events.Add(same with { EventType = EventInternalLiquidity });

                if (TryDelayedReclaim1h(bars1hNewestFirst, i, z, out var delayed))
                    events.Add(delayed);

                if (TryMultiSweep1h(bars1hNewestFirst, i, z, out var multi))
                    events.Add(multi);
            }
        }

        return events;
    }

    private static bool IsInternal(string zoneType) =>
        zoneType.StartsWith("internal_", StringComparison.Ordinal);

    private static bool TrySameBarReclaim(
        LiquidityAnalysisService.Ohlcv candle,
        LiquidityAnalysisService.Zone z,
        out LiquidityAnalysisService.LiquidityEvent evt)
    {
        evt = default!;
        if (z.IsSupportLike && candle.Low < z.Price && candle.Close > z.Price)
        {
            evt = MakeEvent(
                EventExternalSweep, SignalSides.Buy, z.Type, z.Price,
                candle.High, candle.Low, candle.BarTime, candle.BarTime,
                sweepCount: 1, clusterSize: 1, new[] { z.Type },
                Math.Max(0m, z.Price - candle.Low));
            return true;
        }
        if (z.IsResistanceLike && candle.High > z.Price && candle.Close < z.Price)
        {
            evt = MakeEvent(
                EventExternalSweep, SignalSides.Sell, z.Type, z.Price,
                candle.High, candle.Low, candle.BarTime, candle.BarTime,
                sweepCount: 1, clusterSize: 1, new[] { z.Type },
                Math.Max(0m, candle.High - z.Price));
            return true;
        }
        return false;
    }

    internal static bool TryDelayedReclaim(
        List<LiquidityAnalysisService.Ohlcv> bars4hNewestFirst,
        int sweepIdx,
        LiquidityAnalysisService.Zone z,
        out LiquidityAnalysisService.LiquidityEvent evt)
    {
        evt = default!;
        var sweep = bars4hNewestFirst[sweepIdx];
        // Newer bars are lower indices.
        var maxNext = Math.Min(2, sweepIdx);
        if (maxNext < 1)
            return false;

        if (z.IsSupportLike)
        {
            if (!(sweep.Low < z.Price && sweep.Close < z.Price))
                return false;
            for (var k = 1; k <= maxNext; k++)
            {
                var reclaim = bars4hNewestFirst[sweepIdx - k];
                if (reclaim.Close > z.Price)
                {
                    evt = MakeEvent(
                        EventDelayedReclaim, SignalSides.Buy, z.Type, z.Price,
                        Math.Max(sweep.High, reclaim.High), Math.Min(sweep.Low, reclaim.Low),
                        reclaim.BarTime, sweep.BarTime,
                        1, 1, new[] { z.Type },
                        Math.Max(0m, z.Price - sweep.Low));
                    return true;
                }
            }
        }
        else if (z.IsResistanceLike)
        {
            if (!(sweep.High > z.Price && sweep.Close > z.Price))
                return false;
            for (var k = 1; k <= maxNext; k++)
            {
                var reclaim = bars4hNewestFirst[sweepIdx - k];
                if (reclaim.Close < z.Price)
                {
                    evt = MakeEvent(
                        EventDelayedReclaim, SignalSides.Sell, z.Type, z.Price,
                        Math.Max(sweep.High, reclaim.High), Math.Min(sweep.Low, reclaim.Low),
                        reclaim.BarTime, sweep.BarTime,
                        1, 1, new[] { z.Type },
                        Math.Max(0m, sweep.High - z.Price));
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryDelayedReclaim1h(
        List<MarketIntradayBarRow> bars1hNewestFirst,
        int sweepIdx,
        LiquidityAnalysisService.Zone z,
        out LiquidityAnalysisService.LiquidityEvent evt)
    {
        evt = default!;
        var sweep = bars1hNewestFirst[sweepIdx];
        var maxNext = Math.Min(2, sweepIdx);
        if (maxNext < 1)
            return false;

        if (z.IsSupportLike)
        {
            if (!(sweep.Low < z.Price && sweep.Close < z.Price))
                return false;
            for (var k = 1; k <= maxNext; k++)
            {
                var reclaim = bars1hNewestFirst[sweepIdx - k];
                if (reclaim.Close > z.Price)
                {
                    evt = MakeEvent(
                        EventDelayedReclaim, SignalSides.Buy, z.Type, z.Price,
                        Math.Max(sweep.High, reclaim.High), Math.Min(sweep.Low, reclaim.Low),
                        reclaim.BarTime, sweep.BarTime,
                        1, 1, new[] { z.Type },
                        Math.Max(0m, z.Price - sweep.Low));
                    return true;
                }
            }
        }
        else if (z.IsResistanceLike)
        {
            if (!(sweep.High > z.Price && sweep.Close > z.Price))
                return false;
            for (var k = 1; k <= maxNext; k++)
            {
                var reclaim = bars1hNewestFirst[sweepIdx - k];
                if (reclaim.Close < z.Price)
                {
                    evt = MakeEvent(
                        EventDelayedReclaim, SignalSides.Sell, z.Type, z.Price,
                        Math.Max(sweep.High, reclaim.High), Math.Min(sweep.Low, reclaim.Low),
                        reclaim.BarTime, sweep.BarTime,
                        1, 1, new[] { z.Type },
                        Math.Max(0m, sweep.High - z.Price));
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TrySameBarCluster(
        LiquidityAnalysisService.Ohlcv candle,
        LiquidityAnalysisService.ZoneCluster cluster,
        out LiquidityAnalysisService.LiquidityEvent evt)
    {
        evt = default!;
        if (cluster.Side == SignalSides.Buy
            && candle.Low < cluster.Low && candle.Close > cluster.Low)
        {
            evt = MakeEvent(
                EventLiquidityCluster, SignalSides.Buy, "cluster", cluster.MidPrice,
                candle.High, candle.Low, candle.BarTime, candle.BarTime,
                1, cluster.MemberCount, cluster.MemberTypes,
                Math.Max(0m, cluster.Low - candle.Low));
            return true;
        }
        if (cluster.Side == SignalSides.Sell
            && candle.High > cluster.High && candle.Close < cluster.High)
        {
            evt = MakeEvent(
                EventLiquidityCluster, SignalSides.Sell, "cluster", cluster.MidPrice,
                candle.High, candle.Low, candle.BarTime, candle.BarTime,
                1, cluster.MemberCount, cluster.MemberTypes,
                Math.Max(0m, candle.High - cluster.High));
            return true;
        }
        return false;
    }

    private static bool TryDelayedCluster(
        List<LiquidityAnalysisService.Ohlcv> bars4hNewestFirst,
        int sweepIdx,
        LiquidityAnalysisService.ZoneCluster cluster,
        out LiquidityAnalysisService.LiquidityEvent evt)
    {
        evt = default!;
        var sweep = bars4hNewestFirst[sweepIdx];
        var maxNext = Math.Min(2, sweepIdx);
        if (maxNext < 1)
            return false;

        if (cluster.Side == SignalSides.Buy)
        {
            if (!(sweep.Low < cluster.Low && sweep.Close < cluster.Low))
                return false;
            for (var k = 1; k <= maxNext; k++)
            {
                var reclaim = bars4hNewestFirst[sweepIdx - k];
                if (reclaim.Close > cluster.Low)
                {
                    evt = MakeEvent(
                        EventLiquidityCluster, SignalSides.Buy, "cluster", cluster.MidPrice,
                        Math.Max(sweep.High, reclaim.High), Math.Min(sweep.Low, reclaim.Low),
                        reclaim.BarTime, sweep.BarTime,
                        1, cluster.MemberCount, cluster.MemberTypes,
                        Math.Max(0m, cluster.Low - sweep.Low));
                    return true;
                }
            }
        }
        else
        {
            if (!(sweep.High > cluster.High && sweep.Close > cluster.High))
                return false;
            for (var k = 1; k <= maxNext; k++)
            {
                var reclaim = bars4hNewestFirst[sweepIdx - k];
                if (reclaim.Close < cluster.High)
                {
                    evt = MakeEvent(
                        EventLiquidityCluster, SignalSides.Sell, "cluster", cluster.MidPrice,
                        Math.Max(sweep.High, reclaim.High), Math.Min(sweep.Low, reclaim.Low),
                        reclaim.BarTime, sweep.BarTime,
                        1, cluster.MemberCount, cluster.MemberTypes,
                        Math.Max(0m, sweep.High - cluster.High));
                    return true;
                }
            }
        }

        return false;
    }

    internal static bool TryMultiSweep4h(
        List<LiquidityAnalysisService.Ohlcv> bars4hNewestFirst,
        int latestIdx,
        LiquidityAnalysisService.Zone z,
        out LiquidityAnalysisService.LiquidityEvent evt,
        int clusterSize = 1,
        string[]? tags = null)
    {
        evt = default!;
        var window = Math.Min(MultiLookback4h, bars4hNewestFirst.Count - latestIdx);
        if (window < 3)
            return false;

        var touches = new List<int>();
        var inTouch = false;
        // Walk oldest→newest within the window ending at latestIdx.
        for (var offset = window - 1; offset >= 0; offset--)
        {
            var idx = latestIdx + offset;
            if (idx >= bars4hNewestFirst.Count) continue;
            var c = bars4hNewestFirst[idx];
            var beyond = z.IsSupportLike
                ? c.Low < z.Price * (1m - TouchTolPct)
                : c.High > z.Price * (1m + TouchTolPct);
            var reclaimed = z.IsSupportLike
                ? c.Close > z.Price
                : c.Close < z.Price;

            if (beyond && reclaimed)
            {
                if (!inTouch)
                {
                    touches.Add(idx);
                    inTouch = true;
                }
                else
                {
                    // Keep the newest bar of a consecutive beyond+reclaim streak.
                    touches[^1] = idx;
                }
            }
            else if (!beyond)
            {
                inTouch = false;
            }
        }

        if (touches.Count < 2)
            return false;

        // Latest touch must be the candle under evaluation.
        if (touches[^1] != latestIdx)
            return false;

        var latest = bars4hNewestFirst[latestIdx];
        var depth = z.IsSupportLike
            ? Math.Max(0m, z.Price - latest.Low)
            : Math.Max(0m, latest.High - z.Price);
        var side = z.IsSupportLike ? SignalSides.Buy : SignalSides.Sell;
        evt = MakeEvent(
            EventMultiSweep, side, z.Type, z.Price,
            latest.High, latest.Low, latest.BarTime, latest.BarTime,
            touches.Count, clusterSize, tags ?? new[] { z.Type }, depth);
        return true;
    }

    private static bool TryMultiSweep1h(
        List<MarketIntradayBarRow> bars1hNewestFirst,
        int latestIdx,
        LiquidityAnalysisService.Zone z,
        out LiquidityAnalysisService.LiquidityEvent evt)
    {
        evt = default!;
        var window = Math.Min(MultiLookback1h, bars1hNewestFirst.Count - latestIdx);
        if (window < 3)
            return false;

        var touches = new List<int>();
        var inTouch = false;
        for (var offset = window - 1; offset >= 0; offset--)
        {
            var idx = latestIdx + offset;
            if (idx >= bars1hNewestFirst.Count) continue;
            var c = bars1hNewestFirst[idx];
            var beyond = z.IsSupportLike
                ? c.Low < z.Price * (1m - TouchTolPct)
                : c.High > z.Price * (1m + TouchTolPct);
            var reclaimed = z.IsSupportLike
                ? c.Close > z.Price
                : c.Close < z.Price;

            if (beyond && reclaimed)
            {
                if (!inTouch)
                {
                    touches.Add(idx);
                    inTouch = true;
                }
                else
                {
                    touches[^1] = idx;
                }
            }
            else if (!beyond)
            {
                inTouch = false;
            }
        }

        if (touches.Count < 2 || touches[^1] != latestIdx)
            return false;

        var latest = bars1hNewestFirst[latestIdx];
        var depth = z.IsSupportLike
            ? Math.Max(0m, z.Price - latest.Low)
            : Math.Max(0m, latest.High - z.Price);
        var side = z.IsSupportLike ? SignalSides.Buy : SignalSides.Sell;
        evt = MakeEvent(
            EventMultiSweep, side, z.Type, z.Price,
            latest.High, latest.Low, latest.BarTime, latest.BarTime,
            touches.Count, 1, new[] { z.Type }, depth);
        return true;
    }

    private static LiquidityAnalysisService.LiquidityEvent MakeEvent(
        string eventType, string side, string zoneType, decimal zonePrice,
        decimal high, decimal low, DateTimeOffset eventTime, DateTimeOffset sweepTime,
        int sweepCount, int clusterSize, string[] tags, decimal depth) =>
        new(eventType, side, zoneType, zonePrice, high, low, eventTime, sweepTime,
            sweepCount, clusterSize, tags, depth);

    internal static LiquidityAnalysisService.LiquidityEvent? PreferEvent(
        List<LiquidityAnalysisService.LiquidityEvent> events)
    {
        if (events.Count == 0)
            return null;

        return events
            .OrderBy(e => LiquidityAnalysisService.ZonePriority(e.ZoneType))
            .ThenBy(e => EventPriority(e.EventType))
            .ThenByDescending(e => e.EventTime)
            .ThenByDescending(e => e.Depth)
            .First();
    }

    private static int EventPriority(string eventType) => eventType switch
    {
        EventMultiSweep => 0,
        EventLiquidityCluster => 1,
        EventDelayedReclaim => 2,
        EventExternalSweep => 3,
        EventInternalLiquidity => 4,
        _ => 5
    };

    private static bool PassesTrendFilter(string side, decimal price, decimal ema20, decimal tolerance) =>
        side == SignalSides.Buy
            ? price >= ema20 - tolerance
            : price <= ema20 + tolerance;

    private static bool Has4hStructure(
        List<LiquidityAnalysisService.Ohlcv> bars4hNewestFirst,
        string side,
        DateTimeOffset sweepTime)
    {
        var prior = bars4hNewestFirst
            .Where(b => b.BarTime < sweepTime)
            .Take(12)
            .Reverse()
            .ToList();
        if (prior.Count < 6)
            return false;

        var highs = new List<(int Idx, decimal Price)>();
        var lows = new List<(int Idx, decimal Price)>();
        for (var i = 1; i < prior.Count - 1; i++)
        {
            if (prior[i].High >= prior[i - 1].High && prior[i].High >= prior[i + 1].High)
                highs.Add((i, prior[i].High));
            if (prior[i].Low <= prior[i - 1].Low && prior[i].Low <= prior[i + 1].Low)
                lows.Add((i, prior[i].Low));
        }

        if (highs.Count < 2 || lows.Count < 2)
        {
            var mid = prior.Count / 2;
            var first = prior.Take(mid).ToList();
            var second = prior.Skip(mid).ToList();
            if (first.Count == 0 || second.Count == 0)
                return false;
            if (side == SignalSides.Buy)
                return second.Max(b => b.High) > first.Max(b => b.High)
                    && second.Min(b => b.Low) > first.Min(b => b.Low);
            return second.Max(b => b.High) < first.Max(b => b.High)
                && second.Min(b => b.Low) < first.Min(b => b.Low);
        }

        var h1 = highs[^2].Price;
        var h2 = highs[^1].Price;
        var l1 = lows[^2].Price;
        var l2 = lows[^1].Price;

        return side == SignalSides.Buy
            ? h2 > h1 && l2 > l1
            : h2 < h1 && l2 < l1;
    }

    private static decimal ClosePositionPct(MarketIntradayBarRow bar)
    {
        var range = bar.High - bar.Low;
        if (range <= 0) return 0.5m;
        return (bar.Close - bar.Low) / range;
    }

    private static decimal DisplacementMultiple(List<MarketIntradayBarRow> barsNewestFirst, int barIndex)
    {
        if (barsNewestFirst.Count < barIndex + DisplacementLookback + 1)
            return 0m;
        var bar = barsNewestFirst[barIndex];
        var body = Math.Abs(bar.Close - bar.Open);
        var prior = barsNewestFirst.Skip(barIndex + 1).Take(DisplacementLookback).ToList();
        if (prior.Count < DisplacementLookback)
            return 0m;
        var avgBody = prior.Average(b => Math.Abs(b.Close - b.Open));
        if (avgBody <= 0)
            return body > 0 ? 99m : 0m;
        return body / avgBody;
    }

    private static (decimal rvol, double percentile, bool isVolumePeak) ComputeRvol(
        List<MarketIntradayBarRow> barsNewestFirst, int barIndex)
    {
        if (barsNewestFirst.Count < barIndex + VolSmaPeriod + 1)
            return (0, 0, false);

        var bar = barsNewestFirst[barIndex];
        var window = barsNewestFirst.Skip(barIndex + 1).Take(VolSmaPeriod).ToList();
        if (window.Count < VolSmaPeriod)
            return (0, 0, false);

        var sma = window.Average(b => (decimal)b.Volume);
        if (sma <= 0)
            return (0, 0, false);

        var rvol = bar.Volume / sma;
        var last5 = barsNewestFirst.Skip(barIndex).Take(5).ToList();
        var isPeak = last5.Count >= 1 && bar.Volume >= last5.Max(b => b.Volume);

        var history = new List<double>();
        var maxI = Math.Min(50, barsNewestFirst.Count - VolSmaPeriod - 1);
        for (var i = 0; i <= maxI; i++)
        {
            var w = barsNewestFirst.Skip(i + 1).Take(VolSmaPeriod).ToList();
            if (w.Count < VolSmaPeriod) continue;
            var a = w.Average(b => (double)b.Volume);
            if (a <= 0) continue;
            history.Add(barsNewestFirst[i].Volume / a);
        }

        double pctile = 0;
        if (history.Count >= 10)
        {
            history.Sort();
            var rank = history.Count(h => h <= (double)rvol);
            pctile = rank / (double)history.Count;
        }

        return (rvol, pctile, isPeak);
    }

    private static bool HasGapOnConfirmDay(
        MarketIntradayBarRow confirmBar,
        List<MarketBarRow> dailyNewestFirst,
        List<MarketIntradayBarRow> bars1hNewestFirst)
    {
        var confirmDay = DateOnly.FromDateTime(confirmBar.BarTime.ToOffset(Ist).DateTime);
        var dayBar = dailyNewestFirst.FirstOrDefault(d => d.TradeDate == confirmDay);
        var priorDay = dailyNewestFirst.FirstOrDefault(d => d.TradeDate < confirmDay);
        if (dayBar is not null && priorDay is not null && priorDay.Close > 0)
        {
            var gap = Math.Abs(dayBar.Open - priorDay.Close) / priorDay.Close;
            return gap > GapRejectPct;
        }

        if (priorDay is null || priorDay.Close <= 0)
            return false;

        var sessionOpen = bars1hNewestFirst
            .Where(b => DateOnly.FromDateTime(b.BarTime.ToOffset(Ist).DateTime) == confirmDay)
            .OrderBy(b => b.BarTime)
            .FirstOrDefault();
        if (sessionOpen is null)
            return false;

        var gap1h = Math.Abs(sessionOpen.Open - priorDay.Close) / priorDay.Close;
        return gap1h > GapRejectPct;
    }

    private static bool HasRetestThenBounce(
        List<MarketIntradayBarRow> barsNewestFirst,
        int confirmIndex,
        string side,
        decimal breakLevel,
        DateTimeOffset sweepTime)
    {
        var candidates = new List<(int Idx, MarketIntradayBarRow Bar)>();
        for (var j = confirmIndex + 1; j < barsNewestFirst.Count; j++)
        {
            var b = barsNewestFirst[j];
            if (b.BarTime < sweepTime)
                break;
            candidates.Add((j, b));
        }

        int? breakIdx = null;
        foreach (var (idx, b) in candidates.AsEnumerable().Reverse())
        {
            var broke = side == SignalSides.Buy
                ? b.High > breakLevel
                : b.Low < breakLevel;
            if (broke)
            {
                breakIdx = idx;
                break;
            }
        }

        if (breakIdx is null)
            return false;

        for (var j = breakIdx.Value - 1; j > confirmIndex; j--)
        {
            var b = barsNewestFirst[j];
            var touched = Math.Abs(b.Low - breakLevel) / breakLevel <= RetestTolPct
                || Math.Abs(b.High - breakLevel) / breakLevel <= RetestTolPct
                || (b.Low <= breakLevel && b.High >= breakLevel);
            if (!touched)
                continue;

            var bounce = side == SignalSides.Buy
                ? b.Close > breakLevel && b.Close > b.Open
                : b.Close < breakLevel && b.Close < b.Open;
            if (bounce)
                return true;
        }

        return false;
    }

    private static bool PassesRelativeStrength(
        string side,
        List<MarketBarRow> stockDailyNewestFirst,
        IReadOnlyList<MarketBarRow>? niftyDailyNewestFirst,
        DateOnly asOf)
    {
        if (niftyDailyNewestFirst is null || niftyDailyNewestFirst.Count < 2)
            return false;

        static (decimal? Pct, DateOnly? Day) DayPct(IReadOnlyList<MarketBarRow> bars, DateOnly asOf)
        {
            var ordered = bars.OrderByDescending(b => b.TradeDate).ToList();
            var today = ordered.FirstOrDefault(b => b.TradeDate <= asOf);
            if (today is null) return (null, null);
            var prev = ordered.FirstOrDefault(b => b.TradeDate < today.TradeDate);
            if (prev is null || prev.Close <= 0) return (null, null);
            return ((today.Close - prev.Close) / prev.Close, today.TradeDate);
        }

        var (stockPct, stockDay) = DayPct(stockDailyNewestFirst, asOf);
        var (niftyPct, niftyDay) = DayPct(niftyDailyNewestFirst, asOf);
        if (stockPct is null || niftyPct is null || stockDay is null || niftyDay is null)
            return false;
        if (stockDay != niftyDay)
            return false;

        return side == SignalSides.Buy
            ? stockPct > niftyPct
            : stockPct < niftyPct;
    }

    /// <summary>
    /// Stop at sweep invalidation, tightened to the nearest opposing zone when available.
    /// Never invents a mid-price ATR stop.
    /// </summary>
    internal static decimal PickV2Stop(
        string side,
        decimal entry,
        LiquidityAnalysisService.LiquidityEvent evt,
        List<LiquidityAnalysisService.Zone> zones)
    {
        var structural = side == SignalSides.Buy
            ? Math.Min(evt.CandleLow, evt.ZonePrice) * 0.999m
            : Math.Max(evt.CandleHigh, evt.ZonePrice) * 1.001m;

        if (side == SignalSides.Buy)
        {
            // Nearest support below entry but still above the deep sweep (tighter invalidation).
            var tighter = zones
                .Where(z => z.IsSupportLike
                            && z.Price < entry * (1m - TargetMinDistancePct)
                            && z.Price > structural)
                .Select(z => z.Price * 0.999m)
                .OrderByDescending(p => p)
                .FirstOrDefault();
            var sl = tighter > 0 ? tighter : structural;
            if (sl >= entry)
                sl = entry * 0.995m;
            return sl;
        }
        else
        {
            var tighter = zones
                .Where(z => z.IsResistanceLike
                            && z.Price > entry * (1m + TargetMinDistancePct)
                            && z.Price < structural)
                .Select(z => z.Price * 1.001m)
                .OrderBy(p => p)
                .FirstOrDefault();
            var sl = tighter > 0 ? tighter : structural;
            if (sl <= entry)
                sl = entry * 1.005m;
            return sl;
        }
    }

    /// <summary>
    /// Targets from liquidity/structure zones/levels only (same picker as Classic).
    /// Missing slots stay blank — no R-multiple or geometric fill-ins.
    /// </summary>
    internal static (decimal? T1, decimal? T2, decimal? T3) PickV2Targets(
        string side,
        decimal entry,
        List<LiquidityAnalysisService.Zone> zones)
    {
        if (entry <= 0)
            return (null, null, null);

        var levels = LiquidityAnalysisService.PickStructureTargets(side, entry, entry, zones);
        return (
            levels.Count > 0 ? levels[0] : null,
            levels.Count > 1 ? levels[1] : null,
            levels.Count > 2 ? levels[2] : null);
    }

    /// <summary>
    /// Promote T2→T1 when live/post-confirm price already tagged T1.
    /// </summary>
    internal static (decimal? T1, decimal? T2, decimal? T3) RollPastSpentTargets(
        string side,
        decimal? t1,
        decimal? t2,
        decimal? t3,
        decimal markPrice,
        List<MarketIntradayBarRow> bars1hNewestFirst,
        int confirmIdx)
    {
        var queue = new List<decimal>();
        if (t1 is decimal a) queue.Add(a);
        if (t2 is decimal b) queue.Add(b);
        if (t3 is decimal c) queue.Add(c);

        while (queue.Count > 0
               && IsTargetAlreadyTagged(side, queue[0], markPrice, bars1hNewestFirst, confirmIdx))
            queue.RemoveAt(0);

        return (
            queue.Count > 0 ? queue[0] : null,
            queue.Count > 1 ? queue[1] : null,
            queue.Count > 2 ? queue[2] : null);
    }

    /// <summary>
    /// True when live mark or any bar after the confirm bar has already tagged the target.
    /// Signal/confirm bar wick alone does not spend the target (price may still be actionable).
    /// </summary>
    internal static bool IsTargetAlreadyTagged(
        string side,
        decimal target,
        decimal markPrice,
        List<MarketIntradayBarRow> bars1hNewestFirst,
        int confirmIdx)
    {
        if (side == SignalSides.Buy)
        {
            if (markPrice >= target)
                return true;
        }
        else
        {
            if (markPrice <= target)
                return true;
        }

        // Bars strictly after confirmation (newer = lower index).
        var end = Math.Min(confirmIdx, bars1hNewestFirst.Count);
        for (var j = 0; j < end; j++)
        {
            var newer = bars1hNewestFirst[j];
            if (side == SignalSides.Buy && newer.High >= target)
                return true;
            if (side == SignalSides.Sell && newer.Low <= target)
                return true;
        }

        return false;
    }

    private static int ClosePositionScore(string side, decimal closePos)
    {
        if (side == SignalSides.Buy)
        {
            if (closePos > 0.90m) return 15;
            if (closePos > 0.80m) return 12;
            if (closePos > 0.70m) return 8;
            if (closePos >= 0.60m) return 4;
            return 0;
        }

        if (closePos < 0.10m) return 15;
        if (closePos < 0.20m) return 12;
        if (closePos < 0.30m) return 8;
        if (closePos <= 0.40m) return 4;
        return 0;
    }

    private static int RvolScore(decimal rvol)
    {
        if (rvol > 2m) return 15;
        if (rvol > 1.5m) return 12;
        if (rvol > 1.2m) return 8;
        if (rvol >= 1.0m) return 4;
        return 0;
    }

    private static int DisplacementScore(decimal mult)
    {
        if (mult > 1.5m) return 8;
        if (mult > 1.2m) return 4;
        return 0;
    }

    internal static int EventScore(string eventType) => eventType switch
    {
        EventMultiSweep => 20,
        EventLiquidityCluster => 15,
        EventDelayedReclaim => 12,
        EventExternalSweep => 10,
        EventInternalLiquidity => 6,
        _ => 0
    };

    private static int ZoneScore(string zoneType)
    {
        var zt = zoneType.ToLowerInvariant();
        if (zt is "equal_high" or "equal_low") return 20;
        if (zt.StartsWith("swing")) return 15;
        if (zt is "pdh" or "pdl") return 12;
        if (zt is "pwh" or "pwl") return 8;
        if (zt.StartsWith("internal_")) return 8;
        if (zt is "round" or "cluster") return 4;
        return 0;
    }

    internal static (int Score, string Grade, string[] Reasons) ScoreSignal(
        LiquidityAnalysisService.LiquidityEvent evt,
        decimal rvol,
        decimal closePos,
        decimal displaceMult,
        bool isVolumePeak,
        bool sectorConfirmed,
        bool trendAligned,
        decimal plannedRr,
        string side)
    {
        var score = 0;
        var reasons = new List<string>();

        var ePts = EventScore(evt.EventType);
        if (ePts > 0)
        {
            score += ePts;
            reasons.Add($"{evt.EventType} +{ePts}");
        }

        var zPts = ZoneScore(evt.ZoneType);
        if (zPts > 0)
        {
            score += zPts;
            reasons.Add($"{evt.ZoneType} +{zPts}");
        }

        var rPts = RvolScore(rvol);
        if (rPts > 0)
        {
            score += rPts;
            reasons.Add($"RVOL +{rPts}");
        }

        if (isVolumePeak)
        {
            score += 5;
            reasons.Add("vol peak +5");
        }

        var cPts = ClosePositionScore(side, closePos);
        if (cPts > 0)
        {
            score += cPts;
            reasons.Add($"close pos +{cPts}");
        }

        var dPts = DisplacementScore(displaceMult);
        if (dPts > 0)
        {
            score += dPts;
            reasons.Add($"displace +{dPts}");
        }

        if (sectorConfirmed)
        {
            score += 10;
            reasons.Add("sector +10");
        }

        if (trendAligned)
        {
            score += 12;
            reasons.Add("trend EMA20 +12");
        }

        if (plannedRr > 3m)
        {
            score += 10;
            reasons.Add("R:R>3 +10");
        }

        if (evt.ClusterSize >= 3)
        {
            score += 5;
            reasons.Add("cluster≥3 +5");
        }

        if (evt.SweepCount >= 3)
        {
            score += 5;
            reasons.Add("sweeps≥3 +5");
        }

        var grade = score >= 92 ? "A+"
            : score >= 84 ? "A"
            : score >= 72 ? "B"
            : score >= 58 ? "C"
            : "D";

        return (score, grade, reasons.ToArray());
    }
}
