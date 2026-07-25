import type { AnalysisRun, LtpQuote, OpenPosition, Signal, User, WatchlistItem } from "./types";
import { gql } from "./client";

/** Factory methods — frontend only fetches / triggers; no market math here. */
export const DataFactory = {
  async me(): Promise<User | null> {
    const data = await gql<{ me: User | null }>(`{ me { id email displayName } }`);
    return data.me;
  },

  async ltp(): Promise<LtpQuote[]> {
    const data = await gql<{ ltp: LtpQuote[] }>(`
      { ltp { instrumentId appSymbol instrumentName exchange ltp fetchedAt } }
    `);
    return data.ltp;
  },

  async signals(runId?: string): Promise<Signal[]> {
    const data = await gql<{ signals: Signal[] }>(
      `query ($runId: UUID) {
        signals(runId: $runId) {
          id analysisRunId instrumentId appSymbol instrumentName side
          entryPrice initialStopLoss targetT1 targetT2 targetT3 volumeOk sectorConfirmed freshCross
        }
      }`,
      { runId: runId ?? null },
    );
    return data.signals;
  },

  async openPositions(): Promise<OpenPosition[]> {
    const data = await gql<{ openPositions: OpenPosition[] }>(`
      {
        openPositions {
          id symbol instrumentName side quantityLots entryPrice
          currentStopLoss lastPrice computedUnrealizedPnl
        }
      }
    `);
    return data.openPositions;
  },

  async watchlist(): Promise<WatchlistItem[]> {
    const data = await gql<{ watchlist: WatchlistItem[] }>(`
      { watchlist { instrumentId symbol name } }
    `);
    return data.watchlist;
  },
};

export const ActionFactory = {
  async runAnalysis(): Promise<AnalysisRun> {
    const data = await gql<{ runAnalysis: AnalysisRun }>(`
      mutation {
        runAnalysis(includeNifty50: true, includeNifty100: true, includeWatchlist: true, includeSectorCheck: false) {
          id status asOfDate
        }
      }
    `);
    return data.runAnalysis;
  },

  async openPositionFromSignal(signalId: string, quantityLots = 1): Promise<string> {
    const data = await gql<{ openPositionFromSignal: string }>(
      `mutation ($signalId: UUID!, $quantityLots: Int!) {
        openPositionFromSignal(signalId: $signalId, quantityLots: $quantityLots)
      }`,
      { signalId, quantityLots },
    );
    return data.openPositionFromSignal;
  },

  async closePosition(positionId: string, exitPrice: number): Promise<boolean> {
    const data = await gql<{ closePosition: boolean }>(
      `mutation ($positionId: UUID!, $exitPrice: Decimal!, $closeReason: String!) {
        closePosition(positionId: $positionId, exitPrice: $exitPrice, closeReason: $closeReason)
      }`,
      { positionId, exitPrice, closeReason: "manual" },
    );
    return data.closePosition;
  },

  async addToWatchlist(instrumentId: string): Promise<boolean> {
    const data = await gql<{ addToWatchlist: boolean }>(
      `mutation ($instrumentId: UUID!) { addToWatchlist(instrumentId: $instrumentId) }`,
      { instrumentId },
    );
    return data.addToWatchlist;
  },

  /** Pulls fresh quotes from Angel into market_ltp; returns how many rows updated. */
  async refreshLtp(): Promise<number> {
    const data = await gql<{ refreshLtp: number }>(`mutation { refreshLtp }`);
    return data.refreshLtp;
  },
};
