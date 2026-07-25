ALTER TABLE analysis_signals
  ADD COLUMN IF NOT EXISTS fresh_cross boolean NOT NULL DEFAULT false;

COMMENT ON COLUMN analysis_signals.fresh_cross IS
  'True when breakout (or 0.5% imminent) is the first in the last ~5 sessions — not a frequent re-break.';
