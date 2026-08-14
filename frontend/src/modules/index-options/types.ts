export type NiftyOrbRecommendation = {
  id: string;
  runId: string;
  instrumentId: string;
  appSymbol: string;
  instrumentName: string;
  side: string;
  signalSource: string;
  status: string;
  skipReason: string | null;
  spotLtp: number | null;
  orbHigh: number | null;
  orbLow: number | null;
  orbRange: number | null;
  underlyingEntry: number;
  underlyingStopLoss: number;
  underlyingTargetT1: number | null;
  underlyingTargetT2: number | null;
  underlyingTargetT3: number | null;
  confidenceScore: number;
  reasons: string[];
  contractTradingSymbol: string | null;
  contractExpiryLabel: string | null;
  contractStrike: number | null;
  contractOptionType: string | null;
  contractLotSize: number | null;
  premiumLtp: number | null;
  premiumStopLoss: number | null;
  premiumTargetT1: number | null;
  premiumTargetT2: number | null;
  premiumTargetT3: number | null;
  delta: number | null;
  gamma: number | null;
  theta: number | null;
  vega: number | null;
  impliedVolatility: number | null;
  tradeVolume: number | null;
  altTradingSymbol: string | null;
  altStrike: number | null;
  altDelta: number | null;
  altImpliedVolatility: number | null;
  altPremiumLtp: number | null;
  flatByIst: string;
};

export type NiftyOrbRun = {
  id: string;
  status: string;
  asOfDate: string;
};

export type NiftyOptionChainStrike = {
  strike: number;
  callOi: number;
  putOi: number;
  callLtp: number | null;
  putLtp: number | null;
};

export type NiftyOptionChainSnapshot = {
  spot: number;
  expiryLabel: string;
  asOf: string;
  usable: boolean;
  pcr: number | null;
  callWallStrike: number | null;
  callWallOi: number;
  putWallStrike: number | null;
  putWallOi: number;
  maxPainStrike: number | null;
  totalCallOi: number;
  totalPutOi: number;
  ladder: NiftyOptionChainStrike[];
};
