using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StockYouNeed.Application.Abstractions;
using StockYouNeed.Application.Options;
using StockYouNeed.Application.Outcomes;
using StockYouNeed.Application.Signals;
using StockYouNeed.Domain;

namespace StockYouNeed.Application.Services;

/// <summary>
/// Breakout setups filtered by momentum score (V2 StepOne-style or V3 Jegadeesh–Titman).
/// Parallel engine — does not touch analysis_signals.
/// </summary>
public sealed class MomentumAnalysisService
{
    public const decimal MinMomentumScoreV2 = 5m;
    /// <summary>V3 cross-section ranks are tighter — slightly lower store floor.</summary>
    public const decimal MinMomentumScoreV3 = 4m;

    public static decimal MinScoreForRuleset(string? ruleset)
        => NormalizeRuleset(ruleset) == "v3" ? MinMomentumScoreV3 : MinMomentumScoreV2;

    [Obsolete("Use MinScoreForRuleset")]
    public const decimal MinMomentumScore = MinMomentumScoreV2;

    private readonly IInstrumentRepository _instruments;
    private readonly IMarketDataRepository _market;
    private readonly IPortfolioRepository _portfolio;
    private readonly MarketBarsSyncService _barsSync;
    private readonly TokenSyncService _tokenSync;
    private readonly UniverseSeedService _universeSeed;
    private readonly SignalOutcomeService _outcomes;
    private readonly AngelOptions _options;
    private readonly ILogger<MomentumAnalysisService> _logger;

    public MomentumAnalysisService(
        IInstrumentRepository instruments,
        IMarketDataRepository market,
        IPortfolioRepository portfolio,
        MarketBarsSyncService barsSync,
        TokenSyncService tokenSync,
        UniverseSeedService universeSeed,
        SignalOutcomeService outcomes,
        IOptions<AngelOptions> options,
        ILogger<MomentumAnalysisService> logger)
    {
        _instruments = instruments;
        _market = market;
        _portfolio = portfolio;
        _barsSync = barsSync;
        _tokenSync = tokenSync;
        _universeSeed = universeSeed;
        _outcomes = outcomes;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AnalysisRunRow> RunAsync(
        Guid userId,
        bool includeNifty50,
        bool includeNifty100,
        bool includeWatchlist,
        string triggeredBy,
        string ruleset,
        CancellationToken ct = default)
    {
        ruleset = NormalizeRuleset(ruleset);
        var asOf = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(5.5)).DateTime);
        var runId = await _portfolio.CreateMomentumAnalysisRunAsync(
            userId, triggeredBy, includeNifty50, includeNifty100, includeWatchlist, asOf, ruleset, ct);

        var stats = new Dictionary<string, object>
        {
            ["scanned"] = 0,
            ["signals"] = 0,
            ["sectorConfirmed"] = 0,
            ["fewBars"] = 0,
            ["noSetup"] = 0,
            ["lowMomentum"] = 0,
            ["ruleset"] = ruleset,
        };

        try
        {
            var tokens = await _instruments.GetActiveTokensForUniversesAsync(ct);
            var watchlistIds = includeWatchlist
                ? await _portfolio.GetWatchlistInstrumentIdsAsync(userId, ct)
                : Array.Empty<Guid>();

            var instrumentIds = tokens.Select(t => t.InstrumentId).ToHashSet();
            foreach (var id in watchlistIds)
                instrumentIds.Add(id);

            stats["scanned"] = instrumentIds.Count;

            var sectorBarsCache = await BuildSectorBarsCacheAsync(asOf, ct);
            var niftyBars = await LoadNiftyDailyBarsAsync(ct);
            var universeBars = await LoadUniverseBarsAsync(instrumentIds, ct);
            var horizonReturns = MomentumUniverseRanker.BuildHorizonReturns(instrumentIds, universeBars);
            var pct12 = MomentumUniverseRanker.BuildPercentileMap(horizonReturns, h => h.Mom12_1);
            var pct6 = MomentumUniverseRanker.BuildPercentileMap(horizonReturns, h => h.Mom6_1);
            var pct3 = MomentumUniverseRanker.BuildPercentileMap(horizonReturns, h => h.Mom3_1);
            var liquidityPct = MomentumUniverseRanker.BuildLiquidityPercentiles(instrumentIds, universeBars);

            var signalCount = 0;
            var skippedFewBars = 0;
            var noSetup = 0;
            var lowMomentum = 0;
            var sectorConfirmedCount = 0;

            foreach (var instrumentId in instrumentIds)
            {
                if (!universeBars.TryGetValue(instrumentId, out var bars) || bars.Count < 5)
                {
                    skippedFewBars++;
                    continue;
                }

                var breakout = BreakoutSignalEvaluator.Evaluate(
                    userId, runId, asOf, bars, livePrice: null,
                    actionableOnly: true,
                    projectPartialSessionVolume: true);
                if (breakout is null)
                {
                    noSetup++;
                    continue;
                }

                var sectorId = await _instruments.GetSectorIdForInstrumentAsync(instrumentId, ct);
                var sectorConfirmed = sectorId is not null
                                      && sectorBarsCache.TryGetValue(sectorId.Value, out var sectorBars)
                                      && CheckSectorConfirmation(breakout.Side, sectorBars);
                if (sectorConfirmed)
                    sectorConfirmedCount++;

                var score = ruleset == "v3"
                    ? MomentumScoreV3Evaluator.Score(
                        breakout.Side, instrumentId, bars, niftyBars, pct12, pct6, pct3, liquidityPct)
                    : MomentumScoreV2Evaluator.Score(breakout.Side, bars, niftyBars, livePrice: null);

                if (score is not decimal s || s <= MinScoreForRuleset(ruleset))
                {
                    lowMomentum++;
                    continue;
                }

                var row = new MomentumSignalRow
                {
                    Id = Guid.NewGuid(),
                    MomentumRunId = runId,
                    UserId = userId,
                    InstrumentId = instrumentId,
                    AppSymbol = breakout.AppSymbol,
                    InstrumentName = breakout.InstrumentName,
                    Side = breakout.Side,
                    AsOfDate = asOf,
                    EntryPrice = breakout.EntryPrice,
                    InitialStopLoss = breakout.InitialStopLoss,
                    TargetT1 = breakout.TargetT1,
                    TargetT2 = breakout.TargetT2,
                    TargetT3 = breakout.TargetT3,
                    VolumeOk = breakout.VolumeOk,
                    SectorConfirmed = sectorConfirmed,
                    FreshCross = breakout.FreshCross,
                    MomentumScore = s,
                };

                await _portfolio.InsertMomentumSignalAsync(row, ct);
                await _outcomes.OpenFromMomentumAsync(row, ruleset, ct);
                signalCount++;
            }

            stats["signals"] = signalCount;
            stats["sectorConfirmed"] = sectorConfirmedCount;
            stats["fewBars"] = skippedFewBars;
            stats["noSetup"] = noSetup;
            stats["lowMomentum"] = lowMomentum;

            await _portfolio.CompleteMomentumAnalysisRunAsync(runId, "succeeded", null, stats, ct);

            _logger.LogInformation(
                "Momentum {Ruleset} run {RunId}: scanned={Scanned}, signals={Signals}, lowMomentum={LowMomentum}",
                ruleset, runId, instrumentIds.Count, signalCount, lowMomentum);

            return new AnalysisRunRow
            {
                Id = runId,
                UserId = userId,
                TriggeredBy = triggeredBy,
                IncludeNifty50 = includeNifty50,
                IncludeNifty100 = includeNifty100,
                IncludeWatchlist = includeWatchlist,
                AsOfDate = asOf,
                StartedAt = DateTimeOffset.UtcNow,
                FinishedAt = DateTimeOffset.UtcNow,
                Status = "succeeded",
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Momentum run {RunId} failed", runId);
            await _portfolio.CompleteMomentumAnalysisRunAsync(runId, "failed", ex.Message, stats, ct);
            throw;
        }
    }

    /// <summary>Live V2/V3 eval for one stock (analyze stock). Does not persist.</summary>
    public async Task<(MomentumSignalRow? V2, MomentumSignalRow? V3)> EvaluateForInstrumentAsync(
        Guid userId,
        Guid instrumentId,
        decimal? livePrice = null,
        CancellationToken ct = default)
    {
        var bars = (await _market.GetBarsForInstrumentAsync(instrumentId, MomentumScoreHelpers.MomentumBarDays, ct))
            .OrderByDescending(b => b.TradeDate)
            .ToList();
        if (bars.Count < 5)
            return (null, null);

        var asOf = bars[0].TradeDate;
        var breakout = BreakoutSignalEvaluator.Evaluate(
            userId, Guid.Empty, asOf, bars, livePrice,
            actionableOnly: true,
            projectPartialSessionVolume: true);
        if (breakout is null)
            return (null, null);

        var sectorId = await _instruments.GetSectorIdForInstrumentAsync(instrumentId, ct);
        var sectorConfirmed = false;
        if (sectorId is Guid sid)
        {
            var sectorBars = (await _market.GetBarsForInstrumentAsync(sid, 10, ct))
                .OrderByDescending(b => b.TradeDate)
                .ToList();
            if (sectorBars.Count >= 3)
                sectorConfirmed = CheckSectorConfirmation(breakout.Side, sectorBars);
        }

        var niftyBars = await LoadNiftyDailyBarsAsync(ct);
        var v2Score = MomentumScoreV2Evaluator.Score(breakout.Side, bars, niftyBars, livePrice);
        var v3Score = MomentumScoreV3Evaluator.ScoreSingleStock(breakout.Side, bars, niftyBars);

        return (
            BuildEvalRow(userId, instrumentId, breakout, sectorConfirmed, v2Score),
            BuildEvalRow(userId, instrumentId, breakout, sectorConfirmed, v3Score));
    }

    private static MomentumSignalRow? BuildEvalRow(
        Guid userId,
        Guid instrumentId,
        AnalysisSignalRow breakout,
        bool sectorConfirmed,
        decimal? score)
    {
        if (score is not decimal s)
            return null;

        return new MomentumSignalRow
        {
            Id = Guid.Empty,
            MomentumRunId = Guid.Empty,
            UserId = userId,
            InstrumentId = instrumentId,
            AppSymbol = breakout.AppSymbol,
            InstrumentName = breakout.InstrumentName,
            Side = breakout.Side,
            AsOfDate = breakout.AsOfDate,
            EntryPrice = breakout.EntryPrice,
            InitialStopLoss = breakout.InitialStopLoss,
            TargetT1 = breakout.TargetT1,
            TargetT2 = breakout.TargetT2,
            TargetT3 = breakout.TargetT3,
            VolumeOk = breakout.VolumeOk,
            SectorConfirmed = sectorConfirmed,
            FreshCross = breakout.FreshCross,
            MomentumScore = s,
        };
    }

    private static string NormalizeRuleset(string? ruleset)
    {
        var s = (ruleset ?? "v2").Trim().ToLowerInvariant();
        return s == "v3" ? "v3" : "v2";
    }

    private async Task<Dictionary<Guid, List<MarketBarRow>>> BuildSectorBarsCacheAsync(
        DateOnly asOf, CancellationToken ct)
    {
        var cache = new Dictionary<Guid, List<MarketBarRow>>();
        try
        {
            await _universeSeed.SeedAsync(ct);
            if (_options.Enabled)
            {
                var sectorTokens = await _instruments.GetActiveTokensForSectorsAsync(ct);
                if (sectorTokens.Count == 0)
                    await _tokenSync.SyncUniverseTokensAsync(ct);
                await _barsSync.SyncMissingSectorBarsAsync(ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Sector prep failed for momentum run.");
        }

        foreach (var sectorId in await _instruments.GetSectorInstrumentIdsAsync(ct))
        {
            var sBars = (await _market.GetBarsForInstrumentAsync(sectorId, 10, ct))
                .OrderByDescending(b => b.TradeDate)
                .ToList();
            if (sBars.Count >= 3)
                cache[sectorId] = sBars;
        }
        return cache;
    }

    private static bool CheckSectorConfirmation(string side, List<MarketBarRow> sectorBarsDesc)
    {
        if (sectorBarsDesc.Count < 3)
            return true;

        var latest = sectorBarsDesc[0];
        var prev = sectorBarsDesc.Skip(1).Take(2).ToList();
        var last2High = prev.Max(b => b.High);
        var last2Low = prev.Min(b => b.Low);

        return side == SignalSides.Buy
            ? latest.High > last2High
            : latest.Low < last2Low;
    }

    private async Task<List<MarketBarRow>?> LoadNiftyDailyBarsAsync(CancellationToken ct)
    {
        foreach (var symbol in new[] { "NIFTY", "NIFTY 50", "NIFTY50" })
        {
            var inst = await _instruments.FindBySymbolAsync(symbol, ct);
            if (inst is null)
                continue;
            var bars = (await _market.GetBarsForInstrumentAsync(inst.Id, MomentumScoreHelpers.MomentumBarDays, ct))
                .OrderByDescending(b => b.TradeDate)
                .ToList();
            if (bars.Count >= 5)
                return bars;
        }
        return null;
    }

    private async Task<Dictionary<Guid, List<MarketBarRow>>> LoadUniverseBarsAsync(
        IEnumerable<Guid> instrumentIds, CancellationToken ct)
    {
        var map = new Dictionary<Guid, List<MarketBarRow>>();
        foreach (var id in instrumentIds)
        {
            var bars = (await _market.GetBarsForInstrumentAsync(id, MomentumScoreHelpers.MomentumBarDays, ct))
                .OrderByDescending(b => b.TradeDate)
                .ToList();
            if (bars.Count > 0)
                map[id] = bars;
        }
        return map;
    }
}
