import type {
  AnalysisRun,
  BacktestNote,
  BacktestNoteInput,
  BacktestSymbolSummary,
  ConfluenceSignal,
  LiquiditySignal,
  LtpQuote,
  OpenPosition,
  Signal,
  UniverseInstrument,
  User,
  WatchlistItem,
} from "./types";
import { gql } from "./client";

const BACKTEST_NOTE_FIELDS = `
  id instrumentId appSymbol instrumentName strategy side signalDate
  entryPrice initialStopLoss targetT1 targetT2 targetT3
  result targetLevel targetHitPct exitPrice exitDate pnlPct rMultiple notes wouldTakeLive source
`;

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

  async universes(): Promise<UniverseInstrument[]> {
    const data = await gql<{ universes: UniverseInstrument[] }>(`
      { universes { id symbol name } }
    `);
    return data.universes;
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

  async liquiditySignals(runId?: string, ruleset: "classic" | "fresh" = "classic"): Promise<LiquiditySignal[]> {
    const data = await gql<{ liquiditySignals: LiquiditySignal[] }>(
      `query ($runId: UUID, $ruleset: String) {
        liquiditySignals(runId: $runId, ruleset: $ruleset) {
          id liquidityRunId instrumentId appSymbol instrumentName side
          entryPrice initialStopLoss targetT1 targetT2 targetT3
          relativeVolume rvolPercentile rvolOk strongClose sectorConfirmed
          sweepSide sweptZoneType sweptZonePrice
          nearestZoneType nearestZonePrice distancePct timeframeContext
        }
      }`,
      { runId: runId ?? null, ruleset },
    );
    return data.liquiditySignals;
  },

  async confluenceSignals(): Promise<ConfluenceSignal[]> {
    const data = await gql<{ confluenceSignals: ConfluenceSignal[] }>(`
      {
        confluenceSignals {
          id instrumentId appSymbol instrumentName side asOfDate
          entryPrice initialStopLoss targetT1 targetT2 targetT3
          analysisSignalId liquiditySignalId
          signalsEntry liquidityEntry signalsStopLoss liquidityStopLoss
          sectorConfirmed freshCross relativeVolume rvolPercentile strongClose
          sweptZoneType timeframeContext
        }
      }
    `);
    return data.confluenceSignals;
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

  async backtestNotes(instrumentId?: string, strategy?: string | null): Promise<BacktestNote[]> {
    const data = await gql<{ backtestNotes: BacktestNote[] }>(
      `query ($instrumentId: UUID, $strategy: String) {
        backtestNotes(instrumentId: $instrumentId, strategy: $strategy) {
          ${BACKTEST_NOTE_FIELDS}
        }
      }`,
      { instrumentId: instrumentId ?? null, strategy: strategy ?? null },
    );
    return data.backtestNotes;
  },

  async backtestSummary(
    instrumentId: string,
    strategy?: string | null,
    minRiskReward?: number | null,
  ): Promise<BacktestSymbolSummary> {
    const data = await gql<{ backtestSummary: BacktestSymbolSummary }>(
      `query ($instrumentId: UUID!, $strategy: String, $minRiskReward: Float) {
        backtestSummary(instrumentId: $instrumentId, strategy: $strategy, minRiskReward: $minRiskReward) {
          instrumentId appSymbol instrumentName strategyFilter
          timesInStrategy targetHits slHits skipped openCount
          targetHitRatePct avgTargetHitPct avgRiskReward avgRMultiple
        }
      }`,
      {
        instrumentId,
        strategy: strategy ?? null,
        minRiskReward: minRiskReward ?? null,
      },
    );
    return data.backtestSummary;
  },

  async backtestSummaries(
    strategy?: string | null,
    minRiskReward?: number | null,
  ): Promise<BacktestSymbolSummary[]> {
    const data = await gql<{ backtestSummaries: BacktestSymbolSummary[] }>(
      `query ($strategy: String, $minRiskReward: Float) {
        backtestSummaries(strategy: $strategy, minRiskReward: $minRiskReward) {
          instrumentId appSymbol instrumentName strategyFilter
          timesInStrategy targetHits slHits skipped openCount
          targetHitRatePct avgTargetHitPct avgRiskReward avgRMultiple
        }
      }`,
      { strategy: strategy ?? null, minRiskReward: minRiskReward ?? null },
    );
    return data.backtestSummaries;
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

  async runLiquidityAnalysis(ruleset: "classic" | "fresh" = "classic"): Promise<AnalysisRun> {
    const data = await gql<{ runLiquidityAnalysis: AnalysisRun }>(
      `mutation ($ruleset: String) {
        runLiquidityAnalysis(includeNifty50: true, includeNifty100: true, includeWatchlist: true, ruleset: $ruleset) {
          id status asOfDate
        }
      }`,
      { ruleset },
    );
    return data.runLiquidityAnalysis;
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

  async openPositionFromLiquiditySignal(signalId: string, quantityLots = 1): Promise<string> {
    const data = await gql<{ openPositionFromLiquiditySignal: string }>(
      `mutation ($signalId: UUID!, $quantityLots: Int!) {
        openPositionFromLiquiditySignal(signalId: $signalId, quantityLots: $quantityLots)
      }`,
      { signalId, quantityLots },
    );
    return data.openPositionFromLiquiditySignal;
  },

  async openPositionFromConfluence(
    liquiditySignalId: string,
    analysisSignalId: string,
    quantityLots = 1,
  ): Promise<string> {
    const data = await gql<{ openPositionFromConfluence: string }>(
      `mutation ($liquiditySignalId: UUID!, $analysisSignalId: UUID!, $quantityLots: Int!) {
        openPositionFromConfluence(
          liquiditySignalId: $liquiditySignalId
          analysisSignalId: $analysisSignalId
          quantityLots: $quantityLots
        )
      }`,
      { liquiditySignalId, analysisSignalId, quantityLots },
    );
    return data.openPositionFromConfluence;
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

  async upsertBacktestNote(input: BacktestNoteInput): Promise<BacktestNote> {
    const data = await gql<{ upsertBacktestNote: BacktestNote }>(
      `mutation ($input: BacktestNoteInput!) {
        upsertBacktestNote(input: $input) {
          ${BACKTEST_NOTE_FIELDS}
        }
      }`,
      { input },
    );
    return data.upsertBacktestNote;
  },

  async deleteBacktestNote(noteId: string): Promise<boolean> {
    const data = await gql<{ deleteBacktestNote: boolean }>(
      `mutation ($noteId: UUID!) { deleteBacktestNote(noteId: $noteId) }`,
      { noteId },
    );
    return data.deleteBacktestNote;
  },

  async runHistoricalBacktest(
    instrumentId: string,
    strategy: string,
  ): Promise<BacktestSymbolSummary> {
    const data = await gql<{ runHistoricalBacktest: BacktestSymbolSummary }>(
      `mutation ($instrumentId: UUID!, $strategy: String!) {
        runHistoricalBacktest(instrumentId: $instrumentId, strategy: $strategy) {
          instrumentId appSymbol instrumentName strategyFilter
          timesInStrategy targetHits slHits skipped openCount
          targetHitRatePct avgTargetHitPct avgRiskReward avgRMultiple
        }
      }`,
      { instrumentId, strategy },
    );
    return data.runHistoricalBacktest;
  },
};
