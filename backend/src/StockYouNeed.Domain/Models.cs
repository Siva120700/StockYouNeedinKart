namespace StockYouNeed.Domain;

public sealed class Instrument
{
    public Guid Id { get; set; }
    public string Kind { get; set; } = "equity";
    public string Symbol { get; set; } = "";
    public string Name { get; set; } = "";
    public string Exchange { get; set; } = "NSE";
    public bool IsActive { get; set; } = true;
}

public sealed class AngelTokenRow
{
    public Guid InstrumentId { get; set; }
    public string Exchange { get; set; } = "NSE";
    public string SymbolToken { get; set; } = "";
    public string TradingSymbol { get; set; } = "";
    public string? Name { get; set; }
    public string AppSymbol { get; set; } = "";
}

public sealed class MarketLtpRow
{
    public Guid InstrumentId { get; set; }
    public string AppSymbol { get; set; } = "";
    public string InstrumentName { get; set; } = "";
    public string Exchange { get; set; } = "";
    public string TradingSymbol { get; set; } = "";
    public string SymbolToken { get; set; } = "";
    public decimal Ltp { get; set; }
    public DateTimeOffset FetchedAt { get; set; }
}

public sealed class MarketBarRow
{
    public Guid InstrumentId { get; set; }
    public string AppSymbol { get; set; } = "";
    public DateOnly TradeDate { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public long Volume { get; set; }
    public string Source { get; set; } = "angel";
}

public sealed class MarketIntradayBarRow
{
    public Guid InstrumentId { get; set; }
    public string AppSymbol { get; set; } = "";
    public string Interval { get; set; } = "1h";
    public DateTimeOffset BarTime { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public long Volume { get; set; }
}

public sealed class MarketOhlcRow
{
    public Guid InstrumentId { get; set; }
    public string AppSymbol { get; set; } = "";
    public string Exchange { get; set; } = "";
    public string TradingSymbol { get; set; } = "";
    public string SymbolToken { get; set; } = "";
    public decimal Ltp { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public long TradeVolume { get; set; }
    public DateTimeOffset FetchedAt { get; set; }
    public Guid? AnalysisRunId { get; set; }
}

public sealed class AnalysisRunRow
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string TriggeredBy { get; set; } = "";
    public bool IncludeNifty50 { get; set; }
    public bool IncludeNifty100 { get; set; }
    public bool IncludeWatchlist { get; set; }
    public DateOnly AsOfDate { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public string Status { get; set; } = "running";
    public string? ErrorMessage { get; set; }
}

public sealed class AnalysisSignalRow
{
    public Guid Id { get; set; }
    public Guid AnalysisRunId { get; set; }
    public Guid UserId { get; set; }
    public Guid InstrumentId { get; set; }
    public string AppSymbol { get; set; } = "";
    public string InstrumentName { get; set; } = "";
    public string Side { get; set; } = "";
    public DateOnly AsOfDate { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal InitialStopLoss { get; set; }
    public decimal? TargetT1 { get; set; }
    public decimal? TargetT2 { get; set; }
    public decimal? TargetT3 { get; set; }
    public bool VolumeOk { get; set; }
    public bool SectorConfirmed { get; set; }
    public bool FreshCross { get; set; }
    public decimal? Ma2d { get; set; }
    public decimal? Ma3d { get; set; }
    public decimal? Ma5d { get; set; }
    public decimal? Last2dHigh { get; set; }
    public decimal? Last2dLow { get; set; }
}

public sealed class LiquiditySignalRow
{
    public Guid Id { get; set; }
    public Guid LiquidityRunId { get; set; }
    public Guid UserId { get; set; }
    public Guid InstrumentId { get; set; }
    public string AppSymbol { get; set; } = "";
    public string InstrumentName { get; set; } = "";
    public string Side { get; set; } = "";
    public DateOnly AsOfDate { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal InitialStopLoss { get; set; }
    public decimal? TargetT1 { get; set; }
    public decimal? TargetT2 { get; set; }
    public decimal? TargetT3 { get; set; }
    public decimal RelativeVolume { get; set; }
    public decimal RvolPercentile { get; set; }
    public bool RvolOk { get; set; }
    public bool StrongClose { get; set; }
    public bool SectorConfirmed { get; set; }
    public string? SweepSide { get; set; }
    public string? SweptZoneType { get; set; }
    public decimal? SweptZonePrice { get; set; }
    public string? NearestZoneType { get; set; }
    public decimal? NearestZonePrice { get; set; }
    public decimal? DistancePct { get; set; }
    public string[] ZoneTags { get; set; } = Array.Empty<string>();
    public string TimeframeContext { get; set; } = "4h_sweep+1h_confirm";
    /// <summary>
    /// V2 event taxonomy: external_sweep | internal_liquidity | liquidity_cluster |
    /// delayed_reclaim | multi_sweep. Null for classic/fresh.
    /// </summary>
    public string? EventType { get; set; }
    public int QualityScore { get; set; }
    public string ConfidenceRating { get; set; } = "";
    public string? SweepStrength { get; set; }
    public decimal? Atr14 { get; set; }
    public string[] ScoreReasons { get; set; } = Array.Empty<string>();
}

/// <summary>Signals + Liquidity Fresh overlap (Confluence menu).</summary>
public sealed class ConfluenceSignalRow
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid InstrumentId { get; set; }
    public string AppSymbol { get; set; } = "";
    public string InstrumentName { get; set; } = "";
    public string Side { get; set; } = "";
    public DateOnly AsOfDate { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal InitialStopLoss { get; set; }
    public decimal? TargetT1 { get; set; }
    public decimal? TargetT2 { get; set; }
    public decimal? TargetT3 { get; set; }
    public Guid AnalysisSignalId { get; set; }
    public Guid LiquiditySignalId { get; set; }
    public decimal SignalsEntry { get; set; }
    public decimal LiquidityEntry { get; set; }
    public decimal SignalsStopLoss { get; set; }
    public decimal LiquidityStopLoss { get; set; }
    public bool SectorConfirmed { get; set; }
    public bool FreshCross { get; set; }
}

/// <summary>Standalone breakout confirmation row (Breakout menu).</summary>
public sealed class BreakoutConfirmationRow
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }
    public Guid UserId { get; set; }
    public Guid InstrumentId { get; set; }
    public string AppSymbol { get; set; } = "";
    public string InstrumentName { get; set; } = "";
    public string Side { get; set; } = "";
    public DateOnly AsOfDate { get; set; }
    public bool Confirmed { get; set; }
    public decimal? ClosePrice { get; set; }
    public decimal? Level20d { get; set; }
    public decimal? VolumeRatio { get; set; }
    public decimal? Adx { get; set; }
    public decimal? Rsi { get; set; }
    public decimal? Atr { get; set; }
    public bool AtrExpansion { get; set; }
    public string? PatternType { get; set; }
}

public sealed class BreakoutAnalysisRunRow
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public DateOnly AsOfDate { get; set; }
    public string Status { get; set; } = "running";
}

/// <summary>Trade confidence run metadata.</summary>
public sealed class TradeConfidenceRunRow
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string TriggeredBy { get; set; } = "manual";
    public DateOnly AsOfDate { get; set; }
    public string Status { get; set; } = "running";
    public string? ErrorMessage { get; set; }
}

/// <summary>Scored high-probability trade (Signals + Liquidity + Breakout + F&amp;O layers).</summary>
public sealed class TradeConfidenceScoreRow
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }
    public Guid UserId { get; set; }
    public Guid InstrumentId { get; set; }
    public string AppSymbol { get; set; } = "";
    public string InstrumentName { get; set; } = "";
    public string Side { get; set; } = "";
    public DateOnly AsOfDate { get; set; }
    public int ConfidenceScore { get; set; }
    public string Rating { get; set; } = "avoid";
    public int SignalsScore { get; set; }
    public int LiquidityScore { get; set; }
    public int BreakoutScore { get; set; }
    public int FuturesScore { get; set; }
    public int OptionsScore { get; set; }
    public string[] Reasons { get; set; } = Array.Empty<string>();
    public decimal EntryPrice { get; set; }
    public decimal InitialStopLoss { get; set; }
    public decimal? TargetT1 { get; set; }
    public decimal? TargetT2 { get; set; }
    public decimal? TargetT3 { get; set; }
    public Guid? AnalysisSignalId { get; set; }
    public Guid? LiquiditySignalId { get; set; }
    public bool BreakoutConfirmed { get; set; }
    public decimal? BreakoutAdx { get; set; }
    public decimal? BreakoutRsi { get; set; }
}

public sealed class OpenPositionRow
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid InstrumentId { get; set; }
    public string Symbol { get; set; } = "";
    public string InstrumentName { get; set; } = "";
    public string Side { get; set; } = "";
    public int QuantityLots { get; set; }
    public int LotSize { get; set; }
    public int QuantityUnits { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal CurrentStopLoss { get; set; }
    public decimal? LastPrice { get; set; }
    public decimal? UnrealizedPnlInr { get; set; }
    public decimal? ComputedUnrealizedPnl { get; set; }
}

public sealed class WatchlistItemRow
{
    public Guid UserId { get; set; }
    public Guid InstrumentId { get; set; }
    public string Symbol { get; set; } = "";
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }
}

public sealed class UserRow
{
    public Guid Id { get; set; }
    public string Email { get; set; } = "";
    public string? DisplayName { get; set; }
}

/// <summary>Manual backtest journal entry — isolated from live signal engines.</summary>
public sealed class BacktestNoteRow
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid InstrumentId { get; set; }
    public string AppSymbol { get; set; } = "";
    public string InstrumentName { get; set; } = "";
    public string Strategy { get; set; } = "signals";
    public string Side { get; set; } = "buy";
    public DateOnly SignalDate { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal InitialStopLoss { get; set; }
    public decimal? TargetT1 { get; set; }
    public decimal? TargetT2 { get; set; }
    public decimal? TargetT3 { get; set; }
    public string Result { get; set; } = "open";
    public string? TargetLevel { get; set; }
    public decimal? TargetHitPct { get; set; }
    public decimal? ExitPrice { get; set; }
    public DateOnly? ExitDate { get; set; }
    public decimal? PnlPct { get; set; }
    public decimal? RMultiple { get; set; }
    public string Notes { get; set; } = "";
    public bool? WouldTakeLive { get; set; }
    public bool SectorConfirmed { get; set; }
    public string Source { get; set; } = "manual";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Aggregated backtest stats for one symbol (and optional strategy filter).</summary>
public sealed class BacktestSymbolSummary
{
    public Guid InstrumentId { get; set; }
    public string AppSymbol { get; set; } = "";
    public string InstrumentName { get; set; } = "";
    public string? StrategyFilter { get; set; }
    public int TimesInStrategy { get; set; }
    public int TargetHits { get; set; }
    public int SlHits { get; set; }
    public int Skipped { get; set; }
    public int OpenCount { get; set; }
    public decimal? TargetHitRatePct { get; set; }
    public decimal? AvgTargetHitPct { get; set; }
    /// <summary>Average planned reward:risk using T1 vs stop (|T1−entry| / |entry−SL|).</summary>
    public decimal? AvgRiskReward { get; set; }
    /// <summary>Average realized R-multiple from trade outcomes.</summary>
    public decimal? AvgRMultiple { get; set; }
}

/// <summary>Live forward outcome for a setup emitted by a strategy engine.</summary>
public sealed class SignalOutcomeRow
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid InstrumentId { get; set; }
    public string AppSymbol { get; set; } = "";
    public string InstrumentName { get; set; } = "";
    public string Strategy { get; set; } = "signals";
    public string Side { get; set; } = "buy";
    public DateOnly SignalDate { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal InitialStopLoss { get; set; }
    public decimal? TargetT1 { get; set; }
    public decimal? TargetT2 { get; set; }
    public decimal? TargetT3 { get; set; }
    public string Result { get; set; } = "open";
    public string? TargetLevel { get; set; }
    public decimal? TargetHitPct { get; set; }
    public decimal? ExitPrice { get; set; }
    public DateOnly? ExitDate { get; set; }
    public decimal? PnlPct { get; set; }
    public decimal? RMultiple { get; set; }
    public Guid? AnalysisSignalId { get; set; }
    public Guid? LiquiditySignalId { get; set; }
    public Guid? TradeConfidenceScoreId { get; set; }
    public Guid? BreakoutConfirmationId { get; set; }
    public bool SectorConfirmed { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Aggregated live forward accuracy for one strategy (or all).</summary>
public sealed class SignalOutcomeSummary
{
    public string? StrategyFilter { get; set; }
    public int Setups { get; set; }
    public int TargetHits { get; set; }
    public int SlHits { get; set; }
    public int TimeStops { get; set; }
    public int OpenCount { get; set; }
    public decimal? TargetHitRatePct { get; set; }
    public decimal? AvgTargetHitPct { get; set; }
    public decimal? AvgRiskReward { get; set; }
    public decimal? AvgRMultiple { get; set; }
}

public sealed class NfoContractRow
{
    public Guid Id { get; set; }
    public Guid UnderlyingInstrumentId { get; set; }
    public string AppSymbol { get; set; } = "";
    public string AngelName { get; set; } = "";
    public string Kind { get; set; } = "option";
    public string? OptionType { get; set; }
    public decimal? Strike { get; set; }
    public DateOnly Expiry { get; set; }
    public string ExpiryLabel { get; set; } = "";
    public string SymbolToken { get; set; } = "";
    public string TradingSymbol { get; set; } = "";
    public int LotSize { get; set; } = 1;
    public decimal TickSize { get; set; } = 0.05m;
    public long? LastOi { get; set; }
    public decimal? LastLtp { get; set; }
}

public sealed class OptionsIntradayRunRow
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public DateOnly AsOfDate { get; set; }
    public string Status { get; set; } = "running";
    public string? ErrorMessage { get; set; }
}

/// <summary>One Options Intraday idea: stock setup + recommended contract.</summary>
public sealed class OptionsIntradayRecommendationRow
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }
    public Guid UserId { get; set; }
    public Guid InstrumentId { get; set; }
    public string AppSymbol { get; set; } = "";
    public string InstrumentName { get; set; } = "";
    public string Side { get; set; } = "buy";
    public string SignalSource { get; set; } = "liquidity_fresh";
    public string Status { get; set; } = "recommended";
    public string? SkipReason { get; set; }
    public decimal? SpotLtp { get; set; }
    public decimal UnderlyingEntry { get; set; }
    public decimal UnderlyingStopLoss { get; set; }
    public decimal? UnderlyingTargetT1 { get; set; }
    public decimal? UnderlyingTargetT2 { get; set; }
    public decimal? UnderlyingTargetT3 { get; set; }
    public string? FuturesBuildUp { get; set; }
    public decimal? FuturesPremiumPct { get; set; }
    public int ConfidenceScore { get; set; }
    public string[] Reasons { get; set; } = Array.Empty<string>();
    public string? ContractTradingSymbol { get; set; }
    public string? ContractExpiryLabel { get; set; }
    public decimal? ContractStrike { get; set; }
    public string? ContractOptionType { get; set; }
    public string? ContractToken { get; set; }
    public int? ContractLotSize { get; set; }
    public decimal? PremiumLtp { get; set; }
    public decimal? Delta { get; set; }
    public decimal? Gamma { get; set; }
    public decimal? Theta { get; set; }
    public decimal? Vega { get; set; }
    public decimal? ImpliedVolatility { get; set; }
    public decimal? TradeVolume { get; set; }
    public string? AltTradingSymbol { get; set; }
    public decimal? AltStrike { get; set; }
    public decimal? AltDelta { get; set; }
    public decimal? AltImpliedVolatility { get; set; }
    public decimal? AltPremiumLtp { get; set; }
    public string FlatByIst { get; set; } = "15:20";
    public Guid? LiquiditySignalId { get; set; }
    public Guid? AnalysisSignalId { get; set; }
}

/// <summary>Single-stock deep dive composed from existing engines.</summary>
public sealed class AnalyzeStockResult
{
    public Guid InstrumentId { get; set; }
    public string Symbol { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal? SpotLtp { get; set; }
    public DateTimeOffset? LtpFetchedAt { get; set; }

    public Guid? SectorInstrumentId { get; set; }
    public string? SectorSymbol { get; set; }
    public string? SectorName { get; set; }
    public bool? SectorConfirmed { get; set; }

    public string Verdict { get; set; } = "neutral";
    public string VerdictLabel { get; set; } = "No clear setup";
    public string[] VerdictReasons { get; set; } = Array.Empty<string>();

    public AnalyzeStockSetup? PrimarySetup { get; set; }
    public AnalyzeStockLevels Levels { get; set; } = new();

    public AnalysisSignalRow? Signal { get; set; }
    public LiquiditySignalRow? LiquidityFresh { get; set; }
    public LiquiditySignalRow? LiquidityClassic { get; set; }
    public ConfluenceSignalRow? Confluence { get; set; }
    public TradeConfidenceScoreRow? TradeScore { get; set; }
    public BreakoutConfirmationRow? Breakout { get; set; }
    public OptionsIntradayRecommendationRow? OptionsIntraday { get; set; }

    public BacktestSymbolSummary? BacktestSummary { get; set; }
    public IReadOnlyList<MarketBarRow> RecentBars { get; set; } = Array.Empty<MarketBarRow>();
}

public sealed class AnalyzeStockSetup
{
    public string Source { get; set; } = "";
    public string Side { get; set; } = "";
    public DateOnly AsOfDate { get; set; }
    public decimal Entry { get; set; }
    public decimal StopLoss { get; set; }
    public decimal? TargetT1 { get; set; }
    public decimal? TargetT2 { get; set; }
    public decimal? TargetT3 { get; set; }
    public decimal? PlannedRiskReward { get; set; }
}

public sealed class AnalyzeStockLevels
{
    public decimal? Pivot { get; set; }
    public decimal? Resistance1 { get; set; }
    public decimal? Resistance2 { get; set; }
    public decimal? Resistance3 { get; set; }
    public decimal? Support1 { get; set; }
    public decimal? Support2 { get; set; }
    public decimal? Support3 { get; set; }
    public decimal? PriorDayHigh { get; set; }
    public decimal? PriorDayLow { get; set; }
    public decimal? Ma2d { get; set; }
    public decimal? Ma3d { get; set; }
    public decimal? Ma5d { get; set; }
    public decimal? Last2dHigh { get; set; }
    public decimal? Last2dLow { get; set; }
    public string? SweptZoneType { get; set; }
    public decimal? SweptZonePrice { get; set; }
    public string? SweepSide { get; set; }
    public string? NearestZoneType { get; set; }
    public decimal? NearestZonePrice { get; set; }
    public decimal? DistancePct { get; set; }
    public string[] ZoneTags { get; set; } = Array.Empty<string>();
    public string? LiquidityContext { get; set; }
    /// <summary>Live liquidity eval status: evaluated | few_bars | no_token | angel_disabled | error.</summary>
    public string? LiquidityEvalStatus { get; set; }
    public string? LiquidityEvalDetail { get; set; }
    public bool LiquidityLive { get; set; }
    public IReadOnlyList<LiquidityZoneLevel> LiquidityZones { get; set; } = Array.Empty<LiquidityZoneLevel>();
    public decimal? BreakoutLevel { get; set; }
    public string? BreakoutPattern { get; set; }
}

/// <summary>One liquidity structure zone (PDH/PDL, swing, equal, round, …).</summary>
public sealed class LiquidityZoneLevel
{
    public string Type { get; set; } = "";
    public decimal Price { get; set; }
    public string Kind { get; set; } = ""; // support | resistance | both
}

/// <summary>Live per-stock liquidity evaluation (Analyze Stock).</summary>
public sealed class LiquidityInstrumentEval
{
    public LiquiditySignalRow? Fresh { get; set; }
    public LiquiditySignalRow? Classic { get; set; }
    public IReadOnlyList<LiquidityZoneLevel> Zones { get; set; } = Array.Empty<LiquidityZoneLevel>();
    public string Status { get; set; } = "evaluated";
    public string? Detail { get; set; }
    public int BarsUpserted { get; set; }
    public string? SweepSide { get; set; }
    public string? SweptZoneType { get; set; }
    public decimal? SweptZonePrice { get; set; }
    public string? NearestZoneType { get; set; }
    public decimal? NearestZonePrice { get; set; }
    public decimal? DistancePct { get; set; }
}
