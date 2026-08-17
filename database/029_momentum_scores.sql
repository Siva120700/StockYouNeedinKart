-- Momentum engine (parallel to Signals / Liquidity V2). Does not alter analysis_signals.

CREATE TABLE IF NOT EXISTS momentum_analysis_runs (
  id                  uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id             uuid NOT NULL REFERENCES users (id) ON DELETE CASCADE,
  triggered_by        text NOT NULL DEFAULT 'manual',
  include_nifty50     boolean NOT NULL DEFAULT true,
  include_nifty100    boolean NOT NULL DEFAULT true,
  include_watchlist   boolean NOT NULL DEFAULT true,
  as_of_date          date NOT NULL,
  ruleset             text NOT NULL DEFAULT 'v2' CHECK (ruleset IN ('v2', 'v3')),
  started_at          timestamptz NOT NULL DEFAULT now(),
  finished_at         timestamptz,
  status              text NOT NULL DEFAULT 'running',
  error_message       text,
  stats               jsonb NOT NULL DEFAULT '{}'::jsonb
);

CREATE INDEX IF NOT EXISTS momentum_analysis_runs_user_ruleset_started_idx
  ON momentum_analysis_runs (user_id, ruleset, started_at DESC);

COMMENT ON COLUMN momentum_analysis_runs.ruleset IS
  'v2 = StepOne-style composite; v3 = Jegadeesh–Titman multi-horizon';

CREATE TABLE IF NOT EXISTS momentum_signals (
  id                    uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  momentum_run_id       uuid NOT NULL REFERENCES momentum_analysis_runs (id) ON DELETE CASCADE,
  user_id               uuid NOT NULL REFERENCES users (id) ON DELETE CASCADE,
  instrument_id         uuid NOT NULL REFERENCES instruments (id),
  side                  signal_side NOT NULL,
  as_of_date            date NOT NULL,
  entry_price           numeric(14, 4) NOT NULL,
  initial_stop_loss     numeric(14, 4) NOT NULL,
  target_t1             numeric(14, 4),
  target_t2             numeric(14, 4),
  target_t3             numeric(14, 4),
  volume_ok             boolean NOT NULL DEFAULT false,
  sector_confirmed      boolean NOT NULL DEFAULT false,
  fresh_cross           boolean NOT NULL DEFAULT false,
  momentum_score        numeric(10, 4) NOT NULL,
  created_at            timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT momentum_signals_sl_side_check CHECK (
    (side = 'buy' AND initial_stop_loss < entry_price)
    OR (side = 'sell' AND initial_stop_loss > entry_price)
  ),
  CONSTRAINT momentum_signals_score_range CHECK (
    momentum_score >= 0 AND momentum_score <= 10
  )
);

CREATE INDEX IF NOT EXISTS momentum_signals_user_date_idx
  ON momentum_signals (user_id, as_of_date DESC);
CREATE INDEX IF NOT EXISTS momentum_signals_run_idx
  ON momentum_signals (momentum_run_id);
CREATE INDEX IF NOT EXISTS momentum_signals_score_idx
  ON momentum_signals (user_id, momentum_score DESC);

ALTER TABLE momentum_analysis_runs ENABLE ROW LEVEL SECURITY;
ALTER TABLE momentum_signals ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS momentum_runs_isolation ON momentum_analysis_runs;
CREATE POLICY momentum_runs_isolation ON momentum_analysis_runs
  USING (user_id = current_app_user_id())
  WITH CHECK (user_id = current_app_user_id());

DROP POLICY IF EXISTS momentum_signals_isolation ON momentum_signals;
CREATE POLICY momentum_signals_isolation ON momentum_signals
  USING (user_id = current_app_user_id())
  WITH CHECK (user_id = current_app_user_id());

ALTER TABLE positions
  ADD COLUMN IF NOT EXISTS momentum_signal_id uuid REFERENCES momentum_signals (id);

COMMENT ON COLUMN positions.momentum_signal_id IS
  'Optional origin when opened from Momentum V2 / V3 tab.';
