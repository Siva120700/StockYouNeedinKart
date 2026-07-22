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

        _logger.LogInformation("Universe seed completed ({N50} Nifty50 + {Extra} Nifty100 extras).",
            Nifty50.Length, Nifty100Extra.Length);
    }
}
