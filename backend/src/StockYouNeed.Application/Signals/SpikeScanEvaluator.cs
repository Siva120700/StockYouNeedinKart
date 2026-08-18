using StockYouNeed.Domain;

namespace StockYouNeed.Application.Signals;

/// <summary>
/// Flags a sudden 15-minute impulse: large candle vs typical 15m volume.
/// </summary>
public static class SpikeScanEvaluator
{
    public const string Interval15m = "15m";
    public const decimal MinAbsChangePct = 0.50m;
    public const decimal MinRangePct = 0.70m;
    public const decimal MinRvol = 1.8m;
    public const decimal MinBodyFraction = 0.45m;
    public const int VolumeLookback = 20;
    public static readonly TimeSpan BarLength = TimeSpan.FromMinutes(15);

    public static SpikeScanRow? Evaluate(
        IReadOnlyList<MarketIntradayBarRow> newestFirst,
        DateTimeOffset nowUtc)
    {
        if (newestFirst.Count < VolumeLookback + 1)
            return null;

        var bar = newestFirst[0];
        if (bar.Open <= 0)
            return null;

        var prior = newestFirst.Skip(1).Take(VolumeLookback).ToList();
        var medianVol = Median(prior.Select(p => (decimal)p.Volume).ToList());
        if (medianVol <= 0)
            return null;

        var range = bar.High - bar.Low;
        var body = Math.Abs(bar.Close - bar.Open);
        var changePct = Math.Round((bar.Close - bar.Open) / bar.Open * 100m, 2, MidpointRounding.AwayFromZero);
        var rangePct = Math.Round(range / bar.Open * 100m, 2, MidpointRounding.AwayFromZero);
        var bodyFraction = range > 0 ? body / range : 1m;
        var rvol = Math.Round(bar.Volume / medianVol, 2, MidpointRounding.AwayFromZero);

        var impulse = Math.Abs(changePct) >= MinAbsChangePct || rangePct >= MinRangePct;
        if (!impulse || rvol < MinRvol || bodyFraction < MinBodyFraction)
            return null;

        var buy = bar.Close >= bar.Open;
        var entry = Round(bar.Close);
        var sl = buy ? Round(bar.Low) : Round(bar.High);
        if (buy && sl >= entry)
            sl = Round(entry * 0.997m);
        if (!buy && sl <= entry)
            sl = Round(entry * 1.003m);

        var span = range > 0 ? range : Math.Abs(bar.Close - bar.Open);
        if (span <= 0)
            span = entry * 0.004m;

        return new SpikeScanRow
        {
            InstrumentId = bar.InstrumentId,
            AppSymbol = bar.AppSymbol,
            Side = buy ? SignalSides.Buy : SignalSides.Sell,
            BarTime = bar.BarTime,
            Forming = bar.BarTime + BarLength > nowUtc,
            Open = Round(bar.Open),
            High = Round(bar.High),
            Low = Round(bar.Low),
            Close = Round(bar.Close),
            Volume = bar.Volume,
            ChangePct = changePct,
            RangePct = rangePct,
            RelativeVolume = rvol,
            SpikeScore = Math.Round(Math.Abs(changePct) * rvol, 2, MidpointRounding.AwayFromZero),
            EntryPrice = entry,
            InitialStopLoss = sl,
            TargetT1 = Round(buy ? entry + span : entry - span),
            TargetT2 = Round(buy ? entry + span * 1.5m : entry - span * 1.5m),
            TargetT3 = Round(buy ? entry + span * 2m : entry - span * 2m),
        };
    }

    private static decimal Round(decimal n)
        => Math.Round(n, 2, MidpointRounding.AwayFromZero);

    public static decimal Median(IReadOnlyList<decimal> values)
    {
        if (values.Count == 0)
            return 0;
        var sorted = values.OrderBy(v => v).ToArray();
        var mid = sorted.Length / 2;
        if (sorted.Length % 2 == 1)
            return sorted[mid];
        return (sorted[mid - 1] + sorted[mid]) / 2m;
    }
}
