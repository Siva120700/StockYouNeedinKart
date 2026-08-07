using StockYouNeed.Application.Services;

namespace StockYouNeed.Application.Tests;

public class BreakoutSignalVolumeProjectionTests
{
    [Fact]
    public void EffectiveVolumeForGate_projects_during_live_session()
    {
        var today = DateOnly.FromDateTime(
            DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(5.5)).DateTime);
        var now = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(5.5));
        var open = new TimeOnly(9, 15);
        var close = new TimeOnly(15, 30);
        var t = TimeOnly.FromDateTime(now.DateTime);

        // Only assert the live-projection path while the cash market is open.
        if (t <= open || t >= close)
            return;

        var projected = BreakoutSignalEvaluator.EffectiveVolumeForGate(10_000, today, projectPartialSession: true);
        var elapsed = Math.Max(1, (t.ToTimeSpan() - open.ToTimeSpan()).TotalMinutes);
        var expectedLow = (long)Math.Round(10_000 * (375.0 / (elapsed + 0.05)), MidpointRounding.AwayFromZero);
        var expectedHigh = (long)Math.Round(10_000 * (375.0 / Math.Max(1, elapsed - 0.05)), MidpointRounding.AwayFromZero);
        Assert.InRange(projected, Math.Min(expectedLow, expectedHigh), Math.Max(expectedLow, expectedHigh));
        Assert.True(projected > 10_000);

        // A thin partial-session print scales by (375 / elapsed minutes), regardless of time of day.
        var earlyPrint = 40_000L;
        var projectedEarly = BreakoutSignalEvaluator.EffectiveVolumeForGate(earlyPrint, today, true);
        var scale = 375.0 / elapsed;
        Assert.InRange(projectedEarly, (long)(earlyPrint * scale * 0.98), (long)(earlyPrint * scale * 1.02) + 1);

        // Historical / completed days stay raw.
        Assert.Equal(10_000, BreakoutSignalEvaluator.EffectiveVolumeForGate(10_000, today.AddDays(-1), true));
        Assert.Equal(10_000, BreakoutSignalEvaluator.EffectiveVolumeForGate(10_000, today, projectPartialSession: false));
    }
}
