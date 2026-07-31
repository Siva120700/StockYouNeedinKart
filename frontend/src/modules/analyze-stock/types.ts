export type AnalyzeStockSetup = {
  source: string;
  side: string;
  asOfDate: string;
  entry: number;
  stopLoss: number;
  targetT1: number | null;
  targetT2: number | null;
  targetT3: number | null;
  plannedRiskReward: number | null;
};

export type AnalyzeStockLevels = {
  pivot: number | null;
  resistance1: number | null;
  resistance2: number | null;
  resistance3: number | null;
  support1: number | null;
  support2: number | null;
  support3: number | null;
  priorDayHigh: number | null;
  priorDayLow: number | null;
  ma2d: number | null;
  ma3d: number | null;
  ma5d: number | null;
  last2dHigh: number | null;
  last2dLow: number | null;
  sweptZoneType: string | null;
  sweptZonePrice: number | null;
  sweepSide: string | null;
  nearestZoneType: string | null;
  nearestZonePrice: number | null;
  distancePct: number | null;
  zoneTags: string[];
  liquidityContext: string | null;
  liquidityEvalStatus: string | null;
  liquidityEvalDetail: string | null;
  liquidityLive: boolean;
  liquidityZones: { type: string; price: number; kind: string }[];
  breakoutLevel: number | null;
  breakoutPattern: string | null;
};

export type AnalyzeStockResult = {
  instrumentId: string;
  symbol: string;
  name: string;
  spotLtp: number | null;
  ltpFetchedAt: string | null;
  sectorInstrumentId: string | null;
  sectorSymbol: string | null;
  sectorName: string | null;
  sectorConfirmed: boolean | null;
  verdict: string;
  verdictLabel: string;
  verdictReasons: string[];
  primarySetup: AnalyzeStockSetup | null;
  levels: AnalyzeStockLevels;
  signal: {
    side: string;
    asOfDate: string;
    entryPrice: number;
    initialStopLoss: number;
    targetT1: number | null;
    targetT2: number | null;
    targetT3: number | null;
    volumeOk: boolean;
    sectorConfirmed: boolean;
    freshCross: boolean;
    ma2d: number | null;
    ma3d: number | null;
    ma5d: number | null;
  } | null;
  liquidityFresh: {
    side: string;
    asOfDate: string;
    entryPrice: number;
    initialStopLoss: number;
    targetT1: number | null;
    targetT2: number | null;
    targetT3: number | null;
    relativeVolume: number;
    rvolOk: boolean;
    strongClose: boolean;
    sectorConfirmed: boolean;
    sweepSide: string | null;
    sweptZoneType: string | null;
    sweptZonePrice: number | null;
    nearestZoneType: string | null;
    nearestZonePrice: number | null;
    distancePct: number | null;
    zoneTags: string[];
    timeframeContext: string;
  } | null;
  liquidityClassic: {
    side: string;
    asOfDate: string;
    entryPrice: number;
    initialStopLoss: number;
    targetT1: number | null;
    zoneTags: string[];
    timeframeContext: string;
  } | null;
  confluence: {
    side: string;
    asOfDate: string;
    entryPrice: number;
    initialStopLoss: number;
    targetT1: number | null;
    targetT2: number | null;
    targetT3: number | null;
    sectorConfirmed: boolean;
    freshCross: boolean;
  } | null;
  tradeScore: {
    side: string;
    asOfDate: string;
    confidenceScore: number;
    rating: string;
    signalsScore: number;
    liquidityScore: number;
    breakoutScore: number;
    futuresScore: number;
    optionsScore: number;
    reasons: string[];
    entryPrice: number;
    initialStopLoss: number;
    targetT1: number | null;
    targetT2: number | null;
    targetT3: number | null;
    breakoutConfirmed: boolean;
  } | null;
  breakout: {
    side: string;
    asOfDate: string;
    confirmed: boolean;
    closePrice: number | null;
    level20d: number | null;
    volumeRatio: number | null;
    adx: number | null;
    rsi: number | null;
    patternType: string | null;
  } | null;
  optionsIntraday: {
    side: string;
    signalSource: string;
    confidenceScore: number;
    reasons: string[];
    contractTradingSymbol: string | null;
    contractOptionType: string | null;
    contractStrike: number | null;
    premiumLtp: number | null;
    delta: number | null;
    impliedVolatility: number | null;
    flatByIst: string;
  } | null;
  backtestSummary: {
    timesInStrategy: number;
    targetHits: number;
    slHits: number;
    targetHitRatePct: number | null;
    avgRiskReward: number | null;
    avgRMultiple: number | null;
  } | null;
  recentBars: {
    tradeDate: string;
    open: number;
    high: number;
    low: number;
    close: number;
    volume: number;
  }[];
};

export function verdictColor(verdict: string): "success" | "warning" | "error" | "default" | "info" {
  switch (verdict) {
    case "strong_buy":
    case "buy":
      return "success";
    case "watch":
    case "unconfirmed":
      return "warning";
    case "avoid":
      return "error";
    case "no_setup":
      return "default";
    default:
      return "info";
  }
}

export function sourceLabel(source: string): string {
  switch (source) {
    case "trade_score":
      return "Trade Score";
    case "confluence":
      return "Confluence";
    case "liquidity_fresh":
      return "Liquidity Fresh";
    case "liquidity":
      return "Liquidity";
    case "signals":
      return "Signals";
    default:
      return source;
  }
}

export function fmt(n: number | null | undefined, digits = 2): string {
  if (n == null || !Number.isFinite(Number(n))) return "—";
  return Number(n).toFixed(digits);
}
