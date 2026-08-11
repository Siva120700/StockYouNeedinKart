using StockYouNeed.Application.Abstractions;
using StockYouNeed.Application.OptionsIntraday;
using StockYouNeed.Domain;

namespace StockYouNeed.Application.IndexOptions;

/// <summary>
/// Far OTM strike for Hero Zero — cheap premium, low delta lottery ticket on Nifty weekly options.
/// </summary>
public static class HeroZeroStrikeSelector
{
    /// <summary>Long-option |Δ| band for far OTM (cheap decay, high leverage).</summary>
    public const decimal MinDelta = 0.04m;
    public const decimal MaxDelta = 0.22m;
    public const decimal MinPremium = 8m;
    public const decimal MaxPremium = 45m;
    public const decimal MinTradeVolume = 200m;
    public const int MinOtmSteps = 2;
    public const int MaxOtmSteps = 5;

    public static OptionStrikeSelector.Candidate? SelectFarOtm(
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
            .ToList();
        if (chain.Count == 0 || spot <= 0)
            return null;

        var strikes = chain.Select(g => g.StrikePrice).Distinct().OrderBy(s => s).ToList();
        var atm = strikes.OrderBy(s => Math.Abs(s - spot)).First();
        var step = InferStep(strikes, atm);

        var candidates = new List<OptionStrikeSelector.Candidate>();
        for (var otmSteps = MinOtmSteps; otmSteps <= MaxOtmSteps; otmSteps++)
        {
            var strike = optType == "CE"
                ? atm + step * otmSteps
                : atm - step * otmSteps;
            if (!strikes.Contains(strike))
                continue;

            var g = chain.FirstOrDefault(x => x.StrikePrice == strike);
            if (g is null)
                continue;

            var longDelta = OptionStrikeSelector.ToLongOptionDelta(g.Delta);
            if (longDelta is null or < MinDelta or > MaxDelta)
                continue;
            if (g.TradeVolume is null or < MinTradeVolume)
                continue;

            var contract = optionContracts.FirstOrDefault(c =>
                c.Kind == "option"
                && c.OptionType == optType
                && c.ExpiryLabel.Equals(expiryLabel, StringComparison.OrdinalIgnoreCase)
                && c.Strike == strike);
            if (contract?.SymbolToken is null)
                continue;

            var score = ScoreFarOtm(longDelta.Value, g.TradeVolume, otmSteps);
            candidates.Add(new OptionStrikeSelector.Candidate(
                strike, optType, longDelta, g.Gamma, g.Theta, g.Vega, g.ImpliedVolatility,
                g.TradeVolume, score, contract));
        }

        return candidates
            .OrderByDescending(c => c.Score)
            .FirstOrDefault();
    }

    public static bool PremiumInBand(decimal premium) =>
        premium >= MinPremium && premium <= MaxPremium;

    private static decimal ScoreFarOtm(decimal longDelta, decimal? volume, int otmSteps)
    {
        // Prefer mid-band delta (~0.10) and higher volume; slightly prefer nearer OTM (more gamma).
        var deltaScore = 100m - Math.Abs(longDelta - 0.10m) * 400m;
        var volScore = Math.Min(40m, (decimal)Math.Log10((double)Math.Max(1m, volume ?? 0) + 1) * 12m);
        var proximityScore = Math.Max(0, 20 - (otmSteps - MinOtmSteps) * 4);
        return deltaScore + volScore + proximityScore;
    }

    private static decimal InferStep(List<decimal> strikes, decimal atm)
    {
        var neighbors = strikes
            .Select(s => Math.Abs(s - atm))
            .Where(d => d > 0)
            .OrderBy(d => d)
            .Take(2)
            .ToList();
        if (neighbors.Count == 0)
            return Math.Max(50m, Math.Round(atm * 0.01m));
        return neighbors[0];
    }
}
