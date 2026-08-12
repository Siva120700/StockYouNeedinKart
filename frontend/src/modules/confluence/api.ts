import { gql } from "../../api/client";
import { ActionFactory } from "../../api/factories";
import type { ConfluenceSignal } from "./types";
import { SECTOR_RS_GQL } from "../../utils/sectorRelativeStrength.tsx";

export const ConfluenceApi = {
  async fetchSignals(): Promise<ConfluenceSignal[]> {
    const data = await gql<{ confluenceSignals: ConfluenceSignal[] }>(`
      {
        confluenceSignals {
          id instrumentId appSymbol instrumentName side asOfDate
          entryPrice initialStopLoss targetT1 targetT2 targetT3
          analysisSignalId liquiditySignalId
          signalsEntry liquidityEntry signalsStopLoss liquidityStopLoss
          sectorConfirmed freshCross
          ${SECTOR_RS_GQL}
        }
      }
    `);
    return data.confluenceSignals;
  },

  async runBothAnalyses(): Promise<void> {
    await ActionFactory.runAnalysis();
    await ActionFactory.runLiquidityAnalysis("v2");
  },

  async openPosition(row: ConfluenceSignal): Promise<string> {
    const data = await gql<{ openPositionFromConfluence: string }>(
      `mutation ($liquiditySignalId: UUID!, $analysisSignalId: UUID!, $quantityLots: Int!) {
        openPositionFromConfluence(
          liquiditySignalId: $liquiditySignalId
          analysisSignalId: $analysisSignalId
          quantityLots: $quantityLots
        )
      }`,
      {
        liquiditySignalId: row.liquiditySignalId,
        analysisSignalId: row.analysisSignalId,
        quantityLots: 1,
      },
    );
    return data.openPositionFromConfluence;
  },
};
