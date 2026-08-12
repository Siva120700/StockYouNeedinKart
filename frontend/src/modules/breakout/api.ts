import { gql } from "../../api/client";
import type { BreakoutConfirmation } from "./types";
import { SECTOR_RS_GQL } from "../../utils/sectorRelativeStrength.tsx";

export const BreakoutApi = {
  async fetchConfirmations(confirmedOnly = false): Promise<BreakoutConfirmation[]> {
    const data = await gql<{ breakoutConfirmations: BreakoutConfirmation[] }>(`
      {
        breakoutConfirmations {
          id runId instrumentId appSymbol instrumentName side asOfDate confirmed
          closePrice level20d volumeRatio patternType
          ${SECTOR_RS_GQL}
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
