using StockYouNeed.Application.Abstractions;
using StockYouNeed.Domain;

namespace StockYouNeed.Application.OptionsIntraday;

/// <summary>
/// Pick ATM / 1 ITM CE or PE using Angel optionGreek ranking.
/// We always BUY the option (CE on stock buy, PE on stock sell).
/// Long-option delta is stored/shown as a positive magnitude (0.45–0.60 ideal).
/// Angel quotes put contract delta as negative; that sign is for the contract, not "sell PE".
/// Selling/writing an option would flip the position delta sign — we do not write options here.
/// </summary>
public static class OptionStrikeSelector
{
    public sealed record Candidate(
        decimal Strike,
        string OptionType,
        decimal? Delta,
        decimal? Gamma,
        decimal? Theta,
        decimal? Vega,
        decimal? Iv,
        decimal? Volume,
        decimal Score,
        NfoContractRow? Contract);

    public static (Candidate? Primary, Candidate? Alternative) Select(
        string side,
        decimal spot,
        IReadOnlyList<AngelOptionGreek> greeks,
        IReadOnlyList<NfoContractRow> optionContracts,
        string expiryLabel)
    {
        var optType = side.Equals(SignalSides.Sell, StringComparison.OrdinalIgnoreCase) ? "PE" : "CE";
        var chain = greeks
            .Where(g => g.OptionType.Equals(optType, StringComparison.OrdinalIgnoreCase))
            .Where(g => g.StrikePrice > 0)
            .Where(g => HasExpectedContractSign(optType, g.Delta))
            .ToList();
        if (chain.Count == 0) return (null, null);

        var strikes = chain.Select(g => g.StrikePrice).Distinct().OrderBy(s => s).ToList();
        var atm = strikes.OrderBy(s => Math.Abs(s - spot)).First();
        // Reject absurd ATM (e.g. 240 vs spot 293).
        if (spot > 0 && Math.Abs(atm - spot) / spot > 0.04m)
            return (null, null);

        var step = InferStep(strikes, atm);
        var itm = optType == "CE" ? atm - step : atm + step;
        if (!strikes.Contains(itm))
        {
            itm = optType == "CE"
                ? strikes.Where(s => s < atm).DefaultIfEmpty(atm).Max()
                : strikes.Where(s => s > atm).DefaultIfEmpty(atm).Min();
        }

        var candidates = new List<Candidate>();
        foreach (var strike in new[] { atm, itm }.Distinct())
        {
            if (spot > 0 && Math.Abs(strike - spot) / spot > 0.06m)
                continue;

            var g = chain.FirstOrDefault(x => x.StrikePrice == strike);
            if (g is null) continue;
            var contract = optionContracts.FirstOrDefault(c =>
                c.Kind == "option"
                && c.OptionType == optType
                && c.ExpiryLabel.Equals(expiryLabel, StringComparison.OrdinalIgnoreCase)
                && c.Strike == strike);
            var longDelta = ToLongOptionDelta(g.Delta);
            var score = RankScore(longDelta, g.ImpliedVolatility, g.TradeVolume);
            candidates.Add(new Candidate(
                strike, optType, longDelta, g.Gamma, g.Theta, g.Vega, g.ImpliedVolatility, g.TradeVolume,
                score, contract));
        }

        var ordered = candidates.OrderByDescending(c => c.Score).ToList();
        if (ordered.Count == 0) return (null, null);

        // Long option: positive delta band 0.45–0.60 preferred.
        var inBand = ordered.Where(c => c.Delta is >= 0.45m and <= 0.60m).ToList();
        var pool = inBand.Count > 0 ? inBand : ordered.Where(c => c.Delta is >= 0.35m and <= 0.70m).ToList();
        if (pool.Count == 0) pool = ordered;

        var primary = pool[0];
        var alt = pool.Skip(1).FirstOrDefault() ?? ordered.Skip(1).FirstOrDefault();
        return (primary, alt);
    }

    /// <summary>
    /// Angel CE delta ≥ 0, PE delta ≤ 0. We only buy options, so expose +|Δ|.
    /// </summary>
    public static decimal? ToLongOptionDelta(decimal? rawContractDelta)
        => rawContractDelta is null ? null : Math.Abs(rawContractDelta.Value);

    /// <summary>CE must not have negative Δ; PE must not have positive Δ (vendor sanity).</summary>
    private static bool HasExpectedContractSign(string optType, decimal? delta)
    {
        if (delta is null) return true;
        if (optType == "CE") return delta >= 0;
        if (optType == "PE") return delta <= 0;
        return true;
    }

    private static decimal InferStep(List<decimal> strikes, decimal atm)
    {
        var neighbors = strikes
            .Select(s => Math.Abs(s - atm))
            .Where(d => d > 0)
            .OrderBy(d => d)
            .Take(2)
            .ToList();
        if (neighbors.Count == 0) return Math.Max(5m, Math.Round(atm * 0.01m));
        return neighbors[0];
    }

    private static decimal RankScore(decimal? longDelta, decimal? ivRaw, decimal? volume)
    {
        var absD = longDelta ?? 0;
        var deltaScore = absD <= 0 ? 0 : Math.Max(0, 100m - Math.Abs(absD - 0.52m) * 200m);
        if (absD is < 0.35m or > 0.70m) deltaScore *= 0.4m;

        var iv = ivRaw ?? 25m;
        var ivScore = iv is >= 10 and <= 35 ? 30m : iv is >= 8 and <= 45 ? 15m : 5m;

        var vol = volume ?? 0;
        var volScore = Math.Min(30m, (decimal)Math.Log10((double)Math.Max(1m, vol) + 1) * 10m);

        return deltaScore + ivScore + volScore;
    }
}
