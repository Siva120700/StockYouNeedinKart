-- Sector confirmation flag on liquidity signals (mirrors analysis_signals.sector_confirmed).

ALTER TABLE liquidity_signals
  ADD COLUMN IF NOT EXISTS sector_confirmed boolean NOT NULL DEFAULT false;

COMMENT ON COLUMN liquidity_signals.sector_confirmed IS
  'True when linked sector index also breaks last 2 sessions high/low (same side as signal).';
