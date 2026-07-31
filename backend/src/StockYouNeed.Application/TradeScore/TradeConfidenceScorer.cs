namespace StockYouNeed.Application.TradeScore;

public sealed record TradeConfidenceBreakdown(
    int SignalsScore,
    int LiquidityScore,
    int BreakoutScore,
    int FuturesScore,
    int OptionsScore,
    int TotalScore,
    string Rating,
    IReadOnlyList<string> Reasons)
{
    /// <summary>Points actually earned before scaling to the evaluated layers.</summary>
    public int RawScore { get; init; }

    /// <summary>Maximum points obtainable from the layers that were evaluated.</summary>
    public int AvailableWeight { get; init; }
}

/// <summary>
/// Weighted confidence: Signals 20, Liquidity 20, Breakout 30, Futures 15, Options 15.
/// Futures/Options are optional layers; when they are not evaluated the score is scaled
/// to the layers that were, so the rating bands stay reachable.
/// </summary>
public static class TradeConfidenceScorer
{
    public const int WeightSignals = 20;
    public const int WeightLiquidity = 20;
    public const int WeightBreakout = 30;
    public const int WeightFutures = 15;
    public const int WeightOptions = 15;

    /// <summary>No primary signal at all — nothing to grade.</summary>
    public const string RatingNoSetup = "no_setup";

    /// <summary>Primary signal present but no confirmation layer fired.</summary>
    public const string RatingUnconfirmed = "unconfirmed";

    public static TradeConfidenceBreakdown Score(
        bool hasPrimarySignal,
        bool liquidityAligned,
        bool breakoutConfirmed,
        int futuresScore = 0,
        int optionsScore = 0,
        bool futuresEvaluated = false,
        bool optionsEvaluated = false)
    {
        var reasons = new List<string>();
        var signalsScore = hasPrimarySignal ? WeightSignals : 0;
        if (hasPrimarySignal) reasons.Add("Bullish/Bearish Signal");

        var liquidityScore = liquidityAligned ? WeightLiquidity : 0;
        if (liquidityAligned) reasons.Add("Liquidity Fresh aligned");

        var breakoutScore = breakoutConfirmed ? WeightBreakout : 0;
        if (breakoutConfirmed) reasons.Add("Pattern breakout confirmed");

        futuresScore = Math.Clamp(futuresScore, 0, WeightFutures);
        if (futuresScore > 0) reasons.Add("Futures build-up");

        optionsScore = Math.Clamp(optionsScore, 0, WeightOptions);
        if (optionsScore > 0) reasons.Add("Option chain supportive");

        var raw = signalsScore + liquidityScore + breakoutScore + futuresScore + optionsScore;
        var available = WeightSignals + WeightLiquidity + WeightBreakout
            + (futuresEvaluated ? WeightFutures : 0)
            + (optionsEvaluated ? WeightOptions : 0);

        var total = available <= 0 ? 0 : (int)Math.Round(100m * raw / available);
        var confirmations = liquidityScore + breakoutScore + futuresScore + optionsScore;
        var rating = Rate(total, hasPrimarySignal, confirmations > 0);

        return new TradeConfidenceBreakdown(
            signalsScore, liquidityScore, breakoutScore, futuresScore, optionsScore,
            total, rating, reasons)
        {
            RawScore = raw,
            AvailableWeight = available,
        };
    }

    /// <summary>
    /// Grade a normalized 0–100 score. A missing primary signal is reported as
    /// <see cref="RatingNoSetup"/> and an unconfirmed signal as <see cref="RatingUnconfirmed"/>
    /// so neither is mistaken for a negative "avoid" call.
    /// </summary>
    public static string Rate(int normalizedScore, bool hasPrimarySignal, bool hasConfirmation)
    {
        if (!hasPrimarySignal) return RatingNoSetup;
        if (!hasConfirmation) return RatingUnconfirmed;
        return Classify(normalizedScore);
    }

    public static string Classify(int score) => score switch
    {
        >= 85 => "strong_buy",
        >= 70 => "buy",
        >= 55 => "watch",
        >= 35 => "neutral",
        _ => "avoid"
    };

    public static string RatingLabel(string rating) => rating switch
    {
        "strong_buy" => "★★★★★ Strong Buy",
        "buy" => "★★★★ Buy",
        "watch" => "★★★ Watch",
        "neutral" => "Neutral",
        RatingUnconfirmed => "Unconfirmed — signal only",
        RatingNoSetup => "No setup",
        _ => "Avoid"
    };
}
