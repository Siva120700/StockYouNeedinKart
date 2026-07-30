import { gql } from "../../api/client";
import type { BreakoutConfirmation } from "./types";

export const BreakoutApi = {
  async fetchConfirmations(confirmedOnly = false): Promise<BreakoutConfirmation[]> {
    const data = await gql<{ breakoutConfirmations: BreakoutConfirmation[] }>(`
      {
        breakoutConfirmations {
          id runId instrumentId appSymbol instrumentName side asOfDate confirmed
          closePrice level20d volumeRatio patternType
        }
      }
    `);
    const rows = data.breakoutConfirmations;
    return confirmedOnly ? rows.filter((r) => r.confirmed) : rows;
  },

  async runAnalysis(): Promise<void> {
    await gql(`mutation { runBreakoutAnalysis { id status } }`);
  },
};
