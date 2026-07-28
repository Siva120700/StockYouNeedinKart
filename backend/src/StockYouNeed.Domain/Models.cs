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
}

/// <summary>Signals + Liquidity Fresh overlap with combined entry/SL.</summary>
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
    public decimal RelativeVolume { get; set; }
    public decimal RvolPercentile { get; set; }
    public bool StrongClose { get; set; }
    public string? SweptZoneType { get; set; }
    public string TimeframeContext { get; set; } = "signals+liquidity_fresh";
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
