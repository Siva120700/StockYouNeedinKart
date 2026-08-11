using StockYouNeed.Application.IndexOptions;
using StockYouNeed.Domain;
using Xunit;

namespace StockYouNeed.Application.Tests;

public class NiftyOrbEvaluatorTests
{
    private static readonly TimeSpan Ist = TimeSpan.FromHours(5.5);

    private static (DateTimeOffset, decimal, decimal, decimal) Bar(
        int hour, int minute, decimal h, decimal l, decimal c)
    {
        var t = new DateTimeOffset(2026, 8, 7, hour, minute, 0, Ist);
        return (t, h, l, c);
    }

    [Fact]
    public void Evaluate_BreakAboveOrb_BuyWith2RTarget()
    {
        var asOf = new DateOnly(2026, 8, 7);
        var bars = new List<(DateTimeOffset, decimal, decimal, decimal)>
        {
            Bar(9, 15, 24550, 24480, 24520),
            Bar(9, 30, 24580, 24500, 24560),
            Bar(9, 45, 24620, 24570, 24600), // break above OR high 24580
            Bar(10, 0, 24640, 24590, 24620),
        };

        var now = new DateTimeOffset(2026, 8, 7, 10, 15, 0, Ist);
        var orb = NiftyOrbEvaluator.Evaluate(bars, asOf, liveSpot: 24620m, nowIst: now);

        Assert.Equal("recommended", orb.Status);
        Assert.Equal(SignalSides.Buy, orb.Side);
        Assert.Equal(24580m, orb.High);
        Assert.Equal(24480m, orb.Low);
        Assert.Equal(24580m, orb.Entry);
        Assert.Equal(24480m, orb.StopLoss);
        Assert.Equal(24780m, orb.TargetT1); // risk 100 → 2R
    }

    [Fact]
    public void Evaluate_SkipsNarrowRange()
    {
        var asOf = new DateOnly(2026, 8, 7);
        var bars = new List<(DateTimeOffset, decimal, decimal, decimal)>
        {
            Bar(9, 15, 24520, 24500, 24510),
            Bar(9, 30, 24530, 24505, 24520),
            Bar(10, 0, 24560, 24520, 24550),
        };
        var now = new DateTimeOffset(2026, 8, 7, 10, 15, 0, Ist);
        var orb = NiftyOrbEvaluator.Evaluate(bars, asOf, nowIst: now);

        Assert.Equal("skipped", orb.Status);
        Assert.Contains("below minimum", orb.SkipReason ?? "");
    }

    [Fact]
    public void EvaluateAll_BothSidesBreak_ReturnsTwoRecommendations()
    {
        var asOf = new DateOnly(2026, 8, 7);
        var bars = new List<(DateTimeOffset, decimal, decimal, decimal)>
        {
            Bar(9, 15, 24550, 24480, 24520),
            Bar(9, 30, 24580, 24480, 24560),
            Bar(10, 0, 24620, 24570, 24600),  // break OR high
            Bar(12, 0, 24590, 24460, 24470),  // break OR low
        };

        var now = new DateTimeOffset(2026, 8, 7, 12, 15, 0, Ist);
        // Spot between OR levels — both breaks valid, neither SL/T1 tagged
        var setups = NiftyOrbEvaluator.EvaluateAll(bars, asOf, liveSpot: 24550m, nowIst: now);

        Assert.Equal(2, setups.Count);
        Assert.Contains(setups, s => s.Side == SignalSides.Buy && s.Status == "recommended");
        Assert.Contains(setups, s => s.Side == SignalSides.Sell && s.Status == "recommended");
    }

    [Fact]
    public void EvaluateAll_FirstSideSpent_SecondStillRecommended()
    {
        var asOf = new DateOnly(2026, 8, 7);
        var bars = new List<(DateTimeOffset, decimal, decimal, decimal)>
        {
            Bar(9, 15, 24550, 24480, 24520),
            Bar(9, 30, 24580, 24480, 24560),
            Bar(10, 0, 24620, 24570, 24600),
            Bar(12, 0, 24590, 24460, 24470),
        };

        var now = new DateTimeOffset(2026, 8, 7, 12, 15, 0, Ist);
        // Spot below OR low — buy SL tagged, sell still valid
        var setups = NiftyOrbEvaluator.EvaluateAll(bars, asOf, liveSpot: 24470m, nowIst: now);

        var buy = setups.Single(s => s.Side == SignalSides.Buy);
        var sell = setups.Single(s => s.Side == SignalSides.Sell);
        Assert.Equal("skipped", buy.Status);
        Assert.Contains("stop", buy.SkipReason ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.Equal("recommended", sell.Status);
    }
}

public class NiftyOrbPremiumLevelsTests
{
    [Fact]
    public void EstimatePremiumLevels_UsesDeltaTimesNiftyPoints()
    {
        // Premium 147, Δ 0.54, Nifty risk 90.8 → premium risk ≈ 49
        var (sl, t1, t2, t3) = NiftyOrbService.EstimatePremiumLevels(
            premiumEntry: 146.95m,
            longDelta: 0.541m,
            niftyEntry: 24620m,
            niftySl: 24529.2m,
            niftyT1: 24801.6m,
            niftyT2: 24892.4m,
            niftyT3: 24983.2m);

        Assert.True(sl < 146.95m);
        Assert.InRange(sl, 146.95m - 90.8m * 0.541m - 1m, 146.95m - 90.8m * 0.541m + 1m);
        Assert.True(t1 > 146.95m);
        Assert.True(t2 > t1);
        Assert.True(t3 > t2);
    }
}
