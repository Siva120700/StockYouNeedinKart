namespace StockYouNeed.Application.TradeScore;

public sealed record TradeConfidenceBreakdown(
    int SignalsScore,
    int LiquidityScore,
    int BreakoutScore,
    int FuturesScore,
    int OptionsScore,
    int TotalScore,
    string Rating,
    IReadOnlyList<string> Reasons);

/// <summary>
/// Weighted confidence: Signals 40%, Liquidity 20%, Breakout 20%, Futures 10%, Options 10%.
/// </summary>
public static class TradeConfidenceScorer
{
    public const int WeightSignals = 20;
    public const int WeightLiquidity = 20;
    public const int WeightBreakout = 30;
    public const int WeightFutures = 15;
    public const int WeightOptions = 15;

    public static TradeConfidenceBreakdown Score(
        bool hasPrimarySignal,
        bool liquidityAligned,
        bool breakoutConfirmed,
        int futuresScore = 0,
        int optionsScore = 0)
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

        var total = signalsScore + liquidityScore + breakoutScore + futuresScore + optionsScore;
        var rating = Classify(total);
        return new TradeConfidenceBreakdown(
            signalsScore, liquidityScore, breakoutScore, futuresScore, optionsScore,
            total, rating, reasons);
    }

    public static string Classify(int score) => score switch
    {
        >= 90 => "strong_buy",
        >= 75 => "buy",
        >= 60 => "watch",
        >= 40 => "neutral",
        _ => "avoid"
    };

    public static string RatingLabel(string rating) => rating switch
    {
        "strong_buy" => "★★★★★ Strong Buy",
        "buy" => "★★★★ Buy",
        "watch" => "★★★ Watch",
        "neutral" => "Neutral",
        _ => "Avoid"
    };
}
