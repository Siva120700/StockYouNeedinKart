using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StockYouNeed.Application.Abstractions;
using StockYouNeed.Application.Options;
using StockYouNeed.Application.Signals;
using StockYouNeed.Domain;

namespace StockYouNeed.Application.Services;

public sealed class MarketBarsSyncService
{
    private readonly IAngelMarketDataClient _angel;
    private readonly IInstrumentRepository _instruments;
    private readonly IMarketDataRepository _market;
    private readonly TokenSyncService _tokenSync;
    private readonly AngelOptions _angelOptions;
    private readonly WorkerScheduleOptions _schedule;
    private readonly ILogger<MarketBarsSyncService> _logger;

    public MarketBarsSyncService(
        IAngelMarketDataClient angel,
        IInstrumentRepository instruments,
        IMarketDataRepository market,
        TokenSyncService tokenSync,
        IOptions<AngelOptions> angelOptions,
        IOptions<WorkerScheduleOptions> schedule,
        ILogger<MarketBarsSyncService> logger)
    {
        _angel = angel;
        _instruments = instruments;
        _market = market;
        _tokenSync = tokenSync;
        _angelOptions = angelOptions.Value;
        _schedule = schedule.Value;
        _logger = logger;
    }

    public async Task<int> SyncLastNTradingDaysAsync(CancellationToken ct = default)
        => await SyncLastNTradingDaysAsync(lookbackOverride: null, ct);

    /// <summary>Fetch/upsert last N daily bars (override lookback for pattern breakouts).</summary>
    public async Task<int> SyncLastNTradingDaysAsync(int? lookbackOverride, CancellationToken ct = default)
    {
        if (!_angelOptions.Enabled)
        {
            _logger.LogWarning("Angel is disabled; skipping market bars sync.");
            return 0;
        }

        await _angel.EnsureSessionAsync(ct);
        await _tokenSync.EnsureUniverseTokensMappedAsync(ct);
        var tokens = (await _instruments.GetActiveTokensForUniversesAsync(ct)).ToList();
        var sectorTokens = await _instruments.GetActiveTokensForSectorsAsync(ct);
        foreach (var t in sectorTokens)
            tokens.Add(t);

        if (tokens.Count == 0)
        {
            _logger.LogWarning("No Angel tokens mapped; run token sync first.");
            return 0;
        }

        return await SyncTokensBarsAsync(tokens, lookbackOverride, ct);
    }

    /// <summary>
    /// Ensure ~12M daily history for V3 momentum (253+ bars). Only hits Angel for symbols still short.
    /// </summary>
    public async Task<int> EnsureMomentumDailyHistoryAsync(CancellationToken ct = default)
    {
        if (!_angelOptions.Enabled)
            return 0;

        const int minBars = MomentumScoreHelpers.TradingDays12M + 1;
        await _angel.EnsureSessionAsync(ct);
        await _tokenSync.EnsureUniverseTokensMappedAsync(ct);
        var tokens = (await _instruments.GetActiveTokensForUniversesAsync(ct)).ToList();
        if (tokens.Count == 0)
            return 0;

        var missing = new List<AngelTokenRow>();
        foreach (var token in tokens)
        {
            ct.ThrowIfCancellationRequested();
            var count = (await _market.GetBarsForInstrumentAsync(token.InstrumentId, minBars, ct)).Count;
            if (count < minBars)
                missing.Add(token);
        }

        if (missing.Count == 0)
        {
            _logger.LogInformation("All {Count} universe symbols have V3 daily history ({MinBars}+ bars).", tokens.Count, minBars);
            return 0;
        }

        _logger.LogInformation(
            "Syncing {Missing}/{Total} symbols missing V3 daily history ({Lookback} trading days from Angel; may take several minutes)…",
            missing.Count, tokens.Count, MomentumScoreHelpers.MomentumBarDays);
        return await SyncTokensBarsAsync(missing, MomentumScoreHelpers.MomentumBarDays, ct);
    }

    /// <summary>Fetch/upsert last N daily bars only for sector indexes that lack history.</summary>
    public async Task<int> SyncMissingSectorBarsAsync(CancellationToken ct = default)
    {
        if (!_angelOptions.Enabled)
            return 0;

        var sectorTokens = await _instruments.GetActiveTokensForSectorsAsync(ct);
        if (sectorTokens.Count == 0)
        {
            _logger.LogWarning("No sector Angel tokens mapped yet.");
            return 0;
        }

        var missing = new List<AngelTokenRow>();
        foreach (var token in sectorTokens)
        {
            var bars = await _market.GetBarsForInstrumentAsync(token.InstrumentId, 5, ct);
            if (bars.Count < 5)
                missing.Add(token);
        }

        if (missing.Count == 0)
        {
            _logger.LogInformation("All {Count} sector indexes already have bars.", sectorTokens.Count);
            return 0;
        }

        _logger.LogInformation("Syncing bars for {Count} sector indexes missing history…", missing.Count);
        await _angel.EnsureSessionAsync(ct);
        return await SyncTokensBarsAsync(missing, lookbackOverride: null, ct);
    }

    private async Task<int> SyncTokensBarsAsync(
        IReadOnlyList<AngelTokenRow> tokens, int? lookbackOverride, CancellationToken ct)
    {
        var lookback = Math.Max(10, lookbackOverride ?? _schedule.MarketBarsLookbackDays);
        // Calendar buffer to cover weekends/holidays for ~N trading days
        var toIst = DateTime.Now;
        var fromIst = toIst.Date.AddDays(-(lookback * 2 + 5));

        var barCount = 0;
        foreach (var token in tokens)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var candles = await _angel.GetDailyCandlesAsync(
                    token.Exchange, token.SymbolToken, fromIst, toIst, ct);

                foreach (var candle in candles.OrderByDescending(c => c.TradeDate).Take(lookback))
                {
                    await _market.UpsertMarketBarAsync(
                        token.InstrumentId,
                        candle.TradeDate,
                        candle.Open,
                        candle.High,
                        candle.Low,
                        candle.Close,
                        candle.Volume,
                        ct);
                    barCount++;
                }

                // Angel historical is rate-limited; pace to avoid 403 Access denied
                await Task.Delay(900, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed bars sync for {Symbol} ({Token})", token.AppSymbol, token.SymbolToken);
            }
        }

        // Never trim below V3 momentum lookback — worker's 10-day sync must not wipe 12M history.
        var trimKeep = Math.Max(lookback + 5, MomentumScoreHelpers.MomentumBarDays + 5);
        await _market.TrimMarketBarsOlderThanAsync(trimKeep, ct);
        _logger.LogInformation("Upserted {BarCount} daily bars for {TokenCount} instruments.", barCount, tokens.Count);
        return barCount;
    }
}
