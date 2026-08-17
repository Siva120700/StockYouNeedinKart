using StockYouNeed.Application.TradeScore;

using StockYouNeed.Domain;



namespace StockYouNeed.Application.Signals;



/// <summary>

/// Research-backed Jegadeesh–Titman multi-horizon momentum (0–10).

/// 12–1 / 6–1 / 3–1 cross-sectional ranks + RS vs Nifty + EMA trend + liquidity.

/// </summary>

public static class MomentumScoreV3Evaluator

{

    public static decimal? Score(

        string side,

        Guid instrumentId,

        IReadOnlyList<MarketBarRow> stockBarsNewestFirst,

        IReadOnlyList<MarketBarRow>? niftyBarsNewestFirst,

        IReadOnlyDictionary<Guid, decimal> pct12_1,

        IReadOnlyDictionary<Guid, decimal> pct6_1,

        IReadOnlyDictionary<Guid, decimal> pct3_1,

        IReadOnlyDictionary<Guid, decimal> liquidityPct)

    {

        if (stockBarsNewestFirst.Count < MomentumScoreHelpers.TradingDays12M + 1)

            return null;



        var isBuy = side == SignalSides.Buy;

        var total = 0m;



        if (pct12_1.TryGetValue(instrumentId, out var p12))

            total += PercentileToPoints12(MomentumScoreHelpers.AlignPercentileForSide(p12, isBuy));



        if (pct6_1.TryGetValue(instrumentId, out var p6))

            total += PercentileToPoints6(MomentumScoreHelpers.AlignPercentileForSide(p6, isBuy));



        if (pct3_1.TryGetValue(instrumentId, out var p3))

            total += PercentileToPoints3(MomentumScoreHelpers.AlignPercentileForSide(p3, isBuy));



        total += RelativeStrengthPoints(stockBarsNewestFirst, niftyBarsNewestFirst, isBuy);

        total += TrendPoints(stockBarsNewestFirst, isBuy);

        total += LiquidityPoints(instrumentId, liquidityPct);



        return MomentumScoreHelpers.ClampScore(total);

    }



    /// <summary>

    /// Single-stock V3 when cross-sectional universe is unavailable (backtest / analyze).

    /// </summary>

    public static decimal? ScoreSingleStock(

        string side,

        IReadOnlyList<MarketBarRow> stockBarsNewestFirst,

        IReadOnlyList<MarketBarRow>? niftyBarsNewestFirst)

    {

        if (stockBarsNewestFirst.Count < MomentumScoreHelpers.TradingDays12M + 1)

            return null;



        var isBuy = side == SignalSides.Buy;

        var chron = MomentumScoreHelpers.ToChronological(stockBarsNewestFirst);

        var total = 0m;



        var r12 = MomentumScoreHelpers.ReturnBetween(

            chron, MomentumScoreHelpers.SkipRecentTradingDays, MomentumScoreHelpers.TradingDays12M);

        var r6 = MomentumScoreHelpers.ReturnBetween(

            chron, MomentumScoreHelpers.SkipRecentTradingDays, MomentumScoreHelpers.TradingDays6M);

        var r3 = MomentumScoreHelpers.ReturnBetween(

            chron, MomentumScoreHelpers.SkipRecentTradingDays, MomentumScoreHelpers.TradingDays3M);



        if (r12 is decimal d12)

            total += PercentileToPoints12(ReturnPctToSyntheticPercentile12(AlignedReturnPct(d12, isBuy)));

        if (r6 is decimal d6)

            total += PercentileToPoints6(ReturnPctToSyntheticPercentile6(AlignedReturnPct(d6, isBuy)));

        if (r3 is decimal d3)

            total += PercentileToPoints3(ReturnPctToSyntheticPercentile3(AlignedReturnPct(d3, isBuy)));



        total += RelativeStrengthPoints(stockBarsNewestFirst, niftyBarsNewestFirst, isBuy);

        total += TrendPoints(stockBarsNewestFirst, isBuy);

        total += SelfLiquidityPoints(chron);



        return MomentumScoreHelpers.ClampScore(total);

    }



    private static decimal AlignedReturnPct(decimal returnFraction, bool isBuy)

    {

        var pct = returnFraction * 100m;

        return isBuy ? pct : -pct;

    }



    private static decimal ReturnPctToSyntheticPercentile12(decimal alignedReturnPct) => alignedReturnPct switch
    {
        >= 30m => 92m,
        >= 20m => 82m,
        >= 14m => 72m,
        >= 9m => 62m,
        >= 4m => 52m,
        >= 0m => 42m,
        >= -5m => 32m,
        _ => 20m,
    };

    private static decimal ReturnPctToSyntheticPercentile6(decimal alignedReturnPct) => alignedReturnPct switch
    {
        >= 18m => 92m,
        >= 12m => 82m,
        >= 8m => 72m,
        >= 4m => 62m,
        >= 1m => 52m,
        >= -2m => 42m,
        >= -6m => 32m,
        _ => 20m,
    };

    private static decimal ReturnPctToSyntheticPercentile3(decimal alignedReturnPct) => alignedReturnPct switch
    {
        >= 10m => 92m,
        >= 6m => 82m,
        >= 3m => 72m,
        >= 1.5m => 62m,
        >= 0m => 52m,
        >= -2m => 42m,
        >= -4m => 32m,
        _ => 20m,
    };



    private static decimal SelfLiquidityPoints(IReadOnlyList<MarketBarRow> chron)

    {

        const int lookback = 20;

        if (chron.Count < lookback + 5)

            return 0.5m;



        var recent = chron.TakeLast(lookback).Average(b => (double)(b.Close * b.Volume));

        var history = new List<decimal>();

        for (var i = lookback; i <= chron.Count; i++)

        {

            var window = chron.Skip(i - lookback).Take(lookback);

            var avg = window.Average(b => (double)(b.Close * b.Volume));

            if (avg > 0)

                history.Add((decimal)avg);

        }



        if (history.Count == 0)

            return 0.5m;



        var sorted = history.OrderBy(v => v).ToList();

        var pct = MomentumScoreHelpers.PercentileOfValue((decimal)recent, sorted);

        return pct switch

        {

            >= 90m => 1.0m,

            >= 70m => 0.8m,

            >= 40m => 0.5m,

            >= 20m => 0.2m,

            _ => 0m,

        };

    }



    private static decimal PercentileToPoints12(decimal percentile) => percentile switch
    {
        >= 90m => 3.0m,
        >= 80m => 2.7m,
        >= 70m => 2.4m,
        >= 60m => 2.0m,
        >= 50m => 1.75m,
        >= 40m => 1.25m,
        >= 30m => 0.75m,
        >= 20m => 0.35m,
        _ => 0m,
    };

    private static decimal PercentileToPoints6(decimal percentile) => percentile switch
    {
        >= 90m => 2.0m,
        >= 80m => 1.7m,
        >= 70m => 1.4m,
        >= 60m => 1.15m,
        >= 50m => 0.9m,
        >= 40m => 0.6m,
        >= 30m => 0.35m,
        >= 20m => 0.15m,
        _ => 0m,
    };

    private static decimal PercentileToPoints3(decimal percentile) => percentile switch
    {
        >= 90m => 1.5m,
        >= 80m => 1.25m,
        >= 70m => 1.0m,
        >= 60m => 0.85m,
        >= 50m => 0.65m,
        >= 40m => 0.45m,
        >= 30m => 0.3m,
        >= 20m => 0.15m,
        _ => 0m,
    };



    private static decimal RelativeStrengthPoints(

        IReadOnlyList<MarketBarRow> stockBarsNewestFirst,

        IReadOnlyList<MarketBarRow>? niftyBarsNewestFirst,

        bool isBuy)

    {

        if (niftyBarsNewestFirst is null || niftyBarsNewestFirst.Count < MomentumScoreHelpers.TradingDays3M + 1)

            return 0m;



        var stockChron = MomentumScoreHelpers.ToChronological(stockBarsNewestFirst);

        var niftyChron = MomentumScoreHelpers.ToChronological(niftyBarsNewestFirst);

        var stockRet = MomentumScoreHelpers.ReturnBetween(stockChron, 0, MomentumScoreHelpers.TradingDays3M);

        var niftyRet = MomentumScoreHelpers.ReturnBetween(niftyChron, 0, MomentumScoreHelpers.TradingDays3M);

        if (stockRet is not decimal sr || niftyRet is not decimal nr)

            return 0m;



        var spread = (sr - nr) * 100m;

        var aligned = isBuy ? spread : -spread;

        return aligned switch
        {
            >= 12m => 1.5m,
            >= 8m => 1.25m,
            >= 5m => 1.0m,
            >= 2m => 0.6m,
            >= 0m => 0.35m,
            >= -2m => 0.15m,
            _ => 0m,
        };

    }



    private static decimal TrendPoints(IReadOnlyList<MarketBarRow> stockBarsNewestFirst, bool isBuy)

    {

        if (stockBarsNewestFirst.Count < 200)

            return 0m;



        var chron = MomentumScoreHelpers.ToChronological(stockBarsNewestFirst);

        var price = chron[^1].Close;

        var ema50 = TechnicalIndicators.Ema(chron, 50);

        var ema200 = TechnicalIndicators.Ema(chron, 200);

        if (ema50 is not decimal e50 || ema200 is not decimal e200)

            return 0m;



        if (isBuy)

        {

            if (price > e50 && e50 > e200) return 1.0m;

            if (price > e50 || e50 > e200) return 0.5m;

            return 0m;

        }



        if (price < e50 && e50 < e200) return 1.0m;

        if (price < e50 || e50 < e200) return 0.5m;

        return 0m;

    }



    private static decimal LiquidityPoints(Guid instrumentId, IReadOnlyDictionary<Guid, decimal> liquidityPct)

    {

        if (!liquidityPct.TryGetValue(instrumentId, out var pct))

            return 0.5m;



        return pct switch

        {

            >= 90m => 1.0m,

            >= 70m => 0.8m,

            >= 40m => 0.5m,

            >= 20m => 0.2m,

            _ => 0m,

        };

    }

}


