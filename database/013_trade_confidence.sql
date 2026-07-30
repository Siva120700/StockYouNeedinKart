-- Trade Confidence / High-Probability scoring layer (separate from signals & liquidity runs).

CREATE TABLE IF NOT EXISTS trade_confidence_runs (
  id              uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id         uuid NOT NULL REFERENCES users (id) ON DELETE CASCADE,
  triggered_by    text NOT NULL DEFAULT 'manual',
  as_of_date      date NOT NULL DEFAULT (CURRENT_TIMESTAMP AT TIME ZONE 'Asia/Kolkata')::date,
  status          text NOT NULL DEFAULT 'running' CHECK (status IN ('running', 'succeeded', 'failed')),
  error_message   text,
  started_at      timestamptz NOT NULL DEFAULT now(),
  finished_at     timestamptz
);

CREATE INDEX IF NOT EXISTS trade_confidence_runs_user_started_idx
  ON trade_confidence_runs (user_id, started_at DESC);

-- Quality breakout confirmation (20d high/low + volume + ADX + RSI + ATR expansion).
CREATE TABLE IF NOT EXISTS analysis_breakout (
  id              uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  run_id          uuid NOT NULL REFERENCES trade_confidence_runs (id) ON DELETE CASCADE,
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

CREATE INDEX IF NOT EXISTS analysis_breakout_run_idx ON analysis_breakout (run_id);
CREATE INDEX IF NOT EXISTS analysis_breakout_user_date_idx ON analysis_breakout (user_id, as_of_date DESC);

-- Placeholder for Phase 3 futures analytics.
CREATE TABLE IF NOT EXISTS analysis_futures (
  id              uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  run_id          uuid NOT NULL REFERENCES trade_confidence_runs (id) ON DELETE CASCADE,
  user_id         uuid NOT NULL REFERENCES users (id) ON DELETE CASCADE,
  instrument_id   uuid NOT NULL REFERENCES instruments (id),
  as_of_date      date NOT NULL,
  build_up        text,
  oi_change_pct   numeric(10, 4),
  premium_pct     numeric(10, 4),
  volume_ratio    numeric(10, 4),
  score           int NOT NULL DEFAULT 0,
  created_at      timestamptz NOT NULL DEFAULT now()
);

-- Placeholder for Phase 4 option chain analytics.
CREATE TABLE IF NOT EXISTS analysis_option_chain (
  id              uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  run_id          uuid NOT NULL REFERENCES trade_confidence_runs (id) ON DELETE CASCADE,
  user_id         uuid NOT NULL REFERENCES users (id) ON DELETE CASCADE,
  instrument_id   uuid NOT NULL REFERENCES instruments (id),
  as_of_date      date NOT NULL,
  pcr             numeric(10, 4),
  max_pain        numeric(18, 4),
  call_oi_strike  numeric(18, 4),
  put_oi_strike   numeric(18, 4),
  score           int NOT NULL DEFAULT 0,
  created_at      timestamptz NOT NULL DEFAULT now()
);

-- Combined confidence score per instrument.
CREATE TABLE IF NOT EXISTS trade_confidence_scores (
  id                  uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  run_id              uuid NOT NULL REFERENCES trade_confidence_runs (id) ON DELETE CASCADE,
  user_id             uuid NOT NULL REFERENCES users (id) ON DELETE CASCADE,
  instrument_id       uuid NOT NULL REFERENCES instruments (id),
  side                signal_side NOT NULL,
  as_of_date          date NOT NULL,
  confidence_score    int NOT NULL CHECK (confidence_score BETWEEN 0 AND 100),
  rating              text NOT NULL,
  signals_score       int NOT NULL DEFAULT 0,
  liquidity_score     int NOT NULL DEFAULT 0,
  breakout_score      int NOT NULL DEFAULT 0,
  futures_score       int NOT NULL DEFAULT 0,
  options_score       int NOT NULL DEFAULT 0,
  reasons             jsonb NOT NULL DEFAULT '[]'::jsonb,
  entry_price         numeric(18, 4) NOT NULL,
  initial_stop_loss   numeric(18, 4) NOT NULL,
  target_t1           numeric(18, 4),
  target_t2           numeric(18, 4),
  target_t3           numeric(18, 4),
  analysis_signal_id  uuid REFERENCES analysis_signals (id),
  liquidity_signal_id uuid REFERENCES liquidity_signals (id),
  created_at          timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS trade_confidence_scores_run_idx ON trade_confidence_scores (run_id);
CREATE INDEX IF NOT EXISTS trade_confidence_scores_user_date_idx
  ON trade_confidence_scores (user_id, as_of_date DESC);

ALTER TABLE trade_confidence_runs ENABLE ROW LEVEL SECURITY;
ALTER TABLE analysis_breakout ENABLE ROW LEVEL SECURITY;
ALTER TABLE analysis_futures ENABLE ROW LEVEL SECURITY;
ALTER TABLE analysis_option_chain ENABLE ROW LEVEL SECURITY;
ALTER TABLE trade_confidence_scores ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS trade_confidence_runs_isolation ON trade_confidence_runs;
CREATE POLICY trade_confidence_runs_isolation ON trade_confidence_runs
  USING (user_id = current_setting('app.current_user_id', true)::uuid);

DROP POLICY IF EXISTS analysis_breakout_isolation ON analysis_breakout;
CREATE POLICY analysis_breakout_isolation ON analysis_breakout
  USING (user_id = current_setting('app.current_user_id', true)::uuid);

DROP POLICY IF EXISTS analysis_futures_isolation ON analysis_futures;
CREATE POLICY analysis_futures_isolation ON analysis_futures
  USING (user_id = current_setting('app.current_user_id', true)::uuid);

DROP POLICY IF EXISTS analysis_option_chain_isolation ON analysis_option_chain;
CREATE POLICY analysis_option_chain_isolation ON analysis_option_chain
  USING (user_id = current_setting('app.current_user_id', true)::uuid);

DROP POLICY IF EXISTS trade_confidence_scores_isolation ON trade_confidence_scores;
CREATE POLICY trade_confidence_scores_isolation ON trade_confidence_scores
  USING (user_id = current_setting('app.current_user_id', true)::uuid);

-- Backtest strategy name for trade-score replay.
ALTER TABLE backtest_notes DROP CONSTRAINT IF EXISTS backtest_notes_strategy_check;
ALTER TABLE backtest_notes
  ADD CONSTRAINT backtest_notes_strategy_check
  CHECK (strategy IN ('signals', 'liquidity', 'liquidity_fresh', 'confluence', 'trade_score'));

ALTER TABLE backtest_auto_notes DROP CONSTRAINT IF EXISTS backtest_auto_notes_strategy_check;
ALTER TABLE backtest_auto_notes
  ADD CONSTRAINT backtest_auto_notes_strategy_check
  CHECK (strategy IN ('signals', 'liquidity', 'liquidity_fresh', 'confluence', 'trade_score'));
