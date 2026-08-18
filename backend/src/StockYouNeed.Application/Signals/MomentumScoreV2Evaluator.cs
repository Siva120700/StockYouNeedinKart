using StockYouNeed.Application.TradeScore;
using StockYouNeed.Domain;

namespace StockYouNeed.Application.Signals;

/// <summary>
/// Composite stock momentum /10: trend (1H+Daily) + 5D/20D returns + RVOL + RSI + ATR breakout + candle strength.
/// Separate from liquidity/structure — ranks breakout setups; does not hide low scores.
/// </summary>
public static class MomentumScoreV2Evaluator
{
    private const int VolLookback = 20;
    private const int MinDailyBars = 25;

    public sealed class IntradayContext
    {
        public IReadOnlyList<MarketIntradayBarRow>? Bars1hNewestFirst { get; init; }
        public IReadOnlyList<MarketIntradayBarRow>? Bars15mNewestFirst { get; init; }
    }

    /// <summary>UI / export tier from score /10.</summary>
    public static string TierLabel(decimal score) => score switch
    {
        >= 8m => "Strong",
        >= 6m => "Good",
        >= 4m => "Average",
        _ => "Weak",
    };

    public static decimal? Score(
        string side,
        IReadOnlyList<MarketBarRow> stockBarsNewestFirst,
        IReadOnlyList<MarketBarRow>? niftyBarsNewestFirst = null,
        decimal? livePrice = null,
        IntradayContext? intraday = null)
        => ScoreCore(side, stockBarsNewestFirst, livePrice, intraday);

    private static decimal? ScoreCore(
        string side,
        IReadOnlyList<MarketBarRow> stockBarsNewestFirst,
        decimal? livePrice,
        IntradayContext? intraday)
    {
        if (stockBarsNewestFirst.Count < MinDailyBars)
            return null;

        var dailyChron = MomentumScoreHelpers.ToChronological(stockBarsNewestFirst);
        var h1Chron = intraday?.Bars1hNewestFirst is { Count: > 0 }
            ? MomentumScoreHelpers.ToChronological(intraday.Bars1hNewestFirst)
            : null;
        var m15Chron = intraday?.Bars15mNewestFirst is { Count: > 0 }
            ? MomentumScoreHelpers.ToChronological(intraday.Bars15mNewestFirst)
            : null;

        var isBuy = side == SignalSides.Buy;
        var latest = dailyChron[^1];
        var price = livePrice is > 0 ? livePrice.Value : latest.Close;
        if (price <= 0)
            return null;

        var total =
            ScoreTrend(isBuy, dailyChron, h1Chron)
            + ScorePriceMomentum(isBuy, dailyChron)
            + ScoreRelativeVolume(dailyChron)
            + ScoreRsi(isBuy, dailyChron, h1Chron, m15Chron)
            + ScoreBreakoutStrength(isBuy, price, stockBarsNewestFirst, dailyChron)
            + ScoreCandleStrength(isBuy, h1Chron, dailyChron);

        return MomentumScoreHelpers.ClampScore(total);
    }

    /// <summary>1H + Daily EMA20 vs EMA50 — each worth 1 pt (independent).</summary>
    private static decimal ScoreTrend(
        bool isBuy,
        IReadOnlyList<MarketBarRow> dailyChron,
        IReadOnlyList<MarketBarRow>? h1Chron)
    {
        var score = 0m;
        score += ScoreEmaStack(isBuy, dailyChron, minBars: 50);
        if (h1Chron is not null)
            score += ScoreEmaStack(isBuy, h1Chron, minBars: 50);
        return score;
    }

    private static decimal ScoreEmaStack(bool isBuy, IReadOnlyList<MarketBarRow> chron, int minBars)
    {
        if (chron.Count < minBars)
            return 0m;

        var ema20 = TechnicalIndicators.Ema(chron, 20);
        var ema50 = TechnicalIndicators.Ema(chron, 50);
        if (ema20 is not decimal e20 || ema50 is not decimal e50)
            return 0m;

        if (isBuy && e20 > e50)
            return 1m;
        if (!isBuy && e20 < e50)
            return 1m;
        return 0m;
    }

    /// <summary>5D + 20D simple returns (1 pt each).</summary>
    private static decimal ScorePriceMomentum(bool isBuy, IReadOnlyList<MarketBarRow> dailyChron)
    {
        var score = 0m;
        var ret5 = MomentumScoreHelpers.ReturnBetween(dailyChron, 0, 5);
        var ret20 = MomentumScoreHelpers.ReturnBetween(dailyChron, 0, 20);
        if (ret5 is decimal r5)
            score += ScoreReturnBucket(isBuy, r5 * 100m, isShort: true);
        if (ret20 is decimal r20)
            score += ScoreReturnBucket(isBuy, r20 * 100m, isShort: false);
        return score;
    }

    private static decimal ScoreReturnBucket(bool isBuy, decimal pct, bool isShort)
    {
        var aligned = isBuy ? pct : -pct;
        if (isShort)
        {
            return aligned switch
            {
                < 0m => 0m,
                < 2m => 0.25m,
                < 4m => 0.5m,
                < 7m => 0.75m,
                _ => 1m,
            };
        }

        return aligned switch
        {
            < 0m => 0m,
            < 3m => 0.25m,
            < 6m => 0.5m,
            < 10m => 0.75m,
            _ => 1m,
        };
    }

    private static decimal ScoreRelativeVolume(IReadOnlyList<MarketBarRow> dailyChron)
    {
        if (dailyChron.Count < VolLookback + 1)
            return 0m;

        var todayVol = (double)dailyChron[^1].Volume;
        var avgVol = dailyChron.Skip(dailyChron.Count - VolLookback - 1).Take(VolLookback)
            .Average(b => (double)b.Volume);
        if (avgVol <= 0)
            return 0m;

        var rvol = todayVol / avgVol;
        return rvol switch
        {
            < 0.75 => 0m,
            < 1.0 => 0.5m,
            < 1.25 => 1.0m,
            < 1.75 => 1.5m,
            _ => 2.0m,
        };
    }

    /// <summary>1H + 15M RSI level/direction (0.75 each); falls back to daily when intraday missing.</summary>
    private static decimal ScoreRsi(
        bool isBuy,
        IReadOnlyList<MarketBarRow> dailyChron,
        IReadOnlyList<MarketBarRow>? h1Chron,
        IReadOnlyList<MarketBarRow>? m15Chron)
    {
        var score = 0m;
        if (h1Chron is { Count: >= 18 })
            score += ScoreRsiSingle(isBuy, h1Chron, weight: 0.75m);
        else if (dailyChron.Count >= 18)
            score += ScoreRsiSingle(isBuy, dailyChron, weight: 0.75m);

        if (m15Chron is { Count: >= 18 })
            score += ScoreRsiSingle(isBuy, m15Chron, weight: 0.75m);
        else if (h1Chron is { Count: >= 18 })
            score += ScoreRsiSingle(isBuy, h1Chron, weight: 0.75m);
        else if (dailyChron.Count >= 18)
            score += ScoreRsiSingle(isBuy, dailyChron, weight: 0.75m);

        return Math.Min(1.5m, score);
    }

    private static decimal ScoreRsiSingle(bool isBuy, IReadOnlyList<MarketBarRow> chron, decimal weight)
    {
        var rsi = TechnicalIndicators.Rsi(chron, 14);
        if (rsi is not decimal now)
            return 0m;

        var priorChron = chron.Take(chron.Count - 3).ToList();
        var prev = priorChron.Count >= 15 ? TechnicalIndicators.Rsi(priorChron, 14) : null;
        var rising = prev is decimal p && now > p;
        var falling = prev is decimal f && now < f;

        if (isBuy)
        {
            if (now > 72m && falling)
                return 0m;
            if (now < 45m)
                return 0m;
            if (now >= 55m && now <= 65m && rising)
                return weight;
            if (now >= 50m && now < 55m && rising)
                return weight * (2m / 3m);
            if (now > 65m && now <= 72m && rising)
                return weight * (2m / 3m);
            if (now >= 45m && now < 50m && rising)
                return weight * (1m / 3m);
            return 0m;
        }

        // Bearish — mirror bands
        if (now < 28m && rising)
            return 0m;
        if (now > 55m)
            return 0m;
        if (now >= 35m && now <= 45m && falling)
            return weight;
        if (now > 45m && now <= 50m && falling)
            return weight * (2m / 3m);
        if (now >= 28m && now < 35m && falling)
            return weight * (2m / 3m);
        if (now > 50m && now <= 55m && falling)
            return weight * (1m / 3m);
        return 0m;
    }

    /// <summary>Breakout distance from 2-day structure, normalized by daily ATR.</summary>
    private static decimal ScoreBreakoutStrength(
        bool isBuy,
        decimal price,
        IReadOnlyList<MarketBarRow> dailyNewestFirst,
        IReadOnlyList<MarketBarRow> dailyChron)
    {
        var prev = dailyNewestFirst.Skip(1).Take(2).ToList();
        if (prev.Count < 2)
            return 0m;

        var last2High = prev.Max(b => b.High);
        var last2Low = prev.Min(b => b.Low);
        var atr = TechnicalIndicators.Atr(dailyChron, 14);
        if (atr is not decimal a || a <= 0)
            return 0m;

        var distance = isBuy ? price - last2High : last2Low - price;
        if (distance <= 0)
            return 0m;

        var ratio = distance / a;
        return ratio switch
        {
            < 0.1m => 0m,
            < 0.25m => 0.5m,
            < 0.5m => 1.0m,
            _ => 1.5m,
        };
    }

    /// <summary>Close position in current bar (prefer 1H, else daily).</summary>
    private static decimal ScoreCandleStrength(
        bool isBuy,
        IReadOnlyList<MarketBarRow>? h1Chron,
        IReadOnlyList<MarketBarRow> dailyChron)
    {
        var bar = h1Chron is { Count: > 0 } ? h1Chron[^1] : dailyChron[^1];
        var range = bar.High - bar.Low;
        if (range <= 0)
            return 0m;

        var position = isBuy
            ? (bar.Close - bar.Low) / range
            : (bar.High - bar.Close) / range;

        return position switch
        {
            < 0.5m => 0m,
            < 0.65m => 0.5m,
            < 0.8m => 0.75m,
            _ => 1m,
        };
    }
}
