import { gql } from "../../api/client";
import type { TradeConfidenceRun, TradeConfidenceScore } from "./types";
import { SECTOR_RS_GQL } from "../../utils/sectorRelativeStrength.tsx";
import { SECTOR_ROTATION_GQL } from "../../utils/sectorRotation.tsx";

export const TradeScoreApi = {
  async fetchScores(): Promise<TradeConfidenceScore[]> {
    const data = await gql<{ tradeConfidenceScores: TradeConfidenceScore[] }>(`
      {
        tradeConfidenceScores {
          id runId instrumentId appSymbol instrumentName side asOfDate
          confidenceScore rating
          signalsScore liquidityScore breakoutScore futuresScore optionsScore
          reasons entryPrice initialStopLoss targetT1 targetT2 targetT3
          analysisSignalId liquiditySignalId
          breakoutConfirmed breakoutAdx breakoutRsi
          ${SECTOR_RS_GQL}
          ${SECTOR_ROTATION_GQL}
        }
      }
    `);
    return data.tradeConfidenceScores;
  },

  async runAnalysis(refreshSignals: boolean, refreshLiquidity: boolean): Promise<TradeConfidenceRun> {
    const data = await gql<{ runTradeConfidenceAnalysis: TradeConfidenceRun }>(
      `mutation ($refreshSignals: Boolean!, $refreshLiquidity: Boolean!) {
        runTradeConfidenceAnalysis(
          refreshSignals: $refreshSignals
          refreshLiquidity: $refreshLiquidity
        ) {
          id status asOfDate
        }
      }`,
      { refreshSignals, refreshLiquidity },
    );
    return data.runTradeConfidenceAnalysis;
  },

  async openPosition(scoreId: string, quantityLots = 1): Promise<string> {
    const data = await gql<{ openPositionFromTradeScore: string }>(
      `mutation ($scoreId: UUID!, $quantityLots: Int!) {
        openPositionFromTradeScore(scoreId: $scoreId, quantityLots: $quantityLots)
      }`,
      { scoreId, quantityLots },
    );
    return data.openPositionFromTradeScore;
  },
};
