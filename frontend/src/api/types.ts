import type { SectorRelativeStrength } from "../utils/sectorRelativeStrength.tsx";

export type LtpQuote = {
  instrumentId: string;
  appSymbol: string;
  instrumentName: string;
  exchange: string;
  ltp: number;
  fetchedAt: string | null;
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
  sectorRs?: SectorRelativeStrength | null;
};

export type MomentumSignal = {
  id: string;
  momentumRunId: string;
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
  momentumScore: number;
  sectorRs?: SectorRelativeStrength | null;
};

export type MomentumFuturesSuggestion = {
  instrumentId: string;
  side: string;
  tradingSymbol?: string | null;
  expiryLabel?: string | null;
  symbolToken?: string | null;
  lotSize: number;
  spotLtp?: number | null;
  underlyingEntry: number;
  underlyingStopLoss: number;
  underlyingTargetT1?: number | null;
  underlyingTargetT2?: number | null;
  underlyingTargetT3?: number | null;
  futuresEntry?: number | null;
  futuresExit?: number | null;
  futuresTargetT1?: number | null;
  futuresTargetT2?: number | null;
  futuresTargetT3?: number | null;
  premiumPct?: number | null;
  buildUp?: string | null;
  futuresConflict: boolean;
  skipReason?: string | null;
  contractValue?: number | null;
  marginRequired?: number | null;
  expectedProfitT1?: number | null;
  expectedProfitT2?: number | null;
  expectedProfitT3?: number | null;
  expectedStopLoss?: number | null;
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
  /** V2 event taxonomy; null for classic/fresh. */
  eventType?: string | null;
  zoneTags?: string[] | null;
  qualityScore?: number | null;
  confidenceRating?: string | null;
  sweepStrength?: string | null;
  atr14?: number | null;
  scoreReasons?: string[] | null;
  sectorRs?: SectorRelativeStrength | null;
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

export type SpikeScanRow = {
  instrumentId: string;
  appSymbol: string;
  side: string;
  barTime: string;
  forming: boolean;
  open: number;
  high: number;
  low: number;
  close: number;
  volume: number;
  changePct: number;
  rangePct: number;
  relativeVolume: number;
  spikeScore: number;
  entryPrice: number;
  initialStopLoss: number;
  targetT1?: number | null;
  targetT2?: number | null;
  targetT3?: number | null;
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
