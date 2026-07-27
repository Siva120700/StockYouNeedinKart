-- Store historical backtest output separately from manual journal entries.

CREATE TABLE IF NOT EXISTS backtest_auto_notes (
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
  CONSTRAINT backtest_auto_notes_result_target_check CHECK (
    (result = 'target' AND target_level IS NOT NULL)
    OR (result <> 'target')
  )
);

CREATE INDEX IF NOT EXISTS backtest_auto_notes_user_instrument_idx
  ON backtest_auto_notes (user_id, instrument_id, strategy, signal_date DESC);

CREATE INDEX IF NOT EXISTS backtest_auto_notes_user_strategy_idx
  ON backtest_auto_notes (user_id, strategy, signal_date DESC);

COMMENT ON TABLE backtest_auto_notes IS
  'Auto-generated historical backtest notes from runHistoricalBacktest.';

ALTER TABLE backtest_auto_notes ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS backtest_auto_notes_isolation ON backtest_auto_notes;
CREATE POLICY backtest_auto_notes_isolation ON backtest_auto_notes
  USING (user_id = current_app_user_id())
  WITH CHECK (user_id = current_app_user_id());

-- Move older auto rows from backtest_notes into the new dedicated table.
INSERT INTO backtest_auto_notes (
  id, user_id, instrument_id, strategy, side, signal_date,
  entry_price, initial_stop_loss, target_t1, target_t2, target_t3,
  result, target_level, target_hit_pct, exit_price, exit_date,
  pnl_pct, r_multiple, notes, would_take_live, created_at, updated_at
)
SELECT
  id, user_id, instrument_id, strategy, side, signal_date,
  entry_price, initial_stop_loss, target_t1, target_t2, target_t3,
  result, target_level, target_hit_pct, exit_price, exit_date,
  pnl_pct, r_multiple, notes, would_take_live, created_at, updated_at
FROM backtest_notes
WHERE COALESCE(NULLIF(source, ''), 'manual') = 'auto'
ON CONFLICT (id) DO NOTHING;

DELETE FROM backtest_notes
WHERE COALESCE(NULLIF(source, ''), 'manual') = 'auto';
