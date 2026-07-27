-- Manual backtest notes (one stock at a time). Does not alter signals / liquidity.

CREATE TABLE IF NOT EXISTS backtest_notes (
  id                  uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id             uuid NOT NULL REFERENCES users (id) ON DELETE CASCADE,
  instrument_id       uuid NOT NULL REFERENCES instruments (id),
  strategy            text NOT NULL CHECK (strategy IN ('signals', 'liquidity')),
  side                signal_side NOT NULL,
  signal_date         date NOT NULL,
  entry_price         numeric(14, 4) NOT NULL,
  initial_stop_loss   numeric(14, 4) NOT NULL,
  target_t1           numeric(14, 4),
  target_t2           numeric(14, 4),
  target_t3           numeric(14, 4),
  result              text NOT NULL CHECK (result IN ('target', 'sl', 'skipped', 'open', 'time_stop')),
  target_level        text CHECK (target_level IS NULL OR target_level IN ('t1', 't2', 't3')),
  target_hit_pct      numeric(8, 2),
  exit_price          numeric(14, 4),
  exit_date           date,
  pnl_pct             numeric(10, 4),
  r_multiple          numeric(10, 4),
  notes               text NOT NULL DEFAULT '',
  would_take_live     boolean,
  created_at          timestamptz NOT NULL DEFAULT now(),
  updated_at          timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT backtest_notes_result_target_check CHECK (
    (result = 'target' AND target_level IS NOT NULL)
    OR (result <> 'target')
  )
);

CREATE INDEX IF NOT EXISTS backtest_notes_user_instrument_idx
  ON backtest_notes (user_id, instrument_id, signal_date DESC);

CREATE INDEX IF NOT EXISTS backtest_notes_user_strategy_idx
  ON backtest_notes (user_id, strategy, signal_date DESC);

COMMENT ON TABLE backtest_notes IS
  'Manual per-trade backtest journal. Isolated from analysis_signals and liquidity_signals.';

ALTER TABLE backtest_notes ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS backtest_notes_isolation ON backtest_notes;
CREATE POLICY backtest_notes_isolation ON backtest_notes
  USING (user_id = current_app_user_id())
  WITH CHECK (user_id = current_app_user_id());
