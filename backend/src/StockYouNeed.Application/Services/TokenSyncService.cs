using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StockYouNeed.Application.Abstractions;
using StockYouNeed.Application.Options;
using StockYouNeed.Domain;

namespace StockYouNeed.Application.Services;

public sealed class TokenSyncService
{
    /// <summary>App symbol → Angel NSE equity root (before -EQ) when renamed/demerged.</summary>
    private static readonly Dictionary<string, string> EquitySymbolAliases =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["LTIM"] = "LTM",
            ["TATAMOTORS"] = "TMPV", // renamed; CV lists separately as TMCV
        };

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
        // Prefer lookup by trading root (RELIANCE from RELIANCE-EQ) — Angel Name is often the company name.
        var byTradingRoot = new Dictionary<string, AngelScrip>(StringComparer.OrdinalIgnoreCase);
        var nseIndex = new List<AngelScrip>();
        foreach (var s in scrips.Where(s => s.ExchSeg.Equals("NSE", StringComparison.OrdinalIgnoreCase)))
        {
            if (s.Symbol.EndsWith("-EQ", StringComparison.OrdinalIgnoreCase))
            {
                var root = s.Symbol[..^3]; // strip -EQ
                if (!string.IsNullOrWhiteSpace(root) && !byTradingRoot.ContainsKey(root))
                    byTradingRoot[root] = s;
            }
            else if (s.InstrumentType.Equals("AMXIDX", StringComparison.OrdinalIgnoreCase)
                     || s.Name.Contains("Nifty", StringComparison.OrdinalIgnoreCase))
            {
                nseIndex.Add(s);
            }
        }

        var matched = 0;
        foreach (var equity in equities)
        {
            var appKey = equity.Symbol.Trim().ToUpperInvariant();
            var lookupKeys = new List<string> { appKey };
            if (EquitySymbolAliases.TryGetValue(appKey, out var alias))
                lookupKeys.Add(alias.ToUpperInvariant());

            AngelScrip? scrip = null;
            string? matchedAs = null;
            foreach (var key in lookupKeys)
            {
                if (byTradingRoot.TryGetValue(key, out scrip))
                {
                    matchedAs = key;
                    break;
                }

                scrip = scrips.FirstOrDefault(s =>
                    s.ExchSeg.Equals("NSE", StringComparison.OrdinalIgnoreCase)
                    && s.Symbol.StartsWith(key + "-", StringComparison.OrdinalIgnoreCase));
                if (scrip is not null)
                {
                    matchedAs = key;
                    break;
                }
            }

            if (scrip is null)
            {
                _logger.LogWarning("No Angel NSE token for {Symbol}", equity.Symbol);
                continue;
            }

            if (matchedAs is not null && !matchedAs.Equals(appKey, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation(
                    "Token alias {AppSymbol} → Angel {Trading} ({Token})",
                    equity.Symbol, scrip.Symbol, scrip.Token);
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

        var sectors = await _instruments.GetSectorIndexesAsync(ct);
        var sectorMatched = 0;
        foreach (var sector in sectors)
        {
            if (!UniverseSeedService.SectorAngelNameHints.TryGetValue(sector.Symbol, out var hint))
                hint = sector.Name;

            var scrip = nseIndex.FirstOrDefault(s =>
                s.Name.Equals(hint, StringComparison.OrdinalIgnoreCase)
                || s.Name.Equals(sector.Name, StringComparison.OrdinalIgnoreCase));

            if (scrip is null)
            {
                var candidates = nseIndex
                    .Where(s => s.Name.Contains(hint, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(s => s.Name.Length)
                    .ToList();
                if (candidates.Count > 0)
                    scrip = candidates[0];
            }

            if (scrip is null)
            {
                _logger.LogWarning("No Angel NSE index token for sector {Symbol} (hint: {Hint})", sector.Symbol, hint);
                continue;
            }

            await _instruments.UpsertAngelTokenAsync(new AngelTokenRow
            {
                InstrumentId = sector.Id,
                Exchange = "NSE",
                SymbolToken = scrip.Token,
                TradingSymbol = scrip.Symbol,
                Name = scrip.Name,
                AppSymbol = sector.Symbol
            }, ct);
            sectorMatched++;
            _logger.LogInformation("Sector token {Symbol} → Angel {Name} ({Token})",
                sector.Symbol, scrip.Name, scrip.Token);
        }

        _logger.LogInformation(
            "Token sync matched equities {Matched}/{Total}, sectors {SectorMatched}/{SectorTotal}.",
            matched, equities.Count, sectorMatched, sectors.Count);
        return matched + sectorMatched;
    }
}
