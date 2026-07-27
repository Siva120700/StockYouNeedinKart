using StockYouNeed.Domain;

namespace StockYouNeed.Application.Abstractions;

public interface IDbConnectionFactory
{
    System.Data.IDbConnection CreateConnection();
}

public interface IAngelMarketDataClient
{
    Task EnsureSessionAsync(CancellationToken ct = default);

    Task<IReadOnlyList<AngelQuote>> GetQuotesAsync(
        string mode,
        IReadOnlyDictionary<string, IReadOnlyList<string>> exchangeTokens,
        CancellationToken ct = default);

    Task<IReadOnlyList<AngelCandle>> GetDailyCandlesAsync(
        string exchange,
        string symbolToken,
        DateTime fromIst,
        DateTime toIst,
        CancellationToken ct = default);

    Task<IReadOnlyList<AngelCandle>> GetHourlyCandlesAsync(
        string exchange,
        string symbolToken,
        DateTime fromIst,
        DateTime toIst,
        CancellationToken ct = default);

    Task<IReadOnlyList<AngelScrip>> DownloadScripMasterAsync(CancellationToken ct = default);
}

public sealed class AngelQuote
{
    public string Exchange { get; set; } = "";
    public string TradingSymbol { get; set; } = "";
    public string SymbolToken { get; set; } = "";
    public decimal? Ltp { get; set; }
    public decimal? Open { get; set; }
    public decimal? High { get; set; }
    public decimal? Low { get; set; }
    public decimal? Close { get; set; }
    public long? TradeVolume { get; set; }
    public string RawJson { get; set; } = "{}";
}

public sealed class AngelCandle
{
    public DateOnly TradeDate { get; set; }
    /// <summary>Bar open time (IST wall clock as DateTimeOffset) for intraday intervals.</summary>
    public DateTimeOffset? BarTime { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public long Volume { get; set; }
}

public sealed class AngelScrip
{
    public string Token { get; set; } = "";
    public string Symbol { get; set; } = "";
    public string Name { get; set; } = "";
    public string ExchSeg { get; set; } = "";
    public string InstrumentType { get; set; } = "";
    public string LotSize { get; set; } = "1";
    public string TickSize { get; set; } = "0.05";
    public string Expiry { get; set; } = "";
}

public interface IInstrumentRepository
{
    Task<IReadOnlyList<Instrument>> GetUniverseEquitiesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<AngelTokenRow>> GetActiveTokensForUniversesAsync(CancellationToken ct = default);
    Task UpsertAngelTokenAsync(AngelTokenRow row, CancellationToken ct = default);
    Task EnsureDemoUserAsync(Guid userId, string email, string displayName, CancellationToken ct = default);
    Task SeedInstrumentIfMissingAsync(string symbol, string name, CancellationToken ct = default);
    Task EnsureUniverseMembershipAsync(string universe, string symbol, CancellationToken ct = default);
    /// <summary>Deactivate old symbols (e.g. LTIM→LTM) and end their universe memberships.</summary>
    Task RetireEquitySymbolsAsync(IReadOnlyList<string> symbols, CancellationToken ct = default);
    Task SeedSectorIndexIfMissingAsync(string symbol, string name, CancellationToken ct = default);
    Task LinkEquityToSectorAsync(string equitySymbol, string sectorSymbol, CancellationToken ct = default);
    Task<IReadOnlyList<Instrument>> GetSectorIndexesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Guid>> GetSectorInstrumentIdsAsync(CancellationToken ct = default);
    Task<Guid?> GetSectorIdForInstrumentAsync(Guid instrumentId, CancellationToken ct = default);
    Task<IReadOnlyList<AngelTokenRow>> GetActiveTokensForSectorsAsync(CancellationToken ct = default);
}

public interface IMarketDataRepository
{
    Task UpsertLtpAsync(Guid instrumentId, string exchange, string tradingSymbol, string symbolToken, decimal ltp, string rawJson, CancellationToken ct = default);
    Task UpsertOhlcAsync(Guid instrumentId, string exchange, string tradingSymbol, string symbolToken, decimal ltp, decimal open, decimal high, decimal low, decimal close, long tradeVolume, Guid? analysisRunId, string rawJson, CancellationToken ct = default);
    Task UpsertMarketBarAsync(Guid instrumentId, DateOnly tradeDate, decimal open, decimal high, decimal low, decimal close, long volume, CancellationToken ct = default);
    Task UpsertIntradayBarAsync(Guid instrumentId, string interval, DateTimeOffset barTime, decimal open, decimal high, decimal low, decimal close, long volume, CancellationToken ct = default);
    Task TrimMarketBarsOlderThanAsync(int keepTradingDaysApprox, CancellationToken ct = default);
    Task<IReadOnlyList<MarketLtpRow>> GetAllLtpAsync(CancellationToken ct = default);
    Task<IReadOnlyList<MarketBarRow>> GetBarsAsync(Guid? instrumentId, int limitDays, CancellationToken ct = default);
    Task<IReadOnlyList<MarketBarRow>> GetBarsForInstrumentAsync(Guid instrumentId, int limitDays, CancellationToken ct = default);
    Task<IReadOnlyList<MarketIntradayBarRow>> GetIntradayBarsForInstrumentAsync(Guid instrumentId, string interval, int limitBars, CancellationToken ct = default);
    Task<int> CountIntradayBarsAsync(Guid instrumentId, string interval, CancellationToken ct = default);
    Task<DateTimeOffset?> GetLatestIntradayBarTimeAsync(Guid instrumentId, string interval, CancellationToken ct = default);
    Task LogQuoteFetchBatchAsync(string mode, int requested, int fetched, int unfetched, bool statusOk, string? message, string? errorCode, string exchangeTokensJson, string unfetchedJson, Guid? analysisRunId, int? durationMs, CancellationToken ct = default);
}

public interface IPortfolioRepository
{
    Task<UserRow?> GetUserAsync(Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<AnalysisSignalRow>> GetSignalsAsync(Guid userId, Guid? runId, CancellationToken ct = default);
    Task<IReadOnlyList<OpenPositionRow>> GetOpenPositionsAsync(Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<WatchlistItemRow>> GetWatchlistAsync(Guid userId, CancellationToken ct = default);
    Task AddWatchlistAsync(Guid userId, Guid instrumentId, CancellationToken ct = default);
    Task RemoveWatchlistAsync(Guid userId, Guid instrumentId, CancellationToken ct = default);
    Task<Guid> CreateAnalysisRunAsync(Guid userId, string triggeredBy, bool nifty50, bool nifty100, bool watchlist, DateOnly asOfDate, CancellationToken ct = default);
    Task CompleteAnalysisRunAsync(Guid runId, string status, string? error, object stats, CancellationToken ct = default);
    Task InsertSignalAsync(AnalysisSignalRow signal, CancellationToken ct = default);
    Task<AnalysisSignalRow?> GetSignalAsync(Guid signalId, Guid userId, CancellationToken ct = default);
    Task<Guid> CreateLiquidityAnalysisRunAsync(Guid userId, string triggeredBy, bool nifty50, bool nifty100, bool watchlist, DateOnly asOfDate, CancellationToken ct = default);
    Task CompleteLiquidityAnalysisRunAsync(Guid runId, string status, string? error, object stats, CancellationToken ct = default);
    Task InsertLiquiditySignalAsync(LiquiditySignalRow signal, CancellationToken ct = default);
    Task<IReadOnlyList<LiquiditySignalRow>> GetLiquiditySignalsAsync(Guid userId, Guid? runId, CancellationToken ct = default);
    Task<LiquiditySignalRow?> GetLiquiditySignalAsync(Guid signalId, Guid userId, CancellationToken ct = default);
    Task<Guid> OpenPositionFromLiquiditySignalAsync(Guid userId, Guid signalId, int quantityLots, CancellationToken ct = default);
    Task<Guid> OpenPositionFromSignalAsync(Guid userId, Guid signalId, int quantityLots, CancellationToken ct = default);
    Task UpdateStopLossAsync(Guid userId, Guid positionId, decimal newStop, CancellationToken ct = default);
    Task ClosePositionAsync(Guid userId, Guid positionId, decimal exitPrice, string closeReason, CancellationToken ct = default);
    Task RefreshPositionMarksFromLtpAsync(Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<Guid>> GetWatchlistInstrumentIdsAsync(Guid userId, CancellationToken ct = default);
}

public interface IBacktestRepository
{
    Task<IReadOnlyList<BacktestNoteRow>> GetNotesAsync(
        Guid userId, Guid? instrumentId, string? strategy, CancellationToken ct = default);

    Task<BacktestSymbolSummary> GetSymbolSummaryAsync(
        Guid userId, Guid instrumentId, string? strategy, CancellationToken ct = default);

    Task<BacktestNoteRow> UpsertNoteAsync(BacktestNoteRow note, CancellationToken ct = default);

    Task<bool> DeleteNoteAsync(Guid userId, Guid noteId, CancellationToken ct = default);

    Task DeleteAutoNotesAsync(Guid userId, Guid instrumentId, string strategy, CancellationToken ct = default);

    Task InsertAutoNotesAsync(IReadOnlyList<BacktestNoteRow> notes, CancellationToken ct = default);
}

public interface ICurrentUserAccessor
{
    Guid UserId { get; }
}
