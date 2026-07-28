export type LtpQuote = {
  instrumentId: string;
  appSymbol: string;
  instrumentName: string;
  exchange: string;
  ltp: number;
  fetchedAt: string;
};

export type Signal = {
  id: string;
  analysisRunId: string;
  instrumentId: string;
  appSymbol: string;
  instrumentName: string;
  side: string;
  entryPrice: number;
  initialStopLoss: number;
  targetT1?: number | null;
  targetT2?: number | null;
  targetT3?: number | null;
  volumeOk: boolean;
  sectorConfirmed: boolean;
  freshCross: boolean;
};

export type LiquiditySignal = {
  id: string;
  liquidityRunId: string;
  instrumentId: string;
  appSymbol: string;
  instrumentName: string;
  side: string;
  entryPrice: number;
  initialStopLoss: number;
  targetT1?: number | null;
  targetT2?: number | null;
  targetT3?: number | null;
  relativeVolume: number;
  rvolPercentile: number;
  rvolOk: boolean;
  strongClose: boolean;
  sectorConfirmed: boolean;
  sweepSide?: string | null;
  sweptZoneType?: string | null;
  sweptZonePrice?: number | null;
  nearestZoneType?: string | null;
  nearestZonePrice?: number | null;
  distancePct?: number | null;
  timeframeContext: string;
};

export type ConfluenceSignal = {
  id: string;
  instrumentId: string;
  appSymbol: string;
  instrumentName: string;
  side: string;
  asOfDate: string;
  entryPrice: number;
  initialStopLoss: number;
  targetT1?: number | null;
  targetT2?: number | null;
  targetT3?: number | null;
  analysisSignalId: string;
  liquiditySignalId: string;
  signalsEntry: number;
  liquidityEntry: number;
  signalsStopLoss: number;
  liquidityStopLoss: number;
  sectorConfirmed: boolean;
  freshCross: boolean;
  relativeVolume: number;
  rvolPercentile: number;
  strongClose: boolean;
  sweptZoneType?: string | null;
  timeframeContext: string;
};

export type OpenPosition = {
  id: string;
  symbol: string;
  instrumentName: string;
  side: string;
  quantityLots: number;
  entryPrice: number;
  currentStopLoss: number;
  lastPrice?: number | null;
  computedUnrealizedPnl?: number | null;
};

export type WatchlistItem = {
  instrumentId: string;
  symbol: string;
  name: string;
};

export type User = {
  id: string;
  email: string;
  displayName?: string | null;
};

export type AnalysisRun = {
  id: string;
  status: string;
  asOfDate: string;
};

export type UniverseInstrument = {
  id: string;
  symbol: string;
  name: string;
};

export type BacktestNote = {
  id: string;
  instrumentId: string;
  appSymbol: string;
  instrumentName: string;
  strategy: string;
  side: string;
  signalDate: string;
  entryPrice: number;
  initialStopLoss: number;
  targetT1?: number | null;
  targetT2?: number | null;
  targetT3?: number | null;
  result: string;
  targetLevel?: string | null;
  targetHitPct?: number | null;
  exitPrice?: number | null;
  exitDate?: string | null;
  pnlPct?: number | null;
  rMultiple?: number | null;
  notes: string;
  wouldTakeLive?: boolean | null;
  source?: string | null;
};

export type BacktestSymbolSummary = {
  instrumentId: string;
  appSymbol: string;
  instrumentName: string;
  strategyFilter?: string | null;
  timesInStrategy: number;
  targetHits: number;
  slHits: number;
  skipped: number;
  openCount: number;
  targetHitRatePct?: number | null;
  avgTargetHitPct?: number | null;
  /** Average planned R:R (|T1−entry| / |entry−SL|). */
  avgRiskReward?: number | null;
  /** Average realized R-multiple from outcomes. */
  avgRMultiple?: number | null;
};

export type BacktestNoteInput = {
  id?: string | null;
  instrumentId: string;
  strategy: string;
  side: string;
  signalDate: string;
  entryPrice: number;
  initialStopLoss: number;
  targetT1?: number | null;
  targetT2?: number | null;
  targetT3?: number | null;
  result: string;
  targetLevel?: string | null;
  targetHitPct?: number | null;
  exitPrice?: number | null;
  exitDate?: string | null;
  pnlPct?: number | null;
  rMultiple?: number | null;
  notes?: string | null;
  wouldTakeLive?: boolean | null;
};
