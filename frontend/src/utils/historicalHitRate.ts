import { DataFactory } from "../api/factories";
import { OutcomesApi } from "../modules/outcomes/api";
import { columnFactories } from "../zen_components/table/columnFactories";
import type { TextColumnConfig } from "../zen_components/table/columnTypes";

/** Strategy keys used by backtest / accuracy (snake_case). */
export type ScreenerStrategy =
  | "signals"
  | "liquidity"
  | "liquidity_fresh"
  | "liquidity_v2"
  | "confluence"
  | "breakout"
  | "trade_score"
  | "options_intraday"
  | "nifty_orb"
  | "nifty_orb_liq_v2"
  | "nifty_liq_breakout"
  | "nifty_breakout_volume"
  | "nifty_breakout_chain"
  | "nifty_momentum_v2"
  | "nifty_hero_zero";

export type HitRateByInstrument = Map<string, number | null>;

/**
 * Historical hit rate % by instrument for a strategy.
 * Prefers 1Y backtest summaries; fills gaps from Accuracy (signal_outcomes)
 * so Options (no auto-backtest) still shows rates when outcomes exist.
 */
export async function loadHistoricalHitRates(
  strategy: ScreenerStrategy,
): Promise<HitRateByInstrument> {
  const map: HitRateByInstrument = new Map();

  const [summaries, outcomes] = await Promise.all([
    DataFactory.backtestSummaries(strategy).catch(() => []),
    OutcomesApi.fetchOutcomes(strategy).catch(() => []),
  ]);

  for (const s of summaries) {
    if (!s.instrumentId) continue;
    const pct =
      s.targetHitRatePct != null && Number.isFinite(Number(s.targetHitRatePct))
        ? Number(s.targetHitRatePct)
        : null;
    map.set(s.instrumentId, pct);
  }

  // Accuracy fallback / fill when backtest has no row for that symbol.
  const byInst = new Map<string, { target: number; sl: number }>();
  for (const o of outcomes) {
    if (!o.instrumentId) continue;
    let bucket = byInst.get(o.instrumentId);
    if (!bucket) {
      bucket = { target: 0, sl: 0 };
      byInst.set(o.instrumentId, bucket);
    }
    if (o.result === "target") bucket.target += 1;
    else if (o.result === "sl") bucket.sl += 1;
  }
  for (const [id, bucket] of byInst) {
    if (map.has(id)) continue;
    const closed = bucket.target + bucket.sl;
    map.set(id, closed === 0 ? null : Math.round((1000 * bucket.target) / closed) / 10);
  }

  return map;
}

export function formatHitRatePct(pct: number | null | undefined): string {
  if (pct == null || !Number.isFinite(Number(pct))) return "—";
  return `${Number(pct).toFixed(1)}%`;
}

/** ZenTable column: Historical hit rate from map keyed by instrumentId. */
export function createHistoricalHitRateColumn<T>(
  hitRates: HitRateByInstrument,
  getInstrumentId: (row: T) => string,
): TextColumnConfig<T> {
  return columnFactories.createTextColumn<T>({
    field: "historicalHitRatePct",
    headerName: "Hit %",
    width: 90,
    getValue: (r) => {
      const pct = hitRates.get(getInstrumentId(r));
      return pct != null && Number.isFinite(pct) ? pct : null;
    },
    displayRenderer: (v) => formatHitRatePct(v as number | null),
  });
}
