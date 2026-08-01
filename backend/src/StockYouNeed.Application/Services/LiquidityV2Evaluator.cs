using StockYouNeed.Application.TradeScore;
using StockYouNeed.Domain;

namespace StockYouNeed.Application.Services;

/// <summary>
/// Liquidity V2 ruleset — stricter filters + quality score for A/B vs classic/fresh.
/// Does not alter classic or fresh behavior.
/// </summary>
public static class LiquidityV2Evaluator
{
    private const decimal ImminentMargin = 0.005m;
    private const decimal TargetMinDistancePct = 0.002m;
    private const decimal RetestTolPct = 0.003m;
    private const decimal DensityTolPct = 0.003m;
    private const decimal GapRejectPct = 0.02m;
    private const int ConfirmWindow = 8;
    private const int MinDailyBars = 60;
    private const int VolSmaPeriod = 20;
    private const int DisplacementLookback = 20;

    public sealed record Options(bool RequireRetest = false, bool RequireRelativeStrength = false);

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
        Options? options = null)
    {
        options ??= new Options();

        if (bars1hNewestFirst.Count < 45 || bars4hNewestFirst.Count < 8)
            return null;
        if (dailyNewestFirst.Count < MinDailyBars)
            return null;

        var dailyChron = dailyNewestFirst.OrderBy(b => b.TradeDate).ToList();
        var atr = TechnicalIndicators.Atr(dailyChron, 14);
        var ema20 = TechnicalIndicators.Ema(dailyChron, 20);
        if (atr is null or <= 0 || ema20 is null)
            return null;

        var markPrice = livePrice is > 0
            ? livePrice.Value
            : bars1hNewestFirst[0].Close;

        // #2 HTF trend filter (hard)
        if (markPrice > ema20.Value)
        {
            // buy-only path allowed later
        }
        else if (markPrice < ema20.Value)
        {
            // sell-only path allowed later
        }
        else
            return null;

        var sweep = LiquidityAnalysisService.Detect4hSweep(bars4hNewestFirst, dailyNewestFirst, maxBars: 4);
        if (sweep is null)
            return null;

        // Align sweep side with HTF
        if (sweep.Side == SignalSides.Buy && markPrice <= ema20.Value)
            return null;
        if (sweep.Side == SignalSides.Sell && markPrice >= ema20.Value)
            return null;

        // #7 4H trend structure
        if (!Has4hStructure(bars4hNewestFirst, sweep.Side))
            return null;

        var sweepDepth = SweepDepth(sweep);
        var depthPct = markPrice > 0 ? sweepDepth / markPrice : 0m;

        // #4 Reject tiny sweeps
        var minDepth = Math.Max(0.2m * atr.Value, 0.0025m * markPrice);
        if (sweepDepth < minDepth)
            return null;

        // #5 SweepStrength
        var sweepStrength = depthPct < 0.003m ? "Weak"
            : depthPct < 0.008m ? "Medium"
            : "Strong";

        for (var i = 0; i < Math.Min(ConfirmWindow, bars1hNewestFirst.Count - 2); i++)
        {
            var bar = bars1hNewestFirst[i];
            if (bar.BarTime < sweep.BarTime)
                continue;

            var prev1h = bars1hNewestFirst.Skip(i + 1).Take(2).ToList();
            if (prev1h.Count < 2)
                continue;

            var last2High = prev1h.Max(b => b.High);
            var last2Low = prev1h.Min(b => b.Low);
            var price = i == 0 && livePrice is > 0 ? livePrice.Value : bar.Close;

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

            // Re-check HTF with side
            if (side == SignalSides.Buy && price <= ema20.Value)
                continue;
            if (side == SignalSides.Sell && price >= ema20.Value)
                continue;

            // #6 Strong close: top/bottom 30% AND body/range > 60%
            if (!IsStrongCloseV2(bar, side))
                continue;

            // #8 Displacement after reclaim
            if (!HasDisplacement(bars1hNewestFirst, i))
                continue;

            // #9 Volume gates
            var (rvol, rvolPctile, volOk) = ComputeVolumeGates(bars1hNewestFirst, i);
            if (!volOk)
                continue;

            // #10 Gap filter
            if (HasGapOnConfirmDay(bar, dailyNewestFirst, bars1hNewestFirst))
                continue;

            var breakLevel = side == SignalSides.Buy ? last2High : last2Low;

            // #11 Optional retest
            if (options.RequireRetest)
            {
                if (!HasRetestThenBounce(bars1hNewestFirst, i, side, breakLevel, sweep.BarTime))
                    continue;
            }

            // #15 Optional relative strength vs Nifty
            if (options.RequireRelativeStrength)
            {
                if (!PassesRelativeStrength(side, dailyNewestFirst, niftyDailyNewestFirst, asOf))
                    continue;
            }

            var entry = breakLevel;
            // #3 ATR(14) stop
            var sl = side == SignalSides.Buy
                ? Math.Min(sweep.CandleLow, sweep.ZonePrice) - 0.5m * atr.Value
                : Math.Max(sweep.CandleHigh, sweep.ZonePrice) + 0.5m * atr.Value;

            if (side == SignalSides.Buy && sl >= entry)
                sl = entry - Math.Max(0.5m * atr.Value, entry * 0.005m);
            if (side == SignalSides.Sell && sl <= entry)
                sl = entry + Math.Max(0.5m * atr.Value, entry * 0.005m);

            var risk = Math.Abs(entry - sl);
            if (risk <= 0)
                continue;

            var zones = LiquidityAnalysisService.BuildZones(bars4hNewestFirst, dailyNewestFirst, entry);
            // #16 Volume Profile (POC/VAH/VAL) — not implemented; stub only for V2.
            var (t1, t2, t3) = PickV2Targets(side, entry, risk, zones);

            var nearest = LiquidityAnalysisService.NearestZone(price, zones);
            var densityBonus = CountLiquidityDensity(zones, sweep.ZonePrice, sweep.ZoneType) >= 2;

            var closePos = ClosePositionPct(bar);
            var plannedRr = t1 is decimal t1v
                ? Math.Abs(t1v - entry) / risk
                : 0m;
            var trendAligned = side == SignalSides.Buy
                ? price > ema20.Value
                : price < ema20.Value;

            var (score, grade, reasons) = ScoreSignal(
                sweep.ZoneType,
                rvol,
                closePos,
                sectorConfirmed,
                trendAligned,
                plannedRr,
                densityBonus,
                side);

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
                RvolOk = true,
                StrongClose = true,
                SectorConfirmed = sectorConfirmed,
                SweepSide = side,
                SweptZoneType = sweep.ZoneType,
                SweptZonePrice = LiquidityAnalysisService.RoundPrice(sweep.ZonePrice),
                NearestZoneType = nearest?.Type,
                NearestZonePrice = nearest is null
                    ? null
                    : LiquidityAnalysisService.RoundPrice(nearest.Price),
                DistancePct = nearest is null || price == 0
                    ? null
                    : Math.Round(Math.Abs(price - nearest.Price) / price, 6),
                ZoneTags = zones.Select(z => z.Type).Distinct().Take(12).ToArray(),
                TimeframeContext = "4h_sweep+1h_confirm_v2",
                QualityScore = score,
                ConfidenceRating = grade,
                SweepStrength = sweepStrength,
                Atr14 = atr,
                ScoreReasons = reasons,
            };
        }

        return null;
    }

    private static decimal SweepDepth(LiquidityAnalysisService.SweepResult sweep) =>
        sweep.Side == SignalSides.Buy
            ? Math.Max(0m, sweep.ZonePrice - sweep.CandleLow)
            : Math.Max(0m, sweep.CandleHigh - sweep.ZonePrice);

    private static bool Has4hStructure(
        List<LiquidityAnalysisService.Ohlcv> bars4hNewestFirst, string side)
    {
        var chron = bars4hNewestFirst.Take(12).Reverse().ToList();
        if (chron.Count < 6)
            return false;

        var highs = new List<(int Idx, decimal Price)>();
        var lows = new List<(int Idx, decimal Price)>();
        for (var i = 1; i < chron.Count - 1; i++)
        {
            if (chron[i].High >= chron[i - 1].High && chron[i].High >= chron[i + 1].High)
                highs.Add((i, chron[i].High));
            if (chron[i].Low <= chron[i - 1].Low && chron[i].Low <= chron[i + 1].Low)
                lows.Add((i, chron[i].Low));
        }

        if (highs.Count < 2 || lows.Count < 2)
        {
            // Fallback: compare first vs second half of window
            var mid = chron.Count / 2;
            var first = chron.Take(mid).ToList();
            var second = chron.Skip(mid).ToList();
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

    private static bool IsStrongCloseV2(MarketIntradayBarRow bar, string side)
    {
        var range = bar.High - bar.Low;
        if (range <= 0)
            return false;
        var pos = (bar.Close - bar.Low) / range;
        var body = Math.Abs(bar.Close - bar.Open);
        if (body / range <= 0.60m)
            return false;
        return side == SignalSides.Buy
            ? pos >= 0.70m
            : pos <= 0.30m;
    }

    private static decimal ClosePositionPct(MarketIntradayBarRow bar)
    {
        var range = bar.High - bar.Low;
        if (range <= 0) return 0.5m;
        return (bar.Close - bar.Low) / range;
    }

    private static bool HasDisplacement(List<MarketIntradayBarRow> barsNewestFirst, int barIndex)
    {
        if (barsNewestFirst.Count < barIndex + DisplacementLookback + 1)
            return false;
        var bar = barsNewestFirst[barIndex];
        var body = Math.Abs(bar.Close - bar.Open);
        var prior = barsNewestFirst.Skip(barIndex + 1).Take(DisplacementLookback).ToList();
        if (prior.Count < DisplacementLookback)
            return false;
        var avgBody = prior.Average(b => Math.Abs(b.Close - b.Open));
        if (avgBody <= 0)
            return body > 0;
        return body > 1.5m * avgBody;
    }

    private static (decimal rvol, double percentile, bool ok) ComputeVolumeGates(
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
        if (bar.Volume <= sma || rvol <= 1.5m)
            return (rvol, 0, false);

        var last5 = barsNewestFirst.Skip(barIndex).Take(5).ToList();
        if (last5.Count < 5 || bar.Volume < last5.Max(b => b.Volume))
            return (rvol, 0, false);

        // Percentile vs recent RVOL history (informational / scoring adjacent)
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

        return (rvol, pctile, true);
    }

    private static bool HasGapOnConfirmDay(
        MarketIntradayBarRow confirmBar,
        List<MarketBarRow> dailyNewestFirst,
        List<MarketIntradayBarRow> bars1hNewestFirst)
    {
        var ist = TimeSpan.FromHours(5.5);
        var confirmDay = DateOnly.FromDateTime(confirmBar.BarTime.ToOffset(ist).DateTime);

        // Prefer daily open vs prior close
        var dayBar = dailyNewestFirst.FirstOrDefault(d => d.TradeDate == confirmDay);
        var priorDay = dailyNewestFirst.FirstOrDefault(d => d.TradeDate < confirmDay);
        if (dayBar is not null && priorDay is not null && priorDay.Close > 0)
        {
            var gap = Math.Abs(dayBar.Open - priorDay.Close) / priorDay.Close;
            return gap > GapRejectPct;
        }

        // Fallback: first 1H of session vs prior daily close
        if (priorDay is null || priorDay.Close <= 0)
            return false;

        var sessionOpen = bars1hNewestFirst
            .Where(b => DateOnly.FromDateTime(b.BarTime.ToOffset(ist).DateTime) == confirmDay)
            .OrderBy(b => b.BarTime)
            .FirstOrDefault();
        if (sessionOpen is null)
            return false;

        var gap1h = Math.Abs(sessionOpen.Open - priorDay.Close) / priorDay.Close;
        return gap1h > GapRejectPct;
    }

    /// <summary>
    /// After the break of last-2 high/low, require a later 1H retest (touch within 0.3%)
    /// then bounce in trade direction before the confirm/entry bar.
    /// </summary>
    private static bool HasRetestThenBounce(
        List<MarketIntradayBarRow> barsNewestFirst,
        int confirmIndex,
        string side,
        decimal breakLevel,
        DateTimeOffset sweepTime)
    {
        // Bars between sweep and confirm (older than confirm, newer than or equal sweep)
        // Newest-first: indices confirmIndex+1 .. n are older.
        // We need a break bar older than retest, retest older than confirm.
        var candidates = new List<(int Idx, MarketIntradayBarRow Bar)>();
        for (var j = confirmIndex + 1; j < barsNewestFirst.Count; j++)
        {
            var b = barsNewestFirst[j];
            if (b.BarTime < sweepTime)
                break;
            candidates.Add((j, b));
        }

        // Find break bar (oldest-first within candidates): high/low through level
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

        // Retest after break, before confirm (newer than breakIdx means smaller index)
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
        // Allow same calendar day only
        if (stockDay != niftyDay)
            return false;

        return side == SignalSides.Buy
            ? stockPct > niftyPct
            : stockPct < niftyPct;
    }

    private static int CountLiquidityDensity(
        List<LiquidityAnalysisService.Zone> zones, decimal sweptPrice, string sweptType)
    {
        if (sweptPrice <= 0) return 0;
        return zones.Count(z =>
            !(z.Type == sweptType && Math.Abs(z.Price - sweptPrice) / sweptPrice < 0.0001m)
            && Math.Abs(z.Price - sweptPrice) / sweptPrice <= DensityTolPct);
    }

    private static (decimal? T1, decimal? T2, decimal? T3) PickV2Targets(
        string side,
        decimal entry,
        decimal risk,
        List<LiquidityAnalysisService.Zone> zones)
    {
        List<decimal> structure;
        if (side == SignalSides.Buy)
        {
            structure = zones
                .Where(z => z.IsResistanceLike && z.Price > entry * (1m + TargetMinDistancePct))
                .Select(z => z.Price)
                .OrderBy(p => p)
                .Distinct()
                .ToList();
        }
        else
        {
            structure = zones
                .Where(z => z.IsSupportLike && z.Price < entry * (1m - TargetMinDistancePct))
                .Select(z => z.Price)
                .OrderByDescending(p => p)
                .Distinct()
                .ToList();
        }

        // Deduplicate near levels
        var cleaned = new List<decimal>();
        foreach (var p in structure)
        {
            if (cleaned.Any(x => Math.Abs(x - p) / entry < TargetMinDistancePct))
                continue;
            cleaned.Add(p);
        }

        decimal? t1 = cleaned.Count > 0 ? cleaned[0] : null;
        var t2 = side == SignalSides.Buy ? entry + 2m * risk : entry - 2m * risk;
        decimal? t3 = cleaned.Count > 1
            ? cleaned[1]
            : (side == SignalSides.Buy ? entry + 3m * risk : entry - 3m * risk);

        return (t1, t2, t3);
    }

    private static (int Score, string Grade, string[] Reasons) ScoreSignal(
        string zoneType,
        decimal rvol,
        decimal closePos,
        bool sectorConfirmed,
        bool trendAligned,
        decimal plannedRr,
        bool densityBonus,
        string side)
    {
        var score = 0;
        var reasons = new List<string>();

        // Zone type — pick best one only (the swept zone)
        var zt = zoneType.ToLowerInvariant();
        if (zt is "equal_high" or "equal_low")
        {
            score += 25;
            reasons.Add("equal zone +25");
        }
        else if (zt.StartsWith("swing"))
        {
            score += 20;
            reasons.Add("swing zone +20");
        }
        else if (zt is "pdh" or "pdl")
        {
            score += 15;
            reasons.Add("PDH/PDL +15");
        }
        else if (zt is "pwh" or "pwl")
        {
            score += 10;
            reasons.Add("PWH/PWL +10");
        }
        else if (zt == "round")
        {
            score += 5;
            reasons.Add("round +5");
        }

        if (rvol > 2m)
        {
            score += 20;
            reasons.Add("RVOL>2 +20");
        }
        else if (rvol > 1.5m)
        {
            score += 15;
            reasons.Add("RVOL>1.5 +15");
        }

        var strongPos = side == SignalSides.Buy ? closePos > 0.85m : closePos < 0.15m;
        if (strongPos)
        {
            score += 10;
            reasons.Add("close pos >85% +10");
        }

        if (sectorConfirmed)
        {
            score += 10;
            reasons.Add("sector +10");
        }

        if (trendAligned)
        {
            score += 15;
            reasons.Add("trend EMA20 +15");
        }

        if (plannedRr > 3m)
        {
            score += 15;
            reasons.Add("R:R>3 +15");
        }

        // #17 Liquidity density bonus
        if (densityBonus)
        {
            score += 10;
            reasons.Add("liq density +10");
        }

        var grade = score >= 92 ? "A+"
            : score >= 84 ? "A"
            : score >= 72 ? "B"
            : score >= 58 ? "C"
            : "D";

        return (score, grade, reasons.ToArray());
    }
}
