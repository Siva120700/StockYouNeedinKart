-- Options Intraday recommendations + NFO contract cache (linked to underlying equity).

CREATE TABLE IF NOT EXISTS nfo_contracts (
  id                      uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  underlying_instrument_id uuid NOT NULL REFERENCES instruments (id) ON DELETE CASCADE,
  app_symbol              text NOT NULL,
  angel_name              text NOT NULL,
  kind                    text NOT NULL CHECK (kind IN ('future', 'option')),
  option_type             text CHECK (option_type IS NULL OR option_type IN ('CE', 'PE')),
  strike                  numeric(18, 4),
  expiry                  date NOT NULL,
  expiry_label            text NOT NULL,
  symbol_token            text NOT NULL,
  trading_symbol          text NOT NULL,
  lot_size                int NOT NULL DEFAULT 1,
  tick_size               numeric(12, 4) NOT NULL DEFAULT 0.05,
  last_oi                 bigint,
  last_ltp                numeric(18, 4),
  updated_at              timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT nfo_contracts_token_unique UNIQUE (symbol_token)
);

CREATE INDEX IF NOT EXISTS nfo_contracts_underlying_idx
  ON nfo_contracts (underlying_instrument_id, kind, expiry);

CREATE INDEX IF NOT EXISTS nfo_contracts_option_lookup_idx
  ON nfo_contracts (app_symbol, kind, expiry, option_type, strike);

CREATE TABLE IF NOT EXISTS options_intraday_runs (
  id              uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id         uuid NOT NULL REFERENCES users (id) ON DELETE CASCADE,
  as_of_date      date NOT NULL DEFAULT (CURRENT_TIMESTAMP AT TIME ZONE 'Asia/Kolkata')::date,
  status          text NOT NULL DEFAULT 'running' CHECK (status IN ('running', 'succeeded', 'failed')),
  error_message   text,
  started_at      timestamptz NOT NULL DEFAULT now(),
  finished_at     timestamptz
);

CREATE INDEX IF NOT EXISTS options_intraday_runs_user_idx
  ON options_intraday_runs (user_id, started_at DESC);

CREATE TABLE IF NOT EXISTS options_intraday_recommendations (
  id                        uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  run_id                    uuid NOT NULL REFERENCES options_intraday_runs (id) ON DELETE CASCADE,
  user_id                   uuid NOT NULL REFERENCES users (id) ON DELETE CASCADE,
  instrument_id             uuid NOT NULL REFERENCES instruments (id),
  app_symbol                text NOT NULL,
  instrument_name           text NOT NULL DEFAULT '',
  side                      signal_side NOT NULL,
  signal_source             text NOT NULL DEFAULT 'liquidity_fresh',
  status                    text NOT NULL DEFAULT 'recommended'
                              CHECK (status IN ('recommended', 'skipped')),
  skip_reason               text,
  spot_ltp                  numeric(18, 4),
  underlying_entry          numeric(18, 4) NOT NULL,
  underlying_stop_loss      numeric(18, 4) NOT NULL,
  underlying_target_t1      numeric(18, 4),
  underlying_target_t2      numeric(18, 4),
  underlying_target_t3      numeric(18, 4),
  futures_build_up          text,
  futures_premium_pct       numeric(10, 4),
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
  flat_by_ist               time NOT NULL DEFAULT '15:20',
  liquidity_signal_id       uuid,
  analysis_signal_id        uuid,
  created_at                timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS options_intraday_rec_run_idx
  ON options_intraday_recommendations (run_id);

CREATE INDEX IF NOT EXISTS options_intraday_rec_user_idx
  ON options_intraday_recommendations (user_id, created_at DESC);

ALTER TABLE options_intraday_runs ENABLE ROW LEVEL SECURITY;
ALTER TABLE options_intraday_recommendations ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS options_intraday_runs_isolation ON options_intraday_runs;
CREATE POLICY options_intraday_runs_isolation ON options_intraday_runs
  USING (user_id = current_app_user_id())
  WITH CHECK (user_id = current_app_user_id());

DROP POLICY IF EXISTS options_intraday_rec_isolation ON options_intraday_recommendations;
CREATE POLICY options_intraday_rec_isolation ON options_intraday_recommendations
  USING (user_id = current_app_user_id())
  WITH CHECK (user_id = current_app_user_id());

-- Allow Accuracy forward tracking for options_intraday strategy.
ALTER TABLE signal_outcomes DROP CONSTRAINT IF EXISTS signal_outcomes_strategy_check;
ALTER TABLE signal_outcomes ADD CONSTRAINT signal_outcomes_strategy_check CHECK (strategy IN (
  'signals', 'liquidity', 'liquidity_fresh', 'confluence', 'trade_score', 'breakout', 'options_intraday'));
