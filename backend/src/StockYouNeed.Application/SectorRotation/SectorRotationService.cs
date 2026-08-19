using Microsoft.Extensions.Logging;
using StockYouNeed.Application.Abstractions;
using StockYouNeed.Application.SectorScope;
using StockYouNeed.Domain;

namespace StockYouNeed.Application.SectorRotation;

public sealed class SectorRotationService
{
    private readonly IInstrumentRepository _instruments;
    private readonly IMarketDataRepository _market;
    private readonly ILogger<SectorRotationService> _logger;

    public SectorRotationService(
        IInstrumentRepository instruments,
        IMarketDataRepository market,
        ILogger<SectorRotationService> logger)
    {
        _instruments = instruments;
        _market = market;
        _logger = logger;
    }

    public async Task<SectorRotationSnapshot> GetSnapshotAsync(CancellationToken ct = default)
    {
        var equities = await _instruments.GetUniverseEquitiesWithSectorAsync(ct);
        if (equities.Count == 0)
        {
            return new SectorRotationSnapshot
            {
                AsOf = DateTimeOffset.UtcNow,
                Regime = new MarketRegimeInfo { Label = "neutral", Reasons = new[] { "No universe equities with sector mapping" } },
            };
        }

        var equityIds = equities.Select(e => e.InstrumentId).ToHashSet();
        var sectorIds = equities.Select(e => e.SectorInstrumentId).Distinct().ToHashSet();

        var allBars = await _market.GetDailyBarsForInstrumentsAsync(
            equityIds.Union(sectorIds).ToList(),
            SectorRotationCalculator.LookbackDays,
            ct);

        var barsByInstrument = allBars
            .GroupBy(b => b.InstrumentId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(b => b.TradeDate).ToList());

        var nifty = await _instruments.FindBySymbolAsync(SectorScopeService.NiftySymbol, ct);
        IReadOnlyList<MarketBarRow> niftyBars = Array.Empty<MarketBarRow>();
        if (nifty is not null && barsByInstrument.TryGetValue(nifty.Id, out var nb))
            niftyBars = nb;
        else if (nifty is not null)
            niftyBars = await _market.GetBarsForInstrumentAsync(nifty.Id, SectorRotationCalculator.LookbackDays, ct);

        var niftyReturn5d = SectorRotationCalculator.Return5dPct(niftyBars);
        var niftyAboveEma20 = false;
        if (niftyBars.Count >= 25)
        {
            var chron = Application.Signals.MomentumScoreHelpers.ToChronological(niftyBars);
            var ema20 = Application.TradeScore.TechnicalIndicators.Ema(chron, 20);
            niftyAboveEma20 = ema20 is decimal e && chron[^1].Close > e;
        }

        var stockMetrics = new List<SectorRotationCalculator.StockDayMetrics>();
        foreach (var eq in equities)
        {
            if (!barsByInstrument.TryGetValue(eq.InstrumentId, out var bars)) continue;
            var m = SectorRotationCalculator.BuildStockMetrics(eq, bars);
            if (m is not null) stockMetrics.Add(m);
        }

        var advancers = stockMetrics.Count(s => s.TodayReturnPct > 0);
        var decliners = stockMetrics.Count(s => s.TodayReturnPct < 0);
        var breadth = stockMetrics.Count > 0
            ? Math.Round(100m * advancers / stockMetrics.Count, 1)
            : 50m;

        var regimeLabel = SectorRotationCalculator.RegimeLabel(niftyAboveEma20, breadth, null);
        var regimeReasons = new List<string>();
        if (niftyAboveEma20) regimeReasons.Add("Nifty above EMA20");
        else regimeReasons.Add("Nifty below EMA20");
        regimeReasons.Add($"Market breadth {breadth:0}% ({advancers}↑ / {decliners}↓)");

        var bySector = stockMetrics.GroupBy(s => s.SectorInstrumentId).ToList();
        var sectors = new List<SectorRotationRow>();

        foreach (var grp in bySector)
        {
            var sample = grp.First();
            var sectorSymbol = sample.SectorSymbol;
            var displayName = SectorScopeService.DisplayName(sectorSymbol, sample.SectorName);

            var maxDays = grp.Max(s => s.DailyFlows.Count);
            if (maxDays == 0) continue;

            var sectorFlowSeries = new List<decimal>();
            for (var d = 0; d < maxDays; d++)
            {
                decimal sum = 0;
                foreach (var st in grp)
                {
                    var idx = st.DailyFlows.Count - 1 - d;
                    if (idx >= 0) sum += st.DailyFlows[idx];
                }
                sectorFlowSeries.Add(sum);
            }
            sectorFlowSeries.Reverse();

            var (flowZ, flowAccel) = SectorRotationCalculator.FlowStats(sectorFlowSeries);
            var todayFlow = sectorFlowSeries.Count > 0 ? sectorFlowSeries[^1] : 0;

            var positive = grp.Count(s => s.TodayReturnPct > 0);
            var breadthPct = grp.Any()
                ? Math.Round(100m * positive / grp.Count(), 1)
                : 0;

            var sectorBars = barsByInstrument.GetValueOrDefault(grp.Key) ?? new List<MarketBarRow>();
            var rs5d = SectorRotationCalculator.Return5dPct(sectorBars) - niftyReturn5d;
            var trend = SectorRotationCalculator.TrendScore(sectorBars);

            var volToday = grp.Sum(s => s.TodayTradedValue);
            var volHist = new List<decimal>();
            for (var d = 1; d <= Math.Min(20, maxDays); d++)
            {
                decimal v = 0;
                foreach (var st in grp)
                {
                    if (!barsByInstrument.TryGetValue(st.InstrumentId, out var sb) || sb.Count <= d) continue;
                    v += sb[d].Close * sb[d].Volume;
                }
                volHist.Add(v);
            }
            var volExp = volHist.Count > 0 && volHist.Average() > 0
                ? Math.Round(volToday / volHist.Average() * 100m, 1)
                : 100m;

            var score = SectorRotationCalculator.CompositeSectorScore(
                flowZ, flowAccel, rs5d, breadthPct, trend, volExp);
            var bucket = SectorRotationCalculator.ClassifyBucket(score, flowZ, flowAccel);
            var upcomingScore = SectorRotationCalculator.UpcomingMomentumScore(
                flowZ, flowAccel, rs5d, breadthPct, volExp, score);
            var upcomingReasons = SectorRotationCalculator.UpcomingMomentumReasons(
                flowZ, flowAccel, rs5d, breadthPct, volExp);

            var peerFlows = grp.Select(s => s.TodayFlow).ToList();
            var peerRet5d = grp.Select(s => s.Return5dPct).ToList();
            var sectorStocks = grp
                .Select(s =>
                {
                    var mom = SectorRotationCalculator.StockMomentumScore(
                        s.Return5dPct, s.TodayReturnPct, s.TodayFlow, peerFlows, peerRet5d);
                    return new SectorRotationStockRow
                    {
                        InstrumentId = s.InstrumentId,
                        Symbol = s.Symbol,
                        Name = s.Name,
                        MomentumScore = mom,
                        Alignment = SectorRotationCalculator.AlignmentLabel(score, mom),
                        ChangePct = s.TodayReturnPct,
                        Return5dPct = s.Return5dPct,
                        FlowCr = SectorRotationCalculator.ToCr(s.TodayFlow),
                        SectorInstrumentId = grp.Key,
                        SectorSymbol = sectorSymbol,
                        SectorScore = score,
                        SectorBucket = bucket,
                    };
                })
                .OrderByDescending(s => s.MomentumScore)
                .ToList();

            sectors.Add(new SectorRotationRow
            {
                SectorInstrumentId = grp.Key,
                Symbol = sectorSymbol,
                DisplayName = displayName,
                Bucket = bucket,
                Score = score,
                FlowZScore = flowZ,
                FlowAccelerationPct = flowAccel,
                RelativeStrength5dPct = Math.Round(rs5d, 2),
                BreadthPct = breadthPct,
                TrendScore = trend,
                VolumeExpansionPct = volExp,
                TodayFlowCr = SectorRotationCalculator.ToCr(todayFlow),
                ConstituentCount = grp.Count(),
                UpcomingMomentumScore = upcomingScore,
                UpcomingMomentumReasons = upcomingReasons,
                TopStocks = sectorStocks,
            });
        }

        sectors = sectors.OrderByDescending(s => s.Score).ToList();
        for (var i = 0; i < sectors.Count; i++)
            sectors[i].Rank = i + 1;

        SectorRotationCalculator.ApplyRelativeBuckets(sectors);

        var capitalEntering = sectors.Where(s => s.Bucket == "capital_entering").ToList();
        var leading = sectors.Where(s => s.Bucket == "leading").ToList();
        var neutral = sectors.Where(s => s.Bucket == "neutral").ToList();
        var capitalLeaving = sectors.Where(s => s.Bucket == "capital_leaving").ToList();

        var momentumBuilding = sectors
            .Where(s => s.Bucket != "capital_leaving" && s.UpcomingMomentumScore >= 45)
            .OrderByDescending(s => s.UpcomingMomentumScore)
            .ThenByDescending(s => s.FlowAccelerationPct)
            .Take(5)
            .ToList();

        var sectorById = sectors.ToDictionary(s => s.SectorInstrumentId);
        var allStocks = stockMetrics
            .Select(s =>
            {
                if (!sectorById.TryGetValue(s.SectorInstrumentId, out var sector)) return null;
                var peerFlows = stockMetrics.Where(x => x.SectorInstrumentId == s.SectorInstrumentId).Select(x => x.TodayFlow).ToList();
                var peerRet5d = stockMetrics.Where(x => x.SectorInstrumentId == s.SectorInstrumentId).Select(x => x.Return5dPct).ToList();
                var mom = SectorRotationCalculator.StockMomentumScore(
                    s.Return5dPct, s.TodayReturnPct, s.TodayFlow, peerFlows, peerRet5d);
                return new SectorRotationStockRow
                {
                    InstrumentId = s.InstrumentId,
                    Symbol = s.Symbol,
                    Name = s.Name,
                    MomentumScore = mom,
                    Alignment = SectorRotationCalculator.AlignmentLabel(sector.Score, mom),
                    ChangePct = s.TodayReturnPct,
                    Return5dPct = s.Return5dPct,
                    FlowCr = SectorRotationCalculator.ToCr(s.TodayFlow),
                    SectorInstrumentId = sector.SectorInstrumentId,
                    SectorSymbol = sector.Symbol,
                    SectorScore = sector.Score,
                    SectorBucket = sector.Bucket,
                };
            })
            .Where(x => x is not null)
            .Cast<SectorRotationStockRow>()
            .OrderByDescending(s => s.MomentumScore)
            .ToList();

        _logger.LogInformation(
            "Sector rotation: {Count} sectors · regime={Regime} · breadth={Breadth}%",
            sectors.Count, regimeLabel, breadth);

        return new SectorRotationSnapshot
        {
            AsOf = DateTimeOffset.UtcNow,
            Regime = new MarketRegimeInfo
            {
                Label = regimeLabel,
                NiftyReturn5dPct = niftyReturn5d,
                NiftyAboveEma20 = niftyAboveEma20,
                MarketBreadthPct = breadth,
                Advancers = advancers,
                Decliners = decliners,
                Reasons = regimeReasons,
            },
            Sectors = sectors,
            CapitalEntering = capitalEntering,
            Leading = leading,
            Neutral = neutral,
            CapitalLeaving = capitalLeaving,
            MomentumBuilding = momentumBuilding,
            AllStocks = allStocks,
        };
    }

    public async Task<IReadOnlyList<AnalysisSignalRow>> ApplyToSignalsAsync(
        IReadOnlyList<AnalysisSignalRow> rows, CancellationToken ct = default)
    {
        if (rows.Count == 0) return rows;
        var snap = await GetSnapshotAsync(ct);
        var byInstrument = snap.AllStocks.ToDictionary(s => s.InstrumentId);
        var sectorById = snap.Sectors.ToDictionary(s => s.SectorInstrumentId);

        foreach (var row in rows)
        {
            if (!byInstrument.TryGetValue(row.InstrumentId, out var stock)) continue;
            if (!sectorById.TryGetValue(stock.SectorInstrumentId, out var sector)) continue;
            row.SectorRotation = BuildOverlay(stock, sector, row.Side);
        }

        return rows
            .OrderBy(r => r.SectorRotation?.Downranked == true)
            .ThenByDescending(r => r.SectorRotation?.BlendedScore ?? 0)
            .ToList();
    }

    public async Task<IReadOnlyList<TradeConfidenceScoreRow>> ApplyToTradeScoresAsync(
        IReadOnlyList<TradeConfidenceScoreRow> rows, CancellationToken ct = default)
    {
        if (rows.Count == 0) return rows;
        var snap = await GetSnapshotAsync(ct);
        var byInstrument = snap.AllStocks.ToDictionary(s => s.InstrumentId);
        var sectorById = snap.Sectors.ToDictionary(s => s.SectorInstrumentId);

        foreach (var row in rows)
        {
            if (!byInstrument.TryGetValue(row.InstrumentId, out var stock)) continue;
            if (!sectorById.TryGetValue(stock.SectorInstrumentId, out var sector)) continue;
            var overlay = BuildOverlay(stock, sector, row.Side);
            row.SectorRotation = overlay;
            if (overlay.BlendedScore is int blended && blended != row.ConfidenceScore)
            {
                row.ConfidenceScore = blended;
                row.Rating = Application.TradeScore.TradeConfidenceScorer.Rate(
                    blended, row.SignalsScore > 0, row.LiquidityScore + row.BreakoutScore > 0);
            }
        }

        return rows
            .OrderBy(r => r.SectorRotation?.Downranked == true)
            .ThenByDescending(r => r.ConfidenceScore)
            .ToList();
    }

    private static SectorRotationInfo BuildOverlay(
        SectorRotationStockRow stock, SectorRotationRow sector, string side)
    {
        var blended = SectorRotationCalculator.BlendedStockScore(
            stock.MomentumScore, sector.Score, sector.RelativeStrength5dPct, sector.VolumeExpansionPct);
        var buy = side.Equals(SignalSides.Buy, StringComparison.OrdinalIgnoreCase);
        var downranked = buy
            ? sector.Bucket is "capital_leaving" || stock.Alignment is "avoid" or "stock_only"
            : sector.Bucket is "capital_entering" or "leading" || stock.Alignment is "avoid";

        return new SectorRotationInfo
        {
            SectorSymbol = sector.Symbol,
            SectorName = sector.DisplayName,
            SectorScore = sector.Score,
            StockMomentumScore = stock.MomentumScore,
            Alignment = stock.Alignment,
            Bucket = sector.Bucket,
            BlendedScore = blended,
            Downranked = downranked,
        };
    }
}
