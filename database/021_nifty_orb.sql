-- Nifty ORB (index options buying) runs + recommendations.

CREATE TABLE IF NOT EXISTS nifty_orb_runs (
  id              uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id         uuid NOT NULL REFERENCES users (id) ON DELETE CASCADE,
  as_of_date      date NOT NULL DEFAULT (CURRENT_TIMESTAMP AT TIME ZONE 'Asia/Kolkata')::date,
  status          text NOT NULL DEFAULT 'running' CHECK (status IN ('running', 'succeeded', 'failed')),
  error_message   text,
  started_at      timestamptz NOT NULL DEFAULT now(),
  finished_at     timestamptz
);

CREATE INDEX IF NOT EXISTS nifty_orb_runs_user_idx
  ON nifty_orb_runs (user_id, started_at DESC);

CREATE TABLE IF NOT EXISTS nifty_orb_recommendations (
  id                        uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  run_id                    uuid NOT NULL REFERENCES nifty_orb_runs (id) ON DELETE CASCADE,
  user_id                   uuid NOT NULL REFERENCES users (id) ON DELETE CASCADE,
  instrument_id             uuid NOT NULL REFERENCES instruments (id),
  app_symbol                text NOT NULL DEFAULT 'NIFTY',
  instrument_name           text NOT NULL DEFAULT 'Nifty 50',
  side                      signal_side NOT NULL,
  signal_source             text NOT NULL DEFAULT 'nifty_orb',
  status                    text NOT NULL DEFAULT 'recommended'
                              CHECK (status IN ('recommended', 'skipped', 'waiting')),
  skip_reason               text,
  spot_ltp                  numeric(18, 4),
  orb_high                  numeric(18, 4),
  orb_low                   numeric(18, 4),
  orb_range                 numeric(18, 4),
  underlying_entry          numeric(18, 4) NOT NULL,
  underlying_stop_loss      numeric(18, 4) NOT NULL,
  underlying_target_t1      numeric(18, 4),
  underlying_target_t2      numeric(18, 4),
  underlying_target_t3      numeric(18, 4),
  confidence_score          int NOT NULL DEFAULT 0,
  reasons                   text[] NOT NULL DEFAULT '{}',
  contract_trading_symbol   text,
  contract_expiry_label     text,
  contract_strike           numeric(18, 4),
  contract_option_type      text,
  contract_token            text,
  contract_lot_size         int,
  premium_ltp               numeric(18, 4),
  delta                     numeric(12, 6),
  gamma                     numeric(12, 6),
  theta                     numeric(12, 6),
  vega                      numeric(12, 6),
  implied_volatility        numeric(12, 4),
  trade_volume              numeric(18, 2),
  alt_trading_symbol        text,
  alt_strike                numeric(18, 4),
  alt_delta                 numeric(12, 6),
  alt_implied_volatility    numeric(12, 4),
  alt_premium_ltp           numeric(18, 4),
  flat_by_ist               time NOT NULL DEFAULT '14:30',
  created_at                timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS nifty_orb_rec_run_idx
  ON nifty_orb_recommendations (run_id);

CREATE INDEX IF NOT EXISTS nifty_orb_rec_user_idx
  ON nifty_orb_recommendations (user_id, created_at DESC);

ALTER TABLE nifty_orb_runs ENABLE ROW LEVEL SECURITY;
ALTER TABLE nifty_orb_recommendations ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS nifty_orb_runs_isolation ON nifty_orb_runs;
CREATE POLICY nifty_orb_runs_isolation ON nifty_orb_runs
  USING (user_id = current_app_user_id())
  WITH CHECK (user_id = current_app_user_id());

DROP POLICY IF EXISTS nifty_orb_rec_isolation ON nifty_orb_recommendations;
CREATE POLICY nifty_orb_rec_isolation ON nifty_orb_recommendations
  USING (user_id = current_app_user_id())
  WITH CHECK (user_id = current_app_user_id());

ALTER TABLE signal_outcomes DROP CONSTRAINT IF EXISTS signal_outcomes_strategy_check;
ALTER TABLE signal_outcomes ADD CONSTRAINT signal_outcomes_strategy_check CHECK (strategy IN (
  'signals', 'liquidity', 'liquidity_fresh', 'liquidity_v2', 'confluence',
  'trade_score', 'breakout', 'options_intraday', 'nifty_orb'));
