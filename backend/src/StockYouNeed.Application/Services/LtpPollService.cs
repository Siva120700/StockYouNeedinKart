using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StockYouNeed.Application.Abstractions;
using StockYouNeed.Application.Options;
using StockYouNeed.Domain;

namespace StockYouNeed.Application.Services;

public sealed class LtpPollService
{
    private readonly IAngelMarketDataClient _angel;
    private readonly IInstrumentRepository _instruments;
    private readonly IMarketDataRepository _market;
    private readonly AngelOptions _options;
    private readonly ILogger<LtpPollService> _logger;

    public LtpPollService(
        IAngelMarketDataClient angel,
        IInstrumentRepository instruments,
        IMarketDataRepository market,
        IOptions<AngelOptions> options,
        ILogger<LtpPollService> logger)
    {
        _angel = angel;
        _instruments = instruments;
        _market = market;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<int> PollOnceAsync(CancellationToken ct = default)
    {
        if (!_options.Enabled)
            return 0;

        await _angel.EnsureSessionAsync(ct);
        var tokens = await _instruments.GetActiveTokensForUniversesAsync(ct);
        if (tokens.Count == 0)
            return 0;

        var updated = 0;
        foreach (var chunk in tokens.Chunk(50))
        {
            ct.ThrowIfCancellationRequested();
            var started = DateTimeOffset.UtcNow;
            var exchangeTokens = chunk
                .GroupBy(t => t.Exchange)
                .ToDictionary(
                    g => g.Key,
                    g => (IReadOnlyList<string>)g.Select(x => x.SymbolToken).Distinct().ToList());

            var requestJson = JsonSerializer.Serialize(exchangeTokens);
            try
            {
                var quotes = await _angel.GetQuotesAsync(QuoteModes.Ltp, exchangeTokens, ct);
                var byToken = quotes.ToDictionary(
                    q => (q.Exchange, q.SymbolToken),
                    q => q);

                foreach (var token in chunk)
                {
                    if (!byToken.TryGetValue((token.Exchange, token.SymbolToken), out var quote)
                        || quote.Ltp is null)
                        continue;

                    await _market.UpsertLtpAsync(
                        token.InstrumentId,
                        token.Exchange,
                        quote.TradingSymbol.Length > 0 ? quote.TradingSymbol : token.TradingSymbol,
                        token.SymbolToken,
                        quote.Ltp.Value,
                        quote.RawJson,
                        ct);
                    updated++;
                }

                await _market.LogQuoteFetchBatchAsync(
                    QuoteModes.Ltp,
                    chunk.Length,
                    quotes.Count,
                    Math.Max(0, chunk.Length - quotes.Count),
                    true,
                    "SUCCESS",
                    "",
                    requestJson,
                    "[]",
                    null,
                    (int)(DateTimeOffset.UtcNow - started).TotalMilliseconds,
                    ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "LTP poll chunk failed");
                await _market.LogQuoteFetchBatchAsync(
                    QuoteModes.Ltp,
                    chunk.Length,
                    0,
                    chunk.Length,
                    false,
                    ex.Message,
                    "LOCAL",
                    requestJson,
                    "[]",
                    null,
                    (int)(DateTimeOffset.UtcNow - started).TotalMilliseconds,
                    ct);
            }

            // Angel: 1 request per second
            await Task.Delay(1100, ct);
        }

        return updated;
    }
}
