-- Forward-tracking of live signal correctness (separate from historical backtest notes).

CREATE TABLE IF NOT EXISTS signal_outcomes (
  id                        uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id                   uuid NOT NULL REFERENCES users (id) ON DELETE CASCADE,
  instrument_id             uuid NOT NULL REFERENCES instruments (id),
  strategy                  text NOT NULL CHECK (strategy IN (
                              'signals', 'liquidity', 'liquidity_fresh',
                              'confluence', 'trade_score', 'breakout')),
  side                      signal_side NOT NULL,
  signal_date               date NOT NULL,
  entry_price               numeric(14, 4) NOT NULL,
  initial_stop_loss         numeric(14, 4) NOT NULL,
  target_t1                 numeric(14, 4),
  target_t2                 numeric(14, 4),
  target_t3                 numeric(14, 4),
  result                    text NOT NULL DEFAULT 'open'
                              CHECK (result IN ('target', 'sl', 'open', 'time_stop')),
  target_level              text CHECK (target_level IS NULL OR target_level IN ('t1', 't2', 't3')),
  target_hit_pct            numeric(8, 2),
  exit_price                numeric(14, 4),
  exit_date                 date,
  pnl_pct                   numeric(10, 4),
  r_multiple                numeric(10, 4),
  analysis_signal_id        uuid,
  liquidity_signal_id       uuid,
  trade_confidence_score_id uuid,
  breakout_confirmation_id  uuid,
  created_at                timestamptz NOT NULL DEFAULT now(),
  updated_at                timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT signal_outcomes_result_target_check CHECK (
    (result = 'target' AND target_level IS NOT NULL)
    OR (result <> 'target')
  ),
  CONSTRAINT signal_outcomes_unique_setup UNIQUE (user_id, strategy, instrument_id, side, signal_date)
);

CREATE INDEX IF NOT EXISTS signal_outcomes_user_open_idx
  ON signal_outcomes (user_id, result, strategy)
  WHERE result = 'open';

CREATE INDEX IF NOT EXISTS signal_outcomes_user_strategy_idx
  ON signal_outcomes (user_id, strategy, signal_date DESC);

COMMENT ON TABLE signal_outcomes IS
  'Live forward outcomes: opened when a setup is emitted; resolved by Worker via same SL/target rules as backtest.';

ALTER TABLE signal_outcomes ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS signal_outcomes_isolation ON signal_outcomes;
CREATE POLICY signal_outcomes_isolation ON signal_outcomes
  USING (user_id = current_app_user_id())
  WITH CHECK (user_id = current_app_user_id());
