using StockYouNeed.Application.SectorRotation;
using StockYouNeed.Domain;
using Xunit;

namespace StockYouNeed.Application.Tests;

public class SectorRotationCalculatorTests
{
    [Fact]
    public void DailyFlow_ReturnTimesTradedValue()
    {
        var today = Bar(100, 1_000_000);
        var prev = Bar(98, 900_000);
        var flow = SectorRotationCalculator.DailyFlow(today, prev);
        Assert.True(flow > 0);
    }

    [Fact]
    public void FlowStats_ComputesZScoreAndAcceleration()
    {
        var flows = new List<decimal> { 10, 12, 11, 13, 12, 10, 11, 12, 13, 40m };
        var (z, accel) = SectorRotationCalculator.FlowStats(flows);
        Assert.True(z > 1);
        Assert.True(accel > 0);
    }

    [Fact]
    public void CompositeSectorScore_WithinRange()
    {
        var score = SectorRotationCalculator.CompositeSectorScore(
            flowZ: 2m, flowAccelPct: 30m, rs5dPct: 3m,
            breadthPct: 80m, trendScore: 90, volumeExpansionPct: 120m);
        Assert.InRange(score, 60, 100);
    }

    [Fact]
    public void ClassifyBucket_EnteringWhenHighScoreAndPositiveAccel()
    {
        var bucket = SectorRotationCalculator.ClassifyBucket(score: 62, flowZ: 0.5m, flowAccelPct: 12m);
        Assert.Equal("capital_entering", bucket);
    }

    [Fact]
    public void ApplyRelativeBuckets_PromotesTopWhenNoAbsoluteEntering()
    {
        var sectors = new List<SectorRotationRow>
        {
            new() { Score = 62, FlowZScore = 0.4m, FlowAccelerationPct = 5, Bucket = "neutral" },
            new() { Score = 50, FlowZScore = -0.2m, FlowAccelerationPct = -3, Bucket = "neutral" },
            new() { Score = 30, FlowZScore = -1m, FlowAccelerationPct = -8, Bucket = "capital_leaving" },
        };
        SectorRotationCalculator.ApplyRelativeBuckets(sectors);
        Assert.Contains(sectors, s => s.Bucket == "capital_entering");
    }

    [Fact]
    public void UpcomingMomentumScore_HigherWhenFlowAccelerating()
    {
        var building = SectorRotationCalculator.UpcomingMomentumScore(
            flowZ: 0.6m, flowAccelPct: 18m, rs5dPct: 1.2m,
            breadthPct: 58m, volumeExpansionPct: 115m, compositeScore: 48);
        var fading = SectorRotationCalculator.UpcomingMomentumScore(
            flowZ: -0.8m, flowAccelPct: -12m, rs5dPct: -1.5m,
            breadthPct: 35m, volumeExpansionPct: 85m, compositeScore: 32);
        Assert.True(building > fading);
        Assert.InRange(building, 50, 100);
    }

    [Fact]
    public void UpcomingMomentumReasons_ListsEarlySignals()
    {
        var reasons = SectorRotationCalculator.UpcomingMomentumReasons(
            flowZ: 0.8m, flowAccelPct: 10m, rs5dPct: 1.5m,
            breadthPct: 62m, volumeExpansionPct: 118m);
        Assert.NotEmpty(reasons);
        Assert.Contains(reasons, r => r.Contains("Flow", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ClassifyBucket_LeavingWhenLowScore()
    {
        var bucket = SectorRotationCalculator.ClassifyBucket(score: 30, flowZ: -1.5m, flowAccelPct: -10m);
        Assert.Equal("capital_leaving", bucket);
    }

    [Fact]
    public void AlignmentLabel_APlusWhenBothStrong()
    {
        Assert.Equal("a_plus", SectorRotationCalculator.AlignmentLabel(sectorScore: 85, stockScore: 80));
        Assert.Equal("stock_only", SectorRotationCalculator.AlignmentLabel(sectorScore: 40, stockScore: 85));
    }

    [Fact]
    public void BlendedStockScore_WeightsMomentumAndSector()
    {
        var high = SectorRotationCalculator.BlendedStockScore(90, 90, 3m, 110m);
        var low = SectorRotationCalculator.BlendedStockScore(40, 35, -2m, 80m);
        Assert.True(high > low);
        Assert.InRange(high, 0, 100);
    }

    [Fact]
    public void RegimeLabel_RiskOnWhenAboveEmaAndBreadth()
    {
        Assert.Equal("risk_on", SectorRotationCalculator.RegimeLabel(
            niftyAboveEma20: true, breadthPct: 60, niftyChangePct: 0.5m));
        Assert.Equal("risk_off", SectorRotationCalculator.RegimeLabel(
            niftyAboveEma20: false, breadthPct: 40, niftyChangePct: -0.5m));
    }

    private static MarketBarRow Bar(decimal close, long volume) => new()
    {
        InstrumentId = Guid.NewGuid(),
        TradeDate = DateOnly.FromDateTime(DateTime.UtcNow),
        Open = close,
        High = close,
        Low = close,
        Close = close,
        Volume = volume,
    };
}
