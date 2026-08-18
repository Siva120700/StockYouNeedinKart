using Microsoft.Extensions.Logging;
using StockYouNeed.Application.Abstractions;
using StockYouNeed.Application.Signals;
using StockYouNeed.Domain;

namespace StockYouNeed.Application.Services;

public sealed class SpikeScanService
{
    public const int BarsPerSymbol = SpikeScanEvaluator.VolumeLookback + 8;

    private readonly IntradayBarsSyncService _intradaySync;
    private readonly IMarketDataRepository _market;
    private readonly ILogger<SpikeScanService> _logger;

    public SpikeScanService(
        IntradayBarsSyncService intradaySync,
        IMarketDataRepository market,
        ILogger<SpikeScanService> logger)
    {
        _intradaySync = intradaySync;
        _market = market;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SpikeScanRow>> GetLatestAsync(CancellationToken ct = default)
        => ScoreCached(await _market.GetIntradayBarsForUniverseAsync(
            SpikeScanEvaluator.Interval15m, BarsPerSymbol, ct));

    public async Task<IReadOnlyList<SpikeScanRow>> RunAsync(CancellationToken ct = default)
    {
        var upserted = await _intradaySync.SyncUniverseFifteenMinuteAsync(ct);
        _logger.LogInformation("15m spike scan synced {Count} bars.", upserted);
        return await GetLatestAsync(ct);
    }

    private static IReadOnlyList<SpikeScanRow> ScoreCached(IReadOnlyList<MarketIntradayBarRow> bars)
    {
        var now = DateTimeOffset.UtcNow;
        var hits = new List<SpikeScanRow>();
        foreach (var group in bars.GroupBy(b => b.InstrumentId))
        {
            var newestFirst = group
                .OrderByDescending(b => b.BarTime)
                .ToList();
            var hit = SpikeScanEvaluator.Evaluate(newestFirst, now);
            if (hit is not null)
                hits.Add(hit);
        }

        return hits
            .OrderByDescending(h => h.SpikeScore)
            .ThenByDescending(h => Math.Abs(h.ChangePct))
            .ToList();
    }
}
