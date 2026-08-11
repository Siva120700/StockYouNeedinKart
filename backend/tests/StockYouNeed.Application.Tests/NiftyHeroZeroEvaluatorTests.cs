using StockYouNeed.Application.IndexOptions;
using StockYouNeed.Domain;
using Xunit;

namespace StockYouNeed.Application.Tests;

public class NiftyHeroZeroEvaluatorTests
{
    [Fact]
    public void ResolveSetup_OrbBreakoutSameSide_ReturnsBuy()
    {
        var orb = new NiftyOrbEvaluator.OrbLevels(
            24500, 24400, 100, SignalSides.Buy,
            24500, 24400, 24700, 24800, 24900,
            "recommended", null, new[] { "ORB break" });

        var brk = new AnalysisSignalRow
        {
            Side = SignalSides.Buy,
            VolumeOk = true,
            EntryPrice = 24500,
            InitialStopLoss = 24400,
            TargetT1 = 24700,
        };

        var catalysts = NiftyHeroZeroEvaluator.CollectCatalysts(new[] { orb }, brk);
        var setup = NiftyHeroZeroEvaluator.ResolveSetup(catalysts, new[] { orb }, brk);

        Assert.NotNull(setup);
        Assert.Equal(SignalSides.Buy, setup!.Side);
        Assert.True(setup.Confidence >= 70);
        Assert.Contains("ORB buy break", setup.CatalystLabels);
        Assert.Contains("Breakout + volume", setup.CatalystLabels);
    }

    [Fact]
    public void ResolveSetup_ConflictingCatalysts_ReturnsNull()
    {
        var orbBuy = new NiftyOrbEvaluator.OrbLevels(
            24500, 24400, 100, SignalSides.Buy,
            24500, 24400, 24700, 24800, 24900,
            "recommended", null, Array.Empty<string>());
        var orbSell = new NiftyOrbEvaluator.OrbLevels(
            24500, 24400, 100, SignalSides.Sell,
            24400, 24500, 24200, 24100, 24000,
            "recommended", null, Array.Empty<string>());

        var catalysts = NiftyHeroZeroEvaluator.CollectCatalysts(
            new[] { orbBuy, orbSell }, breakout: null);
        var setup = NiftyHeroZeroEvaluator.ResolveSetup(
            catalysts, new[] { orbBuy, orbSell }, breakout: null);

        Assert.Null(setup);
    }

    [Fact]
    public void BuildPremiumTicket_UsesMultiplierTargets()
    {
        var ticket = NiftyHeroZeroEvaluator.BuildPremiumTicket(20m);
        Assert.Equal(20m, ticket.Entry);
        Assert.Equal(40m, ticket.TargetT1);
        Assert.Equal(60m, ticket.TargetT2);
        Assert.Equal(100m, ticket.TargetT3);
    }
}
