import { gql } from "../../api/client";
import type { OptionsIntradayRecommendation, OptionsIntradayRun } from "./types";
import { SECTOR_RS_GQL } from "../../utils/sectorRelativeStrength.tsx";

const REC_FIELDS = `
  id runId instrumentId appSymbol instrumentName side signalSource status skipReason
  spotLtp underlyingEntry underlyingStopLoss underlyingTargetT1 underlyingTargetT2 underlyingTargetT3
  futuresBuildUp futuresPremiumPct confidenceScore reasons
  contractTradingSymbol contractExpiryLabel contractStrike contractOptionType contractLotSize
  premiumLtp delta gamma theta vega impliedVolatility tradeVolume
  altTradingSymbol altStrike altDelta altImpliedVolatility altPremiumLtp flatByIst
  ${SECTOR_RS_GQL}
`;

export const OptionsIntradayApi = {
  async fetchRecommendations(runId?: string | null): Promise<OptionsIntradayRecommendation[]> {
    const data = await gql<{ optionsIntradayRecommendations: OptionsIntradayRecommendation[] }>(
      `query ($runId: UUID) {
        optionsIntradayRecommendations(runId: $runId) { ${REC_FIELDS} }
      }`,
      { runId: runId || null },
    );
    return data.optionsIntradayRecommendations;
  },

  async runAnalysis(): Promise<OptionsIntradayRun> {
    const data = await gql<{ runOptionsIntradayAnalysis: OptionsIntradayRun }>(`
      mutation {
        runOptionsIntradayAnalysis { id status asOfDate }
      }
    `);
    return data.runOptionsIntradayAnalysis;
  },
};
