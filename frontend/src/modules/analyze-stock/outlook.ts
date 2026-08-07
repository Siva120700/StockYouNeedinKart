import { fmt, sourceLabel, type AnalyzeStockResult } from "./types";

export type UserBias = "bullish" | "bearish" | "neutral" | "unclear";
export type SystemBias = "bullish" | "bearish" | "neutral";
export type BiasStrength = "strong" | "moderate" | "weak" | "none";
export type OutlookLean = "favoured" | "possible" | "unlikely";
export type ViewHorizon = "intraday" | "swing" | "positional";

export type UserViewRead = {
  bias: UserBias;
  strength: BiasStrength;
  /** Phrases picked up from the note, negation-aware. */
  cues: string[];
  horizon: ViewHorizon | null;
};

export type OutlookSection = {
  title: string;
  summary: string;
  bullets: string[];
  lean: OutlookLean | null;
  /** null when the note gave no readable direction. */
  matchesUserView: boolean | null;
};

type WeightedTerm = { re: RegExp; w: number };

const BULL_TERMS: WeightedTerm[] = [
  { re: /\b(moon|rocket|multibagger|skyrocket)\b/, w: 3 },
  { re: /\b(bullish|bull\s*run|rally|rallying|surge|breakout)\b/, w: 3 },
  { re: /\b(go(ing)?\s*up|goes\s*up|move\s*up|will\s*rise|rising|upside|up\s*move)\b/, w: 3 },
  { re: /\b(buy|buying|bought|long|accumulate|accumulating|accumulation)\b/, w: 2 },
  { re: /\b(bounce|recover|recovery|reversal\s*up|reclaim)\b/, w: 2 },
  { re: /\b(strong|strength|positive|good|healthy|support\s*holding|oversold)\b/, w: 1 },
  { re: /\b(higher|uptrend|upward)\b/, w: 1 },
];

const BEAR_TERMS: WeightedTerm[] = [
  { re: /\b(crash|collapse|tank|plunge|dump|nosedive)\b/, w: 3 },
  { re: /\b(bearish|bear\s*market|sell\s*off|selloff|breakdown)\b/, w: 3 },
  { re: /\b(go(ing)?\s*down|goes\s*down|move\s*down|will\s*fall|falling|downside|down\s*move)\b/, w: 3 },
  { re: /\b(sell|selling|sold|short|shorting|exit|book\s*profit|distribution)\b/, w: 2 },
  { re: /\b(fall|falls|drop|drops|dip|decline|correction|pullback|retrace)\b/, w: 2 },
  { re: /\b(weak|weakness|negative|bad|risky|overbought|extended|stretched|expensive)\b/, w: 1 },
  { re: /\b(lower|downtrend|downward)\b/, w: 1 },
];

const NEUTRAL_TERMS: WeightedTerm[] = [
  { re: /\b(sideways|chop|choppy|range\s*bound|rangebound|ranging|flat)\b/, w: 3 },
  { re: /\b(consolidat\w*|no\s*trend|directionless)\b/, w: 2 },
  { re: /\b(confused|unsure|uncertain|not\s*sure|no\s*idea|neutral|mixed)\b/, w: 2 },
  { re: /\b(wait|waiting|watch|watching|hold|holding)\b/, w: 1 },
];

/**
 * A negator a few words before a term flips its sign ("I don't think it will go up").
 * Only letters/spaces may sit between, so punctuation ends the negation scope —
 * "not sure, this will go up" keeps the second clause positive.
 */
const NEGATOR_TAIL =
  /\b(not|no|never|dont|don't|doesnt|doesn't|didnt|didn't|wont|won't|cant|can't|cannot|isnt|isn't|arent|aren't|unlikely|hardly|barely|avoid|stop)\b[\s\w']{0,24}$/i;

const HORIZON_PATTERNS: { re: RegExp; horizon: ViewHorizon }[] = [
  { re: /\b(intraday|today|now|scalp|this\s*session|by\s*close)\b/i, horizon: "intraday" },
  { re: /\b(swing|this\s*week|next\s*week|few\s*days|couple\s*of\s*days|short\s*term)\b/i, horizon: "swing" },
  { re: /\b(positional|long\s*term|invest|investment|months?|years?|hold\s*for)\b/i, horizon: "positional" },
];

function isNegated(text: string, matchIndex: number): boolean {
  const before = text.slice(Math.max(0, matchIndex - 40), matchIndex);
  return NEGATOR_TAIL.test(before);
}

function scoreTerms(text: string, terms: WeightedTerm[]): { score: number; cues: string[] } {
  let score = 0;
  const cues: string[] = [];
  for (const term of terms) {
    const re = new RegExp(term.re.source, "gi");
    let match: RegExpExecArray | null;
    while ((match = re.exec(text)) !== null) {
      if (match[0].length === 0) break;
      const negated = isNegated(text, match.index);
      score += negated ? -term.w : term.w;
      cues.push(`${negated ? "not " : ""}${match[0].toLowerCase()}`);
    }
  }
  return { score, cues };
}

function strengthOf(score: number): BiasStrength {
  const n = Math.abs(score);
  if (n >= 5) return "strong";
  if (n >= 3) return "moderate";
  if (n >= 1) return "weak";
  return "none";
}

function detectHorizon(text: string): ViewHorizon | null {
  for (const { re, horizon } of HORIZON_PATTERNS) {
    if (re.test(text)) return horizon;
  }
  return null;
}

/**
 * Read free-text user feelings into a direction, conviction and horizon.
 * Negation-aware so "I don't think it goes up" reads bearish, not bullish.
 */
export function readUserView(note: string | null | undefined): UserViewRead {
  const text = (note ?? "").trim();
  if (!text) return { bias: "unclear", strength: "none", cues: [], horizon: null };

  const bull = scoreTerms(text, BULL_TERMS);
  const bear = scoreTerms(text, BEAR_TERMS);
  const neutral = scoreTerms(text, NEUTRAL_TERMS);
  const net = bull.score - bear.score;
  const horizon = detectHorizon(text);
  const cues = [...bull.cues, ...bear.cues, ...neutral.cues].slice(0, 6);

  if (neutral.score >= 2 && Math.abs(net) <= 2) {
    return { bias: "neutral", strength: strengthOf(neutral.score), cues, horizon };
  }
  if (net === 0) {
    if (neutral.score > 0) return { bias: "neutral", strength: strengthOf(neutral.score), cues, horizon };
    return { bias: "unclear", strength: "none", cues, horizon };
  }
  return { bias: net > 0 ? "bullish" : "bearish", strength: strengthOf(net), cues, horizon };
}

/** Coarse direction only — kept for callers that just need the tag. */
export function inferUserBias(note: string | null | undefined): UserBias {
  return readUserView(note).bias;
}

export function inferSystemBias(result: AnalyzeStockResult): SystemBias {
  const v = result.verdict;
  if (v === "strong_buy" || v === "buy") return "bullish";

  const side = (
    result.primarySetup?.side ??
    result.tradeScore?.side ??
    result.signal?.side ??
    result.liquidityFresh?.side ??
    result.confluence?.side ??
    ""
  ).toLowerCase();

  if (side === "sell") return "bearish";
  if (side === "buy") return v === "avoid" ? "neutral" : "bullish";

  const spot = result.spotLtp;
  const pp = result.levels.pivot;
  if (spot != null && pp != null && Number.isFinite(spot) && Number.isFinite(pp)) {
    if (spot > pp * 1.002) return "bullish";
    if (spot < pp * 0.998) return "bearish";
  }
  return "neutral";
}

type BarStats = {
  sample: number;
  /** Average (high-low)/close as a percent — how much this stock usually travels in a day. */
  typicalRangePct: number | null;
  atr: number | null;
  upDays: number;
  downDays: number;
  windowChangePct: number | null;
  volumeVsAvg: number | null;
};

const EMPTY_STATS: BarStats = {
  sample: 0,
  typicalRangePct: null,
  atr: null,
  upDays: 0,
  downDays: 0,
  windowChangePct: null,
  volumeVsAvg: null,
};

/** Recent-bar context. Bars arrive newest-first or oldest-first depending on run, so sort defensively. */
function computeBarStats(result: AnalyzeStockResult): BarStats {
  const seen = new Set<string>();
  const bars = [...(result.recentBars ?? [])]
    .filter((b) => {
      if (seen.has(b.tradeDate)) return false;
      seen.add(b.tradeDate);
      return Number.isFinite(b.close) && b.close > 0;
    })
    .sort((a, b) => (a.tradeDate < b.tradeDate ? 1 : -1))
    .slice(0, 10);

  if (bars.length === 0) return EMPTY_STATS;

  const rangePcts: number[] = [];
  const trueRanges: number[] = [];
  let upDays = 0;
  let downDays = 0;

  bars.forEach((bar, i) => {
    rangePcts.push(((bar.high - bar.low) / bar.close) * 100);
    const prevClose = bars[i + 1]?.close;
    trueRanges.push(
      prevClose != null
        ? Math.max(bar.high - bar.low, Math.abs(bar.high - prevClose), Math.abs(bar.low - prevClose))
        : bar.high - bar.low,
    );
    if (bar.close > bar.open) upDays++;
    else if (bar.close < bar.open) downDays++;
  });

  const avg = (xs: number[]) => xs.reduce((a, b) => a + b, 0) / xs.length;
  const oldestClose = bars[bars.length - 1].close;
  const latest = bars[0];
  const avgVolume = avg(bars.map((b) => b.volume));

  return {
    sample: bars.length,
    typicalRangePct: avg(rangePcts),
    atr: avg(trueRanges),
    upDays,
    downDays,
    windowChangePct: oldestClose > 0 ? ((latest.close - oldestClose) / oldestClose) * 100 : null,
    volumeVsAvg: avgVolume > 0 ? latest.volume / avgVolume : null,
  };
}

/** "(1.8% above, ~1.2 typical sessions)" — distance plus how long that move usually takes. */
function distanceNote(spot: number | null, level: number | null | undefined, stats: BarStats): string {
  if (spot == null || level == null || !Number.isFinite(spot) || !Number.isFinite(level) || spot <= 0) {
    return "";
  }
  const pct = ((level - spot) / spot) * 100;
  const abs = Math.abs(pct);
  if (abs < 0.05) return " (at spot)";

  let note = `${fmt(abs)}% ${pct > 0 ? "above" : "below"} spot`;
  if (stats.typicalRangePct != null && stats.typicalRangePct > 0.1) {
    const sessions = fmt(abs / stats.typicalRangePct, 1);
    note += `, ~${sessions} typical session${sessions === "1.0" ? "" : "s"}`;
  }
  return ` (${note})`;
}

function biasLabel(bias: UserBias | SystemBias): string {
  switch (bias) {
    case "bullish":
      return "bullish";
    case "bearish":
      return "bearish";
    case "neutral":
      return "neutral / sideways";
    default:
      return "unclear";
  }
}

function strengthWord(strength: BiasStrength): string {
  switch (strength) {
    case "strong":
      return "strong";
    case "moderate":
      return "moderate";
    case "weak":
      return "mild";
    default:
      return "";
  }
}

function horizonWord(horizon: ViewHorizon | null): string {
  switch (horizon) {
    case "intraday":
      return "intraday";
    case "swing":
      return "swing (days)";
    case "positional":
      return "positional (weeks+)";
    default:
      return "";
  }
}

function alignmentSummary(view: UserViewRead, system: SystemBias): string {
  if (view.bias === "unclear") {
    return `No clear direction read from your note, so the paths below come from levels and engines alone. System bias is ${biasLabel(system)}.`;
  }
  if (view.bias === "neutral") {
    return `Your note reads sideways / undecided while the system leans ${biasLabel(system)}. The base path carries most weight until price closes outside its range.`;
  }
  if (view.bias === system) {
    return `Your ${strengthWord(view.strength)} ${biasLabel(view.bias)} view agrees with the system. Agreement raises confidence but not certainty — the invalidation level still decides.`;
  }
  return `Your ${strengthWord(view.strength)} ${biasLabel(view.bias)} view goes against the system read of ${biasLabel(system)}. Treat the opposite path as the risk case and wait for a confirming break before acting on the note.`;
}

function pathLean(direction: "up" | "down", result: AnalyzeStockResult, system: SystemBias): OutlookLean {
  const spot = result.spotLtp;
  const L = result.levels;
  let score = 0;

  if (direction === "up") {
    if (system === "bullish") score += 2;
    if (system === "bearish") score -= 2;
  } else {
    if (system === "bearish") score += 2;
    if (system === "bullish") score -= 2;
  }

  if (spot != null && L.pivot != null) {
    const above = spot > L.pivot;
    score += (direction === "up" ? above : !above) ? 1 : -1;
  }
  if (spot != null && L.ma5d != null) {
    const above = spot > L.ma5d;
    score += (direction === "up" ? above : !above) ? 1 : -1;
  }

  const setupSide = result.primarySetup?.side.toLowerCase();
  if (setupSide) {
    const matches = direction === "up" ? setupSide === "buy" : setupSide === "sell";
    score += matches ? 1 : -1;
  }

  if (score >= 3) return "favoured";
  if (score <= -2) return "unlikely";
  return "possible";
}

function basePathLean(result: AnalyzeStockResult): OutlookLean {
  const spot = result.spotLtp;
  const L = result.levels;
  const inRange =
    spot != null && L.last2dHigh != null && L.last2dLow != null && spot <= L.last2dHigh && spot >= L.last2dLow;
  if (inRange && !result.primarySetup) return "favoured";
  if (inRange) return "possible";
  return result.primarySetup ? "unlikely" : "possible";
}

function matchesView(direction: "up" | "down" | "flat", view: UserViewRead): boolean | null {
  if (view.bias === "unclear") return null;
  if (direction === "flat") return view.bias === "neutral";
  return view.bias === (direction === "up" ? "bullish" : "bearish");
}

function currentSituation(result: AnalyzeStockResult, stats: BarStats): OutlookSection {
  const bullets: string[] = [];
  const spot = result.spotLtp;
  const L = result.levels;

  if (stats.sample >= 2 && stats.windowChangePct != null) {
    bullets.push(
      `Last ${stats.sample} sessions: ${stats.windowChangePct >= 0 ? "+" : ""}${fmt(stats.windowChangePct)}% net, ${stats.upDays} up / ${stats.downDays} down closes.`,
    );
  }
  if (stats.typicalRangePct != null) {
    bullets.push(
      `Typical daily travel is about ${fmt(stats.typicalRangePct)}%${stats.atr != null ? ` (ATR ≈ ${fmt(stats.atr)})` : ""} — use it to judge whether a level is realistically reachable in a session.`,
    );
  }
  if (stats.volumeVsAvg != null) {
    const v = stats.volumeVsAvg;
    bullets.push(
      v >= 1.3
        ? `Latest volume is ${fmt(v, 1)}x the recent average — moves have participation behind them.`
        : v <= 0.7
          ? `Latest volume is only ${fmt(v, 1)}x the recent average — thin tape, breaks fail more often.`
          : `Volume is near average (${fmt(v, 1)}x) — no unusual participation.`,
    );
  }
  if (spot != null && L.pivot != null) {
    const d = ((spot - L.pivot) / L.pivot) * 100;
    bullets.push(
      `Spot ${fmt(spot)} sits ${d >= 0 ? "+" : ""}${fmt(d)}% versus pivot ${fmt(L.pivot)} — ${d >= 0 ? "buyers hold the mid-line" : "sellers hold the mid-line"}.`,
    );
  }
  if (spot != null && L.ma5d != null) {
    bullets.push(
      `Spot is ${spot >= L.ma5d ? "above" : "below"} the 5-day mean ${fmt(L.ma5d)} — short-term trend ${spot >= L.ma5d ? "up" : "down"}.`,
    );
  }
  if (result.sectorSymbol) {
    bullets.push(
      result.sectorConfirmed === true
        ? `Sector ${result.sectorSymbol} is moving with the stock, which supports follow-through.`
        : result.sectorConfirmed === false
          ? `Sector ${result.sectorSymbol} is not confirming — single-stock moves fade more easily.`
          : `Mapped sector is ${result.sectorSymbol}.`,
    );
  }
  if (L.liquidityEvalDetail) bullets.push(L.liquidityEvalDetail);
  if (bullets.length === 0) {
    bullets.push("Not enough recent bars to describe the current move — sync market data and retry.");
  }

  return {
    title: "Where it stands now",
    summary: `${result.symbol} is graded “${result.verdictLabel}”${result.tradeScore ? ` with Trade Score ${result.tradeScore.confidenceScore}/100` : ""}.`,
    bullets,
    lean: null,
    matchesUserView: null,
  };
}

function upsidePath(result: AnalyzeStockResult, stats: BarStats, view: UserViewRead, system: SystemBias): OutlookSection {
  const bullets: string[] = [];
  const setup = result.primarySetup;
  const L = result.levels;
  const spot = result.spotLtp;

  if (setup?.side.toLowerCase() === "buy" && setup.targetT1 != null) {
    bullets.push(
      `${sourceLabel(setup.source)} targets T1 ${fmt(setup.targetT1)}${distanceNote(spot, setup.targetT1, stats)}${setup.targetT2 != null ? `, then T2 ${fmt(setup.targetT2)}${distanceNote(spot, setup.targetT2, stats)}` : ""}.`,
    );
  } else if (setup?.side.toLowerCase() === "sell" && setup.entry > 0) {
    bullets.push(
      `A short-covering bounce first meets the sell entry zone ${fmt(setup.entry)}${distanceNote(spot, setup.entry, stats)}.`,
    );
  }
  if (L.last2dHigh != null) {
    bullets.push(
      `Confirmation: accept above the last-2-session high ${fmt(L.last2dHigh)}${distanceNote(spot, L.last2dHigh, stats)}.`,
    );
  }
  if (L.resistance1 != null) {
    bullets.push(
      `Pivot resistance R1 ${fmt(L.resistance1)}${distanceNote(spot, L.resistance1, stats)}${L.resistance2 != null ? `, R2 ${fmt(L.resistance2)}${distanceNote(spot, L.resistance2, stats)}` : ""}.`,
    );
  }
  if (L.nearestZoneType && L.nearestZonePrice != null && /supply|resist|high/i.test(L.nearestZoneType)) {
    bullets.push(
      `Liquidity overhead: ${L.nearestZoneType} @ ${fmt(L.nearestZonePrice)}${distanceNote(spot, L.nearestZonePrice, stats)} — expect first reaction there.`,
    );
  }
  if (L.breakoutLevel != null) {
    bullets.push(
      `Breakout reference ${L.breakoutPattern ?? "level"} @ ${fmt(L.breakoutLevel)}${distanceNote(spot, L.breakoutLevel, stats)}.`,
    );
  }
  if (bullets.length === 0) {
    bullets.push("No mapped resistance yet — mark the prior session high once bars refresh.");
  }

  const lean = pathLean("up", result, system);
  return {
    title: "If it goes up",
    summary:
      lean === "favoured"
        ? "Structure currently supports this path."
        : lean === "unlikely"
          ? "Possible, but structure argues against it right now."
          : "A realistic path if the triggers below clear.",
    bullets,
    lean,
    matchesUserView: matchesView("up", view),
  };
}

function basePath(result: AnalyzeStockResult, stats: BarStats, view: UserViewRead): OutlookSection {
  const bullets: string[] = [];
  const L = result.levels;
  const spot = result.spotLtp;

  if (L.last2dHigh != null && L.last2dLow != null) {
    bullets.push(
      `Range box ${fmt(L.last2dLow)} – ${fmt(L.last2dHigh)} holds until a session closes outside it.`,
    );
  } else if (L.support1 != null && L.resistance1 != null) {
    bullets.push(`Pivot band S1 ${fmt(L.support1)} to R1 ${fmt(L.resistance1)} contains the chop.`);
  }
  if (L.pivot != null) {
    bullets.push(`Pivot ${fmt(L.pivot)}${distanceNote(spot, L.pivot, stats)} is the fair-value mid-line.`);
  }
  if (L.nearestZoneType && L.nearestZonePrice != null) {
    bullets.push(
      `Nearest liquidity ${L.nearestZoneType} @ ${fmt(L.nearestZonePrice)}${distanceNote(spot, L.nearestZonePrice, stats)} can pin price while it ranges.`,
    );
  }
  if (stats.typicalRangePct != null) {
    bullets.push(
      `Inside the range, expect roughly ${fmt(stats.typicalRangePct)}% of daily noise — do not read every wick as a trend change.`,
    );
  }
  if (bullets.length === 0) {
    bullets.push("Wait-and-see until a clean break of nearby highs or lows.");
  }

  const lean = basePathLean(result);
  return {
    title: "If it stays sideways",
    summary:
      lean === "favoured"
        ? "With no active setup and price inside the range, chop is the default."
        : "Range behaviour stays possible until one side breaks with volume.",
    bullets,
    lean,
    matchesUserView: matchesView("flat", view),
  };
}

function downsidePath(result: AnalyzeStockResult, stats: BarStats, view: UserViewRead, system: SystemBias): OutlookSection {
  const bullets: string[] = [];
  const setup = result.primarySetup;
  const L = result.levels;
  const spot = result.spotLtp;

  if (setup && setup.stopLoss > 0) {
    bullets.push(
      `Hard invalidation for the ${setup.side.toUpperCase()} idea: ${fmt(setup.stopLoss)}${distanceNote(spot, setup.stopLoss, stats)} — through it, the setup is done.`,
    );
  }
  if (setup?.side.toLowerCase() === "sell" && setup.targetT1 != null) {
    bullets.push(
      `${sourceLabel(setup.source)} targets T1 ${fmt(setup.targetT1)}${distanceNote(spot, setup.targetT1, stats)}${setup.targetT2 != null ? `, then T2 ${fmt(setup.targetT2)}${distanceNote(spot, setup.targetT2, stats)}` : ""}.`,
    );
  }
  if (L.last2dLow != null) {
    bullets.push(
      `Confirmation: acceptance below the last-2-session low ${fmt(L.last2dLow)}${distanceNote(spot, L.last2dLow, stats)}.`,
    );
  }
  if (L.support1 != null) {
    bullets.push(
      `Pivot support S1 ${fmt(L.support1)}${distanceNote(spot, L.support1, stats)}${L.support2 != null ? `, S2 ${fmt(L.support2)}${distanceNote(spot, L.support2, stats)}` : ""}.`,
    );
  }
  if (L.nearestZoneType && L.nearestZonePrice != null && /demand|support|low/i.test(L.nearestZoneType)) {
    bullets.push(
      `Liquidity underneath: ${L.nearestZoneType} @ ${fmt(L.nearestZonePrice)}${distanceNote(spot, L.nearestZonePrice, stats)} — first place buyers may defend.`,
    );
  }
  if (bullets.length === 0) {
    bullets.push("No mapped support yet — mark the prior session low once bars refresh.");
  }

  const lean = pathLean("down", result, system);
  return {
    title: "If it goes down",
    summary:
      lean === "favoured"
        ? "Structure currently supports this path."
        : lean === "unlikely"
          ? "Possible, but structure argues against it right now."
          : "A realistic path if the triggers below break.",
    bullets,
    lean,
    matchesUserView: matchesView("down", view),
  };
}

function watchNext(
  result: AnalyzeStockResult,
  stats: BarStats,
  view: UserViewRead,
  system: SystemBias,
): OutlookSection {
  const bullets: string[] = [];
  const setup = result.primarySetup;
  const L = result.levels;
  const spot = result.spotLtp;

  if (setup && setup.entry > 0) {
    bullets.push(
      `${setup.side.toLowerCase() === "buy" ? "Reclaim and hold above" : "Break and hold below"} ${fmt(setup.entry)}${distanceNote(spot, setup.entry, stats)} to activate the ${sourceLabel(setup.source)} setup.`,
    );
  }
  if (setup && setup.stopLoss > 0) {
    bullets.push(`Stand aside if price trades through ${fmt(setup.stopLoss)} — do not average against it.`);
  }
  if (L.last2dHigh != null && L.last2dLow != null) {
    bullets.push(
      `A daily close outside ${fmt(L.last2dLow)} – ${fmt(L.last2dHigh)} picks the direction; inside it, assume chop.`,
    );
  } else if (L.pivot != null) {
    bullets.push(`Sustained trade through pivot ${fmt(L.pivot)} tips the short-term bias.`);
  }
  if (stats.volumeVsAvg != null && stats.volumeVsAvg < 1) {
    bullets.push("Require above-average volume on any break — current participation is below average.");
  }
  if (view.bias !== "unclear" && view.bias !== "neutral" && view.bias !== system) {
    bullets.push(
      `Your note leans ${biasLabel(view.bias)} against the system: act on it only after one of the confirmations above prints, not on the view alone.`,
    );
  }
  if (view.horizon != null) {
    bullets.push(
      `You framed this as ${horizonWord(view.horizon)} — ${
        view.horizon === "intraday"
          ? "these daily levels are wide for that; use them as boundaries, not entries."
          : view.horizon === "positional"
            ? "daily levels only cover the next few sessions; re-check weekly structure too."
            : "the daily levels above match that horizon well."
      }`,
    );
  }
  if (bullets.length === 0) {
    bullets.push("Watch the next session's high/low break with volume, then re-run Analyze Stock.");
  }

  return {
    title: "What to watch next",
    summary: "Concrete triggers that decide which path plays out.",
    bullets: bullets.slice(0, 6),
    lean: null,
    matchesUserView: null,
  };
}

/**
 * Scenario-based future outlook from engines, levels and recent bars, with optional
 * free-text user view. The note changes emphasis and alignment only — never the price map.
 */
export function buildFutureOutlookSections(
  result: AnalyzeStockResult,
  userNote?: string | null,
): OutlookSection[] {
  const view = readUserView(userNote);
  const system = inferSystemBias(result);
  const stats = computeBarStats(result);

  return [
    {
      title: "Your view vs system",
      summary: alignmentSummary(view, system),
      bullets: [
        `Your note reads: ${biasLabel(view.bias)}${view.strength !== "none" ? ` (${strengthWord(view.strength)} conviction)` : ""}${view.horizon ? `, ${horizonWord(view.horizon)}` : ""}.`,
        `System read: ${biasLabel(system)} from verdict “${result.verdictLabel}”${result.primarySetup ? ` and the ${sourceLabel(result.primarySetup.source)} ${result.primarySetup.side.toUpperCase()} setup` : ""}.`,
        ...(view.cues.length > 0 ? [`Picked up from your words: ${view.cues.join(", ")}.`] : []),
      ],
      lean: null,
      matchesUserView: null,
    },
    currentSituation(result, stats),
    upsidePath(result, stats, view, system),
    basePath(result, stats, view),
    downsidePath(result, stats, view, system),
    watchNext(result, stats, view, system),
  ];
}
