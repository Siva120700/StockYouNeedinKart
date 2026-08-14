using Microsoft.Extensions.Logging;
using StockYouNeed.Application.Abstractions;
using StockYouNeed.Domain;

namespace StockYouNeed.Application.IndexOptions;

/// <summary>
/// Snapshot nearest-expiry Nifty OPTIDX OI ladder via Angel FULL quotes.
/// </summary>
public sealed class NiftyOptionChainService
{
    public const int StrikeHalfWindow = 12;
    public const decimal StrikeStep = 50m;
    private const int QuoteBatchSize = 40;

    private readonly IOptionsIntradayRepository _nfo;
    private readonly IAngelMarketDataClient _angel;
    private readonly ILogger<NiftyOptionChainService> _logger;

    public NiftyOptionChainService(
        IOptionsIntradayRepository nfo,
        IAngelMarketDataClient angel,
        ILogger<NiftyOptionChainService> logger)
    {
        _nfo = nfo;
        _angel = angel;
        _logger = logger;
    }

    public async Task<NiftyOptionChainSnapshot> GetSnapshotAsync(
        Guid niftyInstrumentId, decimal spot, CancellationToken ct = default)
    {
        var nfo = await _nfo.GetNfoForUnderlyingAsync(niftyInstrumentId, ct);
        var options = nfo.Where(c => c.Kind == "option" && c.Strike is > 0).ToList();
        if (options.Count == 0 || spot <= 0)
        {
            return new NiftyOptionChainSnapshot
            {
                Spot = spot,
                AsOf = DateTimeOffset.UtcNow,
                Metrics = NiftyOptionChainAnalyzer.Build(spot, "", Array.Empty<NiftyOptionChainAnalyzer.StrikeOi>()),
            };
        }

        var nearestExpiry = options.Min(o => o.Expiry);
        var expiryContracts = options.Where(o => o.Expiry == nearestExpiry).ToList();
        var expiryLabel = expiryContracts[0].ExpiryLabel;

        var atm = Math.Round(spot / StrikeStep, MidpointRounding.AwayFromZero) * StrikeStep;
        var lo = atm - StrikeHalfWindow * StrikeStep;
        var hi = atm + StrikeHalfWindow * StrikeStep;
        var window = expiryContracts
            .Where(c => c.Strike is decimal s && s >= lo && s <= hi)
            .ToList();

        var byStrike = new Dictionary<decimal, (NfoContractRow? Ce, NfoContractRow? Pe)>();
        foreach (var c in window)
        {
            var strike = c.Strike!.Value;
            byStrike.TryGetValue(strike, out var pair);
            if (IsCall(c.OptionType))
                byStrike[strike] = (c, pair.Pe);
            else if (IsPut(c.OptionType))
                byStrike[strike] = (pair.Ce, c);
        }

        var tokens = byStrike.Values
            .SelectMany(p => new[] { p.Ce?.SymbolToken, p.Pe?.SymbolToken })
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.Ordinal)
            .Cast<string>()
            .ToList();

        var quotes = await QuoteBatchedAsync(tokens, ct);
        var quoteByToken = quotes
            .Where(q => !string.IsNullOrWhiteSpace(q.SymbolToken))
            .GroupBy(q => q.SymbolToken, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        foreach (var q in quotes)
        {
            if (string.IsNullOrWhiteSpace(q.SymbolToken)) continue;
            try
            {
                await _nfo.UpdateNfoQuoteAsync(q.SymbolToken, q.Ltp, q.OpenInterest, ct);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed updating NFO quote cache for {Token}", q.SymbolToken);
            }
        }

        var ladder = new List<NiftyOptionChainAnalyzer.StrikeOi>();
        foreach (var (strike, pair) in byStrike.OrderBy(kv => kv.Key))
        {
            long callOi = 0, putOi = 0;
            decimal? callLtp = null, putLtp = null;
            if (pair.Ce?.SymbolToken is string ceTok && quoteByToken.TryGetValue(ceTok, out var ceQ))
            {
                callOi = ceQ.OpenInterest ?? pair.Ce.LastOi ?? 0;
                callLtp = ceQ.Ltp ?? pair.Ce.LastLtp;
            }
            else if (pair.Ce?.LastOi is long cachedCe)
            {
                callOi = cachedCe;
                callLtp = pair.Ce.LastLtp;
            }

            if (pair.Pe?.SymbolToken is string peTok && quoteByToken.TryGetValue(peTok, out var peQ))
            {
                putOi = peQ.OpenInterest ?? pair.Pe.LastOi ?? 0;
                putLtp = peQ.Ltp ?? pair.Pe.LastLtp;
            }
            else if (pair.Pe?.LastOi is long cachedPe)
            {
                putOi = cachedPe;
                putLtp = pair.Pe.LastLtp;
            }

            if (callOi <= 0 && putOi <= 0) continue;
            ladder.Add(new NiftyOptionChainAnalyzer.StrikeOi
            {
                Strike = strike,
                CallOi = callOi,
                PutOi = putOi,
                CallLtp = callLtp,
                PutLtp = putLtp,
            });
        }

        var metrics = NiftyOptionChainAnalyzer.Build(spot, expiryLabel, ladder);
        _logger.LogInformation(
            "Nifty chain {Expiry}: {Strikes} strikes PCR={Pcr} putWall={Put} callWall={Call}",
            expiryLabel, metrics.StrikeCount, metrics.Pcr, metrics.PutWallStrike, metrics.CallWallStrike);

        return new NiftyOptionChainSnapshot
        {
            Spot = spot,
            Expiry = nearestExpiry,
            ExpiryLabel = expiryLabel,
            AsOf = DateTimeOffset.UtcNow,
            Metrics = metrics,
            Ladder = ladder.Select(r => new NiftyOptionChainStrike
            {
                Strike = r.Strike,
                CallOi = r.CallOi,
                PutOi = r.PutOi,
                CallLtp = r.CallLtp,
                PutLtp = r.PutLtp,
            }).ToList(),
        };
    }

    private async Task<List<AngelQuote>> QuoteBatchedAsync(IReadOnlyList<string> tokens, CancellationToken ct)
    {
        var all = new List<AngelQuote>();
        for (var i = 0; i < tokens.Count; i += QuoteBatchSize)
        {
            var batch = tokens.Skip(i).Take(QuoteBatchSize).ToList();
            try
            {
                var quotes = await _angel.GetQuotesAsync(
                    QuoteModes.Full,
                    new Dictionary<string, IReadOnlyList<string>> { ["NFO"] = batch },
                    ct);
                all.AddRange(quotes);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Nifty chain quote batch failed ({From}-{To})", i, i + batch.Count);
            }

            if (i + QuoteBatchSize < tokens.Count)
                await Task.Delay(TimeSpan.FromMilliseconds(350), ct);
        }
        return all;
    }

    private static bool IsCall(string? t) =>
        t is not null && (t.Equals("CE", StringComparison.OrdinalIgnoreCase)
                          || t.Equals("CALL", StringComparison.OrdinalIgnoreCase));

    private static bool IsPut(string? t) =>
        t is not null && (t.Equals("PE", StringComparison.OrdinalIgnoreCase)
                          || t.Equals("PUT", StringComparison.OrdinalIgnoreCase));
}

public sealed class NiftyOptionChainSnapshot
{
    public decimal Spot { get; set; }
    public DateOnly? Expiry { get; set; }
    public string ExpiryLabel { get; set; } = "";
    public DateTimeOffset AsOf { get; set; }
    public NiftyOptionChainAnalyzer.Metrics Metrics { get; set; } = new();
    public IReadOnlyList<NiftyOptionChainStrike> Ladder { get; set; } = Array.Empty<NiftyOptionChainStrike>();

    // Flattened for GraphQL convenience
    public decimal? Pcr => Metrics.Pcr;
    public decimal? CallWallStrike => Metrics.CallWallStrike;
    public long CallWallOi => Metrics.CallWallOi;
    public decimal? PutWallStrike => Metrics.PutWallStrike;
    public long PutWallOi => Metrics.PutWallOi;
    public decimal? MaxPainStrike => Metrics.MaxPainStrike;
    public long TotalCallOi => Metrics.TotalCallOi;
    public long TotalPutOi => Metrics.TotalPutOi;
    public bool Usable => Metrics.Usable;
}

public sealed class NiftyOptionChainStrike
{
    public decimal Strike { get; set; }
    public long CallOi { get; set; }
    public long PutOi { get; set; }
    public decimal? CallLtp { get; set; }
    public decimal? PutLtp { get; set; }
}
