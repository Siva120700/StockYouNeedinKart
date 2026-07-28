-- Separate classic vs freshness liquidity runs so both can be compared.

ALTER TABLE liquidity_analysis_runs
  ADD COLUMN IF NOT EXISTS ruleset text NOT NULL DEFAULT 'classic'
    CHECK (ruleset IN ('classic', 'fresh'));

CREATE INDEX IF NOT EXISTS liquidity_analysis_runs_user_ruleset_started_idx
  ON liquidity_analysis_runs (user_id, ruleset, started_at DESC);

COMMENT ON COLUMN liquidity_analysis_runs.ruleset IS
  'classic = original 10h confirm; fresh = tighter confirm + skip already-hit T1';
