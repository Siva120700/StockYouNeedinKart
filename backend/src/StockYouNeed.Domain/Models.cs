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
