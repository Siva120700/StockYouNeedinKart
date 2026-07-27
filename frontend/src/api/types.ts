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
