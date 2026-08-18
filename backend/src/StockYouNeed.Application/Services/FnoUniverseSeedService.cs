using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StockYouNeed.Application.Abstractions;
using StockYouNeed.Application.Options;
using StockYouNeed.Domain;

namespace StockYouNeed.Application.Services;

/// <summary>
/// Seeds all NSE F&O equity underlyings (from Angel NFO FUTSTK) into universe <c>nifty_fno</c>.
/// </summary>
public sealed class FnoUniverseSeedService
{
    private static readonly HashSet<string> ExcludedUnderlyings = new(StringComparer.OrdinalIgnoreCase)
    {
        "NIFTY", "BANKNIFTY", "FINNIFTY", "MIDCPNIFTY", "NIFTYNXT50", "NIFTYNXT",
    };

    private readonly IAngelMarketDataClient _angel;
    private readonly IInstrumentRepository _instruments;
    private readonly AngelOptions _options;
    private readonly ILogger<FnoUniverseSeedService> _logger;

    public FnoUniverseSeedService(
        IAngelMarketDataClient angel,
        IInstrumentRepository instruments,
        IOptions<AngelOptions> options,
        ILogger<FnoUniverseSeedService> logger)
    {
        _angel = angel;
        _instruments = instruments;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<int> SeedFromAngelAsync(CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Angel disabled — skipping F&O universe seed.");
            return 0;
        }

        _logger.LogInformation("Downloading Angel scrip master for F&O universe (often 1–3 min)…");
        var scrips = await _angel.DownloadScripMasterAsync(ct);
        _logger.LogInformation("Scrip master loaded ({Count} rows). Building F&O list…", scrips.Count);
        var eqNames = scrips
            .Where(s => s.ExchSeg.Equals("NSE", StringComparison.OrdinalIgnoreCase)
                        && s.Symbol.EndsWith("-EQ", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(
                s => s.Symbol[..^3].Trim().ToUpperInvariant(),
                s => s.Name.Trim(),
                StringComparer.OrdinalIgnoreCase);

        var underlyings = scrips
            .Where(s => s.ExchSeg.Equals("NFO", StringComparison.OrdinalIgnoreCase))
            .Where(s => s.InstrumentType.Equals("FUTSTK", StringComparison.OrdinalIgnoreCase))
            .Select(s => s.Name.Trim().ToUpperInvariant())
            .Where(n => !string.IsNullOrWhiteSpace(n) && !ExcludedUnderlyings.Contains(n))
            .Where(n => !n.Contains("NSETEST", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();

        var seeded = 0;
        foreach (var symbol in underlyings)
        {
            ct.ThrowIfCancellationRequested();
            var name = eqNames.TryGetValue(symbol, out var n) && !string.IsNullOrWhiteSpace(n)
                ? n
                : symbol;
            await _instruments.SeedInstrumentIfMissingAsync(symbol, name, ct);
            await _instruments.EnsureUniverseMembershipAsync(UniverseCodes.NiftyFno, symbol, ct);
            seeded++;
            if (seeded % 50 == 0)
                _logger.LogInformation("F&O seed progress: {Seeded}/{Total}…", seeded, underlyings.Count);
        }

        _logger.LogInformation("F&O universe seed: {Count} NSE F&O underlyings (nifty_fno).", seeded);
        return seeded;
    }
}
