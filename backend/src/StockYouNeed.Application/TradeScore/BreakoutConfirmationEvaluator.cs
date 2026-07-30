using StockYouNeed.Domain;

namespace StockYouNeed.Application.TradeScore;

/// <summary>Pattern breakout facade used by Breakout menu and Trade Score.</summary>
public static class BreakoutConfirmationEvaluator
{
    public sealed record Result(
        bool Confirmed,
        string Side,
        string PatternType,
        decimal Close,
        decimal BreakoutLevel,
        decimal VolumeRatio,
        decimal? PatternDepthPct);

    public static Result? Evaluate(List<MarketBarRow> barsDesc)
    {
        var match = PatternBreakoutEvaluator.Evaluate(barsDesc);
        if (match is null) return null;

        return new Result(
            match.Confirmed,
            match.Side,
            match.PatternType,
            match.Close,
            match.BreakoutLevel,
            match.VolumeRatio,
            match.PatternDepthPct);
    }

    public static string PatternLabel(string patternType) => patternType switch
    {
        "range_breakout" => "Range breakout",
        "ascending_triangle" => "Ascending triangle",
        "descending_triangle" => "Descending triangle",
        "double_bottom" => "Double bottom",
        "double_top" => "Double top",
        _ => "—"
    };
}
