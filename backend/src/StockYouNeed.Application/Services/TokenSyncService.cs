using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StockYouNeed.Application.Abstractions;
using StockYouNeed.Application.Options;
using StockYouNeed.Domain;

namespace StockYouNeed.Application.Services;

public sealed class TokenSyncService
{
    private readonly IAngelMarketDataClient _angel;
    private readonly IInstrumentRepository _instruments;
    private readonly AngelOptions _options;
    private readonly ILogger<TokenSyncService> _logger;

    public TokenSyncService(
        IAngelMarketDataClient angel,
        IInstrumentRepository instruments,
        IOptions<AngelOptions> options,
        ILogger<TokenSyncService> logger)
    {
        _angel = angel;
        _instruments = instruments;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<int> SyncUniverseTokensAsync(CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            _logger.LogWarning("Angel is disabled; skipping token sync.");
            return 0;
        }

        var equities = await _instruments.GetUniverseEquitiesAsync(ct);
        if (equities.Count == 0)
        {
            _logger.LogWarning("No universe equities found. Seed Nifty symbols first.");
            return 0;
        }

        var scrips = await _angel.DownloadScripMasterAsync(ct);
        var nseEquity = new Dictionary<string, AngelScrip>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in scrips.Where(s => s.ExchSeg.Equals("NSE", StringComparison.OrdinalIgnoreCase)))
        {
            if (!s.Symbol.EndsWith("-EQ", StringComparison.OrdinalIgnoreCase))
                continue;
            var key = s.Name.Trim().ToUpperInvariant();
            if (!nseEquity.ContainsKey(key))
                nseEquity[key] = s;
        }

        var matched = 0;
        foreach (var equity in equities)
        {
            var key = equity.Symbol.Trim().ToUpperInvariant();
            if (!nseEquity.TryGetValue(key, out var scrip))
            {
                scrip = scrips.FirstOrDefault(s =>
                    s.ExchSeg.Equals("NSE", StringComparison.OrdinalIgnoreCase)
                    && s.Symbol.StartsWith(key + "-", StringComparison.OrdinalIgnoreCase));
            }

            if (scrip is null)
            {
                _logger.LogWarning("No Angel NSE token for {Symbol}", equity.Symbol);
                continue;
            }

            await _instruments.UpsertAngelTokenAsync(new AngelTokenRow
            {
                InstrumentId = equity.Id,
                Exchange = "NSE",
                SymbolToken = scrip.Token,
                TradingSymbol = scrip.Symbol,
                Name = scrip.Name,
                AppSymbol = equity.Symbol
            }, ct);
            matched++;
        }

        _logger.LogInformation("Token sync matched {Matched}/{Total} universe equities.", matched, equities.Count);
        return matched;
    }
}
