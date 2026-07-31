using System.Globalization;
using Microsoft.Extensions.Logging;
using StockYouNeed.Application.Abstractions;
using StockYouNeed.Domain;

namespace StockYouNeed.Application.OptionsIntraday;

/// <summary>Sync NFO FUTSTK/OPTSTK from Angel scrip master for universe equities.</summary>
public sealed class NfoSyncService
{
    private static readonly Dictionary<string, string> EquitySymbolAliases =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["LTIM"] = "LTM",
            ["TATAMOTORS"] = "TMPV",
        };

    private readonly IAngelMarketDataClient _angel;
    private readonly IInstrumentRepository _instruments;
    private readonly IOptionsIntradayRepository _nfo;
    private readonly ILogger<NfoSyncService> _logger;

    public NfoSyncService(
        IAngelMarketDataClient angel,
        IInstrumentRepository instruments,
        IOptionsIntradayRepository nfo,
        ILogger<NfoSyncService> logger)
    {
        _angel = angel;
        _instruments = instruments;
        _nfo = nfo;
        _logger = logger;
    }

    public async Task<int> SyncUniverseNfoAsync(CancellationToken ct = default)
    {
        var equities = await _instruments.GetUniverseEquitiesAsync(ct);
        if (equities.Count == 0) return 0;

        var scrips = await _angel.DownloadScripMasterAsync(ct);
        var nfo = scrips
            .Where(s => s.ExchSeg.Equals("NFO", StringComparison.OrdinalIgnoreCase))
            .Where(s =>
                s.InstrumentType.Equals("OPTSTK", StringComparison.OrdinalIgnoreCase)
                || s.InstrumentType.Equals("FUTSTK", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var byName = nfo
            .GroupBy(s => s.Name.Trim().ToUpperInvariant())
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(5.5)).DateTime);
        var rows = new List<NfoContractRow>();

        foreach (var eq in equities)
        {
            ct.ThrowIfCancellationRequested();
            var keys = LookupKeys(eq.Symbol);
            List<AngelScrip>? match = null;
            string? angelName = null;
            foreach (var key in keys)
            {
                if (byName.TryGetValue(key, out match))
                {
                    angelName = key;
                    break;
                }
            }

            if (match is null || angelName is null) continue;

            foreach (var s in match)
            {
                if (!TryParseExpiry(s.Expiry, out var expiry, out var label))
                    continue;
                if (expiry < today) continue;

                var isOpt = s.InstrumentType.Equals("OPTSTK", StringComparison.OrdinalIgnoreCase);
                string? optType = null;
                decimal? strike = null;
                if (isOpt)
                {
                    optType = InferOptionType(s.Symbol);
                    if (optType is null) continue;
                    strike = ParseStrike(s.Strike);
                    if (strike is null or <= 0) continue;
                }

                _ = int.TryParse(s.LotSize, out var lot);
                if (lot <= 0) lot = 1;
                _ = decimal.TryParse(s.TickSize, NumberStyles.Any, CultureInfo.InvariantCulture, out var tick);
                if (tick <= 0) tick = 0.05m;

                rows.Add(new NfoContractRow
                {
                    Id = Guid.NewGuid(),
                    UnderlyingInstrumentId = eq.Id,
                    AppSymbol = eq.Symbol,
                    AngelName = angelName,
                    Kind = isOpt ? "option" : "future",
                    OptionType = optType,
                    Strike = strike,
                    Expiry = expiry,
                    ExpiryLabel = label,
                    SymbolToken = s.Token,
                    TradingSymbol = s.Symbol,
                    LotSize = lot,
                    TickSize = tick,
                });
            }
        }

        await _nfo.ReplaceNfoContractsAsync(rows, ct);
        _logger.LogInformation("NFO sync: stored {Count} live contracts for universe", rows.Count);
        return rows.Count;
    }

    private static List<string> LookupKeys(string appSymbol)
    {
        var appKey = appSymbol.Trim().ToUpperInvariant();
        var keys = new List<string> { appKey };
        if (EquitySymbolAliases.TryGetValue(appKey, out var alias))
            keys.Add(alias.ToUpperInvariant());
        return keys;
    }

    public static bool TryParseExpiry(string raw, out DateOnly expiry, out string label)
    {
        expiry = default;
        label = "";
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var cleaned = raw.Trim().Replace("-", "").Replace(" ", "").ToUpperInvariant();
        string[] formats = ["ddMMMyyyy", "ddMMMyy", "yyyy-MM-dd", "dd-MMM-yyyy"];
        foreach (var f in formats)
        {
            if (DateTime.TryParseExact(cleaned, f, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var dt)
                || DateTime.TryParseExact(raw.Trim(), f, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out dt))
            {
                expiry = DateOnly.FromDateTime(dt);
                label = expiry.ToString("ddMMMyyyy", CultureInfo.InvariantCulture).ToUpperInvariant();
                return true;
            }
        }

        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var loose))
        {
            expiry = DateOnly.FromDateTime(loose);
            label = expiry.ToString("ddMMMyyyy", CultureInfo.InvariantCulture).ToUpperInvariant();
            return true;
        }

        return false;
    }

    public static decimal? ParseStrike(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (!decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var v))
            return null;
        // Angel equity option strikes are often ×100 (85000 → 850).
        if (v >= 1000 && v == Math.Truncate(v) && v % 100 == 0 && v / 100 < 100000)
            return v / 100m;
        return v;
    }

    public static string? InferOptionType(string tradingSymbol)
    {
        var s = tradingSymbol.Trim().ToUpperInvariant();
        if (s.EndsWith("CE", StringComparison.Ordinal)) return "CE";
        if (s.EndsWith("PE", StringComparison.Ordinal)) return "PE";
        return null;
    }
}
