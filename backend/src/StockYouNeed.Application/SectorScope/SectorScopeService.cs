using StockYouNeed.Application.Abstractions;
using StockYouNeed.Domain;

namespace StockYouNeed.Application.SectorScope;

/// <summary>
/// StepOne-style sector relative strength: median constituent % change vs Nifty,
/// then down-rank equity signals that fight the sector tape.
/// </summary>
public sealed class SectorScopeService
{
    public const string NiftySymbol = "NIFTY";

    private readonly IMarketDataRepository _market;

    public SectorScopeService(IMarketDataRepository market) => _market = market;

    public async Task<SectorScopeSnapshot> GetSnapshotAsync(CancellationToken ct = default)
    {
        var quotes = await _market.GetSectorScopeQuotesAsync(ct);
        return BuildSnapshot(quotes, DateTimeOffset.UtcNow);
    }

    public async Task<IReadOnlyList<T>> RankAsync<T>(IReadOnlyList<T> rows, CancellationToken ct = default)
        where T : class, ISectorRanked
    {
        if (rows.Count == 0)
            return rows;

        var snap = await GetSnapshotAsync(ct);
        var byInstrument = snap.Sectors
            .SelectMany(s => s.Stocks.Select(st => (st.InstrumentId, Sector: s)))
            .GroupBy(x => x.InstrumentId)
            .ToDictionary(g => g.Key, g => g.First().Sector);

        foreach (var row in rows)
        {
            if (!byInstrument.TryGetValue(row.InstrumentId, out var sector))
                continue;

            var lagging = sector.Lagging;
            row.SectorRs = new SectorRelativeStrengthInfo
            {
                Symbol = sector.Symbol,
                Name = sector.DisplayName,
                MedianChangePct = sector.MedianChangePct,
                Rank = sector.Rank,
                Lagging = lagging,
                Downranked = ShouldDownrank(row.Side, lagging),
            };
        }

        return rows
            .OrderBy(r => r.SectorRs?.Downranked == true)
            .ToList();
    }

    public static SectorScopeSnapshot BuildSnapshot(
        IReadOnlyList<SectorScopeQuoteRow> quotes, DateTimeOffset asOf)
    {
        static decimal? ChangePct(SectorScopeQuoteRow q)
        {
            if (q.Ltp is not decimal ltp || q.PrevClose is not decimal prev || prev <= 0)
                return null;
            return Math.Round((ltp - prev) / prev * 100m, 2, MidpointRounding.AwayFromZero);
        }

        var nifty = quotes.FirstOrDefault(q =>
            q.Kind == "sector_index"
            && q.Symbol.Equals(NiftySymbol, StringComparison.OrdinalIgnoreCase));
        var niftyPct = nifty is null ? null : ChangePct(nifty);

        var sectors = quotes
            .GroupBy(q => q.SectorId)
            .Select(g =>
            {
                var index = g.FirstOrDefault(x => x.Kind == "sector_index") ?? g.First();
                var equities = g.Where(x => x.Kind == "equity").ToList();
                var equityChanges = equities
                    .Select(ChangePct)
                    .Where(p => p is not null)
                    .Select(p => p!.Value)
                    .ToList();

                decimal median;
                if (equityChanges.Count > 0)
                    median = Median(equityChanges);
                else
                    median = ChangePct(index) ?? 0m;

                var stocks = equities
                    .Select(e =>
                    {
                        var pct = ChangePct(e);
                        if (pct is null)
                            return null;
                        return new SectorScopeStock
                        {
                            InstrumentId = e.InstrumentId,
                            AppSymbol = e.Symbol,
                            InstrumentName = e.Name,
                            ChangePct = pct.Value,
                            Ltp = e.Ltp,
                        };
                    })
                    .Where(s => s is not null)
                    .Cast<SectorScopeStock>()
                    .OrderByDescending(s => Math.Abs(s.ChangePct))
                    .ToList();

                return new SectorScopeSector
                {
                    InstrumentId = index.SectorId,
                    Symbol = index.SectorSymbol,
                    Name = index.SectorName,
                    DisplayName = DisplayName(index.SectorSymbol, index.SectorName),
                    MedianChangePct = median,
                    Lagging = niftyPct is decimal n && median < n,
                    ConstituentCount = stocks.Count,
                    Stocks = stocks,
                };
            })
            .OrderByDescending(s => s.MedianChangePct)
            .ToList();

        for (var i = 0; i < sectors.Count; i++)
            sectors[i].Rank = i + 1;

        return new SectorScopeSnapshot
        {
            AsOf = asOf,
            NiftyChangePct = niftyPct,
            Sectors = sectors,
        };
    }

    public static decimal Median(IReadOnlyList<decimal> values)
    {
        if (values.Count == 0)
            return 0;
        var sorted = values.OrderBy(v => v).ToArray();
        var mid = sorted.Length / 2;
        if (sorted.Length % 2 == 1)
            return sorted[mid];
        return Math.Round((sorted[mid - 1] + sorted[mid]) / 2m, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>Buy needs a leading sector; sell needs a lagging sector.</summary>
    public static bool ShouldDownrank(string side, bool sectorLagging)
    {
        var sell = side.Equals(SignalSides.Sell, StringComparison.OrdinalIgnoreCase);
        return sell ? !sectorLagging : sectorLagging;
    }

    public static string DisplayName(string symbol, string name)
    {
        return symbol.ToUpperInvariant() switch
        {
            "NIFTY" => "NIFTY 50",
            "NIFTYBANK" => "NIFTY BANK",
            "NIFTYIT" => "NIFTY IT",
            "NIFTYPHARMA" => "NIFTY PHARMA",
            "NIFTYFMCG" => "NIFTY FMCG",
            "NIFTYAUTO" => "NIFTY AUTO",
            "NIFTYMETAL" => "NIFTY METAL",
            "NIFTYENERGY" => "NIFTY ENERGY",
            "NIFTYREALTY" => "NIFTY REALTY",
            "NIFTYFINSERVICE" => "NIFTY FINSERV",
            "NIFTYINFRA" => "NIFTY INFRA",
            "NIFTYMEDIA" => "NIFTY MEDIA",
            "NIFTYPSUBANK" => "PSU BANK",
            "NIFTYPVTBANK" => "PVT BANK",
            "NIFTYHEALTHCARE" => "NIFTY HEALTHCARE",
            "NIFTYCONSUMER" => "NIFTY CONSUMER",
            _ => string.IsNullOrWhiteSpace(name) ? symbol : name.ToUpperInvariant(),
        };
    }
}
