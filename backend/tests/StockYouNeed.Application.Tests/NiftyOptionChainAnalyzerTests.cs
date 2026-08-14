using StockYouNeed.Application.IndexOptions;
using StockYouNeed.Domain;
using Xunit;

namespace StockYouNeed.Application.Tests;

public class NiftyOptionChainAnalyzerTests
{
    [Fact]
    public void Build_ComputesPcrAndWalls()
    {
        var ladder = new[]
        {
            Row(24400, call: 10_000, put: 80_000),
            Row(24500, call: 20_000, put: 40_000),
            Row(24600, call: 90_000, put: 15_000),
            Row(24700, call: 50_000, put: 10_000),
        };

        var m = NiftyOptionChainAnalyzer.Build(24550, "13AUG2026", ladder);
        Assert.True(m.Usable);
        Assert.Equal(24400m, m.PutWallStrike);
        Assert.Equal(24600m, m.CallWallStrike);
        Assert.Equal(Math.Round(145000m / 170000m, 3), m.Pcr);
    }

    [Fact]
    public void EvaluateBreakout_Buy_ConfirmedWithPutSupportAndRoom()
    {
        var m = NiftyOptionChainAnalyzer.Build(24550, "13AUG2026", new[]
        {
            Row(24400, call: 5_000, put: 100_000),
            Row(24500, call: 20_000, put: 40_000),
            Row(24700, call: 90_000, put: 10_000),
            Row(24800, call: 30_000, put: 5_000),
        });

        var gate = NiftyOptionChainAnalyzer.EvaluateBreakout(SignalSides.Buy, m);
        Assert.True(gate.Confirmed);
    }

    [Fact]
    public void EvaluateBreakout_Buy_BlockedByCallWallOnNose()
    {
        var m = NiftyOptionChainAnalyzer.Build(24550, "13AUG2026", new[]
        {
            Row(24400, call: 5_000, put: 40_000),
            Row(24550, call: 200_000, put: 10_000), // wall at spot
            Row(24600, call: 20_000, put: 5_000),
            Row(24700, call: 15_000, put: 5_000),
        });

        var gate = NiftyOptionChainAnalyzer.EvaluateBreakout(SignalSides.Buy, m);
        Assert.False(gate.Confirmed);
        Assert.Contains(gate.Reasons, r => r.Contains("call wall", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EvaluateBreakout_Sell_ConfirmedWithCallResistance()
    {
        var m = NiftyOptionChainAnalyzer.Build(24550, "13AUG2026", new[]
        {
            Row(24300, call: 5_000, put: 20_000),
            Row(24400, call: 10_000, put: 30_000),
            Row(24700, call: 120_000, put: 8_000),
            Row(24800, call: 40_000, put: 5_000),
        });

        var gate = NiftyOptionChainAnalyzer.EvaluateBreakout(SignalSides.Sell, m);
        Assert.True(gate.Confirmed);
    }

    [Fact]
    public void EstimateMaxPain_PrefersOiBalanceStrike()
    {
        var ladder = new[]
        {
            Row(100, call: 10, put: 100),
            Row(110, call: 50, put: 50),
            Row(120, call: 100, put: 10),
        };
        var pain = NiftyOptionChainAnalyzer.EstimateMaxPain(ladder);
        Assert.Equal(110m, pain);
    }

    private static NiftyOptionChainAnalyzer.StrikeOi Row(decimal strike, long call, long put) =>
        new() { Strike = strike, CallOi = call, PutOi = put };
}
