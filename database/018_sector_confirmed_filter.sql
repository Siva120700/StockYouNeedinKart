-- Persist sector confirmation so Backtest / Accuracy can filter like live screeners.

ALTER TABLE backtest_notes
  ADD COLUMN IF NOT EXISTS sector_confirmed boolean NOT NULL DEFAULT false;

ALTER TABLE backtest_auto_notes
  ADD COLUMN IF NOT EXISTS sector_confirmed boolean NOT NULL DEFAULT false;

ALTER TABLE signal_outcomes
  ADD COLUMN IF NOT EXISTS sector_confirmed boolean NOT NULL DEFAULT false;

COMMENT ON COLUMN backtest_notes.sector_confirmed IS
  'True when linked sector index also broke last 2 sessions high/low on signal_date (same rule as live).';
COMMENT ON COLUMN backtest_auto_notes.sector_confirmed IS
  'True when linked sector index also broke last 2 sessions high/low on signal_date (same rule as live).';
COMMENT ON COLUMN signal_outcomes.sector_confirmed IS
  'Copied from the emitting live setup (analysis/liquidity/confluence/…).';

-- Backfill live outcomes from source signal rows where available.
UPDATE signal_outcomes o
SET sector_confirmed = s.sector_confirmed
FROM analysis_signals s
WHERE o.analysis_signal_id = s.id
  AND o.strategy = 'signals';

UPDATE signal_outcomes o
SET sector_confirmed = s.sector_confirmed
FROM liquidity_signals s
WHERE o.liquidity_signal_id = s.id
  AND o.strategy IN ('liquidity', 'liquidity_fresh');

UPDATE signal_outcomes o
SET sector_confirmed = (COALESCE(a.sector_confirmed, false) AND COALESCE(l.sector_confirmed, false))
FROM analysis_signals a, liquidity_signals l
WHERE o.analysis_signal_id = a.id
  AND o.liquidity_signal_id = l.id
  AND o.strategy IN ('confluence', 'trade_score');
