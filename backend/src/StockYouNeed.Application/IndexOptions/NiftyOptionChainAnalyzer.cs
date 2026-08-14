using StockYouNeed.Domain;

namespace StockYouNeed.Application.IndexOptions;

/// <summary>
/// PCR, OI walls, max pain, and breakout confirmation from a Nifty CE/PE OI ladder.
/// </summary>
public static class NiftyOptionChainAnalyzer
{
    /// <summary>Min distance from spot to opposite wall as fraction of spot (~0.25%).</summary>
    public const decimal MinRoomPct = 0.0025m;
    public const decimal MinBuyPcr = 0.75m;
    public const decimal MaxSellPcr = 1.35m;

    public sealed class StrikeOi
    {
        public decimal Strike { get; init; }
        public long CallOi { get; init; }
        public long PutOi { get; init; }
        public decimal? CallLtp { get; init; }
        public decimal? PutLtp { get; init; }
    }

    public sealed class Metrics
    {
        public decimal Spot { get; init; }
        public string ExpiryLabel { get; init; } = "";
        public long TotalCallOi { get; init; }
        public long TotalPutOi { get; init; }
        public decimal? Pcr { get; init; }
        public decimal? CallWallStrike { get; init; }
        public long CallWallOi { get; init; }
        public decimal? PutWallStrike { get; init; }
        public long PutWallOi { get; init; }
        public decimal? MaxPainStrike { get; init; }
        public int StrikeCount { get; init; }
        public IReadOnlyList<StrikeOi> Ladder { get; init; } = Array.Empty<StrikeOi>();
        public bool Usable => StrikeCount >= 4 && TotalCallOi + TotalPutOi > 0;
    }

    public sealed class GateResult
    {
        public bool Confirmed { get; init; }
        public string Summary { get; init; } = "";
        public IReadOnlyList<string> Reasons { get; init; } = Array.Empty<string>();
    }

    public static Metrics Build(decimal spot, string expiryLabel, IReadOnlyList<StrikeOi> ladder)
    {
        var rows = ladder
            .Where(r => r.Strike > 0 && (r.CallOi > 0 || r.PutOi > 0))
            .OrderBy(r => r.Strike)
            .ToList();

        long totalCall = rows.Sum(r => r.CallOi);
        long totalPut = rows.Sum(r => r.PutOi);
        decimal? pcr = totalCall > 0
            ? Math.Round((decimal)totalPut / totalCall, 3, MidpointRounding.AwayFromZero)
            : null;

        StrikeOi? callWall = rows.Where(r => r.CallOi > 0).OrderByDescending(r => r.CallOi).FirstOrDefault();
        StrikeOi? putWall = rows.Where(r => r.PutOi > 0).OrderByDescending(r => r.PutOi).FirstOrDefault();

        return new Metrics
        {
            Spot = spot,
            ExpiryLabel = expiryLabel,
            TotalCallOi = totalCall,
            TotalPutOi = totalPut,
            Pcr = pcr,
            CallWallStrike = callWall?.Strike,
            CallWallOi = callWall?.CallOi ?? 0,
            PutWallStrike = putWall?.Strike,
            PutWallOi = putWall?.PutOi ?? 0,
            MaxPainStrike = EstimateMaxPain(rows),
            StrikeCount = rows.Count,
            Ladder = rows,
        };
    }

    /// <summary>
    /// BUY needs put support below + room above call wall (or put-dominated OI) + PCR not call-extreme.
    /// SELL needs call resistance above + room below put wall (or call-dominated) + PCR not put-extreme.
    /// </summary>
    public static GateResult EvaluateBreakout(string side, Metrics m)
    {
        var reasons = new List<string>();
        if (!m.Usable)
        {
            return new GateResult
            {
                Confirmed = false,
                Summary = "Option chain OI unavailable or too thin",
                Reasons = new[] { "Need CE/PE open interest on nearest expiry" },
            };
        }

        reasons.Add(
            $"Chain {m.ExpiryLabel}: PCR {m.Pcr?.ToString("0.00") ?? "—"} · " +
            $"put wall {FmtStrike(m.PutWallStrike)} ({m.PutWallOi:N0}) · " +
            $"call wall {FmtStrike(m.CallWallStrike)} ({m.CallWallOi:N0})" +
            (m.MaxPainStrike is decimal mp ? $" · max pain {mp:0}" : ""));

        var buy = side.Equals(SignalSides.Buy, StringComparison.OrdinalIgnoreCase);
        var room = m.Spot * MinRoomPct;

        if (buy)
        {
            var putSupport = m.PutWallStrike is decimal putBelow && putBelow < m.Spot;
            if (!putSupport)
            {
                reasons.Add("BUY blocked: put wall not below spot (no OI support)");
                return Fail("Chain conflicts with BUY breakout", reasons);
            }

            var callWall = m.CallWallStrike;
            var callClear = callWall is not decimal callAbove
                || callAbove >= m.Spot + room
                || m.CallWallOi < m.PutWallOi * 0.85m;
            if (!callClear)
            {
                reasons.Add(
                    $"BUY blocked: call wall {callWall:0} jammed overhead (within {MinRoomPct * 100:0.##}% of spot)");
                return Fail("Chain conflicts with BUY breakout", reasons);
            }

            if (m.Pcr is decimal pcr && pcr < MinBuyPcr)
            {
                reasons.Add($"BUY blocked: PCR {pcr:0.00} < {MinBuyPcr:0.00} (call-heavy)");
                return Fail("Chain conflicts with BUY breakout", reasons);
            }

            reasons.Add("Chain confirms BUY: put support below + overhead clear");
            return new GateResult { Confirmed = true, Summary = "Chain confirms BUY", Reasons = reasons };
        }

        var callResist = m.CallWallStrike is decimal cwall && cwall > m.Spot;
        if (!callResist)
        {
            reasons.Add("SELL blocked: call wall not above spot (no OI resistance)");
            return Fail("Chain conflicts with SELL breakout", reasons);
        }

        var putWall = m.PutWallStrike;
        var putClear = putWall is not decimal putUnder
            || putUnder <= m.Spot - room
            || m.PutWallOi < m.CallWallOi * 0.85m;
        if (!putClear)
        {
            reasons.Add(
                $"SELL blocked: put wall {putWall:0} jammed underneath (within {MinRoomPct * 100:0.##}% of spot)");
            return Fail("Chain conflicts with SELL breakout", reasons);
        }

        if (m.Pcr is decimal sellPcr && sellPcr > MaxSellPcr)
        {
            reasons.Add($"SELL blocked: PCR {sellPcr:0.00} > {MaxSellPcr:0.00} (put-heavy)");
            return Fail("Chain conflicts with SELL breakout", reasons);
        }

        reasons.Add("Chain confirms SELL: call resistance above + downside clear");
        return new GateResult { Confirmed = true, Summary = "Chain confirms SELL", Reasons = reasons };
    }

    /// <summary>Strike that minimizes total intrinsic payoff across call+put OI (classic max pain).</summary>
    public static decimal? EstimateMaxPain(IReadOnlyList<StrikeOi> ladder)
    {
        if (ladder.Count == 0) return null;
        var strikes = ladder.Select(r => r.Strike).Distinct().OrderBy(s => s).ToList();
        decimal? best = null;
        decimal bestPain = decimal.MaxValue;
        foreach (var test in strikes)
        {
            decimal pain = 0;
            foreach (var row in ladder)
            {
                if (test > row.Strike)
                    pain += (test - row.Strike) * row.CallOi;
                if (test < row.Strike)
                    pain += (row.Strike - test) * row.PutOi;
            }
            if (pain < bestPain)
            {
                bestPain = pain;
                best = test;
            }
        }
        return best;
    }

    private static GateResult Fail(string summary, List<string> reasons) =>
        new() { Confirmed = false, Summary = summary, Reasons = reasons };

    private static string FmtStrike(decimal? s) => s is decimal v ? v.ToString("0") : "—";
}
