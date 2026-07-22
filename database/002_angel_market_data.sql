-- =============================================================================
-- StockYouNeed — Angel One SmartAPI live market data (v2)
-- Endpoint: POST /rest/secure/angelbroking/market/v1/quote/
-- Modes: LTP | OHLC | FULL · max 50 tokens / request · 1 req / sec
--
-- Design (separate tables = separate API calls, lower payload):
--   market_ltp         → mode LTP  · frequent (UI, MTM, open positions)
--   market_ohlc        → on analysis Run (need open/high/low/close + tradeVolume)
--                        Angel OHLC mode has no volume → use mode FULL on Run,
--                        map fields into this table; do NOT write depth yet
--   market_quotes_full → mode FULL full row · reserved for later
--   market_quote_depth → FULL depth · kept in schema, unused for now
--
-- Shared (not tenant-scoped). instruments stay canonical; Angel tokens in map.
-- =============================================================================

-- ---------------------------------------------------------------------------
-- Enums
-- ---------------------------------------------------------------------------

CREATE TYPE angel_exchange AS ENUM (
  'NSE',
  'NFO',
  'BSE',
  'MCX',
  'CDS',
  'NCDEX'
);

CREATE TYPE market_quote_mode AS ENUM (
  'LTP',
  'OHLC',
  'FULL'
);

CREATE TYPE book_side AS ENUM (
  'buy',
  'sell'
);


-- =============================================================================
-- Angel token map (builds request.exchangeTokens)
-- =============================================================================

CREATE TABLE angel_instrument_map (
  instrument_id     uuid PRIMARY KEY REFERENCES instruments (id) ON DELETE CASCADE,
  exchange          angel_exchange NOT NULL,
  symbol_token      text NOT NULL,           -- e.g. "3045"
  trading_symbol    text NOT NULL,           -- e.g. "SBIN-EQ" / "RELIANCE30JUL26FUT"
  name              text,
  lot_size          integer,
  tick_size         numeric(12, 4),
  expiry_date       date,
  is_active         boolean NOT NULL DEFAULT true,
  metadata          jsonb NOT NULL DEFAULT '{}'::jsonb,
  created_at        timestamptz NOT NULL DEFAULT now(),
  updated_at        timestamptz NOT NULL DEFAULT now(),

  CONSTRAINT angel_instrument_map_exchange_token_unique
    UNIQUE (exchange, symbol_token),
  CONSTRAINT angel_instrument_map_token_nonempty
    CHECK (length(trim(symbol_token)) > 0)
);

CREATE INDEX angel_instrument_map_exchange_idx
  ON angel_instrument_map (exchange)
  WHERE is_active;

CREATE INDEX angel_instrument_map_trading_symbol_idx
  ON angel_instrument_map (exchange, trading_symbol);

CREATE TRIGGER angel_instrument_map_set_updated_at
  BEFORE UPDATE ON angel_instrument_map
  FOR EACH ROW EXECUTE PROCEDURE set_updated_at();

COMMENT ON TABLE angel_instrument_map IS
  'Maps StockYouNeed instruments → Angel exchange + symbolToken for quote/order APIs.';


-- =============================================================================
-- LTP — hot path (poll often, lightest Angel mode)
-- =============================================================================
-- Worker: mode = "LTP". Upsert only this table. Do NOT touch OHLC/FULL here.
-- =============================================================================

CREATE TABLE market_ltp (
  instrument_id     uuid PRIMARY KEY
                    REFERENCES instruments (id) ON DELETE CASCADE,
  exchange          angel_exchange NOT NULL,
  trading_symbol    text NOT NULL,
  symbol_token      text NOT NULL,
  ltp               numeric(14, 4) NOT NULL,
  fetched_at        timestamptz NOT NULL DEFAULT now(),
  raw_payload       jsonb NOT NULL DEFAULT '{}'::jsonb
);

CREATE INDEX market_ltp_fetched_at_idx ON market_ltp (fetched_at DESC);
CREATE INDEX market_ltp_exchange_token_idx ON market_ltp (exchange, symbol_token);

COMMENT ON TABLE market_ltp IS
  'Latest LTP per instrument. Frequent refresh (UI ticks, position MTM). Angel mode=LTP only.';


-- =============================================================================
-- OHLC + volume — analysis Run path (Manual Run / first-open auto analysis)
-- =============================================================================
-- Need: open, high, low, close, ltp, AND trade volume (strategy volume_ok).
-- Angel mode=OHLC does NOT return tradeVolume — only mode=FULL does.
-- On Run: call mode FULL → upsert THIS table only (ltp/OHLC/trade_volume).
-- Skip market_quotes_full + market_quote_depth until you need the full book.
-- =============================================================================

CREATE TABLE market_ohlc (
  instrument_id     uuid PRIMARY KEY
                    REFERENCES instruments (id) ON DELETE CASCADE,
  exchange          angel_exchange NOT NULL,
  trading_symbol    text NOT NULL,
  symbol_token      text NOT NULL,
  ltp               numeric(14, 4) NOT NULL,
  open              numeric(14, 4) NOT NULL,
  high              numeric(14, 4) NOT NULL,
  low               numeric(14, 4) NOT NULL,
  close             numeric(14, 4) NOT NULL,  -- previous close from Angel live quote
  trade_volume      bigint NOT NULL CHECK (trade_volume >= 0),  -- API: tradeVolume
  fetched_at        timestamptz NOT NULL DEFAULT now(),
  -- Which analysis_run triggered this fetch (NULL if refreshed outside a run)
  analysis_run_id   uuid REFERENCES analysis_runs (id) ON DELETE SET NULL,
  raw_payload       jsonb NOT NULL DEFAULT '{}'::jsonb,

  CONSTRAINT market_ohlc_range_check CHECK (high >= low)
);

CREATE INDEX market_ohlc_fetched_at_idx ON market_ohlc (fetched_at DESC);
CREATE INDEX market_ohlc_analysis_run_idx ON market_ohlc (analysis_run_id)
  WHERE analysis_run_id IS NOT NULL;

COMMENT ON TABLE market_ohlc IS
  'Latest OHLC + volume per instrument. On Run: Angel mode=FULL, map into this table only.';

COMMENT ON COLUMN market_ohlc.close IS
  'Previous close from Angel live quote (not a historical daily bar close).';

COMMENT ON COLUMN market_ohlc.trade_volume IS
  'Session trade volume from Angel FULL.tradeVolume (OHLC mode does not provide this).';


-- =============================================================================
-- FULL quote — reserved for later (not used in current app flows)
-- =============================================================================

CREATE TABLE market_quotes_full (
  instrument_id       uuid PRIMARY KEY
                      REFERENCES instruments (id) ON DELETE CASCADE,
  exchange            angel_exchange NOT NULL,
  trading_symbol      text NOT NULL,
  symbol_token        text NOT NULL,
  ltp                 numeric(14, 4) NOT NULL,
  open                numeric(14, 4),
  high                numeric(14, 4),
  low                 numeric(14, 4),
  close               numeric(14, 4),
  last_trade_qty      integer,
  exch_feed_time      timestamptz,
  exch_trade_time     timestamptz,
  exch_feed_time_raw  text,
  exch_trade_time_raw text,
  net_change          numeric(14, 4),
  percent_change      numeric(12, 6),
  avg_price           numeric(14, 4),
  trade_volume        bigint,
  opn_interest        bigint,
  lower_circuit       numeric(14, 4),
  upper_circuit       numeric(14, 4),
  tot_buy_quan        bigint,
  tot_sell_quan       bigint,
  week_52_low         numeric(14, 4),
  week_52_high        numeric(14, 4),
  fetched_at          timestamptz NOT NULL DEFAULT now(),
  raw_payload         jsonb NOT NULL DEFAULT '{}'::jsonb,

  CONSTRAINT market_quotes_full_ohlc_range_check CHECK (
    high IS NULL OR low IS NULL OR high >= low
  ),
  CONSTRAINT market_quotes_full_circuit_check CHECK (
    upper_circuit IS NULL
    OR lower_circuit IS NULL
    OR upper_circuit >= lower_circuit
  ),
  CONSTRAINT market_quotes_full_volume_nonneg CHECK (
    trade_volume IS NULL OR trade_volume >= 0
  ),
  CONSTRAINT market_quotes_full_oi_nonneg CHECK (
    opn_interest IS NULL OR opn_interest >= 0
  )
);

CREATE INDEX market_quotes_full_fetched_at_idx
  ON market_quotes_full (fetched_at DESC);

COMMENT ON TABLE market_quotes_full IS
  'Reserved: Angel mode=FULL snapshot. Not polled yet; enables depth later without redesign.';


-- =============================================================================
-- Market depth (FULL only) — kept in schema, unused for now
-- =============================================================================

CREATE TABLE market_quote_depth (
  instrument_id   uuid NOT NULL
                  REFERENCES market_quotes_full (instrument_id) ON DELETE CASCADE,
  side            book_side NOT NULL,
  level           smallint NOT NULL CHECK (level BETWEEN 1 AND 5),
  price           numeric(14, 4) NOT NULL,
  quantity        integer NOT NULL CHECK (quantity >= 0),
  orders          integer NOT NULL CHECK (orders >= 0),
  fetched_at      timestamptz NOT NULL DEFAULT now(),

  PRIMARY KEY (instrument_id, side, level)
);

COMMENT ON TABLE market_quote_depth IS
  'Reserved: Angel FULL depth.buy/sell (≤5 levels). Do not populate until FULL mode is enabled.';


-- =============================================================================
-- Fetch batch log (one HTTP call ≤50 tokens)
-- =============================================================================

CREATE TABLE market_quote_fetch_batches (
  id              uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  mode            market_quote_mode NOT NULL,
  requested_count integer NOT NULL CHECK (requested_count > 0 AND requested_count <= 50),
  fetched_count   integer NOT NULL DEFAULT 0 CHECK (fetched_count >= 0),
  unfetched_count integer NOT NULL DEFAULT 0 CHECK (unfetched_count >= 0),
  status_ok       boolean NOT NULL,
  message         text,
  error_code      text,
  exchange_tokens jsonb NOT NULL,
  unfetched       jsonb NOT NULL DEFAULT '[]'::jsonb,
  started_at      timestamptz NOT NULL DEFAULT now(),
  finished_at     timestamptz,
  duration_ms     integer,
  -- Optional: link OHLC batches to the analysis run that triggered them
  analysis_run_id uuid REFERENCES analysis_runs (id) ON DELETE SET NULL
);

CREATE INDEX market_quote_fetch_batches_started_idx
  ON market_quote_fetch_batches (started_at DESC);

CREATE INDEX market_quote_fetch_batches_mode_idx
  ON market_quote_fetch_batches (mode, started_at DESC);

CREATE INDEX market_quote_fetch_batches_failures_idx
  ON market_quote_fetch_batches (started_at DESC)
  WHERE status_ok = false OR unfetched_count > 0;

COMMENT ON TABLE market_quote_fetch_batches IS
  'Audit of Angel quote HTTP calls. LTP polls frequent; Run uses FULL→market_ohlc (+volume).';


-- =============================================================================
-- Align daily bars source with Angel
-- =============================================================================

ALTER TABLE market_bars
  ALTER COLUMN source SET DEFAULT 'angel';

COMMENT ON COLUMN market_bars.source IS
  'Daily bars (historical). Live prices live in market_ltp / market_ohlc, not here.';


-- =============================================================================
-- Read models
-- =============================================================================

CREATE VIEW v_market_ltp AS
SELECT
  l.instrument_id,
  i.symbol AS app_symbol,
  i.name AS instrument_name,
  i.kind,
  l.exchange,
  l.trading_symbol,
  l.symbol_token,
  l.ltp,
  l.fetched_at
FROM market_ltp l
JOIN instruments i ON i.id = l.instrument_id;

COMMENT ON VIEW v_market_ltp IS
  'UI/GraphQL hot path: latest LTP + instrument identity.';


CREATE VIEW v_market_ohlc AS
SELECT
  o.instrument_id,
  i.symbol AS app_symbol,
  i.name AS instrument_name,
  i.kind,
  o.exchange,
  o.trading_symbol,
  o.symbol_token,
  o.ltp,
  o.open,
  o.high,
  o.low,
  o.close,
  o.trade_volume,
  o.fetched_at,
  o.analysis_run_id
FROM market_ohlc o
JOIN instruments i ON i.id = o.instrument_id;

COMMENT ON VIEW v_market_ohlc IS
  'Analysis Run path: latest OHLC + volume + instrument identity.';


CREATE VIEW v_instruments_needing_angel_token AS
SELECT
  i.id AS instrument_id,
  i.kind,
  i.symbol,
  i.name,
  i.exchange AS app_exchange
FROM instruments i
LEFT JOIN angel_instrument_map m ON m.instrument_id = i.id AND m.is_active
WHERE i.is_active
  AND m.instrument_id IS NULL;

COMMENT ON VIEW v_instruments_needing_angel_token IS
  'Active instruments missing an Angel map row — block quote polling until seeded.';
