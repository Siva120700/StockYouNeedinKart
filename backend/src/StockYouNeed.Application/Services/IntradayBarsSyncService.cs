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
    private const int MinBarsToSkip = 80;
    private const int LookbackSessions = 15;

    private readonly IAngelMarketDataClient _angel;
    private readonly IInstrumentRepository _instruments;
    private readonly IMarketDataRepository _market;
    private readonly AngelOptions _angelOptions;
    private readonly ILogger<IntradayBarsSyncService> _logger;

    public IntradayBarsSyncService(
        IAngelMarketDataClient angel,
        IInstrumentRepository instruments,
        IMarketDataRepository market,
        IOptions<AngelOptions> angelOptions,
        ILogger<IntradayBarsSyncService> logger)
    {
        _angel = angel;
        _instruments = instruments;
        _market = market;
        _angelOptions = angelOptions.Value;
        _logger = logger;
    }

    /// <summary>Ensure universe equities have enough 1H history. Skips symbols that already have enough bars.</summary>
    public async Task<int> SyncUniverseHourlyAsync(CancellationToken ct = default, bool force = false)
    {
        if (!_angelOptions.Enabled)
        {
            _logger.LogWarning("Angel disabled; skipping intraday bar sync.");
            return 0;
        }

        await _angel.EnsureSessionAsync(ct);
        var tokens = await _instruments.GetActiveTokensForUniversesAsync(ct);
        if (tokens.Count == 0)
        {
            _logger.LogWarning("No Angel tokens for intraday sync.");
            return 0;
        }

        var toIst = DateTime.Now;
        var fromIst = toIst.Date.AddDays(-(LookbackSessions * 2 + 5));
        var upserted = 0;

        foreach (var token in tokens)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (!force)
                {
                    var existing = await _market.CountIntradayBarsAsync(token.InstrumentId, Interval1h, ct);
                    if (existing >= MinBarsToSkip)
                        continue;
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
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "1H sync failed for {Symbol}", token.AppSymbol);
            }

            await Task.Delay(900, ct);
        }

        _logger.LogInformation("Intraday 1H sync upserted {Count} bars across universe.", upserted);
        return upserted;
    }
}
