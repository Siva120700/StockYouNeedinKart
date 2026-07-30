using Microsoft.Extensions.Logging;
using StockYouNeed.Application.Abstractions;
using StockYouNeed.Application.Outcomes;
using StockYouNeed.Application.Services;
using StockYouNeed.Application.TradeScore;
using StockYouNeed.Domain;

namespace StockYouNeed.Application.Breakout;

/// <summary>
/// Standalone pattern-breakout scan for F&amp;O confirmation path.
/// Separate from primary Signals strategy and Trade Score.
/// </summary>
public sealed class BreakoutAnalysisService
{
    private const int RequiredBars = 40;

    private readonly IBreakoutRepository _breakout;
    private readonly IMarketDataRepository _market;
    private readonly IInstrumentRepository _instruments;
    private readonly MarketBarsSyncService _barsSync;
    private readonly SignalOutcomeService _outcomes;
    private readonly ILogger<BreakoutAnalysisService> _logger;

    public BreakoutAnalysisService(
        IBreakoutRepository breakout,
        IMarketDataRepository market,
        IInstrumentRepository instruments,
        MarketBarsSyncService barsSync,
        SignalOutcomeService outcomes,
        ILogger<BreakoutAnalysisService> logger)
    {
        _breakout = breakout;
        _market = market;
        _instruments = instruments;
        _barsSync = barsSync;
        _outcomes = outcomes;
        _logger = logger;
    }

    public async Task<IReadOnlyList<BreakoutConfirmationRow>> GetConfirmationsAsync(
        Guid userId, Guid? runId, CancellationToken ct = default)
        => await _breakout.GetConfirmationsAsync(userId, runId, ct);

    public async Task<BreakoutAnalysisRunRow> RunAsync(Guid userId, CancellationToken ct = default)
    {
        var asOf = DateOnly.FromDateTime(DateTime.Now);
        var runId = await _breakout.CreateRunAsync(userId, "manual", asOf, ct);

        try
        {
            var universe = await _instruments.GetUniverseEquitiesAsync(ct);

            // Pattern breakouts need ~40–60 daily bars; worker historically kept ~10.
            var sampleId = universe.FirstOrDefault()?.Id;
            var sampleCount = sampleId is Guid id
                ? (await _market.GetBarsForInstrumentAsync(id, RequiredBars, ct)).Count
                : 0;
            if (sampleCount < RequiredBars)
            {
                _logger.LogInformation(
                    "Breakout: only {Count} daily bars in DB — syncing {Lookback} days from Angel (may take a few minutes)…",
                    sampleCount, RequiredBars);
                await _barsSync.SyncLastNTradingDaysAsync(RequiredBars, ct);
            }

            var confirmed = 0;
            var scanned = 0;
            var fewBars = 0;

            foreach (var inst in universe)
            {
                ct.ThrowIfCancellationRequested();
                var bars = await _market.GetBarsForInstrumentAsync(inst.Id, 80, ct);
                var barsDesc = bars.OrderByDescending(b => b.TradeDate).ToList();
                if (barsDesc.Count < PatternBreakoutEvaluator.MinBars)
                {
                    fewBars++;
                    continue;
                }

                var result = BreakoutConfirmationEvaluator.Evaluate(barsDesc);
                if (result is null)
                    continue;

                scanned++;
                var confirmation = new BreakoutConfirmationRow
                {
                    Id = Guid.NewGuid(),
                    RunId = runId,
                    UserId = userId,
                    InstrumentId = inst.Id,
                    AppSymbol = inst.Symbol,
                    InstrumentName = inst.Name,
                    Side = result.Confirmed ? result.Side : SignalSides.Buy,
                    AsOfDate = barsDesc[0].TradeDate,
                    Confirmed = result.Confirmed,
                    ClosePrice = result.Close,
                    Level20d = result.BreakoutLevel,
                    VolumeRatio = result.VolumeRatio,
                    PatternType = result.PatternType,
                };
                await _breakout.InsertConfirmationAsync(confirmation, ct);
                if (result.Confirmed)
                {
                    await _outcomes.OpenFromBreakoutAsync(confirmation, ct);
                    confirmed++;
                }
            }

            await _breakout.CompleteRunAsync(runId, userId, "succeeded", null, ct);
            _logger.LogInformation(
                "Breakout run {RunId}: confirmed={Confirmed}, scanned={Scanned}, fewBars={FewBars}, universe={Total}",
                runId, confirmed, scanned, fewBars, universe.Count);

            return new BreakoutAnalysisRunRow { Id = runId, UserId = userId, AsOfDate = asOf, Status = "succeeded" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Breakout run {RunId} failed", runId);
            try
            {
                await _breakout.CompleteRunAsync(runId, userId, "failed", ex.Message, ct);
            }
            catch (Exception completeEx)
            {
                _logger.LogWarning(completeEx, "Could not mark breakout run failed");
            }

            throw new InvalidOperationException($"Breakout analysis failed: {ex.Message}", ex);
        }
    }
}
