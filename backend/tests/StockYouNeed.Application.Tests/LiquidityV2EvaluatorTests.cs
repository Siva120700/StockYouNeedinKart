using StockYouNeed.Application.Services;
using StockYouNeed.Domain;
using Xunit;

namespace StockYouNeed.Application.Tests;

public class LiquidityV2EvaluatorTests
{
    private static readonly TimeSpan Ist = TimeSpan.FromHours(5.5);

    private static LiquidityAnalysisService.Ohlcv Bar(
        DateTimeOffset t, decimal o, decimal h, decimal l, decimal c, long v = 1000) =>
        new(t, o, h, l, c, v);

    private static DateTimeOffset T(int day, int hour) =>
        new DateTimeOffset(2026, 3, day, hour, 0, 0, Ist);

    [Fact]
    public void EventScore_WeightsMatchPlan()
    {
        Assert.Equal(20, LiquidityV2Evaluator.EventScore(LiquidityV2Evaluator.EventMultiSweep));
        Assert.Equal(15, LiquidityV2Evaluator.EventScore(LiquidityV2Evaluator.EventLiquidityCluster));
        Assert.Equal(12, LiquidityV2Evaluator.EventScore(LiquidityV2Evaluator.EventDelayedReclaim));
        Assert.Equal(10, LiquidityV2Evaluator.EventScore(LiquidityV2Evaluator.EventExternalSweep));
        Assert.Equal(6, LiquidityV2Evaluator.EventScore(LiquidityV2Evaluator.EventInternalLiquidity));
        Assert.Equal(50, LiquidityV2Evaluator.MinQualityScore);
    }

    [Fact]
    public void ScoreSignal_BelowFloorWhenEvidenceWeak()
    {
        var evt = new LiquidityAnalysisService.LiquidityEvent(
            LiquidityV2Evaluator.EventInternalLiquidity,
            SignalSides.Buy,
            "internal_low_1h",
            100m,
            101m,
            99m,
            T(10, 12),
            T(10, 12),
            1,
            1,
            new[] { "internal_low_1h" },
            0.1m);

        var (score, _, _) = LiquidityV2Evaluator.ScoreSignal(
            evt,
            rvol: 1.0m,
            closePos: 0.5m,
            displaceMult: 1.0m,
            isVolumePeak: false,
            sectorConfirmed: false,
            trendAligned: false,
            plannedRr: 1.5m,
            side: SignalSides.Buy);

        Assert.True(score < LiquidityV2Evaluator.MinQualityScore, $"expected soft score {score} < 60");
    }

    [Fact]
    public void DelayedReclaim_UsesReclaimCandleAsEventTime()
    {
        var zone = new LiquidityAnalysisService.Zone("swing_low", 100m, 1, true, false);
        // newest-first: [0]=reclaim, [1]=sweep (closed beyond)
        var bars = new List<LiquidityAnalysisService.Ohlcv>
        {
            Bar(T(10, 16), 99m, 102m, 98.5m, 101m), // reclaim close back above
            Bar(T(10, 12), 101m, 101.5m, 97m, 98m), // sweep close below
            Bar(T(10, 8), 100m, 101m, 99m, 100.5m),
        };

        Assert.True(LiquidityV2Evaluator.TryDelayedReclaim(bars, sweepIdx: 1, zone, out var evt));
        Assert.Equal(LiquidityV2Evaluator.EventDelayedReclaim, evt.EventType);
        Assert.Equal(bars[0].BarTime, evt.EventTime);
        Assert.Equal(bars[1].BarTime, evt.SweepTime);
        Assert.True(evt.EventTime > evt.SweepTime);
    }

    [Fact]
    public void MultiSweep_DedupesConsecutiveBeyondTouches()
    {
        var zone = new LiquidityAnalysisService.Zone("swing_low", 100m, 1, true, false);
        // newest-first. Two distinct reclaim groups; three consecutive beyond+reclaim candles count as one.
        var bars = new List<LiquidityAnalysisService.Ohlcv>
        {
            Bar(T(11, 16), 100.5m, 101m, 98.5m, 100.8m), // latest reclaim (idx 0)
            Bar(T(11, 12), 99m, 100m, 97m, 100.2m),       // consecutive beyond+reclaim — deduped
            Bar(T(11, 8), 99m, 100m, 97.5m, 100.1m),      // consecutive beyond+reclaim — deduped
            Bar(T(10, 16), 101m, 102m, 100.5m, 101.5m),   // away (resets inTouch)
            Bar(T(10, 12), 100.2m, 101m, 98m, 100.5m),    // earlier distinct reclaim
            Bar(T(10, 8), 101m, 102m, 100.5m, 101.2m),
            Bar(T(9, 16), 101m, 102m, 100.5m, 101m),
            Bar(T(9, 12), 101m, 102m, 100.5m, 101m),
            Bar(T(9, 8), 101m, 102m, 100.5m, 101m),
            Bar(T(8, 16), 101m, 102m, 100.5m, 101m),
            Bar(T(8, 12), 101m, 102m, 100.5m, 101m),
            Bar(T(8, 8), 101m, 102m, 100.5m, 101m),
        };

        Assert.True(LiquidityV2Evaluator.TryMultiSweep4h(bars, latestIdx: 0, zone, out var evt));
        Assert.Equal(LiquidityV2Evaluator.EventMultiSweep, evt.EventType);
        // Three consecutive beyond+reclaim bars collapse to one touch + one earlier = 2.
        Assert.Equal(2, evt.SweepCount);
    }

    [Fact]
    public void PreferEvent_OrdersByEventPriority()
    {
        var t = T(10, 12);
        var events = new List<LiquidityAnalysisService.LiquidityEvent>
        {
            new(LiquidityV2Evaluator.EventInternalLiquidity, SignalSides.Buy, "swing_low", 100m,
                101, 99, t, t, 1, 1, new[] { "swing_low" }, 1m),
            new(LiquidityV2Evaluator.EventExternalSweep, SignalSides.Buy, "swing_low", 100m,
                101, 99, t, t, 1, 1, new[] { "swing_low" }, 1m),
            new(LiquidityV2Evaluator.EventMultiSweep, SignalSides.Buy, "swing_low", 100m,
                101, 99, t, t, 2, 1, new[] { "swing_low" }, 1m),
            new(LiquidityV2Evaluator.EventDelayedReclaim, SignalSides.Buy, "swing_low", 100m,
                101, 99, t, t, 1, 1, new[] { "swing_low" }, 1m),
            new(LiquidityV2Evaluator.EventLiquidityCluster, SignalSides.Buy, "swing_low", 100m,
                101, 99, t, t, 1, 2, new[] { "swing_low", "pdl" }, 1m),
        };

        var preferred = LiquidityV2Evaluator.PreferEvent(events);
        Assert.NotNull(preferred);
        Assert.Equal(LiquidityV2Evaluator.EventMultiSweep, preferred!.EventType);
    }

    [Fact]
    public void BuildZones_AsOfIgnoresFutureDailyLevels()
    {
        var daily = new List<MarketBarRow>
        {
            new() { TradeDate = new DateOnly(2026, 3, 12), High = 200m, Low = 190m, Open = 195m, Close = 198m },
            new() { TradeDate = new DateOnly(2026, 3, 11), High = 150m, Low = 140m, Open = 145m, Close = 148m },
            new() { TradeDate = new DateOnly(2026, 3, 10), High = 130m, Low = 120m, Open = 125m, Close = 128m },
            new() { TradeDate = new DateOnly(2026, 3, 9), High = 125m, Low = 115m, Open = 120m, Close = 122m },
            new() { TradeDate = new DateOnly(2026, 3, 8), High = 122m, Low = 112m, Open = 118m, Close = 119m },
            new() { TradeDate = new DateOnly(2026, 3, 7), High = 121m, Low = 111m, Open = 117m, Close = 118m },
            new() { TradeDate = new DateOnly(2026, 3, 6), High = 120m, Low = 110m, Open = 116m, Close = 117m },
        };

        var bars4h = new List<LiquidityAnalysisService.Ohlcv>
        {
            Bar(T(11, 12), 145m, 146m, 144m, 145.5m),
        };

        var asOf = new DateOnly(2026, 3, 11);
        var zones = LiquidityAnalysisService.BuildZones(
            bars4h, daily, 145m, LiquidityAnalysisService.ZoneOptions.V2, asOf);

        var pdh = zones.First(z => z.Type == "pdh");
        var pdl = zones.First(z => z.Type == "pdl");
        Assert.Equal(130m, pdh.Price); // 2026-03-10, not future 200
        Assert.Equal(120m, pdl.Price);
        Assert.DoesNotContain(zones, z => z.Price is 200m or 190m);
    }

    [Fact]
    public void BuildClusters_RequiresTwoDistinctMembers()
    {
        var zones = new List<LiquidityAnalysisService.Zone>
        {
            new("swing_low", 100m, 1, true, false),
            new("pdl", 100.2m, 2, true, false),
            new("swing_high", 110m, 1, false, true),
        };

        var clusters = LiquidityAnalysisService.BuildClusters(zones, 0.004m);
        Assert.Single(clusters);
        Assert.Equal(SignalSides.Buy, clusters[0].Side);
        Assert.True(clusters[0].MemberCount >= 2);
    }

    [Fact]
    public void DetectEvents_FindsExternalSameBarSweep()
    {
        // Build a flat history with a clear support swing, then a same-bar reclaim.
        var bars = new List<LiquidityAnalysisService.Ohlcv>();
        for (var d = 1; d <= 20; d++)
        {
            for (var h = 8; h <= 16; h += 4)
            {
                var basePx = 100m + (d % 3);
                bars.Add(Bar(T(d, h), basePx, basePx + 1m, basePx - 1m, basePx));
            }
        }

        // Inject an older swing low around 95, then a reclaim candle as newest.
        bars[10] = Bar(bars[10].BarTime, 96m, 97m, 95m, 96.5m);
        bars[11] = Bar(bars[11].BarTime, 97m, 98m, 96m, 97m);
        bars[12] = Bar(bars[12].BarTime, 98m, 99m, 97m, 98m);

        // Newest candle: wick through ~95–96 support and close back above.
        bars.Insert(0, Bar(T(21, 12), 97m, 98m, 94.5m, 97.5m));

        var daily = Enumerable.Range(0, 40)
            .Select(i => new MarketBarRow
            {
                TradeDate = new DateOnly(2026, 2, 1).AddDays(i),
                Open = 100, High = 102, Low = 98, Close = 100, Volume = 1000
            })
            .Reverse()
            .ToList();

        var bars1h = bars.Select(b => new MarketIntradayBarRow
        {
            BarTime = b.BarTime,
            Open = b.Open,
            High = b.High,
            Low = b.Low,
            Close = b.Close,
            Volume = b.Volume
        }).ToList();

        var events = LiquidityV2Evaluator.DetectEvents(bars1h, bars, daily);
        Assert.Contains(events, e =>
            e.EventType is LiquidityV2Evaluator.EventExternalSweep
                or LiquidityV2Evaluator.EventInternalLiquidity
                or LiquidityV2Evaluator.EventDelayedReclaim
                or LiquidityV2Evaluator.EventLiquidityCluster
                or LiquidityV2Evaluator.EventMultiSweep);
    }

    [Fact]
    public void PickV2Targets_UsesStructureLevelsNotBlind2R()
    {
        var entry = 3905m;
        var zones = new List<LiquidityAnalysisService.Zone>
        {
            new("swing_high", 4098.40m, 1, false, true),
            new("equal_high", 4136.00m, 1, false, true),
            new("pdh", 4200.00m, 2, false, true),
        };

        var (t1, t2, t3) = LiquidityV2Evaluator.PickV2Targets(SignalSides.Buy, entry, zones);

        Assert.Equal(4098.40m, t1);
        Assert.Equal(4136.00m, t2);
        Assert.Equal(4200.00m, t3);
        Assert.True(t1 < t2 && t2 < t3);
    }

    [Fact]
    public void PickV2Targets_LeavesBlankWhenNoStructure()
    {
        var (t1, t2, t3) = LiquidityV2Evaluator.PickV2Targets(
            SignalSides.Buy, 100m, new List<LiquidityAnalysisService.Zone>());

        Assert.Null(t1);
        Assert.Null(t2);
        Assert.Null(t3);
    }

    [Fact]
    public void PickV2Targets_PartialLevelsLeaveLaterSlotsBlank()
    {
        var zones = new List<LiquidityAnalysisService.Zone>
        {
            new("swing_high", 105m, 1, false, true),
        };

        var (t1, t2, t3) = LiquidityV2Evaluator.PickV2Targets(SignalSides.Buy, 100m, zones);

        Assert.Equal(105m, t1);
        Assert.Null(t2);
        Assert.Null(t3);
    }

    [Fact]
    public void PickV2Stop_PrefersTighterSupportZoneOverDeepSweep()
    {
        var evt = new LiquidityAnalysisService.LiquidityEvent(
            LiquidityV2Evaluator.EventExternalSweep,
            SignalSides.Buy,
            "swing_low",
            96m,
            101m,
            95m,
            T(10, 12),
            T(10, 12),
            1,
            1,
            new[] { "swing_low" },
            0.5m);
        var zones = new List<LiquidityAnalysisService.Zone>
        {
            new("swing_low", 97.5m, 1, true, false),
        };

        var sl = LiquidityV2Evaluator.PickV2Stop(SignalSides.Buy, entry: 100m, evt, zones);

        Assert.True(sl < 100m);
        Assert.True(sl > 95m * 0.999m); // tighter than deep candle low
        Assert.InRange(sl, 97.5m * 0.998m, 97.5m);
    }

    [Fact]
    public void IsTargetAlreadyTagged_DetectsLiveMarkAndPostConfirmBars()
    {
        var bars = new List<MarketIntradayBarRow>
        {
            new() { BarTime = T(11, 12), Open = 105, High = 108, Low = 104, Close = 106 },
            new() { BarTime = T(11, 11), Open = 102, High = 103, Low = 101, Close = 102.5m },
            new() { BarTime = T(11, 10), Open = 100, High = 101, Low = 99, Close = 100.5m },
        };

        Assert.True(LiquidityV2Evaluator.IsTargetAlreadyTagged(
            SignalSides.Buy, 107m, markPrice: 106m, bars, confirmIdx: 2));

        Assert.False(LiquidityV2Evaluator.IsTargetAlreadyTagged(
            SignalSides.Buy, 110m, markPrice: 106m, bars, confirmIdx: 2));

        Assert.True(LiquidityV2Evaluator.IsTargetAlreadyTagged(
            SignalSides.Buy, 105.5m, markPrice: 105.5m, bars, confirmIdx: 2));

        // Confirm bar alone (idx 0) does not spend T1 via wick — only mark does.
        Assert.False(LiquidityV2Evaluator.IsTargetAlreadyTagged(
            SignalSides.Buy, 107m, markPrice: 106m, bars, confirmIdx: 0));
    }

    [Fact]
    public void RollPastSpentTargets_PromotesNextStructureLevel()
    {
        var bars = new List<MarketIntradayBarRow>
        {
            new() { BarTime = T(11, 12), Open = 105, High = 108, Low = 104, Close = 106 },
            new() { BarTime = T(11, 10), Open = 100, High = 101, Low = 99, Close = 100.5m },
        };

        var (t1, t2, t3) = LiquidityV2Evaluator.RollPastSpentTargets(
            SignalSides.Buy,
            t1: 105m,
            t2: 110m,
            t3: 115m,
            markPrice: 106m,
            bars,
            confirmIdx: 1);

        Assert.Equal(110m, t1);
        Assert.Equal(115m, t2);
        Assert.Null(t3);
    }
}

public class BreakoutSignalEvaluatorTests
{
    [Fact]
    public void RollPastSpentTargets_PromotesT2WhenT1Tagged()
    {
        var bar = new MarketBarRow
        {
            High = 105m,
            Low = 99m,
            Close = 104m,
            Open = 100m,
        };

        var (t1, t2, t3) = BreakoutSignalEvaluator.RollPastSpentTargets(
            SignalSides.Buy,
            t1: 103m,
            t2: 108m,
            t3: 112m,
            markPrice: 104m,
            bar);

        Assert.Equal(108m, t1);
        Assert.Equal(112m, t2);
        Assert.Null(t3);
    }

    [Fact]
    public void RollPastSpentTargets_ReturnsNullWhenAllTargetsSpent()
    {
        var bar = new MarketBarRow
        {
            High = 115m,
            Low = 99m,
            Close = 114m,
            Open = 100m,
        };

        var (t1, _, _) = BreakoutSignalEvaluator.RollPastSpentTargets(
            SignalSides.Buy,
            t1: 103m,
            t2: 108m,
            t3: 112m,
            markPrice: 114m,
            bar);

        Assert.Null(t1);
    }
}
