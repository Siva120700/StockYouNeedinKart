import { fmt, sourceLabel, type AnalyzeStockResult } from "./types";

export type NextMoveSection = {
  title: string;
  body: string;
};

function sideWord(side: string | null | undefined): string {
  if (!side) return "either side";
  return side.toLowerCase() === "sell" ? "sell / bearish" : "buy / bullish";
}

function sideAction(side: string | null | undefined): string {
  if (!side) return "wait";
  return side.toLowerCase() === "sell" ? "sell" : "buy";
}

function spotVsPivot(result: AnalyzeStockResult): string | null {
  const spot = result.spotLtp;
  const pp = result.levels.pivot;
  if (spot == null || pp == null || !Number.isFinite(spot) || !Number.isFinite(pp)) return null;
  if (spot > pp * 1.002) return `Spot ${fmt(spot)} is above pivot ${fmt(pp)} — short-term bias leans bullish until pivot breaks.`;
  if (spot < pp * 0.998) return `Spot ${fmt(spot)} is below pivot ${fmt(pp)} — short-term bias leans bearish until pivot is reclaimed.`;
  return `Spot ${fmt(spot)} is hugging pivot ${fmt(pp)} — expect chop until a clean break.`;
}

function rrNote(rr: number | null | undefined): string | null {
  if (rr == null || !Number.isFinite(rr)) return null;
  if (rr < 1) return `Planned R:R is only ${fmt(rr)} — skip or wait for a better entry; risk is larger than reward to T1.`;
  if (rr < 1.5) return `Planned R:R is ${fmt(rr)} — acceptable only with strong confirmation (Trade Score Buy / Confluence + sector OK).`;
  return `Planned R:R is ${fmt(rr)} — reward justifies the risk if confirmation holds.`;
}

/**
 * Plain-language next-move narrative from live Analyze Stock fields.
 * Not advice — a structured reading of engines already computed.
 */
export function buildNextMoveSections(result: AnalyzeStockResult): NextMoveSection[] {
  const sections: NextMoveSection[] = [];
  const setup = result.primarySetup;
  const ts = result.tradeScore;
  const spot = result.spotLtp;
  const side = setup?.side ?? ts?.side ?? result.signal?.side ?? result.liquidityFresh?.side ?? null;

  // 1) Situation
  {
    const bits: string[] = [];
    bits.push(
      `${result.symbol} (${result.name}) is currently graded as “${result.verdictLabel}”` +
        (ts ? ` with Trade Score ${ts.confidenceScore}/100.` : "."),
    );
    if (result.sectorSymbol) {
      bits.push(
        result.sectorConfirmed === true
          ? `Sector ${result.sectorSymbol} is confirmed with the stock’s direction.`
          : result.sectorConfirmed === false
            ? `Sector ${result.sectorSymbol} is NOT confirmed — treat any setup as weaker.`
            : `Mapped sector is ${result.sectorSymbol}.`,
      );
    }
    const pivotLine = spotVsPivot(result);
    if (pivotLine) bits.push(pivotLine);
    if (result.levels.liquidityEvalDetail) bits.push(result.levels.liquidityEvalDetail);
    sections.push({ title: "What’s happening", body: bits.join(" ") });
  }

  // 2) Next move
  {
    const bits: string[] = [];
    const v = result.verdict;

    if (v === "strong_buy" || v === "buy") {
      bits.push(
        `Bias is actionable ${sideWord(side)}. Prefer taking the ${sideAction(side)} only if sector stays confirmed and planned R:R stays ≥ 1.5.`,
      );
      if (setup && setup.entry > 0) {
        bits.push(
          `Work from ${sourceLabel(setup.source)} levels: entry around ${fmt(setup.entry)}, stop ${fmt(setup.stopLoss)}, first target T1 ${fmt(setup.targetT1)}` +
            (setup.targetT2 != null ? `, then T2 ${fmt(setup.targetT2)}` : "") +
            ".",
        );
      }
      if (spot != null && setup && setup.entry > 0) {
        const sideBuy = setup.side.toLowerCase() === "buy";
        if (sideBuy && spot < setup.entry) {
          bits.push(`Spot ${fmt(spot)} is still below entry — wait for a reclaim of ${fmt(setup.entry)} rather than chasing.`);
        } else if (sideBuy && spot > setup.entry) {
          bits.push(`Spot ${fmt(spot)} is already above entry — only join if risk to SL still fits your size; otherwise leave it.`);
        } else if (!sideBuy && spot > setup.entry) {
          bits.push(`Spot ${fmt(spot)} is still above sell entry — wait for a break/hold below ${fmt(setup.entry)}.`);
        } else if (!sideBuy && spot < setup.entry) {
          bits.push(`Spot ${fmt(spot)} is already below sell entry — only join if stop distance is still acceptable.`);
        }
      }
      if (result.optionsIntraday?.contractTradingSymbol) {
        bits.push(
          `Options path available: ${result.optionsIntraday.contractOptionType ?? "option"} ${fmt(result.optionsIntraday.contractStrike)} (${result.optionsIntraday.contractTradingSymbol}) — still exit on stock SL/T1 and flat by ${result.optionsIntraday.flatByIst}.`,
        );
      } else {
        bits.push("No Options Intraday recommendation passed the hard gates right now — trade cash/futures levels only if you take it.");
      }
    } else if (v === "watch" || v === "unconfirmed" || v === "neutral") {
      bits.push(
        `Do not force a fresh trade yet. Treat this as a watchlist name: wait for Liquidity Fresh alignment and/or breakout confirmation on the same ${sideWord(side)} side.`,
      );
      if (setup && setup.entry > 0) {
        bits.push(
          `If price tags entry ${fmt(setup.entry)} with confirmation, the working plan would be SL ${fmt(setup.stopLoss)} and T1 ${fmt(setup.targetT1)}.`,
        );
      } else if (result.levels.nearestZonePrice != null) {
        bits.push(
          `Watch reaction at nearest liquidity zone ${result.levels.nearestZoneType ?? "zone"} @ ${fmt(result.levels.nearestZonePrice)}` +
            (result.levels.sweepSide
              ? ` (recent ${result.levels.sweepSide} sweep of ${result.levels.sweptZoneType ?? "zone"} @ ${fmt(result.levels.sweptZonePrice)}).`
              : "."),
        );
      } else {
        bits.push("Wait for a fresh daily signal or a completed 4H sweep + 1H confirm before sizing.");
      }
      bits.push("Skip Options until confidence clears the ≥75 gate with Confluence or supportive futures OI.");
    } else if (v === "avoid") {
      bits.push(
        "Engines disagree or quality is too low — stand aside. Do not fade randomly; wait for a clean new setup on a later session.",
      );
    } else {
      // no_setup
      bits.push(
        "No active daily setup right now. Next move is patience: mark pivot / nearest liquidity zones and wait for Signals or Liquidity Fresh to print with sector confirmation.",
      );
      if (result.levels.sweptZoneType) {
        bits.push(
          `A ${result.levels.sweepSide ?? ""} sweep of ${result.levels.sweptZoneType} @ ${fmt(result.levels.sweptZonePrice)} is on the board but not fully confirmed — if 1H confirm + RVOL appear, re-check Analyze Stock.`,
        );
      }
    }

    const rr = rrNote(setup?.plannedRiskReward ?? null);
    if (rr) bits.push(rr);

    sections.push({ title: "Suggested next move", body: bits.join(" ") });
  }

  // 3) Levels to watch
  {
    const bits: string[] = [];
    const L = result.levels;
    if (L.pivot != null) {
      bits.push(`Classic pivots: PP ${fmt(L.pivot)}, R1 ${fmt(L.resistance1)} / R2 ${fmt(L.resistance2)}, S1 ${fmt(L.support1)} / S2 ${fmt(L.support2)}.`);
    }
    if (L.nearestZoneType && L.nearestZonePrice != null) {
      bits.push(
        `Nearest liquidity: ${L.nearestZoneType} @ ${fmt(L.nearestZonePrice)}` +
          (L.distancePct != null ? ` (~${fmt(L.distancePct * 100)}% away).` : "."),
      );
    }
    if (L.breakoutLevel != null) {
      bits.push(`Breakout reference: ${L.breakoutPattern ?? "level"} @ ${fmt(L.breakoutLevel)}.`);
    }
    if (setup && setup.entry > 0) {
      bits.push(
        `Trade map: entry ${fmt(setup.entry)} → SL ${fmt(setup.stopLoss)} → T1 ${fmt(setup.targetT1)}` +
          (setup.targetT2 != null ? ` → T2 ${fmt(setup.targetT2)}` : "") +
          ".",
      );
    }
    if (bits.length === 0) {
      bits.push("Not enough structure yet — sync bars / run engines, then refresh this stock.");
    }
    sections.push({ title: "Levels to watch", body: bits.join(" ") });
  }

  // 4) Risks / invalidation
  {
    const bits: string[] = [];
    if (result.sectorConfirmed === false) {
      bits.push("Invalidation soft-flag: sector not confirmed — cut size or skip.");
    }
    if (ts && ts.liquidityScore === 0) {
      bits.push("No Liquidity Fresh alignment — higher fake-break risk.");
    }
    if (ts && ts.breakoutScore === 0) {
      bits.push("No confirmed pattern breakout — trend follow-through is unproven.");
    }
    if (setup && setup.entry > 0) {
      bits.push(
        `Hard invalidation: spot through stop ${fmt(setup.stopLoss)} closes the idea; do not average against the stop.`,
      );
    } else if (Lsupport(result)) {
      bits.push(`Soft invalidation: sustained trade beyond opposite pivot band without reclaim.`);
    }
    bits.push(
      "Same-stock flip rule: if an opposite open outcome exists within 2 days, do not take a new reverse trade until that outcome resolves.",
    );
    sections.push({ title: "Risks & invalidation", body: bits.join(" ") });
  }

  return sections;
}

function Lsupport(result: AnalyzeStockResult): boolean {
  return result.levels.pivot != null;
}
