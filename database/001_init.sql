-- =============================================================================
-- StockYouNeed — PostgreSQL schema (v1)
-- Stack context: React + .NET + GraphQL + PostgreSQL · multi-user SaaS
--
-- Design goals (senior engineer checklist):
--   1. Tenant isolation: every user-owned row carries user_id + RLS-ready design
--   2. Clear domains: Identity | Instrument master | Market cache | Analysis | Portfolio
--   3. Immutability where it matters: signal snapshots don't rewrite history
--   4. Trailing SL as events, not silent overwrites
--   5. Soft deletes only where the user might "undo"; hard FK integrity elsewhere
-- =============================================================================

CREATE EXTENSION IF NOT EXISTS pgcrypto;   -- gen_random_uuid()
CREATE EXTENSION IF NOT EXISTS citext;     -- case-insensitive email

-- ---------------------------------------------------------------------------
-- Enums: closed sets of values (DB enforces illegal states)
-- ---------------------------------------------------------------------------

CREATE TYPE auth_provider AS ENUM ('local', 'google');

CREATE TYPE instrument_kind AS ENUM (
  'equity',          -- e.g. RELIANCE
  'sector_index',    -- e.g. NIFTY AUTO
  'index',           -- e.g. NIFTY 50 index itself
  'stock_future'     -- F&O contract we size lots against
);

CREATE TYPE universe_code AS ENUM (
  'nifty_50',
  'nifty_100'
);

CREATE TYPE signal_side AS ENUM ('buy', 'sell');

CREATE TYPE analysis_trigger AS ENUM (
  'first_open_of_day',
  'manual_run'
);

CREATE TYPE position_status AS ENUM (
  'open',
  'closed'
);

CREATE TYPE close_reason AS ENUM (
  'manual',
  'stop_loss',
  'target_t1',
  'target_t2',
  'target_t3'
);


-- =============================================================================
-- DOMAIN 1 — Identity & tenancy
-- =============================================================================
-- Why split users vs auth_identities?
--   One person can sign in with Google today and optionally link email/password
--   later without duplicating "user" rows or orphaning positions.
-- =============================================================================

CREATE TABLE users (
  id              uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  email           citext NOT NULL,
  display_name    text,
  avatar_url      text,
  is_active       boolean NOT NULL DEFAULT true,
  created_at      timestamptz NOT NULL DEFAULT now(),
  updated_at      timestamptz NOT NULL DEFAULT now(),

  CONSTRAINT users_email_unique UNIQUE (email)
);

COMMENT ON TABLE users IS
  'SaaS tenant principal. All personal portfolio/watchlist/run data hangs off users.id.';

CREATE TABLE auth_identities (
  id                uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id           uuid NOT NULL REFERENCES users (id) ON DELETE CASCADE,
  provider          auth_provider NOT NULL,
  -- Google "sub" (stable subject id) or a local username key
  provider_subject  text NOT NULL,
  -- Optional password hash for provider = local (never store plaintext)
  password_hash     text,
  raw_profile       jsonb NOT NULL DEFAULT '{}'::jsonb,
  created_at        timestamptz NOT NULL DEFAULT now(),
  last_login_at     timestamptz,

  CONSTRAINT auth_identities_provider_subject_unique
    UNIQUE (provider, provider_subject),
  CONSTRAINT auth_identities_local_needs_password
    CHECK (
      (provider = 'local' AND password_hash IS NOT NULL)
      OR (provider = 'google' AND password_hash IS NULL)
    )
);

CREATE INDEX auth_identities_user_id_idx ON auth_identities (user_id);

COMMENT ON TABLE auth_identities IS
  'Login methods linked to a user. Google OAuth uses provider=google + provider_subject=Google sub.';


-- Optional: remember "already ran analysis today" without scanning analysis_runs every time
CREATE TABLE user_daily_activity (
  user_id           uuid NOT NULL REFERENCES users (id) ON DELETE CASCADE,
  activity_date     date NOT NULL,  -- store in IST business date at app layer
  first_open_at     timestamptz NOT NULL DEFAULT now(),
  auto_analysis_run_id uuid,        -- filled after first-open auto run (FK added later)
  PRIMARY KEY (user_id, activity_date)
);

COMMENT ON TABLE user_daily_activity IS
  'Tracks first app open per calendar day so we auto-run analysis once, then rely on Manual Run.';


-- =============================================================================
-- DOMAIN 2 — Instrument master (shared, not per-user)
-- =============================================================================
-- Shared reference data is NOT tenant-scoped. All users see the same RELIANCE.
-- User-specific choice is only: watchlist membership + which universe they scan.
-- =============================================================================

CREATE TABLE instruments (
  id              uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  kind            instrument_kind NOT NULL,
  symbol          text NOT NULL,              -- exchange trading symbol / our canonical code
  name            text NOT NULL,
  exchange        text NOT NULL DEFAULT 'NSE',
  isin            text,                       -- equities
  -- Link equity → its sector index instrument (NULL for indexes/futures)
  sector_instrument_id uuid REFERENCES instruments (id),
  -- Link future → underlying equity
  underlying_instrument_id uuid REFERENCES instruments (id),
  is_active       boolean NOT NULL DEFAULT true,
  metadata        jsonb NOT NULL DEFAULT '{}'::jsonb,
  created_at      timestamptz NOT NULL DEFAULT now(),
  updated_at      timestamptz NOT NULL DEFAULT now(),

  CONSTRAINT instruments_symbol_exchange_kind_unique
    UNIQUE (exchange, symbol, kind)
);

CREATE INDEX instruments_kind_idx ON instruments (kind);
CREATE INDEX instruments_sector_idx ON instruments (sector_instrument_id)
  WHERE sector_instrument_id IS NOT NULL;
CREATE INDEX instruments_underlying_idx ON instruments (underlying_instrument_id)
  WHERE underlying_instrument_id IS NOT NULL;

COMMENT ON TABLE instruments IS
  'Canonical master for equities, sector indexes, and stock futures. Shared across all tenants.';

COMMENT ON COLUMN instruments.sector_instrument_id IS
  'For equities: points to the sector_index instrument used in sector confirmation rule.';


-- Which stocks belong to Nifty 50 / Nifty 100 (membership can change over time)
CREATE TABLE universe_memberships (
  universe        universe_code NOT NULL,
  instrument_id   uuid NOT NULL REFERENCES instruments (id) ON DELETE CASCADE,
  valid_from      date NOT NULL DEFAULT CURRENT_DATE,
  valid_to        date,  -- NULL = currently a member
  PRIMARY KEY (universe, instrument_id, valid_from),
  CONSTRAINT universe_memberships_equity_only_check
    CHECK (valid_to IS NULL OR valid_to >= valid_from)
);

CREATE INDEX universe_memberships_active_idx
  ON universe_memberships (universe, instrument_id)
  WHERE valid_to IS NULL;

COMMENT ON TABLE universe_memberships IS
  'Historical-friendly index membership. Query WHERE valid_to IS NULL for current constituents.';


-- Futures contract specs: lot size → capital & P&L math (no broker link yet)
CREATE TABLE future_contracts (
  id                uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  instrument_id     uuid NOT NULL REFERENCES instruments (id) ON DELETE CASCADE,
  -- e.g. 2026-07-31 expiry; one row per listed contract we care about
  expiry_date       date NOT NULL,
  lot_size          integer NOT NULL CHECK (lot_size > 0),
  -- Approximate margin to show "capital needed" (manual/seeded until broker wired)
  approx_margin_inr numeric(14, 2),
  tick_size         numeric(12, 4) NOT NULL DEFAULT 0.05,
  is_active         boolean NOT NULL DEFAULT true,
  created_at        timestamptz NOT NULL DEFAULT now(),
  updated_at        timestamptz NOT NULL DEFAULT now(),

  CONSTRAINT future_contracts_instrument_expiry_unique
    UNIQUE (instrument_id, expiry_date)
);

COMMENT ON TABLE future_contracts IS
  'F&O lot/margin reference. UI shows capital ≈ margin and P&L ≈ (exit-entry)*lot_size.';


-- =============================================================================
-- DOMAIN 3 — User watchlist (tenant-scoped)
-- =============================================================================

CREATE TABLE user_watchlist_items (
  user_id         uuid NOT NULL REFERENCES users (id) ON DELETE CASCADE,
  instrument_id   uuid NOT NULL REFERENCES instruments (id) ON DELETE CASCADE,
  sort_order      integer NOT NULL DEFAULT 0,
  notes           text,
  created_at      timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (user_id, instrument_id)
);

CREATE INDEX user_watchlist_items_user_idx ON user_watchlist_items (user_id);

COMMENT ON TABLE user_watchlist_items IS
  'Per-user favourites. Third scan universe alongside nifty_50 and nifty_100.';


-- =============================================================================
-- DOMAIN 4 — Market data cache (shared)
-- =============================================================================
-- We pull last ~10 trading days per instrument when analysis runs.
-- Caching in DB makes re-runs / audits / learning reproducible.
-- =============================================================================

CREATE TABLE market_bars (
  instrument_id   uuid NOT NULL REFERENCES instruments (id) ON DELETE CASCADE,
  trade_date      date NOT NULL,
  open            numeric(14, 4) NOT NULL,
  high            numeric(14, 4) NOT NULL,
  low             numeric(14, 4) NOT NULL,
  close           numeric(14, 4) NOT NULL,
  volume          bigint NOT NULL CHECK (volume >= 0),
  source          text NOT NULL DEFAULT 'angel',    -- Angel One SmartAPI (see 002_angel_market_data.sql)
  ingested_at     timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (instrument_id, trade_date),
  CONSTRAINT market_bars_ohlc_check CHECK (
    high >= low
    AND high >= open
    AND high >= close
    AND low <= open
    AND low <= close
  )
);

CREATE INDEX market_bars_trade_date_idx ON market_bars (trade_date DESC);

COMMENT ON TABLE market_bars IS
  'Daily OHLCV cache. Analysis reads from here so strategy math is replayable.';


-- =============================================================================
-- DOMAIN 5 — Analysis runs & signals (tenant-scoped)
-- =============================================================================
-- Important pattern: store the *snapshot* of the decision (entry, SL, targets),
-- not just "RELIANCE = buy". Tomorrow's SL trail updates on the POSITION, while
-- the original signal remains an audit of why we recommended it that day.
-- =============================================================================

CREATE TABLE analysis_runs (
  id              uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id         uuid NOT NULL REFERENCES users (id) ON DELETE CASCADE,
  triggered_by    analysis_trigger NOT NULL,
  -- Which universes were included in this run
  include_nifty_50    boolean NOT NULL DEFAULT true,
  include_nifty_100   boolean NOT NULL DEFAULT false,
  include_watchlist   boolean NOT NULL DEFAULT true,
  -- Business date in IST (app sets this explicitly — don't rely on server TZ)
  as_of_date      date NOT NULL,
  started_at      timestamptz NOT NULL DEFAULT now(),
  finished_at     timestamptz,
  status          text NOT NULL DEFAULT 'running'
                  CHECK (status IN ('running', 'succeeded', 'failed')),
  error_message   text,
  stats           jsonb NOT NULL DEFAULT '{}'::jsonb  -- counts scanned/buy/sell etc.
);

CREATE INDEX analysis_runs_user_date_idx
  ON analysis_runs (user_id, as_of_date DESC);

COMMENT ON TABLE analysis_runs IS
  'One execution of the screening engine for a user (auto first-open or Manual Run).';

-- Back-fill FK now that analysis_runs exists
ALTER TABLE user_daily_activity
  ADD CONSTRAINT user_daily_activity_auto_run_fk
  FOREIGN KEY (auto_analysis_run_id) REFERENCES analysis_runs (id);


CREATE TABLE analysis_signals (
  id                  uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  analysis_run_id     uuid NOT NULL REFERENCES analysis_runs (id) ON DELETE CASCADE,
  user_id             uuid NOT NULL REFERENCES users (id) ON DELETE CASCADE,
  instrument_id       uuid NOT NULL REFERENCES instruments (id),
  sector_instrument_id uuid REFERENCES instruments (id),
  side                signal_side NOT NULL,

  -- Prices at decision time (snapshot)
  as_of_date          date NOT NULL,
  entry_price         numeric(14, 4) NOT NULL,  -- typically latest close / signal price
  initial_stop_loss   numeric(14, 4) NOT NULL,
  target_t1           numeric(14, 4),           -- nullable if no MA on profitable side
  target_t2           numeric(14, 4),
  target_t3           numeric(14, 4),

  -- Rule evidence (great for learning / debugging the strategy)
  last_2d_high        numeric(14, 4),
  last_2d_low         numeric(14, 4),
  volume_ok           boolean NOT NULL,
  sector_confirmed    boolean NOT NULL,
  ma_2d               numeric(14, 4),
  ma_3d               numeric(14, 4),
  ma_5d               numeric(14, 4),

  -- Futures sizing snapshot (display only until broker wired)
  future_contract_id  uuid REFERENCES future_contracts (id),
  lot_size            integer,
  approx_margin_inr   numeric(14, 2),
  pnl_at_t1           numeric(14, 2),
  pnl_at_t2           numeric(14, 2),
  pnl_at_t3           numeric(14, 2),
  capital_note        text,

  universe_tags       text[] NOT NULL DEFAULT '{}', -- e.g. {nifty_50,watchlist}
  created_at          timestamptz NOT NULL DEFAULT now(),

  CONSTRAINT analysis_signals_targets_order_buy CHECK (
    side <> 'buy'
    OR (
      (target_t1 IS NULL OR target_t1 > entry_price)
      AND (target_t2 IS NULL OR target_t2 >= COALESCE(target_t1, entry_price))
      AND (target_t3 IS NULL OR target_t3 >= COALESCE(target_t2, target_t1, entry_price))
    )
  ),
  CONSTRAINT analysis_signals_targets_order_sell CHECK (
    side <> 'sell'
    OR (
      (target_t1 IS NULL OR target_t1 < entry_price)
      AND (target_t2 IS NULL OR target_t2 <= COALESCE(target_t1, entry_price))
      AND (target_t3 IS NULL OR target_t3 <= COALESCE(target_t2, target_t1, entry_price))
    )
  ),
  CONSTRAINT analysis_signals_sl_side_check CHECK (
    (side = 'buy' AND initial_stop_loss < entry_price)
    OR (side = 'sell' AND initial_stop_loss > entry_price)
  )
);

CREATE INDEX analysis_signals_user_date_idx
  ON analysis_signals (user_id, as_of_date DESC);
CREATE INDEX analysis_signals_run_idx
  ON analysis_signals (analysis_run_id);
CREATE INDEX analysis_signals_side_idx
  ON analysis_signals (user_id, side, as_of_date DESC);

COMMENT ON TABLE analysis_signals IS
  'Buy/sell recommendations produced by a run. Immutable snapshot of entry/SL/targets/evidence.';

COMMENT ON COLUMN analysis_signals.universe_tags IS
  'Why this name appeared: nifty_50 / nifty_100 / watchlist (can be multiple).';


-- =============================================================================
-- DOMAIN 6 — Positions & P&L (tenant-scoped, Zerodha-like book)
-- =============================================================================
-- User clicks Buy/Sell on a signal → opens a paper position.
-- They execute the real trade manually on their broker; we track the book here.
-- =============================================================================

CREATE TABLE positions (
  id                  uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id             uuid NOT NULL REFERENCES users (id) ON DELETE CASCADE,
  instrument_id       uuid NOT NULL REFERENCES instruments (id),
  future_contract_id  uuid REFERENCES future_contracts (id),
  signal_id           uuid REFERENCES analysis_signals (id), -- origin recommendation

  side                signal_side NOT NULL,
  status              position_status NOT NULL DEFAULT 'open',

  quantity_lots       integer NOT NULL DEFAULT 1 CHECK (quantity_lots > 0),
  lot_size            integer NOT NULL CHECK (lot_size > 0),
  -- Total qty in shares/units = quantity_lots * lot_size (stored for easy P&L)
  quantity_units      integer NOT NULL CHECK (quantity_units > 0),

  entry_price         numeric(14, 4) NOT NULL,
  entry_at            timestamptz NOT NULL DEFAULT now(),
  entry_as_of_date    date NOT NULL,

  -- Trailing stop: never move against the trade (enforced in app + partial DB checks)
  current_stop_loss   numeric(14, 4) NOT NULL,
  target_t1           numeric(14, 4),
  target_t2           numeric(14, 4),
  target_t3           numeric(14, 4),

  -- Mark-to-market (updated when market bars refresh / user opens app)
  last_price          numeric(14, 4),
  unrealized_pnl_inr  numeric(14, 2),

  -- Close fields (NULL while open)
  exit_price          numeric(14, 4),
  exit_at             timestamptz,
  exit_as_of_date     date,
  realized_pnl_inr    numeric(14, 2),
  close_reason        close_reason,

  notes               text,
  created_at          timestamptz NOT NULL DEFAULT now(),
  updated_at          timestamptz NOT NULL DEFAULT now(),

  CONSTRAINT positions_units_match_lots
    CHECK (quantity_units = quantity_lots * lot_size),
  CONSTRAINT positions_open_has_no_exit CHECK (
    (status = 'open' AND exit_price IS NULL AND exit_at IS NULL AND realized_pnl_inr IS NULL)
    OR (status = 'closed' AND exit_price IS NOT NULL AND exit_at IS NOT NULL AND realized_pnl_inr IS NOT NULL)
  )
);

CREATE INDEX positions_user_open_idx
  ON positions (user_id, status)
  WHERE status = 'open';
CREATE INDEX positions_user_history_idx
  ON positions (user_id, exit_at DESC NULLS LAST);

COMMENT ON TABLE positions IS
  'Paper trading book. Open rows = current holdings; closed rows = history + realized P&L.';


-- Every SL change is an event (audit + learning how trailing worked day by day)
CREATE TABLE position_stop_loss_events (
  id              uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  position_id     uuid NOT NULL REFERENCES positions (id) ON DELETE CASCADE,
  user_id         uuid NOT NULL REFERENCES users (id) ON DELETE CASCADE,
  as_of_date      date NOT NULL,
  old_stop_loss   numeric(14, 4) NOT NULL,
  new_stop_loss   numeric(14, 4) NOT NULL,
  reason          text NOT NULL DEFAULT 'trail_last_2_session_extreme',
  created_at      timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT position_sl_events_changed CHECK (old_stop_loss <> new_stop_loss)
);

CREATE INDEX position_stop_loss_events_position_idx
  ON position_stop_loss_events (position_id, created_at);

COMMENT ON TABLE position_stop_loss_events IS
  'Append-only trail history. App must only insert tighter SL (buy: higher; sell: lower).';


-- Optional ledger for deposits/withdrawals later; keeps P&L reports extensible
CREATE TABLE portfolio_snapshots (
  id                  uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id             uuid NOT NULL REFERENCES users (id) ON DELETE CASCADE,
  as_of_date          date NOT NULL,
  open_positions      integer NOT NULL DEFAULT 0,
  unrealized_pnl_inr  numeric(14, 2) NOT NULL DEFAULT 0,
  realized_pnl_day_inr numeric(14, 2) NOT NULL DEFAULT 0,
  realized_pnl_ltd_inr numeric(14, 2) NOT NULL DEFAULT 0, -- life-to-date
  created_at          timestamptz NOT NULL DEFAULT now(),
  UNIQUE (user_id, as_of_date)
);

COMMENT ON TABLE portfolio_snapshots IS
  'Daily rollup for dashboard charts. Recomputed from positions; safe to rebuild.';


-- =============================================================================
-- updated_at helper
-- =============================================================================

CREATE OR REPLACE FUNCTION set_updated_at()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
  NEW.updated_at = now();
  RETURN NEW;
END;
$$;

CREATE TRIGGER users_set_updated_at
  BEFORE UPDATE ON users
  FOR EACH ROW EXECUTE PROCEDURE set_updated_at();

CREATE TRIGGER instruments_set_updated_at
  BEFORE UPDATE ON instruments
  FOR EACH ROW EXECUTE PROCEDURE set_updated_at();

CREATE TRIGGER future_contracts_set_updated_at
  BEFORE UPDATE ON future_contracts
  FOR EACH ROW EXECUTE PROCEDURE set_updated_at();

CREATE TRIGGER positions_set_updated_at
  BEFORE UPDATE ON positions
  FOR EACH ROW EXECUTE PROCEDURE set_updated_at();


-- =============================================================================
-- Row Level Security (RLS) — SaaS isolation at the database layer
-- =============================================================================
-- .NET should SET app.current_user_id = '<uuid>' on each connection/request.
-- Even if application code bugs out, Postgres still blocks cross-tenant reads.
-- Shared tables (instruments, market_bars, angel map, market_quotes) stay without tenant RLS.
-- =============================================================================

ALTER TABLE user_watchlist_items ENABLE ROW LEVEL SECURITY;
ALTER TABLE user_daily_activity ENABLE ROW LEVEL SECURITY;
ALTER TABLE analysis_runs ENABLE ROW LEVEL SECURITY;
ALTER TABLE analysis_signals ENABLE ROW LEVEL SECURITY;
ALTER TABLE positions ENABLE ROW LEVEL SECURITY;
ALTER TABLE position_stop_loss_events ENABLE ROW LEVEL SECURITY;
ALTER TABLE portfolio_snapshots ENABLE ROW LEVEL SECURITY;

-- Session GUC helper: SELECT current_setting('app.current_user_id', true)::uuid
CREATE OR REPLACE FUNCTION current_app_user_id()
RETURNS uuid
LANGUAGE sql
STABLE
AS $$
  SELECT NULLIF(current_setting('app.current_user_id', true), '')::uuid;
$$;

CREATE POLICY user_watchlist_isolation ON user_watchlist_items
  USING (user_id = current_app_user_id())
  WITH CHECK (user_id = current_app_user_id());

CREATE POLICY user_daily_activity_isolation ON user_daily_activity
  USING (user_id = current_app_user_id())
  WITH CHECK (user_id = current_app_user_id());

CREATE POLICY analysis_runs_isolation ON analysis_runs
  USING (user_id = current_app_user_id())
  WITH CHECK (user_id = current_app_user_id());

CREATE POLICY analysis_signals_isolation ON analysis_signals
  USING (user_id = current_app_user_id())
  WITH CHECK (user_id = current_app_user_id());

CREATE POLICY positions_isolation ON positions
  USING (user_id = current_app_user_id())
  WITH CHECK (user_id = current_app_user_id());

CREATE POLICY position_sl_events_isolation ON position_stop_loss_events
  USING (user_id = current_app_user_id())
  WITH CHECK (user_id = current_app_user_id());

CREATE POLICY portfolio_snapshots_isolation ON portfolio_snapshots
  USING (user_id = current_app_user_id())
  WITH CHECK (user_id = current_app_user_id());


-- =============================================================================
-- Helpful views for the frontend/GraphQL layer (read models)
-- =============================================================================

CREATE VIEW v_open_positions AS
SELECT
  p.*,
  i.symbol,
  i.name AS instrument_name,
  CASE
    WHEN p.side = 'buy'  THEN (p.last_price - p.entry_price) * p.quantity_units
    WHEN p.side = 'sell' THEN (p.entry_price - p.last_price) * p.quantity_units
  END AS computed_unrealized_pnl
FROM positions p
JOIN instruments i ON i.id = p.instrument_id
WHERE p.status = 'open';

COMMENT ON VIEW v_open_positions IS
  'Convenience read model for Positions tab (Zerodha-like open book).';
