using StockYouNeed.Application.IndexOptions;
using StockYouNeed.Domain;
using Xunit;

namespace StockYouNeed.Application.Tests;

public class IndexOptionNotificationTests
{
    [Fact]
    public void IsHighProbability_RecommendedOrbWithStrike_NotifiesAt80()
    {
        var rec = new NiftyOrbRecommendationRow
        {
            Status = "recommended",
            SignalSource = NiftyOrbService.SourceOrb,
            ConfidenceScore = 80,
            ContractStrike = 24550m,
            PremiumLtp = 150m,
        };
        Assert.True(IndexOptionNotificationService.IsHighProbability(rec));
    }

    [Fact]
    public void IsHighProbability_ComboRequires85()
    {
        var low = new NiftyOrbRecommendationRow
        {
            Status = "recommended",
            SignalSource = NiftyOrbService.SourceOrbLiqV2,
            ConfidenceScore = 84,
            ContractStrike = 24550m,
            PremiumLtp = 150m,
        };
        var ok = new NiftyOrbRecommendationRow
        {
            Status = "recommended",
            SignalSource = NiftyOrbService.SourceOrbLiqV2,
            ConfidenceScore = 85,
            ContractStrike = 24550m,
            PremiumLtp = 150m,
        };
        Assert.False(IndexOptionNotificationService.IsHighProbability(low));
        Assert.True(IndexOptionNotificationService.IsHighProbability(ok));
    }

    [Fact]
    public void IsHighProbability_SkippedOrMissingStrike_NoNotify()
    {
        var rec = new NiftyOrbRecommendationRow
        {
            Status = "skipped",
            SignalSource = NiftyOrbService.SourceOrb,
            ConfidenceScore = 90,
            ContractStrike = 24550m,
            PremiumLtp = 150m,
        };
        Assert.False(IndexOptionNotificationService.IsHighProbability(rec));
    }

    [Fact]
    public void IsHighProbability_LiqBreakoutNotifiesAt80()
    {
        var rec = new NiftyOrbRecommendationRow
        {
            Status = "recommended",
            SignalSource = NiftyOrbService.SourceLiqBreakout,
            ConfidenceScore = 80,
            ContractStrike = 25000m,
            PremiumLtp = 160m,
        };
        Assert.True(IndexOptionNotificationService.IsHighProbability(rec));
    }
}
