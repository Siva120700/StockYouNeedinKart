using StockYouNeed.Application.Services;
using StockYouNeed.Domain;

namespace StockYouNeed.Application.IndexOptions;

/// <summary>
/// Hero Zero: far OTM Nifty option lottery when a directional catalyst fires.
/// Risk = full premium; targets = 2× / 3× / 5× premium.
/// </summary>
public static class NiftyHeroZeroEvaluator
{
    public const int BaseConfidence = 45;
    public const int MaxConfidence = 75;
    public const decimal TargetMultipleT1 = 2m;
    public const decimal TargetMultipleT2 = 3m;
    public const decimal TargetMultipleT3 = 5m;

    public sealed record Catalyst(string Side, string Label, int ScoreBoost);

    public sealed record ResolvedSetup(
        string Side,
        int Confidence,
        List<string> CatalystLabels,
        decimal NiftyEntry,
        decimal NiftySl,
        decimal? NiftyT1);

    public sealed record PremiumTicket(
        decimal Entry,
        decimal StopLoss,
        decimal TargetT1,
        decimal TargetT2,
        decimal TargetT3,
        string[] Reasons);

    public static IReadOnlyList<Catalyst> CollectCatalysts(
        IReadOnlyList<NiftyOrbEvaluator.OrbLevels> orbSetups,
        AnalysisSignalRow? breakout)
    {
        var list = new List<Catalyst>();

        foreach (var orb in orbSetups.Where(s =>
            s.Status == "recommended" && s.Side is not null))
        {
            list.Add(new Catalyst(
                orb.Side!,
                $"ORB {orb.Side} break",
                30));
        }

        if (breakout?.VolumeOk == true)
        {
            list.Add(new Catalyst(
                breakout.Side,
                "Breakout + volume",
                25));
        }

        return list;
    }

    /// <summary>Best side when catalysts agree; null on conflict or no catalyst.</summary>
    public static ResolvedSetup? ResolveSetup(
        IReadOnlyList<Catalyst> catalysts,
        IReadOnlyList<NiftyOrbEvaluator.OrbLevels> orbSetups,
        AnalysisSignalRow? breakout)
    {
        if (catalysts.Count == 0)
            return null;

        var buyBoost = catalysts
            .Where(c => c.Side == SignalSides.Buy)
            .Sum(c => c.ScoreBoost);
        var sellBoost = catalysts
            .Where(c => c.Side == SignalSides.Sell)
            .Sum(c => c.ScoreBoost);

        if (buyBoost > 0 && sellBoost > 0 && Math.Abs(buyBoost - sellBoost) < 15)
            return null;

        var side = buyBoost >= sellBoost ? SignalSides.Buy : SignalSides.Sell;
        var boost = Math.Max(buyBoost, sellBoost);
        var labels = catalysts
            .Where(c => c.Side == side)
            .Select(c => c.Label)
            .Distinct()
            .ToList();
        if (labels.Count == 0)
            return null;

        var confidence = Math.Min(MaxConfidence, BaseConfidence + boost);

        var orb = orbSetups.FirstOrDefault(s =>
            s.Side == side && s.Status == "recommended");
        decimal niftyEntry;
        decimal niftySl;
        decimal? niftyT1;

        if (orb is not null)
        {
            niftyEntry = orb.Entry;
            niftySl = orb.StopLoss;
            niftyT1 = orb.TargetT1 > 0 ? orb.TargetT1 : null;
        }
        else if (breakout?.Side == side && breakout.VolumeOk)
        {
            niftyEntry = breakout.EntryPrice;
            niftySl = breakout.InitialStopLoss;
            niftyT1 = breakout.TargetT1;
        }
        else
        {
            var anyOrb = orbSetups.FirstOrDefault(s => s.High > 0 && s.Low > 0);
            niftyEntry = side == SignalSides.Buy
                ? (anyOrb?.High ?? 0)
                : (anyOrb?.Low ?? 0);
            niftySl = side == SignalSides.Buy
                ? (anyOrb?.Low ?? 0)
                : (anyOrb?.High ?? 0);
            niftyT1 = null;
        }

        return new ResolvedSetup(side, confidence, labels, niftyEntry, niftySl, niftyT1);
    }

    public static PremiumTicket BuildPremiumTicket(decimal livePremium)
    {
        var entry = Math.Round(livePremium, 2, MidpointRounding.AwayFromZero);
        // Hero or zero: SL at ~1% of premium (full loss accepted).
        var sl = Math.Round(Math.Max(0.05m, entry * 0.01m), 2, MidpointRounding.AwayFromZero);
        var t1 = Math.Round(entry * TargetMultipleT1, 2, MidpointRounding.AwayFromZero);
        var t2 = Math.Round(entry * TargetMultipleT2, 2, MidpointRounding.AwayFromZero);
        var t3 = Math.Round(entry * TargetMultipleT3, 2, MidpointRounding.AwayFromZero);

        return new PremiumTicket(
            entry, sl, t1, t2, t3,
            new[]
            {
                "Hero Zero ticket — far OTM, full premium at risk",
                $"Entry ₹{entry:0.00} · accept zero (SL ≈ full premium)",
                $"T1 ₹{t1:0.00} (2×) · T2 ₹{t2:0.00} (3×) · T3 ₹{t3:0.00} (5×)",
                "Small capital · high leverage · flat by 14:30 IST",
            });
    }
}
