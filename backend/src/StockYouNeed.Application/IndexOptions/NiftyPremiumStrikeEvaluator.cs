namespace StockYouNeed.Application.IndexOptions;

/// <summary>
/// Analyze a specific Nifty option strike's 15-min premium chart.
/// Targets are fixed premium points (T1 15 / T2 20), not Δ × Nifty.
/// </summary>
public static class NiftyPremiumStrikeEvaluator
{
    public const decimal TargetT1Pts = 15m;
    public const decimal TargetT2Pts = 20m;
    public const decimal TargetT3Pts = 25m;
    public const decimal MaxRiskPts = 12m;
    public const decimal MinRiskPts = 6m;
    public const decimal NearBreakPts = 2m;
    public const decimal MinPremium = 40m;
    public const decimal MaxPremium = 350m;
    /// <summary>Skip if premium already ran this much off the session low (move already spent).</summary>
    public const decimal MaxRunFromSessionLow = 18m;
    /// <summary>Minimum Nifty-vs-strike match to recommend (0–100).</summary>
    public const int MinMatchScore = 70;

    public static readonly TimeOnly SessionOpen = new(9, 15);
    public static readonly TimeOnly OrbEnd = new(9, 45);
    public static readonly TimeOnly FlatBy = new(14, 30);

    public sealed record Result(
        decimal Entry,
        decimal StopLoss,
        decimal TargetT1,
        decimal TargetT2,
        decimal TargetT3,
        decimal MicroHigh,
        decimal MicroLow,
        string Status,
        string? SkipReason,
        string[] Reasons);

    public static Result Evaluate(
        IReadOnlyList<(DateTimeOffset BarTime, decimal High, decimal Low, decimal Close)> bars,
        DateOnly asOf,
        decimal livePremium,
        DateTimeOffset? nowIst = null)
    {
        var ist = TimeSpan.FromHours(5.5);
        nowIst ??= DateTimeOffset.UtcNow.ToOffset(ist);
        var nowTime = TimeOnly.FromDateTime(nowIst.Value.DateTime);
        var isLive = asOf == DateOnly.FromDateTime(nowIst.Value.DateTime);

        if (livePremium < MinPremium || livePremium > MaxPremium)
            return Skip(0, 0, $"Premium ₹{livePremium:0.00} outside ₹{MinPremium:0}–₹{MaxPremium:0} scalp band");

        if (isLive && nowTime >= FlatBy)
            return Skip(0, 0, "Past 14:30 IST flat cutoff");

        var dayBars = bars
            .Where(b => DateOnly.FromDateTime(b.BarTime.ToOffset(ist).DateTime) == asOf)
            .OrderBy(b => b.BarTime)
            .ToList();

        var sessionBars = dayBars
            .Where(b =>
            {
                var t = TimeOnly.FromDateTime(b.BarTime.ToOffset(ist).DateTime);
                return t >= SessionOpen && t < FlatBy;
            })
            .ToList();

        if (sessionBars.Count == 0 && dayBars.Count == 0)
            return Skip(0, 0, "No 15-min premium bars for this strike");

        var chart = sessionBars.Count > 0 ? sessionBars : dayBars;
        var sessionLow = chart.Min(b => b.Low);
        var sessionHigh = chart.Max(b => b.High);
        var run = livePremium - sessionLow;
        if (run >= MaxRunFromSessionLow)
            return Skip(sessionHigh, sessionLow,
                $"Premium already up {run:0.0} pts from session low — 15–20 pt move likely spent");

        var (microHigh, microLow) = MicroRange(chart, ist);
        if (microHigh <= 0 || microLow <= 0 || microHigh <= microLow)
            return Skip(microHigh, microLow, "Strike chart range not formed yet");

        var nearBreak = livePremium >= microHigh - NearBreakPts;
        if (!nearBreak)
        {
            return new Result(
                microHigh, ClampSl(microHigh, microLow),
                Round(microHigh + TargetT1Pts), Round(microHigh + TargetT2Pts), Round(microHigh + TargetT3Pts),
                microHigh, microLow, "waiting",
                $"Wait for premium break above ₹{microHigh:0.00} (now ₹{livePremium:0.00})",
                new[]
                {
                    $"Strike 15-min range ₹{microLow:0.00}–₹{microHigh:0.00}",
                    $"T1 +{TargetT1Pts:0} / T2 +{TargetT2Pts:0} pts on premium",
                });
        }

        var entry = Round(livePremium);
        var sl = ClampSl(entry, microLow);
        if (sl >= entry)
            return Skip(microHigh, microLow, "Premium SL would be at/above entry");

        var risk = entry - sl;
        if (risk < MinRiskPts - 0.05m)
            sl = Round(entry - MinRiskPts);
        risk = entry - sl;
        if (risk <= 0)
            return Skip(microHigh, microLow, "Zero premium risk");

        var t1 = Round(entry + TargetT1Pts);
        var t2 = Round(entry + TargetT2Pts);
        var t3 = Round(entry + TargetT3Pts);

        if (livePremium >= t1)
            return Skip(microHigh, microLow, "Premium T1 already tagged on live mark — setup spent");

        var rr = TargetT1Pts / risk;
        if (rr < 1.20m)
            return Skip(microHigh, microLow, $"R:R {rr:0.00} below 1.20 with +{TargetT1Pts:0} pt T1 (risk {risk:0.00})");

        var reasons = new[]
        {
            $"Strike 15-min chart · range ₹{microLow:0.00}–₹{microHigh:0.00}",
            $"Premium entry ₹{entry:0.00} · SL ₹{sl:0.00} (−{risk:0.00} pts)",
            $"T1 ₹{t1:0.00} (+{TargetT1Pts:0}) · T2 ₹{t2:0.00} (+{TargetT2Pts:0})",
            $"R:R {rr:0.20} on T1 · session run {run:0.0} pts",
        };

        return new Result(entry, sl, t1, t2, t3, microHigh, microLow, "recommended", null, reasons);
    }

    /// <summary>
    /// How well Nifty chart levels (entry/SL/T1) map onto this strike's premium ticket via delta.
    /// Higher = Nifty risk/target implied premium agrees with strike-chart SL and +15/+20 targets.
    /// </summary>
    public static int ScoreAgainstNifty(
        decimal niftyEntry,
        decimal niftySl,
        decimal niftyT1,
        decimal premEntry,
        decimal premSl,
        decimal premT1,
        decimal longDelta,
        bool bothNiftyEngines,
        bool niftyEntriesAlign)
    {
        var d = longDelta <= 0 ? 0.5m : longDelta;
        var niftyRisk = Math.Abs(niftyEntry - niftySl);
        var niftyT1Move = niftyT1 > 0 ? Math.Abs(niftyT1 - niftyEntry) : 0;
        var premRisk = Math.Abs(premEntry - premSl);
        var premT1Move = Math.Abs(premT1 - premEntry);
        if (premRisk <= 0 || premT1Move <= 0)
            return 0;

        var impliedSl = niftyRisk > 0 ? niftyRisk * d : premRisk;
        var impliedT1 = niftyT1Move > 0 ? niftyT1Move * d : TargetT1Pts;

        var slAgree = 1m - Math.Min(1m, Math.Abs(impliedSl - premRisk) / Math.Max(impliedSl, premRisk));
        var t1Agree = 1m - Math.Min(1m, Math.Abs(impliedT1 - premT1Move) / Math.Max(impliedT1, premT1Move));

        var score = (int)Math.Round(slAgree * 35m + t1Agree * 35m, MidpointRounding.AwayFromZero);
        score += bothNiftyEngines ? 20 : 8;
        if (niftyEntriesAlign)
            score += 10;
        return Math.Clamp(score, 0, 100);
    }

    private static (decimal High, decimal Low) MicroRange(
        List<(DateTimeOffset BarTime, decimal High, decimal Low, decimal Close)> chart,
        TimeSpan ist)
    {
        var orb = chart
            .Where(b =>
            {
                var t = TimeOnly.FromDateTime(b.BarTime.ToOffset(ist).DateTime);
                return t >= SessionOpen && t < OrbEnd;
            })
            .ToList();
        if (orb.Count >= 2)
            return (orb.Max(b => b.High), orb.Min(b => b.Low));

        var last = chart.TakeLast(Math.Min(3, chart.Count)).ToList();
        if (last.Count == 0)
            return (0, 0);
        return (last.Max(b => b.High), last.Min(b => b.Low));
    }

    private static decimal ClampSl(decimal entry, decimal swingLow)
    {
        var sl = swingLow;
        var risk = entry - sl;
        if (risk > MaxRiskPts)
            sl = entry - MaxRiskPts;
        if (entry - sl < MinRiskPts)
            sl = entry - MinRiskPts;
        return Round(Math.Max(0.05m, sl));
    }

    private static decimal Round(decimal v) =>
        Math.Round(v, 2, MidpointRounding.AwayFromZero);

    private static Result Skip(decimal high, decimal low, string reason) =>
        new(0, 0, 0, 0, 0, high, low, "skipped", reason, new[] { reason });
}
