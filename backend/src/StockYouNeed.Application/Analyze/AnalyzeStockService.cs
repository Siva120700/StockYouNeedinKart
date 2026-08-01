using StockYouNeed.Application.Abstractions;
using StockYouNeed.Application.Confluence;
using StockYouNeed.Application.OptionsIntraday;
using StockYouNeed.Application.Services;
using StockYouNeed.Application.TradeScore;
using StockYouNeed.Domain;

namespace StockYouNeed.Application.Analyze;

/// <summary>
/// Per-stock deep dive: compose latest Signals / Liquidity / Confluence / Trade Score /
/// Breakout / Options + classic pivots from daily bars.
/// Liquidity is evaluated live for the selected stock (zones + fresh/classic setup).
/// </summary>
public sealed class AnalyzeStockService
{
    private readonly IInstrumentRepository _instruments;
    private readonly IMarketDataRepository _market;
    private readonly IPortfolioRepository _portfolio;
    private readonly IBreakoutRepository _breakout;
    private readonly IOptionsIntradayRepository _options;
    private readonly IBacktestRepository _backtest;
    private readonly ConfluenceService _confluence;
    private readonly LiquidityAnalysisService _liquidity;
    private readonly TradeConfidenceService _tradeConfidence;

    public AnalyzeStockService(
        IInstrumentRepository instruments,
        IMarketDataRepository market,
        IPortfolioRepository portfolio,
        IBreakoutRepository breakout,
        IOptionsIntradayRepository options,
        IBacktestRepository backtest,
        ConfluenceService confluence,
        LiquidityAnalysisService liquidity,
        TradeConfidenceService tradeConfidence)
    {
        _instruments = instruments;
        _market = market;
        _portfolio = portfolio;
        _breakout = breakout;
        _options = options;
        _backtest = backtest;
        _confluence = confluence;
        _liquidity = liquidity;
        _tradeConfidence = tradeConfidence;
    }

    public async Task<AnalyzeStockResult> AnalyzeAsync(
        Guid userId, Guid instrumentId, CancellationToken ct = default)
    {
        var inst = await _instruments.GetEquityByIdAsync(instrumentId, ct)
                   ?? throw new InvalidOperationException("Instrument not found.");

        var result = new AnalyzeStockResult
        {
            InstrumentId = inst.Id,
            Symbol = inst.Symbol,
            Name = inst.Name,
        };

        var ltpAll = await _market.GetAllLtpAsync(ct);
        var ltp = ltpAll.FirstOrDefault(x => x.InstrumentId == instrumentId);
        result.SpotLtp = ltp?.Ltp;
        result.LtpFetchedAt = ltp?.FetchedAt;

        var sectorId = await _instruments.GetSectorIdForInstrumentAsync(instrumentId, ct);
        result.SectorInstrumentId = sectorId;
        if (sectorId is Guid sid)
        {
            var sectors = await _instruments.GetSectorIndexesAsync(ct);
            var sector = sectors.FirstOrDefault(s => s.Id == sid);
            result.SectorSymbol = sector?.Symbol;
            result.SectorName = sector?.Name;
        }

        var bars = await _market.GetBarsForInstrumentAsync(instrumentId, 30, ct);
        var barsChron = bars.OrderBy(b => b.TradeDate).ToList();
        result.RecentBars = bars.OrderByDescending(b => b.TradeDate).Take(10).ToList();
        result.Levels = BuildLevelsFromBars(barsChron);

        // Live liquidity for this stock only (zones + fresh/classic try).
        LiquidityInstrumentEval liveLiq;
        try
        {
            liveLiq = await _liquidity.EvaluateForInstrumentAsync(userId, instrumentId, ct);
        }
        catch (Exception)
        {
            liveLiq = new LiquidityInstrumentEval
            {
                Status = "error",
                Detail = "Liquidity evaluation failed for this stock.",
            };
        }

        result.LiquidityFresh = liveLiq.Fresh;
        result.LiquidityClassic = liveLiq.Classic;
        result.Levels.LiquidityLive = true;
        result.Levels.LiquidityEvalStatus = liveLiq.Status;
        result.Levels.LiquidityEvalDetail = liveLiq.Detail;
        result.Levels.LiquidityZones = liveLiq.Zones;
        result.Levels.ZoneTags = liveLiq.Zones.Select(z => z.Type).Distinct().ToArray();
        result.Levels.SweptZoneType = liveLiq.SweptZoneType;
        result.Levels.SweptZonePrice = liveLiq.SweptZonePrice;
        result.Levels.SweepSide = liveLiq.SweepSide;
        result.Levels.NearestZoneType = liveLiq.NearestZoneType;
        result.Levels.NearestZonePrice = liveLiq.NearestZonePrice;
        result.Levels.DistancePct = liveLiq.DistancePct;
        result.Levels.LiquidityContext = liveLiq.Fresh?.TimeframeContext
            ?? liveLiq.Classic?.TimeframeContext
            ?? (liveLiq.Status == "evaluated" ? "4h_sweep+1h_confirm" : "live_zones");

        // Trade Score also runs independently for this stock, using its live
        // daily signal + the live Liquidity Fresh result calculated above.
        var liveTradeScore = await _tradeConfidence.EvaluateForInstrumentAsync(
            userId, inst, result.LiquidityFresh, ct);
        result.TradeScore = liveTradeScore.Score;
        result.Signal = liveTradeScore.Signal;

        var breakouts = await _breakout.GetConfirmationsAsync(userId, null, ct);
        var options = await _options.GetRecommendationsAsync(userId, null, ct);
        var confluenceAll = await _confluence.GetSignalsAsync(userId, ct);

        result.Confluence = confluenceAll
            .Where(s => s.InstrumentId == instrumentId)
            .OrderByDescending(s => s.AsOfDate)
            .FirstOrDefault();
        result.Breakout = breakouts
            .Where(s => s.InstrumentId == instrumentId && s.Confirmed)
            .OrderByDescending(s => s.AsOfDate)
            .FirstOrDefault();
        result.OptionsIntraday = options
            .Where(s => s.InstrumentId == instrumentId && s.Status == "recommended")
            .OrderByDescending(s => s.ConfidenceScore)
            .FirstOrDefault();

        MergeEngineLevels(result);
        result.PrimarySetup = PickPrimarySetup(result);
        if (result.Confluence is not null)
            result.SectorConfirmed = result.Confluence.SectorConfirmed;
        else if (result.TradeScore is not null)
            result.SectorConfirmed = result.Signal?.SectorConfirmed == true
                && (result.LiquidityFresh?.SectorConfirmed ?? true);
        else
            result.SectorConfirmed = result.Signal?.SectorConfirmed
                ?? result.LiquidityFresh?.SectorConfirmed
                ?? result.LiquidityClassic?.SectorConfirmed;

        ApplyVerdict(result);

        try
        {
            result.BacktestSummary = await _backtest.GetSymbolSummaryAsync(
                userId, instrumentId, null, null, sectorConfirmedOnly: false, ct: ct);
        }
        catch
        {
            // Backtest table may be empty for this symbol.
        }

        return result;
    }

    private static AnalyzeStockLevels BuildLevelsFromBars(List<MarketBarRow> chron)
    {
        var levels = new AnalyzeStockLevels();
        if (chron.Count == 0) return levels;

        var last = chron[^1];
        levels.PriorDayHigh = last.High;
        levels.PriorDayLow = last.Low;

        // Classic floor pivots from last completed daily bar.
        var pp = (last.High + last.Low + last.Close) / 3m;
        levels.Pivot = Round4(pp);
        levels.Resistance1 = Round4(2m * pp - last.Low);
        levels.Support1 = Round4(2m * pp - last.High);
        levels.Resistance2 = Round4(pp + (last.High - last.Low));
        levels.Support2 = Round4(pp - (last.High - last.Low));
        levels.Resistance3 = Round4(last.High + 2m * (pp - last.Low));
        levels.Support3 = Round4(last.Low - 2m * (last.High - pp));

        return levels;
    }

    private static void MergeEngineLevels(AnalyzeStockResult result)
    {
        var lvl = result.Levels;
        if (result.Signal is { } sig)
        {
            lvl.Ma2d = sig.Ma2d;
            lvl.Ma3d = sig.Ma3d;
            lvl.Ma5d = sig.Ma5d;
            lvl.Last2dHigh = sig.Last2dHigh;
            lvl.Last2dLow = sig.Last2dLow;
        }

        // Live liquidity already set zone/sweep fields; only fill gaps from signal rows.
        var liq = result.LiquidityFresh ?? result.LiquidityClassic;
        if (liq is not null)
        {
            lvl.SweptZoneType ??= liq.SweptZoneType;
            lvl.SweptZonePrice ??= liq.SweptZonePrice;
            lvl.SweepSide ??= liq.SweepSide;
            lvl.NearestZoneType ??= liq.NearestZoneType;
            lvl.NearestZonePrice ??= liq.NearestZonePrice;
            lvl.DistancePct ??= liq.DistancePct;
            if (lvl.ZoneTags.Length == 0)
                lvl.ZoneTags = liq.ZoneTags ?? Array.Empty<string>();
            lvl.LiquidityContext ??= liq.TimeframeContext;
        }

        if (result.Breakout is { } br)
        {
            lvl.BreakoutLevel = br.Level20d;
            lvl.BreakoutPattern = br.PatternType;
        }
    }

    private static AnalyzeStockSetup? PickPrimarySetup(AnalyzeStockResult r)
    {
        if (r.TradeScore is { EntryPrice: > 0 } ts)
            return ToSetup("trade_score", ts.Side, ts.AsOfDate, ts.EntryPrice, ts.InitialStopLoss,
                ts.TargetT1, ts.TargetT2, ts.TargetT3);
        if (r.Confluence is { } c)
            return ToSetup("confluence", c.Side, c.AsOfDate, c.EntryPrice, c.InitialStopLoss,
                c.TargetT1, c.TargetT2, c.TargetT3);
        if (r.LiquidityFresh is { } lf)
            return ToSetup("liquidity_fresh", lf.Side, lf.AsOfDate, lf.EntryPrice, lf.InitialStopLoss,
                lf.TargetT1, lf.TargetT2, lf.TargetT3);
        if (r.Signal is { } s)
            return ToSetup("signals", s.Side, s.AsOfDate, s.EntryPrice, s.InitialStopLoss,
                s.TargetT1, s.TargetT2, s.TargetT3);
        if (r.LiquidityClassic is { } lc)
            return ToSetup("liquidity", lc.Side, lc.AsOfDate, lc.EntryPrice, lc.InitialStopLoss,
                lc.TargetT1, lc.TargetT2, lc.TargetT3);
        return null;
    }

    private static AnalyzeStockSetup ToSetup(
        string source, string side, DateOnly asOf, decimal entry, decimal sl,
        decimal? t1, decimal? t2, decimal? t3)
    {
        decimal? rr = null;
        if (t1 is decimal tt)
        {
            var risk = Math.Abs(entry - sl);
            var reward = Math.Abs(tt - entry);
            if (risk > 0) rr = Math.Round(reward / risk, 2);
        }

        return new AnalyzeStockSetup
        {
            Source = source,
            Side = side,
            AsOfDate = asOf,
            Entry = entry,
            StopLoss = sl,
            TargetT1 = t1,
            TargetT2 = t2,
            TargetT3 = t3,
            PlannedRiskReward = rr,
        };
    }

    private static void ApplyVerdict(AnalyzeStockResult r)
    {
        var reasons = new List<string>();

        if (r.TradeScore is { } ts)
        {
            r.Verdict = ts.Rating;
            r.VerdictLabel = TradeConfidenceScorer.RatingLabel(ts.Rating);
            reasons.AddRange(ts.Reasons ?? Array.Empty<string>());

            if (ts.Rating == TradeConfidenceScorer.RatingNoSetup)
            {
                reasons.Add("No daily signal on the latest bars — nothing to grade");
                if (r.Levels.LiquidityEvalDetail is { Length: > 0 } liqWhy)
                    reasons.Add(liqWhy);
            }
            else
            {
                reasons.Add($"Trade Score {ts.ConfidenceScore}/100");
            }

            if (r.Confluence is not null) reasons.Add("Confluence row present");
            if (r.SectorConfirmed == true) reasons.Add("Sector confirmed");
            else if (r.SectorConfirmed == false) reasons.Add("Sector not confirmed");
            r.VerdictReasons = reasons.Distinct().ToArray();
            return;
        }

        if (r.Confluence is not null)
        {
            r.Verdict = "buy";
            r.VerdictLabel = "Confluence setup";
            reasons.Add("Signals + Liquidity Fresh aligned");
            reasons.Add($"Side {r.Confluence.Side}");
            if (r.Confluence.SectorConfirmed) reasons.Add("Sector confirmed");
            r.VerdictReasons = reasons.ToArray();
            return;
        }

        if (r.LiquidityFresh is not null || r.Signal is not null)
        {
            var side = r.LiquidityFresh?.Side ?? r.Signal!.Side;
            r.Verdict = "watch";
            r.VerdictLabel = "Single-engine setup — watch";
            if (r.LiquidityFresh is not null)
            {
                reasons.Add("Liquidity Fresh present");
                if (!string.IsNullOrEmpty(r.LiquidityFresh.SweptZoneType))
                    reasons.Add($"Sweep {r.LiquidityFresh.SweepSide} {r.LiquidityFresh.SweptZoneType} @ {r.LiquidityFresh.SweptZonePrice}");
            }
            if (r.Signal is not null) reasons.Add("Daily signal present");
            if (r.Breakout?.Confirmed == true)
                reasons.Add($"Breakout {r.Breakout.PatternType}");
            reasons.Add($"Side {side}");
            r.VerdictReasons = reasons.ToArray();
            return;
        }

        if (r.Breakout?.Confirmed == true)
        {
            r.Verdict = "watch";
            r.VerdictLabel = "Breakout only — watch";
            reasons.Add($"Pattern {r.Breakout.PatternType}");
            r.VerdictReasons = reasons.ToArray();
            return;
        }

        r.Verdict = "no_setup";
        r.VerdictLabel = "No active setup";
        reasons.Add("Run Signals / Liquidity Fresh / Trade Score first");
        if (r.SectorSymbol is not null) reasons.Add($"Sector {r.SectorSymbol}");
        r.VerdictReasons = reasons.ToArray();
    }

    private static decimal Round4(decimal v) => Math.Round(v, 4);
}
