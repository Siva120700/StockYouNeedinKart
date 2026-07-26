-- Liquidity signals + intraday bars (1H). Does not alter analysis_signals.

CREATE TABLE IF NOT EXISTS market_intraday_bars (
  instrument_id  uuid NOT NULL REFERENCES instruments (id) ON DELETE CASCADE,
  interval       text NOT NULL CHECK (interval IN ('1h', '4h')),
  bar_time       timestamptz NOT NULL,
  open           numeric(14, 4) NOT NULL,
  high           numeric(14, 4) NOT NULL,
  low            numeric(14, 4) NOT NULL,
  close          numeric(14, 4) NOT NULL,
  volume         bigint NOT NULL CHECK (volume >= 0),
  source         text NOT NULL DEFAULT 'angel',
  ingested_at    timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (instrument_id, interval, bar_time),
  CONSTRAINT market_intraday_bars_ohlc_check CHECK (
    high >= low AND high >= open AND high >= close AND low <= open AND low <= close
  )
);

CREATE INDEX IF NOT EXISTS market_intraday_bars_lookup_idx
  ON market_intraday_bars (instrument_id, interval, bar_time DESC);

COMMENT ON TABLE market_intraday_bars IS
  'Intraday OHLCV for liquidity engine (1h synced; 4h optional aggregate).';

CREATE TABLE IF NOT EXISTS liquidity_analysis_runs (
  id                  uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id             uuid NOT NULL REFERENCES users (id) ON DELETE CASCADE,
  triggered_by        text NOT NULL DEFAULT 'manual',
  include_nifty50     boolean NOT NULL DEFAULT true,
  include_nifty100    boolean NOT NULL DEFAULT true,
  include_watchlist   boolean NOT NULL DEFAULT true,
  as_of_date          date NOT NULL,
  started_at          timestamptz NOT NULL DEFAULT now(),
  finished_at         timestamptz,
  status              text NOT NULL DEFAULT 'running',
  error_message       text,
  stats               jsonb NOT NULL DEFAULT '{}'::jsonb
);

CREATE INDEX IF NOT EXISTS liquidity_analysis_runs_user_date_idx
  ON liquidity_analysis_runs (user_id, as_of_date DESC);

CREATE TABLE IF NOT EXISTS liquidity_signals (
  id                    uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  liquidity_run_id      uuid NOT NULL REFERENCES liquidity_analysis_runs (id) ON DELETE CASCADE,
  user_id               uuid NOT NULL REFERENCES users (id) ON DELETE CASCADE,
  instrument_id         uuid NOT NULL REFERENCES instruments (id),
  side                  signal_side NOT NULL,
  as_of_date            date NOT NULL,
  entry_price           numeric(14, 4) NOT NULL,
  initial_stop_loss     numeric(14, 4) NOT NULL,
  target_t1             numeric(14, 4),
  target_t2             numeric(14, 4),
  target_t3             numeric(14, 4),
  relative_volume       numeric(10, 4) NOT NULL DEFAULT 0,
  rvol_percentile       numeric(8, 4) NOT NULL DEFAULT 0,
  rvol_ok               boolean NOT NULL DEFAULT false,
  strong_close          boolean NOT NULL DEFAULT false,
  sweep_side            text,
  swept_zone_type       text,
  swept_zone_price      numeric(14, 4),
  nearest_zone_type     text,
  nearest_zone_price    numeric(14, 4),
  distance_pct          numeric(10, 6),
  zone_tags             text[] NOT NULL DEFAULT '{}',
  timeframe_context     text NOT NULL DEFAULT '4h_sweep+1h_confirm',
  created_at            timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT liquidity_signals_sl_side_check CHECK (
    (side = 'buy' AND initial_stop_loss < entry_price)
    OR (side = 'sell' AND initial_stop_loss > entry_price)
  )
);

CREATE INDEX IF NOT EXISTS liquidity_signals_user_date_idx
  ON liquidity_signals (user_id, as_of_date DESC);
CREATE INDEX IF NOT EXISTS liquidity_signals_run_idx
  ON liquidity_signals (liquidity_run_id);

ALTER TABLE liquidity_analysis_runs ENABLE ROW LEVEL SECURITY;
ALTER TABLE liquidity_signals ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS liquidity_runs_isolation ON liquidity_analysis_runs;
CREATE POLICY liquidity_runs_isolation ON liquidity_analysis_runs
  USING (user_id = current_app_user_id())
  WITH CHECK (user_id = current_app_user_id());

DROP POLICY IF EXISTS liquidity_signals_isolation ON liquidity_signals;
CREATE POLICY liquidity_signals_isolation ON liquidity_signals
  USING (user_id = current_app_user_id())
  WITH CHECK (user_id = current_app_user_id());

-- Allow positions opened from liquidity (no analysis_signals row).
ALTER TABLE positions
  ADD COLUMN IF NOT EXISTS liquidity_signal_id uuid REFERENCES liquidity_signals (id);

COMMENT ON COLUMN positions.liquidity_signal_id IS
  'Optional origin when opened from Liquidity Signals tab.';
