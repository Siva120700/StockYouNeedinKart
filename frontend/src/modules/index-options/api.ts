import { gql } from "../../api/client";
import type { NiftyOptionChainSnapshot, NiftyOrbRecommendation, NiftyOrbRun } from "./types";

const REC_FIELDS = `
  id runId instrumentId appSymbol instrumentName side signalSource status skipReason
  spotLtp orbHigh orbLow orbRange
  underlyingEntry underlyingStopLoss underlyingTargetT1 underlyingTargetT2 underlyingTargetT3
  confidenceScore reasons
  contractTradingSymbol contractExpiryLabel contractStrike contractOptionType contractLotSize
  premiumLtp premiumStopLoss premiumTargetT1 premiumTargetT2 premiumTargetT3
  delta gamma theta vega impliedVolatility tradeVolume
  altTradingSymbol altStrike altDelta altImpliedVolatility altPremiumLtp flatByIst
`;

export const IndexOptionsApi = {
  async fetchRecommendations(runId?: string | null): Promise<NiftyOrbRecommendation[]> {
    const data = await gql<{ niftyOrbRecommendations: NiftyOrbRecommendation[] }>(
      `query ($runId: UUID) {
        niftyOrbRecommendations(runId: $runId) { ${REC_FIELDS} }
      }`,
      { runId: runId || null },
    );
    return data.niftyOrbRecommendations;
  },

  async runAnalysis(): Promise<NiftyOrbRun> {
    const data = await gql<{ runNiftyOrbAnalysis: NiftyOrbRun }>(`
      mutation {
        runNiftyOrbAnalysis { id status asOfDate }
      }
    `);
    return data.runNiftyOrbAnalysis;
  },

  async fetchOptionChain(): Promise<NiftyOptionChainSnapshot> {
    const data = await gql<{ niftyOptionChain: NiftyOptionChainSnapshot }>(`
      query {
        niftyOptionChain {
          spot expiryLabel asOf usable
          pcr callWallStrike callWallOi putWallStrike putWallOi maxPainStrike
          totalCallOi totalPutOi
          ladder { strike callOi putOi callLtp putLtp }
        }
      }
    `);
    return data.niftyOptionChain;
  },
};
