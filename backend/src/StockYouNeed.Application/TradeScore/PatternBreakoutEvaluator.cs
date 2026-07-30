using StockYouNeed.Domain;

namespace StockYouNeed.Application.TradeScore;

/// <summary>
/// Chart pattern breakouts on daily bars (newest-first input, analyzed chronologically).
/// Patterns: range, ascending/descending triangle, double top/bottom.
/// </summary>
public static class PatternBreakoutEvaluator
{
    public const int MinBars = 25;
    public const int RangeLookback = 15;
    public const decimal MaxRangePct = 0.12m;
    public const decimal MinVolumeRatio = 1.15m;
    public const decimal LevelTolerance = 0.02m;
    public const decimal BreakMargin = 0.002m;

    public sealed record Match(
        bool Confirmed,
        string Side,
        string PatternType,
        decimal Close,
        decimal BreakoutLevel,
        decimal VolumeRatio,
        decimal? PatternDepthPct);

    public static Match? Evaluate(List<MarketBarRow> barsDesc)
    {
        if (barsDesc.Count < MinBars)
            return null;

        var latest = barsDesc[0];
        var volRatio = VolumeRatio(barsDesc);

        // Prefer strongest / first confirmed pattern.
        foreach (var tryMatch in new Func<Match?>[]
        {
            () => TryRangeBreakout(barsDesc, volRatio),
            () => TryAscendingTriangle(barsDesc, volRatio),
            () => TryDescendingTriangle(barsDesc, volRatio),
            () => TryDoubleBottom(barsDesc, volRatio),
            () => TryDoubleTop(barsDesc, volRatio),
        })
        {
            var m = tryMatch();
            if (m is { Confirmed: true })
                return m;
        }

        // Near-miss snapshot so the UI can show the scan ran.
        var prior = barsDesc.Skip(1).Take(RangeLookback).ToList();
        var rangeHigh = prior.Count > 0 ? prior.Max(b => b.High) : latest.Close;
        return new Match(false, SignalSides.Buy, "none", latest.Close, rangeHigh, volRatio, null);
    }

    private static decimal VolumeRatio(List<MarketBarRow> barsDesc)
    {
        var latest = barsDesc[0];
        var prior = barsDesc.Skip(1).Take(20).ToList();
        if (prior.Count == 0) return 0;
        var avg = prior.Average(b => (double)b.Volume);
        if (avg <= 0) return 0;
        return Math.Round((decimal)(latest.Volume / avg), 2);
    }

    private static Match? TryRangeBreakout(List<MarketBarRow> barsDesc, decimal volRatio)
    {
        var latest = barsDesc[0];
        var box = barsDesc.Skip(1).Take(RangeLookback).ToList();
        if (box.Count < 10) return null;

        var rangeHigh = box.Max(b => b.High);
        var rangeLow = box.Min(b => b.Low);
        var mid = (rangeHigh + rangeLow) / 2m;
        if (mid <= 0) return null;

        var depthPct = Math.Round((rangeHigh - rangeLow) / mid * 100m, 2);
        if (depthPct / 100m > MaxRangePct || depthPct < 1.5m)
            return null;

        // Most closes should sit inside the box (consolidation).
        var inside = box.Count(b => b.Close >= rangeLow * 0.985m && b.Close <= rangeHigh * 1.015m);
        if (inside < box.Count * 0.65m)
            return null;

        if (volRatio < MinVolumeRatio)
            return null;

        if (latest.Close > rangeHigh * (1m + BreakMargin)
            || (latest.High > rangeHigh && latest.Close >= rangeHigh))
        {
            return new Match(true, SignalSides.Buy, "range_breakout", latest.Close,
                rangeHigh, volRatio, depthPct);
        }

        if (latest.Close < rangeLow * (1m - BreakMargin)
            || (latest.Low < rangeLow && latest.Close <= rangeLow))
        {
            return new Match(true, SignalSides.Sell, "range_breakout", latest.Close,
                rangeLow, volRatio, depthPct);
        }

        return null;
    }

    private static Match? TryAscendingTriangle(List<MarketBarRow> barsDesc, decimal volRatio)
    {
        var latest = barsDesc[0];
        // Chronological window (oldest → newest), excluding latest bar.
        var chron = barsDesc.Skip(1).Take(20).Reverse().ToList();
        if (chron.Count < 12) return null;

        var resistance = chron.OrderByDescending(b => b.High).Take(3).Average(b => b.High);
        var touches = chron.Count(b => Math.Abs(b.High - resistance) / resistance <= LevelTolerance);
        if (touches < 2) return null;

        var lows = SwingLows(chron, 1, 1);
        if (lows.Count < 2) return null;

        var firstLow = chron[lows[0]].Low;
        var lastLow = chron[lows[^1]].Low;
        if (lastLow <= firstLow * 1.005m) return null; // rising lows
        if (volRatio < MinVolumeRatio) return null;

        if (latest.Close <= resistance * (1m + BreakMargin)
            && !(latest.High > resistance && latest.Close >= resistance))
            return null;

        var depthPct = Math.Round((resistance - firstLow) / resistance * 100m, 2);
        return new Match(true, SignalSides.Buy, "ascending_triangle", latest.Close,
            resistance, volRatio, depthPct);
    }

    private static Match? TryDescendingTriangle(List<MarketBarRow> barsDesc, decimal volRatio)
    {
        var latest = barsDesc[0];
        var chron = barsDesc.Skip(1).Take(20).Reverse().ToList();
        if (chron.Count < 12) return null;

        var support = chron.OrderBy(b => b.Low).Take(3).Average(b => b.Low);
        if (support <= 0) return null;
        var touches = chron.Count(b => Math.Abs(b.Low - support) / support <= LevelTolerance);
        if (touches < 2) return null;

        var highs = SwingHighs(chron, 1, 1);
        if (highs.Count < 2) return null;

        var firstHigh = chron[highs[0]].High;
        var lastHigh = chron[highs[^1]].High;
        if (lastHigh >= firstHigh * 0.995m) return null; // falling highs
        if (volRatio < MinVolumeRatio) return null;

        if (latest.Close >= support * (1m - BreakMargin)
            && !(latest.Low < support && latest.Close <= support))
            return null;

        var depthPct = Math.Round((firstHigh - support) / support * 100m, 2);
        return new Match(true, SignalSides.Sell, "descending_triangle", latest.Close,
            support, volRatio, depthPct);
    }

    private static Match? TryDoubleBottom(List<MarketBarRow> barsDesc, decimal volRatio)
    {
        var latest = barsDesc[0];
        var chron = barsDesc.Skip(1).Take(30).Reverse().ToList();
        if (chron.Count < 16) return null;

        var lows = SwingLows(chron, 2, 2);
        if (lows.Count < 2) return null;

        for (var i = 0; i < lows.Count - 1; i++)
        {
            for (var j = i + 1; j < lows.Count; j++)
            {
                if (lows[j] - lows[i] < 4) continue;
                var lowA = chron[lows[i]].Low;
                var lowB = chron[lows[j]].Low;
                if (lowA <= 0) continue;
                if (Math.Abs(lowA - lowB) / lowA > 0.025m) continue;

                var between = chron.Skip(lows[i]).Take(lows[j] - lows[i] + 1).ToList();
                var neckline = between.Max(b => b.High);
                if (volRatio < MinVolumeRatio) continue;

                if (latest.Close <= neckline * (1m + BreakMargin)
                    && !(latest.High > neckline && latest.Close >= neckline))
                    continue;

                var depthPct = Math.Round((neckline - lowA) / neckline * 100m, 2);
                return new Match(true, SignalSides.Buy, "double_bottom", latest.Close,
                    neckline, volRatio, depthPct);
            }
        }

        return null;
    }

    private static Match? TryDoubleTop(List<MarketBarRow> barsDesc, decimal volRatio)
    {
        var latest = barsDesc[0];
        var chron = barsDesc.Skip(1).Take(30).Reverse().ToList();
        if (chron.Count < 16) return null;

        var highs = SwingHighs(chron, 2, 2);
        if (highs.Count < 2) return null;

        for (var i = 0; i < highs.Count - 1; i++)
        {
            for (var j = i + 1; j < highs.Count; j++)
            {
                if (highs[j] - highs[i] < 4) continue;
                var highA = chron[highs[i]].High;
                var highB = chron[highs[j]].High;
                if (highA <= 0) continue;
                if (Math.Abs(highA - highB) / highA > 0.025m) continue;

                var between = chron.Skip(highs[i]).Take(highs[j] - highs[i] + 1).ToList();
                var neckline = between.Min(b => b.Low);
                if (volRatio < MinVolumeRatio) continue;

                if (latest.Close >= neckline * (1m - BreakMargin)
                    && !(latest.Low < neckline && latest.Close <= neckline))
                    continue;

                var depthPct = Math.Round((highA - neckline) / highA * 100m, 2);
                return new Match(true, SignalSides.Sell, "double_top", latest.Close,
                    neckline, volRatio, depthPct);
            }
        }

        return null;
    }

    private static List<int> SwingLows(IReadOnlyList<MarketBarRow> chron, int left, int right)
    {
        var idx = new List<int>();
        for (var i = left; i < chron.Count - right; i++)
        {
            var ok = true;
            for (var j = i - left; j <= i + right; j++)
            {
                if (j == i) continue;
                if (chron[j].Low < chron[i].Low) { ok = false; break; }
            }
            if (ok) idx.Add(i);
        }
        return idx;
    }

    private static List<int> SwingHighs(IReadOnlyList<MarketBarRow> chron, int left, int right)
    {
        var idx = new List<int>();
        for (var i = left; i < chron.Count - right; i++)
        {
            var ok = true;
            for (var j = i - left; j <= i + right; j++)
            {
                if (j == i) continue;
                if (chron[j].High > chron[i].High) { ok = false; break; }
            }
            if (ok) idx.Add(i);
        }
        return idx;
    }
}
