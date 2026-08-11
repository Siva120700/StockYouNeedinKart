using StockYouNeed.Domain;

namespace StockYouNeed.Application.IndexOptions;

/// <summary>
/// 30-minute Opening Range Breakout on Nifty (9:15–9:45 IST).
/// Up to two tickets per day — CE on OR high break and PE on OR low break (independent).
/// SL = opposite OR side; T1/T2/T3 = 2R/3R/4R per side.
/// </summary>
public static class NiftyOrbEvaluator
{
    public static readonly TimeOnly SessionOpen = new(9, 15);
    public static readonly TimeOnly OrbEnd = new(9, 45);
    public static readonly TimeOnly FlatBy = new(14, 30);
    public const decimal MinOrbRangePoints = 40m;
    public const decimal TargetRiskMultiple = 2m;

    public sealed record OrbLevels(
        decimal High,
        decimal Low,
        decimal Range,
        string? Side,
        decimal Entry,
        decimal StopLoss,
        decimal TargetT1,
        decimal TargetT2,
        decimal TargetT3,
        string Status,
        string? SkipReason,
        string[] Reasons);

    /// <summary>First recommended setup, else first row (waiting/skipped).</summary>
    public static OrbLevels Evaluate(
        IReadOnlyList<(DateTimeOffset BarTime, decimal High, decimal Low, decimal Close)> bars,
        DateOnly asOf,
        decimal? liveSpot = null,
        DateTimeOffset? nowIst = null)
    {
        var all = EvaluateAll(bars, asOf, liveSpot, nowIst);
        return all.FirstOrDefault(s => s.Status == "recommended")
            ?? all.FirstOrDefault()
            ?? Skip(0, 0, 0, "No 15-min bars for today");
    }

    /// <summary>
    /// Evaluate ORB from chronologically ordered (or newest-first) 15-min bars for <paramref name="asOf"/>.
    /// Returns zero rows before OR forms, one waiting/skipped row, or one/two side-specific setups.
    /// </summary>
    public static IReadOnlyList<OrbLevels> EvaluateAll(
        IReadOnlyList<(DateTimeOffset BarTime, decimal High, decimal Low, decimal Close)> bars,
        DateOnly asOf,
        decimal? liveSpot = null,
        DateTimeOffset? nowIst = null)
    {
        var ist = TimeSpan.FromHours(5.5);
        nowIst ??= DateTimeOffset.UtcNow.ToOffset(ist);

        var dayBars = bars
            .Where(b => DateOnly.FromDateTime(b.BarTime.ToOffset(ist).DateTime) == asOf)
            .OrderBy(b => b.BarTime)
            .ToList();

        if (dayBars.Count == 0)
            return new[] { Skip(0, 0, 0, "No 15-min bars for today") };

        var orbBars = dayBars
            .Where(b =>
            {
                var t = TimeOnly.FromDateTime(b.BarTime.ToOffset(ist).DateTime);
                return t >= SessionOpen && t < OrbEnd;
            })
            .ToList();

        if (orbBars.Count == 0)
        {
            orbBars = dayBars
                .Where(b =>
                {
                    var t = TimeOnly.FromDateTime(b.BarTime.ToOffset(ist).DateTime);
                    return t >= SessionOpen && t <= OrbEnd;
                })
                .ToList();
        }

        if (orbBars.Count < 1)
            return new[] { Skip(0, 0, 0, "Opening range not formed yet (need bars 9:15–9:45)") };

        var nowTime = TimeOnly.FromDateTime(nowIst.Value.DateTime);
        var isLiveSession = asOf == DateOnly.FromDateTime(nowIst.Value.DateTime);

        if (isLiveSession && nowTime < OrbEnd)
        {
            return new[]
            {
                Skip(
                    orbBars.Max(b => b.High),
                    orbBars.Min(b => b.Low),
                    0,
                    "Waiting for ORB window to complete (9:45 IST)",
                    status: "waiting")
            };
        }

        var high = orbBars.Max(b => b.High);
        var low = orbBars.Min(b => b.Low);
        var range = high - low;
        if (range < MinOrbRangePoints)
            return new[] { Skip(high, low, range, $"ORB range {range:0.0} pts below minimum {MinOrbRangePoints:0} pts") };

        var postOrb = dayBars
            .Where(b => TimeOnly.FromDateTime(b.BarTime.ToOffset(ist).DateTime) >= OrbEnd)
            .ToList();

        var buyBroken = false;
        var sellBroken = false;
        foreach (var bar in postOrb)
        {
            var t = TimeOnly.FromDateTime(bar.BarTime.ToOffset(ist).DateTime);
            if (t >= FlatBy)
                break;

            if (!buyBroken && bar.High > high)
                buyBroken = true;
            if (!sellBroken && bar.Low < low)
                sellBroken = true;
        }

        if (!buyBroken && liveSpot is decimal spotUp and > 0 && spotUp > high)
            buyBroken = true;
        if (!sellBroken && liveSpot is decimal spotDn and > 0 && spotDn < low)
            sellBroken = true;

        if (!buyBroken && !sellBroken)
        {
            return new[]
            {
                new OrbLevels(
                    high, low, range, null, high, low, 0, 0, 0,
                    "waiting",
                    "No ORB break yet — watching for break of OR high/low",
                    new[] { $"OR {low:0.00}–{high:0.00} ({range:0.0} pts)" })
            };
        }

        var results = new List<OrbLevels>(2);
        if (buyBroken)
            results.Add(BuildSideSetup(high, low, range, SignalSides.Buy, liveSpot, isLiveSession, nowTime));
        if (sellBroken)
            results.Add(BuildSideSetup(high, low, range, SignalSides.Sell, liveSpot, isLiveSession, nowTime));

        return results;
    }

    private static OrbLevels BuildSideSetup(
        decimal high, decimal low, decimal range, string side,
        decimal? liveSpot, bool isLiveSession, TimeOnly nowTime)
    {
        var entry = side == SignalSides.Buy ? high : low;
        var sl = side == SignalSides.Buy ? low : high;
        var risk = Math.Abs(entry - sl);
        if (risk <= 0)
            return SideSkip(high, low, range, side, "Zero ORB risk");

        decimal Target(decimal m) =>
            side == SignalSides.Buy
                ? Math.Round(entry + risk * m, 2, MidpointRounding.AwayFromZero)
                : Math.Round(entry - risk * m, 2, MidpointRounding.AwayFromZero);

        var t1 = Target(2m);
        var t2 = Target(3m);
        var t3 = Target(4m);

        if (liveSpot is decimal mark and > 0)
        {
            var hitSl = side == SignalSides.Buy ? mark <= sl : mark >= sl;
            var hitT1 = side == SignalSides.Buy ? mark >= t1 : mark <= t1;
            if (hitSl)
                return SideSkip(high, low, range, side, "ORB stop already tagged on live mark");
            if (hitT1)
                return SideSkip(high, low, range, side, "ORB T1 already tagged on live mark — setup spent");
        }

        if (isLiveSession && nowTime >= FlatBy)
            return SideSkip(high, low, range, side, "Past 14:30 IST flat cutoff — no new ORB entries");

        var label = side == SignalSides.Buy ? "Break above OR high → buy CE" : "Break below OR low → buy PE";
        var reasons = new[]
        {
            "30-min ORB (9:15–9:45)",
            $"OR {low:0.00}–{high:0.00} ({range:0.0} pts)",
            label,
            $"Risk {risk:0.00} pts · T1 at 2R",
            "Independent side — Nifty can break both OR levels same day",
        };

        return new OrbLevels(
            high, low, range, side, entry, sl, t1, t2, t3,
            "recommended", null, reasons);
    }

    private static OrbLevels SideSkip(
        decimal high, decimal low, decimal range, string side, string reason, string status = "skipped")
        => new(
            high, low, range, side, side == SignalSides.Buy ? high : low,
            side == SignalSides.Buy ? low : high, 0, 0, 0,
            status, reason,
            new[] { $"OR {low:0.00}–{high:0.00}", reason });

    private static OrbLevels Skip(
        decimal high, decimal low, decimal range, string reason, string status = "skipped")
        => new(high, low, range, null,
            high > 0 ? high : 0,
            low > 0 ? low : 0,
            0, 0, 0,
            status, reason,
            high > 0 ? new[] { $"OR {low:0.00}–{high:0.00}", reason } : new[] { reason });
}
