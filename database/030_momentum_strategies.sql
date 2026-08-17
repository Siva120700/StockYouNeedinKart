-- Momentum V2 / V3 in backtest, accuracy (signal_outcomes), and outcome links.

ALTER TABLE signal_outcomes DROP CONSTRAINT IF EXISTS signal_outcomes_strategy_check;
ALTER TABLE signal_outcomes ADD CONSTRAINT signal_outcomes_strategy_check CHECK (strategy IN (
  'signals', 'liquidity', 'liquidity_fresh', 'liquidity_v2', 'confluence',
  'trade_score', 'breakout', 'options_intraday', 'momentum_v2', 'momentum_v3',
  'nifty_orb', 'nifty_orb_liq_v2', 'nifty_liq_breakout', 'nifty_breakout_volume',
  'nifty_hero_zero', 'nifty_breakout_chain'));

ALTER TABLE signal_outcomes
  ADD COLUMN IF NOT EXISTS momentum_signal_id uuid REFERENCES momentum_signals (id);

ALTER TABLE backtest_notes DROP CONSTRAINT IF EXISTS backtest_notes_strategy_check;
ALTER TABLE backtest_notes
  ADD CONSTRAINT backtest_notes_strategy_check
  CHECK (strategy IN (
    'signals', 'liquidity', 'liquidity_fresh', 'liquidity_v2', 'confluence',
    'trade_score', 'breakout', 'momentum_v2', 'momentum_v3'));

ALTER TABLE backtest_auto_notes DROP CONSTRAINT IF EXISTS backtest_auto_notes_strategy_check;
ALTER TABLE backtest_auto_notes
  ADD CONSTRAINT backtest_auto_notes_strategy_check
  CHECK (strategy IN (
    'signals', 'liquidity', 'liquidity_fresh', 'liquidity_v2', 'confluence',
    'trade_score', 'breakout', 'momentum_v2', 'momentum_v3'));
