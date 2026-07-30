export type SignalOutcome = {
  id: string;
  instrumentId: string;
  appSymbol: string;
  instrumentName: string;
  strategy: string;
  side: string;
  signalDate: string;
  entryPrice: number;
  initialStopLoss: number;
  targetT1: number | null;
  targetT2: number | null;
  targetT3: number | null;
  result: string;
  targetLevel: string | null;
  targetHitPct: number | null;
  exitPrice: number | null;
  exitDate: string | null;
  pnlPct: number | null;
  rMultiple: number | null;
};

export type SignalOutcomeSummary = {
  strategyFilter: string | null;
  setups: number;
  targetHits: number;
  slHits: number;
  timeStops: number;
  openCount: number;
  targetHitRatePct: number | null;
  avgTargetHitPct: number | null;
  avgRiskReward: number | null;
  avgRMultiple: number | null;
};

export function strategyLabel(strategy: string | null | undefined): string {
  switch (strategy) {
    case "signals":
      return "Signals";
    case "liquidity":
      return "Liquidity";
    case "liquidity_fresh":
      return "Liquidity Fresh";
    case "confluence":
      return "Confluence";
    case "trade_score":
      return "Trade Score";
    case "breakout":
      return "Breakout";
    default:
      return strategy ?? "All";
  }
}
