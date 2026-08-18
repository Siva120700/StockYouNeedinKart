/** Δ × |spot move| — long CE and short PE both buy the option. */
export type PremiumSide = "long" | "short";

export type PremiumEstimate = {
  entryPremium: number;
  targetPremium: number;
  exitPremium: number;
};

/**
 * Maps spot levels to option premium.
 * Long (CE): target above spot, SL below. Short (PE): target below spot, SL above.
 * Favorable move lifts premium; adverse move cuts it — same as Index Options.
 */
export function estimateOptionPremiums(
  spotEntry: number,
  entryPremium: number,
  delta: number,
  targetSpot: number,
  exitSpot: number,
  side: PremiumSide = "long",
): PremiumEstimate | null {
  if (
    !Number.isFinite(spotEntry) ||
    !Number.isFinite(entryPremium) ||
    !Number.isFinite(delta) ||
    !Number.isFinite(targetSpot) ||
    !Number.isFinite(exitSpot)
  ) {
    return null;
  }

  const d = delta <= 0 ? 0.5 : Math.abs(delta);
  const round = (v: number) => Math.round(Math.max(0.05, v) * 100) / 100;
  const signedMove = (level: number) =>
    side === "short" ? spotEntry - level : level - spotEntry;

  return {
    entryPremium: round(entryPremium),
    targetPremium: round(entryPremium + signedMove(targetSpot) * d),
    exitPremium: round(entryPremium + signedMove(exitSpot) * d),
  };
}
