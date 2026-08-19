import { gql } from "../../api/client";

export type SectorRotationStock = {
  instrumentId: string;
  symbol: string;
  name: string;
  momentumScore: number;
  alignment: string;
  changePct: number;
  return5dPct: number;
  flowCr: number;
  sectorInstrumentId?: string;
  sectorSymbol?: string;
  sectorScore?: number;
  sectorBucket?: string;
};

export type SectorRotationSector = {
  sectorInstrumentId: string;
  symbol: string;
  displayName: string;
  bucket: string;
  score: number;
  rank: number;
  flowZScore: number;
  flowAccelerationPct: number;
  relativeStrength5dPct: number;
  breadthPct: number;
  trendScore: number;
  volumeExpansionPct: number;
  todayFlowCr: number;
  constituentCount: number;
  upcomingMomentumScore: number;
  upcomingMomentumReasons: string[];
  topStocks: SectorRotationStock[];
};

export type MarketRegime = {
  label: string;
  niftyChangePct: number | null;
  niftyReturn5dPct: number | null;
  niftyAboveEma20: boolean;
  marketBreadthPct: number;
  advancers: number;
  decliners: number;
  reasons: string[];
};

export type SectorRotationSnapshot = {
  asOf: string;
  regime: MarketRegime;
  sectors: SectorRotationSector[];
  capitalEntering: SectorRotationSector[];
  leading: SectorRotationSector[];
  neutral: SectorRotationSector[];
  capitalLeaving: SectorRotationSector[];
  momentumBuilding: SectorRotationSector[];
  allStocks: SectorRotationStock[];
};

const SECTOR_FIELDS = `
  sectorInstrumentId symbol displayName bucket score rank
  flowZScore flowAccelerationPct relativeStrength5dPct breadthPct
  trendScore volumeExpansionPct todayFlowCr constituentCount
  upcomingMomentumScore upcomingMomentumReasons
  topStocks {
    instrumentId symbol name momentumScore alignment
    changePct return5dPct flowCr sectorScore sectorBucket
  }
`;

export const SectorRotationApi = {
  async fetch(): Promise<SectorRotationSnapshot> {
    const data = await gql<{ sectorRotation: SectorRotationSnapshot }>(`
      query {
        sectorRotation {
          asOf
          regime {
            label niftyChangePct niftyReturn5dPct niftyAboveEma20
            marketBreadthPct advancers decliners reasons
          }
          sectors { ${SECTOR_FIELDS} }
          capitalEntering { ${SECTOR_FIELDS} }
          leading { ${SECTOR_FIELDS} }
          neutral { ${SECTOR_FIELDS} }
          capitalLeaving { ${SECTOR_FIELDS} }
          momentumBuilding { ${SECTOR_FIELDS} }
          allStocks {
            instrumentId symbol name momentumScore alignment
            changePct return5dPct flowCr sectorSymbol sectorScore sectorBucket
          }
        }
      }
    `);
    return data.sectorRotation;
  },
};
