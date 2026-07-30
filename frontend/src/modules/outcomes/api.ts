import { gql } from "../../api/client";
import type { SignalOutcome, SignalOutcomeSummary } from "./types";

const OUTCOME_FIELDS = `
  id instrumentId appSymbol instrumentName strategy side signalDate
  entryPrice initialStopLoss targetT1 targetT2 targetT3
  result targetLevel targetHitPct exitPrice exitDate pnlPct rMultiple
`;

export const OutcomesApi = {
  async fetchOutcomes(strategy?: string | null, result?: string | null): Promise<SignalOutcome[]> {
    const data = await gql<{ signalOutcomes: SignalOutcome[] }>(
      `query ($strategy: String, $result: String) {
        signalOutcomes(strategy: $strategy, result: $result) { ${OUTCOME_FIELDS} }
      }`,
      { strategy: strategy || null, result: result || null },
    );
    return data.signalOutcomes;
  },

  async fetchSummaries(strategy?: string | null): Promise<SignalOutcomeSummary[]> {
    const data = await gql<{ signalOutcomeSummaries: SignalOutcomeSummary[] }>(
      `query ($strategy: String) {
        signalOutcomeSummaries(strategy: $strategy) {
          strategyFilter setups targetHits slHits timeStops openCount
          targetHitRatePct avgTargetHitPct avgRiskReward avgRMultiple
        }
      }`,
      { strategy: strategy || null },
    );
    return data.signalOutcomeSummaries;
  },

  async resolveOpen(): Promise<number> {
    const data = await gql<{ resolveSignalOutcomes: number }>(`
      mutation { resolveSignalOutcomes }
    `);
    return data.resolveSignalOutcomes;
  },

  async backfillFromLive(): Promise<number> {
    const data = await gql<{ backfillSignalOutcomes: number }>(`
      mutation { backfillSignalOutcomes }
    `);
    return data.backfillSignalOutcomes;
  },
};
