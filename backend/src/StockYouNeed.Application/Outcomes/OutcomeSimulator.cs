using StockYouNeed.Domain;

namespace StockYouNeed.Application.Outcomes;

/// <summary>
/// Shared SL / target / time-stop walk used by historical backtest and live forward tracking.
/// Same-bar SL+target → SL (conservative).
/// </summary>
public static class OutcomeSimulator
{
    public const int DailyTimeStopBars = 20;
    public const int HourlyTimeStopBars = 40;

    public sealed record SimulatedOutcome(
        string Result,
        string? TargetLevel,
        decimal? TargetHitPct,
        decimal? ExitPrice,
        DateOnly? ExitDate,
        decimal? PnlPct,
        decimal? RMultiple);

    public static bool UsesHourlyBars(string strategy) =>
        strategy is "liquidity" or "liquidity_fresh" or "confluence" or "trade_score";

    public static int TimeStopBars(string strategy) =>
        UsesHourlyBars(strategy) ? HourlyTimeStopBars : DailyTimeStopBars;

    /// <summary>Walk forward bars; if SL and target hit same bar, count SL (conservative).</summary>
    public static SimulatedOutcome Simulate(
        string side,
        decimal entry,
        decimal sl,
        decimal? t1,
        decimal? t2,
        decimal? t3,
        List<(decimal High, decimal Low, decimal Close, DateOnly? Date, DateTimeOffset? Time)> forward)
    {
        var risk = Math.Abs(entry - sl);
        if (risk <= 0)
            risk = entry * 0.01m;

        decimal FavorPct(decimal price) =>
            side == SignalSides.Buy
                ? (price - entry) / entry * 100m
                : (entry - price) / entry * 100m;

        decimal RMult(decimal price) =>
            side == SignalSides.Buy
                ? (price - entry) / risk
                : (entry - price) / risk;

        decimal TargetPctOf(decimal target, decimal mfePrice)
        {
            var goal = Math.Abs(target - entry);
            if (goal <= 0) return 0;
            var move = side == SignalSides.Buy
                ? Math.Max(0, mfePrice - entry)
                : Math.Max(0, entry - mfePrice);
            return Math.Round(Math.Min(100m, move / goal * 100m), 2);
        }

        decimal mfe = entry;
        decimal mae = entry;

        for (var i = 0; i < forward.Count; i++)
        {
            var (high, low, close, date, _) = forward[i];
            if (side == SignalSides.Buy)
            {
                if (high > mfe) mfe = high;
                if (low < mae) mae = low;
            }
            else
            {
                if (low < mfe) mfe = low;
                if (high > mae) mae = high;
            }

            var hitSl = side == SignalSides.Buy ? low <= sl : high >= sl;
            string? hitLevel = null;
            decimal? hitPrice = null;
            if (t3 is decimal v3 && (side == SignalSides.Buy ? high >= v3 : low <= v3))
            {
                hitLevel = "t3";
                hitPrice = v3;
            }
            else if (t2 is decimal v2 && (side == SignalSides.Buy ? high >= v2 : low <= v2))
            {
                hitLevel = "t2";
                hitPrice = v2;
            }
            else if (t1 is decimal v1 && (side == SignalSides.Buy ? high >= v1 : low <= v1))
            {
                hitLevel = "t1";
                hitPrice = v1;
            }

            if (hitSl && hitLevel is not null)
            {
                return new SimulatedOutcome("sl", null, TargetPctOf(t1 ?? entry, mfe), sl, date,
                    Math.Round(FavorPct(sl), 4), Math.Round(RMult(sl), 4));
            }

            if (hitSl)
            {
                return new SimulatedOutcome("sl", null, 0m, sl, date,
                    Math.Round(FavorPct(sl), 4), Math.Round(RMult(sl), 4));
            }

            if (hitLevel is not null && hitPrice is decimal tp)
            {
                return new SimulatedOutcome("target", hitLevel, 100m, tp, date,
                    Math.Round(FavorPct(tp), 4), Math.Round(RMult(tp), 4));
            }
        }

        if (forward.Count == 0)
            return new SimulatedOutcome("time_stop", null, 0m, entry, null, 0m, 0m);

        var last = forward[^1];
        var exit = last.Close;
        var tHit = t1 is decimal tt ? TargetPctOf(tt, mfe) : 0m;
        return new SimulatedOutcome("time_stop", null, tHit, exit, last.Date,
            Math.Round(FavorPct(exit), 4), Math.Round(RMult(exit), 4));
    }
}
