import { gql } from "../../api/client";

export type SectorScopeStock = {
  instrumentId: string;
  appSymbol: string;
  instrumentName: string;
  changePct: number;
  ltp: number | null;
};

export type SectorScopeSector = {
  instrumentId: string;
  symbol: string;
  name: string;
  displayName: string;
  medianChangePct: number;
  rank: number;
  lagging: boolean;
  constituentCount: number;
  stocks: SectorScopeStock[];
};

export type SectorScopeSnapshot = {
  asOf: string;
  niftyChangePct: number | null;
  sectors: SectorScopeSector[];
};

export const SectorScopeApi = {
  async fetch(): Promise<SectorScopeSnapshot> {
    const data = await gql<{ sectorScope: SectorScopeSnapshot }>(`
      {
        sectorScope {
          asOf
          niftyChangePct
          sectors {
            instrumentId symbol name displayName
            medianChangePct rank lagging constituentCount
            stocks { instrumentId appSymbol instrumentName changePct ltp }
          }
        }
      }
    `);
    return data.sectorScope;
  },
};
