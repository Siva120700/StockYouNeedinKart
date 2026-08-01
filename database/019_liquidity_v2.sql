-- Liquidity V2 parallel ruleset (A/B vs classic / fresh).

-- Allow ruleset = v2 on liquidity runs.
ALTER TABLE liquidity_analysis_runs DROP CONSTRAINT IF EXISTS liquidity_analysis_runs_ruleset_check;
ALTER TABLE liquidity_analysis_runs
  ADD CONSTRAINT liquidity_analysis_runs_ruleset_check
  CHECK (ruleset IN ('classic', 'fresh', 'v2'));

COMMENT ON COLUMN liquidity_analysis_runs.ruleset IS
  'classic = original 10h confirm; fresh = tighter confirm + skip spent T1; v2 = ATR/HTF quality ruleset';

-- V2 signal quality / metadata columns (nullable-safe defaults for classic/fresh rows).
ALTER TABLE liquidity_signals
  ADD COLUMN IF NOT EXISTS quality_score int NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS confidence_rating text NOT NULL DEFAULT '',
  ADD COLUMN IF NOT EXISTS sweep_strength text,
  ADD COLUMN IF NOT EXISTS atr14 numeric(18, 4),
  ADD COLUMN IF NOT EXISTS score_reasons text[] NOT NULL DEFAULT '{}';

COMMENT ON COLUMN liquidity_signals.quality_score IS 'Liquidity V2 composite quality points (0–100+ capped in UI).';
COMMENT ON COLUMN liquidity_signals.confidence_rating IS 'Liquidity V2 grade: A+, A, B, C, D';
COMMENT ON COLUMN liquidity_signals.sweep_strength IS 'Weak / Medium / Strong from sweep depth %';
COMMENT ON COLUMN liquidity_signals.atr14 IS 'Daily ATR(14) used for stop / filters at signal time';
COMMENT ON COLUMN liquidity_signals.score_reasons IS 'Human-readable reasons contributing to quality_score';

-- Accuracy forward-tracking strategy key.
ALTER TABLE signal_outcomes DROP CONSTRAINT IF EXISTS signal_outcomes_strategy_check;
ALTER TABLE signal_outcomes ADD CONSTRAINT signal_outcomes_strategy_check CHECK (strategy IN (
  'signals', 'liquidity', 'liquidity_fresh', 'liquidity_v2', 'confluence',
  'trade_score', 'breakout', 'options_intraday'));

-- Backtest manual / auto notes.
ALTER TABLE backtest_notes DROP CONSTRAINT IF EXISTS backtest_notes_strategy_check;
ALTER TABLE backtest_notes
  ADD CONSTRAINT backtest_notes_strategy_check
  CHECK (strategy IN (
    'signals', 'liquidity', 'liquidity_fresh', 'liquidity_v2', 'confluence', 'trade_score', 'breakout'));

ALTER TABLE backtest_auto_notes DROP CONSTRAINT IF EXISTS backtest_auto_notes_strategy_check;
ALTER TABLE backtest_auto_notes
  ADD CONSTRAINT backtest_auto_notes_strategy_check
  CHECK (strategy IN (
    'signals', 'liquidity', 'liquidity_fresh', 'liquidity_v2', 'confluence', 'trade_score', 'breakout'));
