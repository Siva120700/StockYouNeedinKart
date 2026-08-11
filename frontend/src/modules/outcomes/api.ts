import { gql } from "../../api/client";
import type { SignalOutcome, SignalOutcomeSummary } from "./types";

const OUTCOME_FIELDS = `
  id instrumentId appSymbol instrumentName strategy side signalDate
  entryPrice initialStopLoss targetT1 targetT2 targetT3
  result targetLevel targetHitPct exitPrice exitDate pnlPct rMultiple
  sectorConfirmed
`;

export type OutcomeDateRange = {
  fromDate?: string | null;
  toDate?: string | null;
};

export const OutcomesApi = {
  async fetchOutcomes(
    strategy?: string | null,
    result?: string | null,
    sectorConfirmedOnly?: boolean | null,
    range?: OutcomeDateRange | null,
  ): Promise<SignalOutcome[]> {
    const data = await gql<{ signalOutcomes: SignalOutcome[] }>(
      `query (
        $strategy: String
        $result: String
        $sectorConfirmedOnly: Boolean
        $fromDate: LocalDate
        $toDate: LocalDate
      ) {
        signalOutcomes(
          strategy: $strategy
          result: $result
          sectorConfirmedOnly: $sectorConfirmedOnly
          fromDate: $fromDate
          toDate: $toDate
        ) { ${OUTCOME_FIELDS} }
      }`,
      {
        strategy: strategy || null,
        result: result || null,
        sectorConfirmedOnly: sectorConfirmedOnly ?? null,
        fromDate: range?.fromDate || null,
        toDate: range?.toDate || null,
      },
    );
    return data.signalOutcomes;
  },

  async fetchSummaries(
    strategy?: string | null,
    sectorConfirmedOnly?: boolean | null,
    range?: OutcomeDateRange | null,
  ): Promise<SignalOutcomeSummary[]> {
    const data = await gql<{ signalOutcomeSummaries: SignalOutcomeSummary[] }>(
      `query (
        $strategy: String
        $sectorConfirmedOnly: Boolean
        $fromDate: LocalDate
        $toDate: LocalDate
      ) {
        signalOutcomeSummaries(
          strategy: $strategy
          sectorConfirmedOnly: $sectorConfirmedOnly
          fromDate: $fromDate
          toDate: $toDate
        ) {
          strategyFilter setups targetHits slHits timeStops openCount
          targetHitRatePct avgTargetHitPct avgRiskReward avgRMultiple
        }
      }`,
      {
        strategy: strategy || null,
        sectorConfirmedOnly: sectorConfirmedOnly ?? null,
        fromDate: range?.fromDate || null,
        toDate: range?.toDate || null,
      },
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
