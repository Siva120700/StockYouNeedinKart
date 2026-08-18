using StockYouNeed.Application.SectorScope;
using StockYouNeed.Domain;
using Xunit;

namespace StockYouNeed.Application.Tests;

public class SectorScopeServiceTests
{
    [Fact]
    public void Median_OddAndEven()
    {
        Assert.Equal(2m, SectorScopeService.Median(new[] { 3m, 1m, 2m }));
        Assert.Equal(1.5m, SectorScopeService.Median(new[] { 1m, 2m }));
    }

    [Fact]
    public void ShouldDownrank_BuyInLagging_SellInLeading()
    {
        Assert.True(SectorScopeService.ShouldDownrank(SignalSides.Buy, sectorLagging: true));
        Assert.False(SectorScopeService.ShouldDownrank(SignalSides.Buy, sectorLagging: false));
        Assert.True(SectorScopeService.ShouldDownrank(SignalSides.Sell, sectorLagging: false));
        Assert.False(SectorScopeService.ShouldDownrank(SignalSides.Sell, sectorLagging: true));
    }

    [Fact]
    public void BuildSnapshot_RanksByMedian_MarksLaggingVsNifty()
    {
        var niftyId = Guid.NewGuid();
        var itId = Guid.NewGuid();
        var bankId = Guid.NewGuid();
        var infy = Guid.NewGuid();
        var hdfc = Guid.NewGuid();

        var quotes = new List<SectorScopeQuoteRow>
        {
            Index("NIFTY", "Nifty 50", niftyId, ltp: 24700, prev: 25000), // -1.20%
            Index("NIFTYIT", "Nifty IT", itId, ltp: 35000, prev: 36000),
            Index("NIFTYBANK", "Nifty Bank", bankId, ltp: 56000, prev: 55000),
            Equity("INFY", infy, itId, "NIFTYIT", "Nifty IT", ltp: 1400, prev: 1500), // -6.67
            Equity("TCS", Guid.NewGuid(), itId, "NIFTYIT", "Nifty IT", ltp: 3000, prev: 3100), // -3.23
            Equity("HDFCBANK", hdfc, bankId, "NIFTYBANK", "Nifty Bank", ltp: 1700, prev: 1650), // +3.03
            Equity("ICICIBANK", Guid.NewGuid(), bankId, "NIFTYBANK", "Nifty Bank", ltp: 1400, prev: 1380), // +1.45
        };

        var snap = SectorScopeService.BuildSnapshot(quotes, DateTimeOffset.UtcNow);

        Assert.Equal(3, snap.Sectors.Count);
        Assert.Equal("NIFTY BANK", snap.Sectors[0].DisplayName);
        Assert.False(snap.Sectors[0].Lagging);
        Assert.Equal(1, snap.Sectors[0].Rank);

        var it = snap.Sectors.First(s => s.Symbol == "NIFTYIT");
        Assert.True(it.Lagging);
        Assert.True(it.Rank > 1);
        Assert.Contains(it.Stocks, s => s.AppSymbol == "INFY");
    }

    [Fact]
    public void BuildSnapshot_DeduplicatesStocksByInstrumentId()
    {
        var itId = Guid.NewGuid();
        var infy = Guid.NewGuid();

        var quotes = new List<SectorScopeQuoteRow>
        {
            Index("NIFTYIT", "Nifty IT", itId, ltp: 35000, prev: 35000),
            Equity("INFY", infy, itId, "NIFTYIT", "Nifty IT", ltp: 1400, prev: 1500),
            Equity("INFY", infy, itId, "NIFTYIT", "Nifty IT", ltp: 1400, prev: 1500),
            Equity("INFY", infy, itId, "NIFTYIT", "Nifty IT", ltp: 1400, prev: 1500),
            Equity("TCS", Guid.NewGuid(), itId, "NIFTYIT", "Nifty IT", ltp: 3000, prev: 3100),
        };

        var snap = SectorScopeService.BuildSnapshot(quotes, DateTimeOffset.UtcNow);
        var it = Assert.Single(snap.Sectors, s => s.Symbol == "NIFTYIT");

        Assert.Equal(2, it.Stocks.Count);
        Assert.Equal(2, it.ConstituentCount);
        Assert.Single(it.Stocks, s => s.AppSymbol == "INFY");
    }

    private static SectorScopeQuoteRow Index(string symbol, string name, Guid id, decimal ltp, decimal prev)
        => new()
        {
            InstrumentId = id,
            Symbol = symbol,
            Name = name,
            Kind = "sector_index",
            SectorId = id,
            SectorSymbol = symbol,
            SectorName = name,
            Ltp = ltp,
            PrevClose = prev,
        };

    private static SectorScopeQuoteRow Equity(
        string symbol, Guid id, Guid sectorId, string sectorSymbol, string sectorName,
        decimal ltp, decimal prev)
        => new()
        {
            InstrumentId = id,
            Symbol = symbol,
            Name = symbol,
            Kind = "equity",
            SectorId = sectorId,
            SectorSymbol = sectorSymbol,
            SectorName = sectorName,
            Ltp = ltp,
            PrevClose = prev,
        };
}
