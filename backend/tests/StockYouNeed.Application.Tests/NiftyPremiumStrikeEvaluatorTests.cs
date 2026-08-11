using StockYouNeed.Application.IndexOptions;
using Xunit;

namespace StockYouNeed.Application.Tests;

public class NiftyPremiumStrikeEvaluatorTests
{
    private static readonly TimeSpan Ist = TimeSpan.FromHours(5.5);

    private static (DateTimeOffset, decimal, decimal, decimal) Bar(
        int hour, int minute, decimal h, decimal l, decimal c)
    {
        var t = new DateTimeOffset(2026, 8, 11, hour, minute, 0, Ist);
        return (t, h, l, c);
    }

    [Fact]
    public void Evaluate_NearBreak_EntrySlAnd15_20Targets()
    {
        var asOf = new DateOnly(2026, 8, 11);
        var bars = new List<(DateTimeOffset, decimal, decimal, decimal)>
        {
            Bar(9, 15, 158, 148, 152),
            Bar(9, 30, 160, 150, 156),
            Bar(9, 45, 162, 154, 161),
        };
        var now = new DateTimeOffset(2026, 8, 11, 10, 5, 0, Ist);

        var r = NiftyPremiumStrikeEvaluator.Evaluate(bars, asOf, livePremium: 161.5m, nowIst: now);

        Assert.Equal("recommended", r.Status);
        Assert.Equal(161.5m, r.Entry);
        Assert.True(r.StopLoss < r.Entry);
        Assert.InRange(r.Entry - r.StopLoss, 6m, 12.05m);
        Assert.Equal(r.Entry + 15m, r.TargetT1);
        Assert.Equal(r.Entry + 20m, r.TargetT2);
    }

    [Fact]
    public void Evaluate_WaitingBelowMicroHigh()
    {
        var asOf = new DateOnly(2026, 8, 11);
        var bars = new List<(DateTimeOffset, decimal, decimal, decimal)>
        {
            Bar(9, 15, 158, 148, 152),
            Bar(9, 30, 160, 150, 151),
        };
        var now = new DateTimeOffset(2026, 8, 11, 10, 5, 0, Ist);

        var r = NiftyPremiumStrikeEvaluator.Evaluate(bars, asOf, livePremium: 152m, nowIst: now);

        Assert.Equal("waiting", r.Status);
        Assert.Contains("Wait for premium break", r.SkipReason ?? "");
    }

    [Fact]
    public void Evaluate_SkipsAlreadyExtendedRun()
    {
        var asOf = new DateOnly(2026, 8, 11);
        var bars = new List<(DateTimeOffset, decimal, decimal, decimal)>
        {
            Bar(9, 15, 155, 140, 145),
            Bar(9, 30, 170, 145, 168),
        };
        var now = new DateTimeOffset(2026, 8, 11, 10, 5, 0, Ist);

        var r = NiftyPremiumStrikeEvaluator.Evaluate(bars, asOf, livePremium: 168m, nowIst: now);

        Assert.Equal("skipped", r.Status);
        Assert.Contains("already up", r.SkipReason ?? "");
    }

    [Fact]
    public void Evaluate_SkipsPremiumOutsideBand()
    {
        var asOf = new DateOnly(2026, 8, 11);
        var now = new DateTimeOffset(2026, 8, 11, 10, 5, 0, Ist);
        var r = NiftyPremiumStrikeEvaluator.Evaluate(
            new[] { Bar(9, 15, 20, 18, 19) }, asOf, livePremium: 19m, nowIst: now);
        Assert.Equal("skipped", r.Status);
        Assert.Contains("outside", r.SkipReason ?? "");
    }

    [Fact]
    public void ScoreAgainstNifty_HighWhenDeltaMapsRiskAndTarget()
    {
        // Nifty risk 20 pts, T1 30 pts, Δ 0.5 → implied prem SL 10 / T1 15
        var score = NiftyPremiumStrikeEvaluator.ScoreAgainstNifty(
            niftyEntry: 24600m,
            niftySl: 24580m,
            niftyT1: 24630m,
            premEntry: 160m,
            premSl: 150m,
            premT1: 175m,
            longDelta: 0.50m,
            bothNiftyEngines: true,
            niftyEntriesAlign: true);

        Assert.True(score >= NiftyPremiumStrikeEvaluator.MinMatchScore, $"score={score}");
    }

    [Fact]
    public void ScoreAgainstNifty_LowWhenPremiumDisagrees()
    {
        var score = NiftyPremiumStrikeEvaluator.ScoreAgainstNifty(
            niftyEntry: 24600m,
            niftySl: 24580m,
            niftyT1: 24630m,
            premEntry: 160m,
            premSl: 80m,
            premT1: 400m,
            longDelta: 0.50m,
            bothNiftyEngines: false,
            niftyEntriesAlign: false);

        Assert.True(score < NiftyPremiumStrikeEvaluator.MinMatchScore, $"score={score}");
    }
}
