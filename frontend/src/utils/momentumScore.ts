export const MIN_MOMENTUM_AVERAGE_SCORE = 4;

export function momentumTierLabel(score: number): string {
  if (score >= 8) return "🔥 Strong";
  if (score >= 6) return "🟢 Good";
  if (score >= 4) return "🟡 Average";
  return "⚪ Weak";
}

export function formatMomentumScore(score: number): string {
  return `${score.toFixed(1)}/10 ${momentumTierLabel(score)}`;
}
