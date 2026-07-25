using Microsoft.Extensions.Logging;
using StockYouNeed.Application.Abstractions;
using StockYouNeed.Domain;

namespace StockYouNeed.Application.Services;

/// <summary>
/// Seeds a starter Nifty 50 set so token sync has something to map.
/// Expand / replace with official index constituents over time.
/// </summary>
public sealed class UniverseSeedService
{
    private readonly IInstrumentRepository _instruments;
    private readonly ILogger<UniverseSeedService> _logger;

    // Representative Nifty 50 symbols (partial seed for bootstrapping)
    private static readonly (string Symbol, string Name)[] Nifty50 =
    [
        ("RELIANCE", "Reliance Industries"),
        ("TCS", "Tata Consultancy Services"),
        ("HDFCBANK", "HDFC Bank"),
        ("INFY", "Infosys"),
        ("ICICIBANK", "ICICI Bank"),
        ("HINDUNILVR", "Hindustan Unilever"),
        ("ITC", "ITC"),
        ("SBIN", "State Bank of India"),
        ("BHARTIARTL", "Bharti Airtel"),
        ("LT", "Larsen & Toubro"),
        ("BAJFINANCE", "Bajaj Finance"),
        ("HCLTECH", "HCL Technologies"),
        ("AXISBANK", "Axis Bank"),
        ("ASIANPAINT", "Asian Paints"),
        ("MARUTI", "Maruti Suzuki"),
        ("SUNPHARMA", "Sun Pharmaceutical"),
        ("TITAN", "Titan Company"),
        ("ULTRACEMCO", "UltraTech Cement"),
        ("NTPC", "NTPC"),
        ("POWERGRID", "Power Grid Corporation"),
        ("TATAMOTORS", "Tata Motors"),
        ("TATASTEEL", "Tata Steel"),
        ("ADANIENT", "Adani Enterprises"),
        ("ADANIPORTS", "Adani Ports"),
        ("WIPRO", "Wipro"),
        ("ONGC", "Oil & Natural Gas Corporation"),
        ("COALINDIA", "Coal India"),
        ("JSWSTEEL", "JSW Steel"),
        ("BAJAJFINSV", "Bajaj Finserv"),
        ("NESTLEIND", "Nestle India"),
        ("TECHM", "Tech Mahindra"),
        ("M&M", "Mahindra & Mahindra"),
        ("CIPLA", "Cipla"),
        ("GRASIM", "Grasim Industries"),
        ("DRREDDY", "Dr Reddy's Laboratories"),
        ("HINDALCO", "Hindalco Industries"),
        ("INDUSINDBK", "IndusInd Bank"),
        ("DIVISLAB", "Divi's Laboratories"),
        ("APOLLOHOSP", "Apollo Hospitals"),
        ("EICHERMOT", "Eicher Motors"),
        ("SBILIFE", "SBI Life Insurance"),
        ("HDFCLIFE", "HDFC Life Insurance"),
        ("BRITANNIA", "Britannia Industries"),
        ("HEROMOTOCO", "Hero MotoCorp"),
        ("BPCL", "Bharat Petroleum"),
        ("TATACONSUM", "Tata Consumer Products"),
        ("LTIM", "LTIMindtree"),
        ("KOTAKBANK", "Kotak Mahindra Bank"),
        ("BEL", "Bharat Electronics"),
        ("TRENT", "Trent")
    ];

    // Extra names often in Nifty 100 beyond Nifty 50 (sample)
    private static readonly (string Symbol, string Name)[] Nifty100Extra =
    [
        ("DMART", "Avenue Supermarts"),
        ("PIDILITIND", "Pidilite Industries"),
        ("GODREJCP", "Godrej Consumer Products"),
        ("HAVELLS", "Havells India"),
        ("SIEMENS", "Siemens"),
        ("DLF", "DLF"),
        ("INDIGO", "InterGlobe Aviation"),
        ("VEDL", "Vedanta"),
        ("AMBUJACEM", "Ambuja Cements"),
        ("BANKBARODA", "Bank of Baroda")
    ];

    public UniverseSeedService(IInstrumentRepository instruments, ILogger<UniverseSeedService> logger)
    {
        _instruments = instruments;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        foreach (var (symbol, name) in Nifty50)
        {
            await _instruments.SeedInstrumentIfMissingAsync(symbol, name, ct);
            await _instruments.EnsureUniverseMembershipAsync(UniverseCodes.Nifty50, symbol, ct);
            await _instruments.EnsureUniverseMembershipAsync(UniverseCodes.Nifty100, symbol, ct);
        }

        foreach (var (symbol, name) in Nifty100Extra)
        {
            await _instruments.SeedInstrumentIfMissingAsync(symbol, name, ct);
            await _instruments.EnsureUniverseMembershipAsync(UniverseCodes.Nifty100, symbol, ct);
        }

        await SeedSectorsAndLinksAsync(ct);

        _logger.LogInformation("Universe seed completed ({N50} Nifty50 + {Extra} Nifty100 extras).",
            Nifty50.Length, Nifty100Extra.Length);
    }

    private static readonly (string Symbol, string Name, string AngelNameContains)[] Sectors =
    [
        ("NIFTYBANK", "Nifty Bank", "Nifty Bank"),
        ("NIFTYIT", "Nifty IT", "Nifty IT"),
        ("NIFTYPHARMA", "Nifty Pharma", "Nifty Pharma"),
        ("NIFTYFMCG", "Nifty FMCG", "Nifty FMCG"),
        ("NIFTYAUTO", "Nifty Auto", "Nifty Auto"),
        ("NIFTYMETAL", "Nifty Metal", "Nifty Metal"),
        ("NIFTYENERGY", "Nifty Energy", "Nifty Energy"),
        ("NIFTYREALTY", "Nifty Realty", "Nifty Realty"),
        ("NIFTYFINSERVICE", "Nifty Financial Services", "Nifty Fin Service"),
        ("NIFTYINFRA", "Nifty Infrastructure", "Nifty Infra"),
        ("NIFTYMEDIA", "Nifty Media", "Nifty Media"),
        ("NIFTYPSUBANK", "Nifty PSU Bank", "Nifty PSU Bank"),
        ("NIFTYPVTBANK", "Nifty Private Bank", "Nifty Private Bank"),
        ("NIFTYHEALTHCARE", "Nifty Healthcare", "Nifty Healthcare"),
        ("NIFTYCONSUMER", "Nifty Consumer Durables", "Nifty Consumer Durables"),
    ];

    // Equity → sector index symbol
    private static readonly Dictionary<string, string> EquitySector = new(StringComparer.OrdinalIgnoreCase)
    {
        ["HDFCBANK"] = "NIFTYBANK", ["ICICIBANK"] = "NIFTYBANK", ["SBIN"] = "NIFTYBANK",
        ["AXISBANK"] = "NIFTYBANK", ["KOTAKBANK"] = "NIFTYBANK", ["INDUSINDBK"] = "NIFTYBANK",
        ["BANKBARODA"] = "NIFTYPSUBANK",
        ["TCS"] = "NIFTYIT", ["INFY"] = "NIFTYIT", ["HCLTECH"] = "NIFTYIT", ["WIPRO"] = "NIFTYIT",
        ["TECHM"] = "NIFTYIT", ["LTIM"] = "NIFTYIT",
        ["SUNPHARMA"] = "NIFTYPHARMA", ["CIPLA"] = "NIFTYPHARMA", ["DRREDDY"] = "NIFTYPHARMA",
        ["DIVISLAB"] = "NIFTYPHARMA", ["APOLLOHOSP"] = "NIFTYHEALTHCARE",
        ["HINDUNILVR"] = "NIFTYFMCG", ["ITC"] = "NIFTYFMCG", ["NESTLEIND"] = "NIFTYFMCG",
        ["BRITANNIA"] = "NIFTYFMCG", ["TATACONSUM"] = "NIFTYFMCG", ["GODREJCP"] = "NIFTYFMCG",
        ["MARUTI"] = "NIFTYAUTO", ["TATAMOTORS"] = "NIFTYAUTO", ["M&M"] = "NIFTYAUTO",
        ["EICHERMOT"] = "NIFTYAUTO", ["HEROMOTOCO"] = "NIFTYAUTO",
        ["TATASTEEL"] = "NIFTYMETAL", ["JSWSTEEL"] = "NIFTYMETAL", ["HINDALCO"] = "NIFTYMETAL", ["VEDL"] = "NIFTYMETAL",
        ["RELIANCE"] = "NIFTYENERGY", ["ONGC"] = "NIFTYENERGY", ["NTPC"] = "NIFTYENERGY",
        ["POWERGRID"] = "NIFTYENERGY", ["BPCL"] = "NIFTYENERGY", ["COALINDIA"] = "NIFTYENERGY",
        ["DLF"] = "NIFTYREALTY",
        ["BAJFINANCE"] = "NIFTYFINSERVICE", ["BAJAJFINSV"] = "NIFTYFINSERVICE",
        ["SBILIFE"] = "NIFTYFINSERVICE", ["HDFCLIFE"] = "NIFTYFINSERVICE",
        ["LT"] = "NIFTYINFRA", ["ADANIPORTS"] = "NIFTYINFRA", ["ADANIENT"] = "NIFTYINFRA", ["SIEMENS"] = "NIFTYINFRA",
        ["ULTRACEMCO"] = "NIFTYINFRA", ["AMBUJACEM"] = "NIFTYINFRA", ["GRASIM"] = "NIFTYINFRA",
        ["ASIANPAINT"] = "NIFTYCONSUMER", ["TITAN"] = "NIFTYCONSUMER", ["HAVELLS"] = "NIFTYCONSUMER",
        ["PIDILITIND"] = "NIFTYCONSUMER", ["DMART"] = "NIFTYCONSUMER", ["TRENT"] = "NIFTYCONSUMER",
        ["BHARTIARTL"] = "NIFTYMEDIA", ["INDIGO"] = "NIFTYINFRA", ["BEL"] = "NIFTYINFRA",
    };

    private async Task SeedSectorsAndLinksAsync(CancellationToken ct)
    {
        foreach (var (symbol, name, _) in Sectors)
            await _instruments.SeedSectorIndexIfMissingAsync(symbol, name, ct);

        var linked = 0;
        foreach (var (equity, sector) in EquitySector)
        {
            await _instruments.LinkEquityToSectorAsync(equity, sector, ct);
            linked++;
        }

        _logger.LogInformation("Seeded {SectorCount} sector indexes and linked {Linked} equities.",
            Sectors.Length, linked);
    }

    /// <summary>Angel scrip-master name fragment used to match NSE AMXIDX tokens.</summary>
    public static IReadOnlyDictionary<string, string> SectorAngelNameHints { get; } =
        Sectors.ToDictionary(s => s.Symbol, s => s.AngelNameContains, StringComparer.OrdinalIgnoreCase);
}
