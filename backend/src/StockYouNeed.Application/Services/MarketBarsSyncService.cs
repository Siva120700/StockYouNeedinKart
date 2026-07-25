using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StockYouNeed.Application.Abstractions;
using StockYouNeed.Application.Options;
using StockYouNeed.Domain;

namespace StockYouNeed.Application.Services;

public sealed class MarketBarsSyncService
{
    private readonly IAngelMarketDataClient _angel;
    private readonly IInstrumentRepository _instruments;
    private readonly IMarketDataRepository _market;
    private readonly AngelOptions _angelOptions;
    private readonly WorkerScheduleOptions _schedule;
    private readonly ILogger<MarketBarsSyncService> _logger;

    public MarketBarsSyncService(
        IAngelMarketDataClient angel,
        IInstrumentRepository instruments,
        IMarketDataRepository market,
        IOptions<AngelOptions> angelOptions,
        IOptions<WorkerScheduleOptions> schedule,
        ILogger<MarketBarsSyncService> logger)
    {
        _angel = angel;
        _instruments = instruments;
        _market = market;
        _angelOptions = angelOptions.Value;
        _schedule = schedule.Value;
        _logger = logger;
    }

    public async Task<int> SyncLastNTradingDaysAsync(CancellationToken ct = default)
    {
        if (!_angelOptions.Enabled)
        {
            _logger.LogWarning("Angel is disabled; skipping market bars sync.");
            return 0;
        }

        await _angel.EnsureSessionAsync(ct);
        var tokens = await _instruments.GetActiveTokensForUniversesAsync(ct);
        if (tokens.Count == 0)
        {
            _logger.LogWarning("No Angel tokens mapped; run token sync first.");
            return 0;
        }

        var lookback = Math.Max(10, _schedule.MarketBarsLookbackDays);
        // Calendar buffer to cover weekends/holidays for ~N trading days
        var toIst = DateTime.Now; // worker should run in IST or convert; candles accept local-like strings
        var fromIst = toIst.Date.AddDays(-(lookback * 2 + 5));

        var barCount = 0;
        foreach (var token in tokens)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var candles = await _angel.GetDailyCandlesAsync(
                    token.Exchange, token.SymbolToken, fromIst, toIst, ct);

                // Keep last N by date
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

        await _market.TrimMarketBarsOlderThanAsync(lookback + 5, ct);
        _logger.LogInformation("Upserted {BarCount} daily bars for {TokenCount} instruments.", barCount, tokens.Count);
        return barCount;
    }
}
