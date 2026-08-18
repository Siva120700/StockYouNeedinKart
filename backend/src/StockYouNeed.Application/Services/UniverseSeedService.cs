using Microsoft.Extensions.Logging;
using StockYouNeed.Application.Abstractions;
using StockYouNeed.Domain;

namespace StockYouNeed.Application.Services;

/// <summary>
/// Seeds Nifty 50 + Nifty Next 50 (full Nifty 100) so token sync has the equity universe.
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
        ("JIOFIN", "Jio Financial Services"),
        ("HCLTECH", "HCL Technologies"),
        ("AXISBANK", "Axis Bank"),
        ("ASIANPAINT", "Asian Paints"),
        ("MARUTI", "Maruti Suzuki"),
        ("SUNPHARMA", "Sun Pharmaceutical"),
        ("TITAN", "Titan Company"),
        ("ULTRACEMCO", "UltraTech Cement"),
        ("NTPC", "NTPC"),
        ("POWERGRID", "Power Grid Corporation"),
        ("TMPV", "Tata Motors Passenger Vehicles"), // demerger: was TATAMOTORS
        ("TMCV", "Tata Motors Commercial Vehicles"),
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
        ("LTM", "LTM Limited"), // formerly LTIM / LTIMindtree
        ("KOTAKBANK", "Kotak Mahindra Bank"),
        ("BEL", "Bharat Electronics"),
        ("TRENT", "Trent")
    ];

    // Nifty Next 50 (Nifty 100 excluding Nifty 50) — as of Apr 2026.
    // Symbols already in Nifty50[] are omitted here (they still get nifty_100 membership above).
    private static readonly (string Symbol, string Name)[] Nifty100Extra =
    [
        ("ABB", "ABB India"),
        ("ADANIENSOL", "Adani Energy Solutions"),
        ("ADANIGREEN", "Adani Green Energy"),
        ("ADANIPOWER", "Adani Power"),
        ("AMBUJACEM", "Ambuja Cements"),
        ("BAJAJHLDNG", "Bajaj Holdings & Investment"),
        ("BANKBARODA", "Bank of Baroda"),
        ("BOSCHLTD", "Bosch"),
        ("CANBK", "Canara Bank"),
        ("CGPOWER", "CG Power and Industrial Solutions"),
        ("CHOLAFIN", "Cholamandalam Investment and Finance"),
        ("CUMMINSIND", "Cummins India"),
        ("DLF", "DLF"),
        ("DMART", "Avenue Supermarts"),
        ("ENRIN", "Siemens Energy India"),
        ("GAIL", "GAIL India"),
        ("GODREJCP", "Godrej Consumer Products"),
        ("HAL", "Hindustan Aeronautics"),
        ("HDFCAMC", "HDFC Asset Management"),
        ("HINDZINC", "Hindustan Zinc"),
        ("HYUNDAI", "Hyundai Motor India"),
        ("INDHOTEL", "Indian Hotels Company"),
        ("IOC", "Indian Oil Corporation"),
        ("IRFC", "Indian Railway Finance Corporation"),
        ("JINDALSTEL", "Jindal Steel"),
        ("LODHA", "Macrotech Developers"),
        ("MAZDOCK", "Mazagon Dock Shipbuilders"),
        ("MOTHERSON", "Samvardhana Motherson International"),
        ("MUTHOOTFIN", "Muthoot Finance"),
        ("PFC", "Power Finance Corporation"),
        ("PIDILITIND", "Pidilite Industries"),
        ("PNB", "Punjab National Bank"),
        ("RECLTD", "REC"),
        ("SHREECEM", "Shree Cement"),
        ("SIEMENS", "Siemens"),
        ("SOLARINDS", "Solar Industries"),
        ("TATACAP", "Tata Capital"),
        ("TATAPOWER", "Tata Power"),
        ("TORNTPHARM", "Torrent Pharmaceuticals"),
        ("TVSMOTOR", "TVS Motor Company"),
        ("UNIONBANK", "Union Bank of India"),
        ("UNITDSPR", "United Spirits"),
        ("VBL", "Varun Beverages"),
        ("VEDL", "Vedanta"),
        ("ZYDUSLIFE", "Zydus Lifesciences"),
        // Still widely traded large-caps often held with Nifty 100 screens
        ("HAVELLS", "Havells India"),
        ("INDIGO", "InterGlobe Aviation"),
    ];

    public UniverseSeedService(IInstrumentRepository instruments, ILogger<UniverseSeedService> logger)
    {
        _instruments = instruments;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        // Old NSE symbols — retire on every seed so we don't need a one-off SQL migration.
        await _instruments.RetireEquitySymbolsAsync(["LTIM", "TATAMOTORS"], ct);
        // Angel F&O scrip master includes exchange test underlyings — never scan these.
        await _instruments.RetireEquitySymbolsLikeAsync("%NSETEST%", ct);

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

        var fnoCount = await SeedFnoUnderlyingsAsync(ct);

        _logger.LogInformation(
            "Universe seed completed ({N50} Nifty50 + {Extra} Nifty100 extras + {Fno} F&O).",
            Nifty50.Length, Nifty100Extra.Length, fnoCount);
    }

    /// <summary>All NSE F&amp;O equity underlyings (embedded — no file/Angel required).</summary>
    private async Task<int> SeedFnoUnderlyingsAsync(CancellationToken ct)
    {
        var symbols = FnoUnderlyingSymbols.All;
        if (symbols.Length == 0)
        {
            _logger.LogWarning("FnoUnderlyingSymbols.All is empty — F&O universe skipped.");
            return 0;
        }

        var seeded = 0;
        try
        {
            foreach (var symbol in symbols)
            {
                ct.ThrowIfCancellationRequested();
                await _instruments.SeedInstrumentIfMissingAsync(symbol, symbol, ct);
                await _instruments.EnsureUniverseMembershipAsync(UniverseCodes.NiftyFno, symbol, ct);
                if (EquitySector.TryGetValue(symbol, out var sector))
                    await _instruments.LinkEquityToSectorAsync(symbol, sector, ct);
                seeded++;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "F&O seed failed at {Seeded}/{Total}. Ensure migration 031_nifty_fno_universe.sql ran (restart API).",
                seeded, symbols.Length);
            throw;
        }

        return seeded;
    }

    private static readonly (string Symbol, string Name, string AngelNameContains)[] Sectors =
    [
        // Benchmark index (also used by Nifty ORB / Index Options).
        ("NIFTY", "Nifty 50", "Nifty 50"),
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
        ["BANKBARODA"] = "NIFTYPSUBANK", ["CANBK"] = "NIFTYPSUBANK", ["PNB"] = "NIFTYPSUBANK",
        ["UNIONBANK"] = "NIFTYPSUBANK",
        ["TCS"] = "NIFTYIT", ["INFY"] = "NIFTYIT", ["HCLTECH"] = "NIFTYIT", ["WIPRO"] = "NIFTYIT",
        ["TECHM"] = "NIFTYIT", ["LTM"] = "NIFTYIT",
        ["SUNPHARMA"] = "NIFTYPHARMA", ["CIPLA"] = "NIFTYPHARMA", ["DRREDDY"] = "NIFTYPHARMA",
        ["DIVISLAB"] = "NIFTYPHARMA", ["TORNTPHARM"] = "NIFTYPHARMA", ["ZYDUSLIFE"] = "NIFTYPHARMA",
        ["APOLLOHOSP"] = "NIFTYHEALTHCARE",
        ["HINDUNILVR"] = "NIFTYFMCG", ["ITC"] = "NIFTYFMCG", ["NESTLEIND"] = "NIFTYFMCG",
        ["BRITANNIA"] = "NIFTYFMCG", ["TATACONSUM"] = "NIFTYFMCG", ["GODREJCP"] = "NIFTYFMCG",
        ["UNITDSPR"] = "NIFTYFMCG", ["VBL"] = "NIFTYFMCG",
        ["MARUTI"] = "NIFTYAUTO", ["TMPV"] = "NIFTYAUTO", ["TMCV"] = "NIFTYAUTO", ["M&M"] = "NIFTYAUTO",
        ["EICHERMOT"] = "NIFTYAUTO", ["HEROMOTOCO"] = "NIFTYAUTO", ["TVSMOTOR"] = "NIFTYAUTO",
        ["BOSCHLTD"] = "NIFTYAUTO", ["MOTHERSON"] = "NIFTYAUTO", ["HYUNDAI"] = "NIFTYAUTO",
        ["TATASTEEL"] = "NIFTYMETAL", ["JSWSTEEL"] = "NIFTYMETAL", ["HINDALCO"] = "NIFTYMETAL",
        ["VEDL"] = "NIFTYMETAL", ["JINDALSTEL"] = "NIFTYMETAL", ["HINDZINC"] = "NIFTYMETAL",
        ["RELIANCE"] = "NIFTYENERGY", ["ONGC"] = "NIFTYENERGY", ["NTPC"] = "NIFTYENERGY",
        ["POWERGRID"] = "NIFTYENERGY", ["BPCL"] = "NIFTYENERGY", ["COALINDIA"] = "NIFTYENERGY",
        ["IOC"] = "NIFTYENERGY", ["GAIL"] = "NIFTYENERGY", ["TATAPOWER"] = "NIFTYENERGY",
        ["ADANIGREEN"] = "NIFTYENERGY", ["ADANIPOWER"] = "NIFTYENERGY", ["ADANIENSOL"] = "NIFTYENERGY",
        ["DLF"] = "NIFTYREALTY", ["LODHA"] = "NIFTYREALTY",
        ["BAJFINANCE"] = "NIFTYFINSERVICE", ["BAJAJFINSV"] = "NIFTYFINSERVICE",
        ["JIOFIN"] = "NIFTYFINSERVICE",
        ["SBILIFE"] = "NIFTYFINSERVICE", ["HDFCLIFE"] = "NIFTYFINSERVICE",
        ["CHOLAFIN"] = "NIFTYFINSERVICE", ["BAJAJHLDNG"] = "NIFTYFINSERVICE",
        ["HDFCAMC"] = "NIFTYFINSERVICE", ["MUTHOOTFIN"] = "NIFTYFINSERVICE",
        ["PFC"] = "NIFTYFINSERVICE", ["RECLTD"] = "NIFTYFINSERVICE", ["IRFC"] = "NIFTYFINSERVICE",
        ["TATACAP"] = "NIFTYFINSERVICE",
        ["LT"] = "NIFTYINFRA", ["ADANIPORTS"] = "NIFTYINFRA", ["ADANIENT"] = "NIFTYINFRA",
        ["SIEMENS"] = "NIFTYINFRA", ["ABB"] = "NIFTYINFRA", ["CGPOWER"] = "NIFTYINFRA",
        ["CUMMINSIND"] = "NIFTYINFRA", ["ENRIN"] = "NIFTYINFRA", ["HAL"] = "NIFTYINFRA",
        ["MAZDOCK"] = "NIFTYINFRA",
        ["ULTRACEMCO"] = "NIFTYINFRA", ["AMBUJACEM"] = "NIFTYINFRA", ["GRASIM"] = "NIFTYINFRA",
        ["SHREECEM"] = "NIFTYINFRA",
        ["ASIANPAINT"] = "NIFTYCONSUMER", ["TITAN"] = "NIFTYCONSUMER", ["HAVELLS"] = "NIFTYCONSUMER",
        ["PIDILITIND"] = "NIFTYCONSUMER", ["DMART"] = "NIFTYCONSUMER", ["TRENT"] = "NIFTYCONSUMER",
        ["INDHOTEL"] = "NIFTYCONSUMER", ["SOLARINDS"] = "NIFTYCONSUMER",
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
