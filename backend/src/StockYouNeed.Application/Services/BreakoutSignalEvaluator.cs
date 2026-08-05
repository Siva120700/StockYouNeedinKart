using StockYouNeed.Domain;

namespace StockYouNeed.Application.Services;

/// <summary>Pure daily breakout evaluation shared by live analysis and historical backtest.</summary>
public static class BreakoutSignalEvaluator
{
    public static AnalysisSignalRow? Evaluate(
        Guid userId, Guid runId, DateOnly asOf, List<MarketBarRow> barsDesc, decimal? livePrice = null,
        bool actionableOnly = false)
    {
        var latest = barsDesc[0];
        var prev = barsDesc.Skip(1).Take(2).ToList();
        if (prev.Count < 2)
            return null;

        var last2High = prev.Max(b => b.High);
        var last2Low = prev.Min(b => b.Low);

        var prior3 = barsDesc.Skip(1).Take(3).ToList();
        if (prior3.Count == 0)
            return null;
        var avgVolPrior3 = prior3.Average(b => (double)b.Volume);
        var volumeOk = latest.Volume >= (long)(avgVolPrior3 * 0.25);

        const decimal ImminentMargin = 0.01m;
        var ltp = livePrice ?? latest.Close;

        var buyBreak = latest.High > last2High;
        var sellBreak = latest.Low < last2Low;
        var buyImminent = !buyBreak && ltp >= last2High * (1m - ImminentMargin) && ltp < last2High;
        var sellImminent = !sellBreak && ltp <= last2Low * (1m + ImminentMargin) && ltp > last2Low;

        string? side = null;
        // Live + backtest both use break or imminent; live filters "already ran" after targets.
        if ((buyBreak || buyImminent) && (sellBreak || sellImminent) && volumeOk)
        {
            var mid = (last2High + last2Low) / 2m;
            side = ltp >= mid ? SignalSides.Buy : SignalSides.Sell;
        }
        else if ((buyBreak || buyImminent) && volumeOk)
            side = SignalSides.Buy;
        else if ((sellBreak || sellImminent) && volumeOk)
            side = SignalSides.Sell;

        if (side is null)
            return null;

        var freshCross = IsFreshCross(barsDesc, side);
        var entry = side == SignalSides.Buy ? last2High : last2Low;

        var closes = barsDesc.Take(5).Select(b => b.Close).Reverse().ToList();
        decimal Ma(int n) => closes.TakeLast(n).Average();

        var ma2 = closes.Count >= 2 ? Ma(2) : (decimal?)null;
        var ma3 = closes.Count >= 3 ? Ma(3) : (decimal?)null;
        var ma5 = closes.Count >= 5 ? Ma(5) : (decimal?)null;

        var avgUp5 = AvgDirectionalMovePct(barsDesc, 5, up: true);
        var avgUp3 = AvgDirectionalMovePct(barsDesc, 3, up: true);
        var avgUp2 = AvgDirectionalMovePct(barsDesc, 2, up: true);
        var avgDn5 = AvgDirectionalMovePct(barsDesc, 5, up: false);
        var avgDn3 = AvgDirectionalMovePct(barsDesc, 3, up: false);
        var avgDn2 = AvgDirectionalMovePct(barsDesc, 2, up: false);

        decimal? t1;
        decimal? t2;
        decimal? t3;
        decimal sl;

        if (side == SignalSides.Buy)
        {
            sl = last2Low;
            if (sl >= entry)
                sl = entry * 0.98m;
            var buyTargets = new[]
                {
                    avgUp5 > 0 ? RoundPrice(entry * (1 + avgUp5)) : (decimal?)null,
                    avgUp3 > 0 ? RoundPrice(entry * (1 + avgUp3)) : null,
                    avgUp2 > 0 ? RoundPrice(entry * (1 + avgUp2)) : null
                }
                .Where(t => t is decimal v && v > entry)
                .Select(t => t!.Value)
                .Distinct()
                .OrderBy(t => t)
                .ToList();
            t1 = buyTargets.Count > 0 ? buyTargets[0] : null;
            t2 = buyTargets.Count > 1 ? buyTargets[1] : null;
            t3 = buyTargets.Count > 2 ? buyTargets[2] : null;
        }
        else
        {
            sl = last2High;
            if (sl <= entry)
                sl = entry * 1.02m;
            var sellTargets = new[]
                {
                    avgDn5 > 0 ? RoundPrice(entry * (1 - avgDn5)) : (decimal?)null,
                    avgDn3 > 0 ? RoundPrice(entry * (1 - avgDn3)) : null,
                    avgDn2 > 0 ? RoundPrice(entry * (1 - avgDn2)) : null
                }
                .Where(t => t is decimal v && v < entry)
                .Select(t => t!.Value)
                .Distinct()
                .OrderByDescending(t => t)
                .ToList();
            t1 = sellTargets.Count > 0 ? sellTargets[0] : null;
            t2 = sellTargets.Count > 1 ? sellTargets[1] : null;
            t3 = sellTargets.Count > 2 ? sellTargets[2] : null;
        }

        // Drop / roll targets already tagged on the signal bar so only actionable entries remain.
        (t1, t2, t3) = RollPastSpentTargets(side, t1, t2, t3, ltp, latest);
        if (t1 is null)
            return null;

        // Still require room to T1 vs stop (skip tiny leftover targets after a wide SL).
        var risk = Math.Abs(entry - sl);
        var reward = Math.Abs(t1.Value - entry);
        if (risk <= 0 || reward < risk)
            return null;

        if (actionableOnly
            && !LiquidityAnalysisService.IsLiveEntryStillOpen(side, entry, t1.Value, ltp))
            return null;

        return new AnalysisSignalRow
        {
            Id = Guid.NewGuid(),
            AnalysisRunId = runId,
            UserId = userId,
            InstrumentId = latest.InstrumentId,
            AppSymbol = latest.AppSymbol,
            Side = side,
            AsOfDate = asOf,
            EntryPrice = entry,
            InitialStopLoss = sl,
            TargetT1 = t1,
            TargetT2 = t2,
            TargetT3 = t3,
            VolumeOk = volumeOk,
            SectorConfirmed = false,
            FreshCross = freshCross,
            Ma2d = ma2,
            Ma3d = ma3,
            Ma5d = ma5,
            Last2dHigh = last2High,
            Last2dLow = last2Low
        };
    }

    /// <summary>
    /// If T1 was already tagged by live mark or today's bar, promote T2→T1 etc.
    /// Returns null T1 when no unused target remains (setup is spent).
    /// </summary>
    internal static (decimal? T1, decimal? T2, decimal? T3) RollPastSpentTargets(
        string side,
        decimal? t1,
        decimal? t2,
        decimal? t3,
        decimal markPrice,
        MarketBarRow signalBar)
    {
        var queue = new List<decimal>();
        if (t1 is decimal a) queue.Add(a);
        if (t2 is decimal b) queue.Add(b);
        if (t3 is decimal c) queue.Add(c);

        while (queue.Count > 0 && IsTargetTagged(side, queue[0], markPrice, signalBar))
            queue.RemoveAt(0);

        return (
            queue.Count > 0 ? queue[0] : null,
            queue.Count > 1 ? queue[1] : null,
            queue.Count > 2 ? queue[2] : null);
    }

    internal static bool IsTargetTagged(
        string side, decimal target, decimal markPrice, MarketBarRow signalBar)
    {
        if (side == SignalSides.Buy)
            return markPrice >= target || signalBar.Close >= target || signalBar.High >= target;
        return markPrice <= target || signalBar.Close <= target || signalBar.Low <= target;
    }

    public static bool IsFreshCross(List<MarketBarRow> barsNewestFirst, string side)
    {
        var priorBreakouts = 0;
        for (var i = 1; i <= 3 && i + 2 < barsNewestFirst.Count; i++)
        {
            var day = barsNewestFirst[i];
            var priorHigh = Math.Max(barsNewestFirst[i + 1].High, barsNewestFirst[i + 2].High);
            var priorLow = Math.Min(barsNewestFirst[i + 1].Low, barsNewestFirst[i + 2].Low);
            if (side == SignalSides.Buy && day.High > priorHigh)
                priorBreakouts++;
            else if (side == SignalSides.Sell && day.Low < priorLow)
                priorBreakouts++;
        }

        return priorBreakouts == 0;
    }

    public static decimal AvgDirectionalMovePct(List<MarketBarRow> barsNewestFirst, int days, bool up)
    {
        if (days < 1 || barsNewestFirst.Count < days + 1)
            return 0m;

        decimal sum = 0m;
        var count = 0;
        for (var i = 0; i < days; i++)
        {
            var day = barsNewestFirst[i];
            var prevClose = barsNewestFirst[i + 1].Close;
            if (prevClose <= 0)
                continue;

            var pct = up
                ? (day.High - prevClose) / prevClose
                : (prevClose - day.Low) / prevClose;
            if (pct < 0)
                pct = 0;
            sum += pct;
            count++;
        }

        return count == 0 ? 0m : sum / count;
    }

    private static decimal RoundPrice(decimal price) =>
        Math.Round(price, 2, MidpointRounding.AwayFromZero);
}
