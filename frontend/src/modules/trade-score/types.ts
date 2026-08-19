import type { SectorRelativeStrength } from "../../utils/sectorRelativeStrength.tsx";
import type { SectorRotationOverlay } from "../../utils/sectorRotation.tsx";

export type TradeConfidenceScore = {
  id: string;
  runId: string;
  instrumentId: string;
  appSymbol: string;
  instrumentName: string;
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
  targetT1?: number | null;
  targetT2?: number | null;
  targetT3?: number | null;
  analysisSignalId?: string | null;
  liquiditySignalId?: string | null;
  breakoutConfirmed: boolean;
  breakoutAdx?: number | null;
  breakoutRsi?: number | null;
  sectorRs?: SectorRelativeStrength | null;
  sectorRotation?: SectorRotationOverlay | null;
};

export type TradeConfidenceRun = {
  id: string;
  status: string;
  asOfDate: string;
};

export function ratingLabel(rating: string): string {
  switch (rating) {
    case "strong_buy":
      return "★★★★★ Strong Buy";
    case "buy":
      return "★★★★ Buy";
    case "watch":
      return "★★★ Watch";
    case "neutral":
      return "Neutral";
    case "unconfirmed":
      return "Unconfirmed — signal only";
    case "no_setup":
      return "No setup";
    default:
      return "Avoid";
  }
}
