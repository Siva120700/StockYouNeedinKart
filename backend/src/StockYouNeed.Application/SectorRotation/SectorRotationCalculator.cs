using StockYouNeed.Application.Signals;
using StockYouNeed.Application.TradeScore;
using StockYouNeed.Domain;

namespace StockYouNeed.Application.SectorRotation;

/// <summary>
/// Institutional-style sector rotation from directional capital-flow proxy,
/// breadth, relative strength, trend, and volume expansion.
/// </summary>
public static class SectorRotationCalculator
{
    public const int LookbackDays = 25;
    public const int FlowHistoryDays = 20;
    public const int ShortFlowDays = 5;

    public sealed class StockDayMetrics
    {
        public required Guid InstrumentId { get; init; }
        public required string Symbol { get; init; }
        public required string Name { get; init; }
        public required Guid SectorInstrumentId { get; init; }
        public required string SectorSymbol { get; init; }
        public required string SectorName { get; init; }
        public decimal TodayReturnPct { get; init; }
        public decimal Return5dPct { get; init; }
        public decimal TodayFlow { get; init; }
        public decimal TodayTradedValue { get; init; }
        public IReadOnlyList<decimal> DailyFlows { get; init; } = Array.Empty<decimal>();
    }

    public static decimal DailyFlow(MarketBarRow today, MarketBarRow prev)
    {
        if (prev.Close <= 0) return 0;
        var ret = (today.Close - prev.Close) / prev.Close;
        var tradedValue = today.Close * today.Volume;
        return ret * tradedValue;
    }

    public static decimal ReturnPct(MarketBarRow today, MarketBarRow prev)
    {
        if (prev.Close <= 0) return 0;
        return Math.Round((today.Close - prev.Close) / prev.Close * 100m, 2, MidpointRounding.AwayFromZero);
    }

    public static StockDayMetrics? BuildStockMetrics(
        EquitySectorRow equity, IReadOnlyList<MarketBarRow> newestFirst, int minBars = 6)
    {
        if (newestFirst.Count < minBars) return null;
        var chron = MomentumScoreHelpers.ToChronological(newestFirst);
        var flows = new List<decimal>();
        for (var i = chron.Count - 1; i >= 1 && flows.Count < FlowHistoryDays; i--)
            flows.Add(DailyFlow(chron[i], chron[i - 1]));
        flows.Reverse();
        if (flows.Count == 0) return null;

        var last = chron[^1];
        var prev = chron[^2];
        var ret5 = MomentumScoreHelpers.ReturnBetween(chron, 0, Math.Min(5, chron.Count - 1));
        return new StockDayMetrics
        {
            InstrumentId = equity.InstrumentId,
            Symbol = equity.Symbol,
            Name = equity.Name,
            SectorInstrumentId = equity.SectorInstrumentId,
            SectorSymbol = equity.SectorSymbol,
            SectorName = equity.SectorName,
            TodayReturnPct = ReturnPct(last, prev),
            Return5dPct = ret5 is decimal r5 ? Math.Round(r5 * 100m, 2) : 0,
            TodayFlow = flows[^1],
            TodayTradedValue = last.Close * last.Volume,
            DailyFlows = flows,
        };
    }

    public static (decimal ZScore, decimal AccelPct) FlowStats(IReadOnlyList<decimal> sectorFlows)
    {
        if (sectorFlows.Count < 2) return (0, 0);
        var today = sectorFlows[^1];
        var hist = sectorFlows.Take(sectorFlows.Count - 1).ToList();
        var mean = hist.Average();
        var std = StdDev(hist);
        var z = std > 0 ? (today - mean) / std : 0;

        var shortTake = Math.Min(ShortFlowDays, sectorFlows.Count);
        var shortAvg = sectorFlows.TakeLast(shortTake).Average();
        var longAvg = sectorFlows.Average();
        var accelPct = longAvg != 0
            ? Math.Round((shortAvg - longAvg) / Math.Abs(longAvg) * 100m, 1)
            : 0;

        return (Math.Round(z, 2), accelPct);
    }

    public static int TrendScore(IReadOnlyList<MarketBarRow> sectorBarsNewestFirst)
    {
        if (sectorBarsNewestFirst.Count < 55) return 0;
        var chron = MomentumScoreHelpers.ToChronological(sectorBarsNewestFirst);
        var close = chron[^1].Close;
        var ema20 = TechnicalIndicators.Ema(chron, 20);
        var ema50 = TechnicalIndicators.Ema(chron, 50);
        var ema200 = TechnicalIndicators.Ema(chron, 200);
        var rsi = TechnicalIndicators.Rsi(chron, 14);
        var score = 0;
        if (ema20 is decimal e20 && close > e20) score += 30;
        if (ema20 is decimal a && ema50 is decimal b && a > b) score += 30;
        if (ema50 is decimal c && ema200 is decimal d && c > d) score += 30;
        if (rsi is decimal r && r > 55) score += 10;
        return score;
    }

    public static decimal Return5dPct(IReadOnlyList<MarketBarRow> newestFirst)
    {
        if (newestFirst.Count < 6) return 0;
        var chron = MomentumScoreHelpers.ToChronological(newestFirst);
        var ret = MomentumScoreHelpers.ReturnBetween(chron, 0, Math.Min(5, chron.Count - 1));
        return ret is decimal r ? Math.Round(r * 100m, 2) : 0;
    }

    public static int CompositeSectorScore(
        decimal flowZ,
        decimal flowAccelPct,
        decimal rs5dPct,
        decimal breadthPct,
        int trendScore,
        decimal volumeExpansionPct)
    {
        var flow = ClampScore((flowZ + 2m) / 4m * 100m);
        var accel = ClampScore((flowAccelPct + 50m) / 100m * 100m);
        var rs = ClampScore((rs5dPct + 5m) / 10m * 100m);
        var breadth = ClampScore(breadthPct);
        var trend = ClampScore(trendScore);
        var vol = ClampScore(volumeExpansionPct);

        var total = 0.25m * flow + 0.20m * accel + 0.20m * rs + 0.15m * breadth
            + 0.10m * trend + 0.10m * vol;
        return (int)Math.Round(total, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Early rotation score: flow acceleration, volume pick-up, and improving RS
    /// before the sector tops the composite ranking.
    /// </summary>
    public static int UpcomingMomentumScore(
        decimal flowZ,
        decimal flowAccelPct,
        decimal rs5dPct,
        decimal breadthPct,
        decimal volumeExpansionPct,
        int compositeScore)
    {
        var accelScore = flowAccelPct > 0
            ? ClampScore(Math.Min(100, 50 + flowAccelPct))
            : ClampScore(Math.Max(0, 50 + flowAccelPct * 0.5m));

        var volScore = volumeExpansionPct >= 100
            ? ClampScore(Math.Min(100, (volumeExpansionPct - 100) * 2 + 55))
            : ClampScore(volumeExpansionPct * 0.45m);

        var flowScore = flowZ switch
        {
            > 0 and <= 1.2m => ClampScore(60 + flowZ * 25),
            > 1.2m => ClampScore(85 - (flowZ - 1.2m) * 15),
            > -0.5m => ClampScore(40 + (flowZ + 0.5m) * 40),
            _ => ClampScore(Math.Max(0, 30 + flowZ * 20)),
        };

        var rsScore = rs5dPct switch
        {
            > 0 and <= 2.5m => ClampScore(55 + rs5dPct * 12),
            > 2.5m => ClampScore(Math.Min(90, 70 + (rs5dPct - 2.5m) * 5)),
            > -2m => ClampScore(35 + (rs5dPct + 2) * 15),
            _ => ClampScore(Math.Max(0, 20 + rs5dPct * 5)),
        };

        var breadthScore = breadthPct >= 50 && breadthPct <= 80
            ? ClampScore(breadthPct)
            : ClampScore(breadthPct * 0.85m);

        var roomToRun = compositeScore < 55 ? 1.08m : compositeScore < 65 ? 1.0m : 0.92m;

        var total = (0.35m * accelScore + 0.25m * volScore + 0.20m * flowScore
            + 0.12m * rsScore + 0.08m * breadthScore) * roomToRun;

        return (int)Math.Round(Math.Clamp(total, 0, 100), MidpointRounding.AwayFromZero);
    }

    public static IReadOnlyList<string> UpcomingMomentumReasons(
        decimal flowZ,
        decimal flowAccelPct,
        decimal rs5dPct,
        decimal breadthPct,
        decimal volumeExpansionPct)
    {
        var reasons = new List<string>();
        if (flowAccelPct > 5)
            reasons.Add($"Flow accelerating +{flowAccelPct:0}%");
        else if (flowAccelPct > 0)
            reasons.Add("Flow picking up");

        if (flowZ > 0 && flowZ <= 1.5m)
            reasons.Add($"Inflow z-score {flowZ:0.##}σ");
        else if (flowZ > 1.5m)
            reasons.Add($"Strong inflow {flowZ:0.##}σ");

        if (volumeExpansionPct > 110)
            reasons.Add($"Volume +{(volumeExpansionPct - 100):0}% vs avg");
        else if (volumeExpansionPct > 105)
            reasons.Add("Volume expanding");

        if (rs5dPct > 0 && rs5dPct <= 3)
            reasons.Add($"RS vs Nifty +{rs5dPct:0.##}% (early)");
        else if (rs5dPct > 3)
            reasons.Add($"RS vs Nifty +{rs5dPct:0.##}%");

        if (breadthPct >= 55)
            reasons.Add($"Breadth {breadthPct:0}% stocks up");

        return reasons.Take(4).ToList();
    }

    public static string ClassifyBucket(int score, decimal flowZ, decimal flowAccelPct)
    {
        if (score >= 60 && flowZ >= 0.35m && flowAccelPct > 0)
            return "capital_entering";
        if (score >= 52 && flowZ >= 0)
            return "leading";
        if (score <= 38 || flowZ <= -0.75m)
            return "capital_leaving";
        return "neutral";
    }

    /// <summary>Ensure top relative performers appear in entering/leading when absolute thresholds are quiet.</summary>
    public static void ApplyRelativeBuckets(IList<SectorRotationRow> sectors)
    {
        if (sectors.Count == 0) return;

        if (!sectors.Any(s => s.Bucket == "capital_entering"))
        {
            foreach (var s in sectors
                         .Where(s => s.FlowZScore > 0 && s.FlowAccelerationPct > 0)
                         .OrderByDescending(s => s.Score)
                         .Take(3))
                s.Bucket = "capital_entering";
        }

        if (!sectors.Any(s => s.Bucket == "leading"))
        {
            foreach (var s in sectors
                         .Where(s => s.Bucket != "capital_entering" && s.Score >= 48)
                         .OrderByDescending(s => s.Score)
                         .Take(3))
                s.Bucket = "leading";
        }
    }

    public static int StockMomentumScore(
        decimal return5dPct,
        decimal todayReturnPct,
        decimal todayFlow,
        IReadOnlyList<decimal> peerFlows5d,
        IReadOnlyList<decimal> peerReturns5d)
    {
        var flowRank = PercentileRank(todayFlow, peerFlows5d);
        var retRank = PercentileRank(return5dPct, peerReturns5d);
        var dayRank = PercentileRank(todayReturnPct, peerReturns5d);
        var score = 0.45m * retRank + 0.35m * flowRank + 0.20m * dayRank;
        return (int)Math.Round(score, MidpointRounding.AwayFromZero);
    }

    public static string AlignmentLabel(int sectorScore, int stockScore) => (sectorScore, stockScore) switch
    {
        ( >= 70, >= 75) => "a_plus",
        ( >= 70, < 60) => "watch",
        ( < 50, >= 75) => "stock_only",
        ( <= 40, _) => "avoid",
        (_, <= 35) => "avoid",
        _ => "neutral",
    };

    /// <summary>Top-down stock score: momentum 45%, sector 25%, RS 15%, volume 10%, breakout proxy 5%.</summary>
    public static int BlendedStockScore(
        int stockMomentum,
        int sectorScore,
        decimal relativeStrength5dPct,
        decimal volumeExpansionPct)
    {
        var rs = ClampScore((relativeStrength5dPct + 5m) / 10m * 100m);
        var vol = ClampScore(volumeExpansionPct);
        var breakoutProxy = ClampScore(stockMomentum * 0.9m);
        var total = 0.45m * stockMomentum + 0.25m * sectorScore + 0.15m * rs
            + 0.10m * vol + 0.05m * breakoutProxy;
        return (int)Math.Round(total, MidpointRounding.AwayFromZero);
    }

    public static string RegimeLabel(bool niftyAboveEma20, decimal breadthPct, decimal? niftyChangePct)
    {
        var riskOn = niftyAboveEma20 && breadthPct >= 55;
        var riskOff = !niftyAboveEma20 && breadthPct <= 45;
        if (riskOn && niftyChangePct is >= 0) return "risk_on";
        if (riskOff) return "risk_off";
        return "neutral";
    }

    public static decimal ToCr(decimal rupees) =>
        Math.Round(rupees / 10_000_000m, 2, MidpointRounding.AwayFromZero);

    private static decimal PercentileRank(decimal value, IReadOnlyList<decimal> peers)
    {
        if (peers.Count == 0) return 50;
        var sorted = peers.OrderBy(v => v).ToList();
        var rank = sorted.Count(v => v <= value);
        return Math.Round(100m * rank / sorted.Count, 0);
    }

    private static decimal ClampScore(decimal v) =>
        Math.Round(Math.Clamp(v, 0m, 100m), 1);

    private static decimal StdDev(IReadOnlyList<decimal> values)
    {
        if (values.Count < 2) return 0;
        var mean = values.Average();
        var var = values.Sum(v => (v - mean) * (v - mean)) / values.Count;
        return (decimal)Math.Sqrt((double)var);
    }
}
