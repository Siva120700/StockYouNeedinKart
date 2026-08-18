using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StockYouNeed.Application.Abstractions;
using StockYouNeed.Application.Options;
using StockYouNeed.Domain;

namespace StockYouNeed.Application.Services;

/// <summary>Syncs Angel ONE_HOUR candles into market_intraday_bars for the liquidity engine.</summary>
public sealed class IntradayBarsSyncService
{
    public const string Interval1h = "1h";
    private const int LookbackSessions = 15;
    /// <summary>Skip Angel fetch only when latest 1H bar is newer than this (avoids stale liquidity).</summary>
    private static readonly TimeSpan MaxStale = TimeSpan.FromHours(3);

    private readonly IAngelMarketDataClient _angel;
    private readonly IInstrumentRepository _instruments;
    private readonly IMarketDataRepository _market;
    private readonly TokenSyncService _tokenSync;
    private readonly AngelOptions _angelOptions;
    private readonly ILogger<IntradayBarsSyncService> _logger;

    public IntradayBarsSyncService(
        IAngelMarketDataClient angel,
        IInstrumentRepository instruments,
        IMarketDataRepository market,
        TokenSyncService tokenSync,
        IOptions<AngelOptions> angelOptions,
        ILogger<IntradayBarsSyncService> logger)
    {
        _angel = angel;
        _instruments = instruments;
        _market = market;
        _tokenSync = tokenSync;
        _angelOptions = angelOptions.Value;
        _logger = logger;
    }

    /// <summary>
    /// Refresh 1H bars for universe equities. Skips a symbol only when its latest bar is fresh
    /// (within <see cref="MaxStale"/>). Older caches are always topped up from Angel.
    /// </summary>
    public async Task<int> SyncUniverseHourlyAsync(CancellationToken ct = default, bool force = false)
    {
        if (!_angelOptions.Enabled)
        {
            _logger.LogWarning("Angel disabled; skipping intraday bar sync.");
            return 0;
        }

        await _angel.EnsureSessionAsync(ct);
        await _tokenSync.EnsureUniverseTokensMappedAsync(ct);
        var tokens = await _instruments.GetActiveTokensForUniversesAsync(ct);
        if (tokens.Count == 0)
        {
            _logger.LogWarning("No Angel tokens for intraday sync.");
            return 0;
        }

        var toIst = DateTime.Now;
        var fullFromIst = toIst.Date.AddDays(-(LookbackSessions * 2 + 5));
        var nowUtc = DateTimeOffset.UtcNow;
        var upserted = 0;
        var skippedFresh = 0;
        var refreshed = 0;

        foreach (var token in tokens)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var latest = await _market.GetLatestIntradayBarTimeAsync(
                    token.InstrumentId, Interval1h, ct);

                if (!force && latest is not null && nowUtc - latest.Value <= MaxStale)
                {
                    skippedFresh++;
                    continue;
                }

                // Incremental: from day before last bar (overlap) when we already have history.
                var fromIst = fullFromIst;
                if (latest is not null && !force)
                {
                    var latestIst = latest.Value.ToOffset(TimeSpan.FromHours(5.5)).DateTime;
                    fromIst = latestIst.AddDays(-1);
                    if (fromIst < fullFromIst)
                        fromIst = fullFromIst;
                }

                var candles = await _angel.GetHourlyCandlesAsync(
                    token.Exchange, token.SymbolToken, fromIst, toIst, ct);

                foreach (var c in candles)
                {
                    if (c.BarTime is null)
                        continue;
                    await _market.UpsertIntradayBarAsync(
                        token.InstrumentId,
                        Interval1h,
                        c.BarTime.Value,
                        c.Open, c.High, c.Low, c.Close, c.Volume,
                        ct);
                    upserted++;
                }

                refreshed++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "1H sync failed for {Symbol}", token.AppSymbol);
            }

            await Task.Delay(900, ct);
        }

        _logger.LogInformation(
            "Intraday 1H sync upserted {Count} bars (refreshed={Refreshed}, skippedFresh={Skipped}).",
            upserted, refreshed, skippedFresh);
        return upserted;
    }

    /// <summary>Refresh 1H bars for one equity token (used by Analyze Stock liquidity deep-dive).</summary>
    public async Task<int> SyncInstrumentHourlyAsync(
        AngelTokenRow token, CancellationToken ct = default, bool force = false)
    {
        if (!_angelOptions.Enabled)
            return 0;

        await _angel.EnsureSessionAsync(ct);
        var toIst = DateTime.Now;
        var fullFromIst = toIst.Date.AddDays(-(LookbackSessions * 2 + 5));
        var nowUtc = DateTimeOffset.UtcNow;

        var latest = await _market.GetLatestIntradayBarTimeAsync(token.InstrumentId, Interval1h, ct);
        if (!force && latest is not null && nowUtc - latest.Value <= MaxStale)
            return 0;

        var fromIst = fullFromIst;
        if (latest is not null && !force)
        {
            var latestIst = latest.Value.ToOffset(TimeSpan.FromHours(5.5)).DateTime;
            fromIst = latestIst.AddDays(-1);
            if (fromIst < fullFromIst)
                fromIst = fullFromIst;
        }

        var candles = await _angel.GetHourlyCandlesAsync(
            token.Exchange, token.SymbolToken, fromIst, toIst, ct);
        var upserted = 0;
        foreach (var c in candles)
        {
            if (c.BarTime is null)
                continue;
            await _market.UpsertIntradayBarAsync(
                token.InstrumentId, Interval1h, c.BarTime.Value,
                c.Open, c.High, c.Low, c.Close, c.Volume, ct);
            upserted++;
        }

        _logger.LogInformation(
            "Intraday 1H sync for {Symbol}: upserted {Count} bars.", token.AppSymbol, upserted);
        return upserted;
    }
}
