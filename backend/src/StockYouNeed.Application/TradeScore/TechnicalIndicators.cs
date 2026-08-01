using StockYouNeed.Domain;

namespace StockYouNeed.Application.TradeScore;

/// <summary>RSI, ADX, ATR helpers on chronological daily bars (oldest first).</summary>
public static class TechnicalIndicators
{
    public static decimal? Rsi(IReadOnlyList<MarketBarRow> chron, int period = 14)
    {
        if (chron.Count < period + 1) return null;
        decimal gain = 0, loss = 0;
        for (var i = chron.Count - period; i < chron.Count; i++)
        {
            var diff = chron[i].Close - chron[i - 1].Close;
            if (diff > 0) gain += diff;
            else loss -= diff;
        }
        if (loss == 0) return 100m;
        var rs = gain / loss;
        return Math.Round(100m - 100m / (1m + rs), 2);
    }

    public static decimal? Adx(IReadOnlyList<MarketBarRow> chron, int period = 14)
    {
        if (chron.Count < period * 2) return null;
        var trList = new List<decimal>();
        var plusDm = new List<decimal>();
        var minusDm = new List<decimal>();

        for (var i = 1; i < chron.Count; i++)
        {
            var up = chron[i].High - chron[i - 1].High;
            var down = chron[i - 1].Low - chron[i].Low;
            plusDm.Add(up > down && up > 0 ? up : 0);
            minusDm.Add(down > up && down > 0 ? down : 0);
            var tr = Math.Max(chron[i].High - chron[i].Low,
                Math.Max(Math.Abs(chron[i].High - chron[i - 1].Close),
                    Math.Abs(chron[i].Low - chron[i - 1].Close)));
            trList.Add(tr);
        }

        if (trList.Count < period) return null;
        decimal Smooth(IReadOnlyList<decimal> src, int start)
        {
            var sum = src.Skip(start).Take(period).Sum();
            return sum / period;
        }

        var start = trList.Count - period;
        var atr = Smooth(trList, start);
        var pDm = Smooth(plusDm, start);
        var mDm = Smooth(minusDm, start);
        if (atr <= 0) return null;
        var plusDi = 100m * pDm / atr;
        var minusDi = 100m * mDm / atr;
        var denom = plusDi + minusDi;
        if (denom <= 0) return null;
        var dx = 100m * Math.Abs(plusDi - minusDi) / denom;
        return Math.Round(dx, 2);
    }

    public static decimal? Atr(IReadOnlyList<MarketBarRow> chron, int period = 14)
    {
        if (chron.Count < period + 1) return null;
        var trs = new List<decimal>();
        for (var i = 1; i < chron.Count; i++)
        {
            var tr = Math.Max(chron[i].High - chron[i].Low,
                Math.Max(Math.Abs(chron[i].High - chron[i - 1].Close),
                    Math.Abs(chron[i].Low - chron[i - 1].Close)));
            trs.Add(tr);
        }
        if (trs.Count < period) return null;
        return Math.Round(trs.TakeLast(period).Average(), 4);
    }

    /// <summary>Simple EMA of closes; bars chronological oldest-first. Null if fewer than period bars.</summary>
    public static decimal? Ema(IReadOnlyList<MarketBarRow> chron, int period = 20)
    {
        if (chron.Count < period || period <= 0) return null;
        var k = 2m / (period + 1);
        decimal ema = chron.Take(period).Average(b => b.Close);
        for (var i = period; i < chron.Count; i++)
            ema = chron[i].Close * k + ema * (1m - k);
        return Math.Round(ema, 4);
    }

    public static bool AtrExpansion(IReadOnlyList<MarketBarRow> chron, int period = 14, int lookback = 5)
    {
        if (chron.Count < period + lookback + 1) return false;
        var current = Atr(chron, period);
        if (current is null) return false;
        var priorAvgs = new List<decimal>();
        for (var offset = 1; offset <= lookback; offset++)
        {
            var slice = chron.Take(chron.Count - offset).ToList();
            var a = Atr(slice, period);
            if (a is decimal v) priorAvgs.Add(v);
        }
        if (priorAvgs.Count == 0) return false;
        return current > priorAvgs.Average();
    }
}
