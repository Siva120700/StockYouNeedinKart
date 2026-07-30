-- Standalone breakout analysis (F&O confirmation layer — separate from Trade Score).

CREATE TABLE IF NOT EXISTS breakout_analysis_runs (
  id              uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id         uuid NOT NULL REFERENCES users (id) ON DELETE CASCADE,
  triggered_by    text NOT NULL DEFAULT 'manual',
  as_of_date      date NOT NULL DEFAULT (CURRENT_TIMESTAMP AT TIME ZONE 'Asia/Kolkata')::date,
  status          text NOT NULL DEFAULT 'running' CHECK (status IN ('running', 'succeeded', 'failed')),
  error_message   text,
  started_at      timestamptz NOT NULL DEFAULT now(),
  finished_at     timestamptz
);

CREATE INDEX IF NOT EXISTS breakout_analysis_runs_user_started_idx
  ON breakout_analysis_runs (user_id, started_at DESC);

CREATE TABLE IF NOT EXISTS breakout_confirmations (
  id              uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  run_id          uuid NOT NULL REFERENCES breakout_analysis_runs (id) ON DELETE CASCADE,
  user_id         uuid NOT NULL REFERENCES users (id) ON DELETE CASCADE,
  instrument_id   uuid NOT NULL REFERENCES instruments (id),
  side            signal_side NOT NULL,
  as_of_date      date NOT NULL,
  confirmed       boolean NOT NULL DEFAULT false,
  close_price     numeric(18, 4),
  level_20d       numeric(18, 4),
  volume_ratio    numeric(10, 4),
  adx             numeric(10, 4),
  rsi             numeric(10, 4),
  atr             numeric(18, 4),
  atr_expansion   boolean NOT NULL DEFAULT false,
  created_at      timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS breakout_confirmations_run_idx ON breakout_confirmations (run_id);
CREATE INDEX IF NOT EXISTS breakout_confirmations_user_date_idx
  ON breakout_confirmations (user_id, as_of_date DESC);

ALTER TABLE breakout_analysis_runs ENABLE ROW LEVEL SECURITY;
ALTER TABLE breakout_confirmations ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS breakout_analysis_runs_isolation ON breakout_analysis_runs;
CREATE POLICY breakout_analysis_runs_isolation ON breakout_analysis_runs
  USING (user_id = current_setting('app.current_user_id', true)::uuid);

DROP POLICY IF EXISTS breakout_confirmations_isolation ON breakout_confirmations;
CREATE POLICY breakout_confirmations_isolation ON breakout_confirmations
  USING (user_id = current_setting('app.current_user_id', true)::uuid);

-- Backtest: quality breakout-only strategy.
ALTER TABLE backtest_notes DROP CONSTRAINT IF EXISTS backtest_notes_strategy_check;
ALTER TABLE backtest_notes
  ADD CONSTRAINT backtest_notes_strategy_check
  CHECK (strategy IN ('signals', 'liquidity', 'liquidity_fresh', 'confluence', 'trade_score', 'breakout'));

ALTER TABLE backtest_auto_notes DROP CONSTRAINT IF EXISTS backtest_auto_notes_strategy_check;
ALTER TABLE backtest_auto_notes
  ADD CONSTRAINT backtest_auto_notes_strategy_check
  CHECK (strategy IN ('signals', 'liquidity', 'liquidity_fresh', 'confluence', 'trade_score', 'breakout'));
