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
  sectorConfirmed?: boolean;
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
    case "liquidity_v2":
      return "Liquidity V2";
    case "confluence":
      return "Confluence";
    case "trade_score":
      return "Trade Score";
    case "breakout":
      return "Breakout";
    case "momentum_v2":
      return "Momentum V2";
    case "momentum_v3":
      return "Momentum V3";
    case "options_intraday":
      return "Options Intraday";
    case "nifty_orb":
      return "Index Options (Nifty ORB)";
    case "nifty_orb_liq_v2":
      return "Index Options (ORB + Liq V2)";
    case "nifty_liq_breakout":
      return "Index Options (Liq + Breakout)";
    case "nifty_breakout_volume":
      return "Index Options (Breakout + Volume)";
    case "nifty_breakout_chain":
      return "Index Options (Breakout + Chain)";
    case "nifty_hero_zero":
      return "Index Options (Hero Zero)";
    default:
      return strategy ?? "All";
  }
}

export function resultLabel(result: string): string {
  switch (result) {
    case "target":
      return "Target";
    case "sl":
      return "SL";
    case "time_stop":
      return "Time stop";
    case "open":
      return "Open";
    default:
      return result;
  }
}

export function resultColor(
  result: string,
): "success" | "error" | "warning" | "default" | "info" {
  switch (result) {
    case "target":
      return "success";
    case "sl":
      return "error";
    case "time_stop":
      return "warning";
    case "open":
      return "info";
    default:
      return "default";
  }
}
