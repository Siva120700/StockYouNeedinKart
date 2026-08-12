import type { SectorRelativeStrength } from "../../utils/sectorRelativeStrength.tsx";

export type OptionsIntradayRecommendation = {
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
  underlyingEntry: number;
  underlyingStopLoss: number;
  underlyingTargetT1: number | null;
  underlyingTargetT2: number | null;
  underlyingTargetT3: number | null;
  futuresBuildUp: string | null;
  futuresPremiumPct: number | null;
  confidenceScore: number;
  reasons: string[];
  contractTradingSymbol: string | null;
  contractExpiryLabel: string | null;
  contractStrike: number | null;
  contractOptionType: string | null;
  contractLotSize: number | null;
  premiumLtp: number | null;
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
  sectorRs?: SectorRelativeStrength | null;
};

export type OptionsIntradayRun = {
  id: string;
  status: string;
  asOfDate: string;
};
