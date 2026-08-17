using StockYouNeed.Application.TradeScore;
using StockYouNeed.Domain;

namespace StockYouNeed.Application.Signals;

/// <summary>
/// StepOne TradeGen–style intraday/daily composite momentum (0–10).
/// RVOL, relative strength vs Nifty, ATR expansion, EMA trend stack, session change.
/// </summary>
public static class MomentumScoreV2Evaluator
{
    private const int VolLookback = 20;
    private const int RsLookback = 21;

    public static decimal? Score(
        string side,
        IReadOnlyList<MarketBarRow> stockBarsNewestFirst,
        IReadOnlyList<MarketBarRow>? niftyBarsNewestFirst,
        decimal? livePrice = null)
    {
        if (stockBarsNewestFirst.Count < VolLookback + 2)
            return null;

        var chron = MomentumScoreHelpers.ToChronological(stockBarsNewestFirst);
        var isBuy = side == SignalSides.Buy;
        var latest = chron[^1];
        var price = livePrice is > 0 ? livePrice.Value : latest.Close;
        if (price <= 0)
            return null;

        var total = 0m;

        // Session / day change aligned with side (0–2)
        if (chron.Count >= 2)
        {
            var prev = chron[^2].Close;
            if (prev > 0)
            {
                var dayPct = (price - prev) / prev * 100m;
                var aligned = isBuy ? dayPct : -dayPct;
                total += aligned switch
                {
                    >= 2.5m => 2.0m,
                    >= 1.5m => 1.6m,
                    >= 0.75m => 1.2m,
                    >= 0.25m => 0.8m,
                    >= 0m => 0.4m,
                    >= -0.5m => 0.2m,
                    _ => 0m,
                };
            }
        }

        // RVOL vs 20-day average (0–2)
        var recentVol = stockBarsNewestFirst.Take(VolLookback).Average(b => (double)b.Volume);
        var todayVol = (double)latest.Volume;
        if (recentVol > 0)
        {
            var rvol = todayVol / recentVol;
            total += rvol switch
            {
                >= 2.5 => 2.0m,
                >= 2.0 => 1.7m,
                >= 1.5 => 1.4m,
                >= 1.2 => 1.0m,
                >= 1.0 => 0.6m,
                >= 0.75 => 0.3m,
                _ => 0m,
            };
        }

        // Relative strength vs Nifty ~1 month (0–2)
        if (niftyBarsNewestFirst is { Count: >= RsLookback + 1 })
        {
            var stockRet = MomentumScoreHelpers.ReturnBetween(chron, 0, RsLookback);
            var niftyChron = MomentumScoreHelpers.ToChronological(niftyBarsNewestFirst);
            var niftyRet = MomentumScoreHelpers.ReturnBetween(niftyChron, 0, RsLookback);
            if (stockRet is decimal sr && niftyRet is decimal nr)
            {
                var spread = (sr - nr) * 100m;
                var aligned = isBuy ? spread : -spread;
                total += aligned switch
                {
                    >= 8m => 2.0m,
                    >= 5m => 1.6m,
                    >= 3m => 1.2m,
                    >= 1m => 0.8m,
                    >= 0m => 0.4m,
                    >= -2m => 0.2m,
                    _ => 0m,
                };
            }
        }

        // ATR expansion — volatility breakout (0–1.5)
        if (TechnicalIndicators.AtrExpansion(chron, period: 14, lookback: 5))
            total += 1.5m;
        else if (TechnicalIndicators.Atr(chron, 14) is decimal atr && atr > 0)
        {
            var range = latest.High - latest.Low;
            if (range / atr >= 1.2m)
                total += 0.8m;
            else if (range / atr >= 0.9m)
                total += 0.4m;
        }

        // EMA trend stack (0–1.5)
        var ema20 = TechnicalIndicators.Ema(chron, 20);
        var ema50 = TechnicalIndicators.Ema(chron, 50);
        if (ema20 is decimal e20 && ema50 is decimal e50)
        {
            if (isBuy)
            {
                if (price > e20 && e20 > e50) total += 1.5m;
                else if (price > e20) total += 0.75m;
            }
            else
            {
                if (price < e20 && e20 < e50) total += 1.5m;
                else if (price < e20) total += 0.75m;
            }
        }

        return MomentumScoreHelpers.ClampScore(total);
    }
}
