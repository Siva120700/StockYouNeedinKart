-- Allow liquidity_fresh as a backtest strategy alongside classic liquidity.

ALTER TABLE backtest_notes DROP CONSTRAINT IF EXISTS backtest_notes_strategy_check;
ALTER TABLE backtest_notes
  ADD CONSTRAINT backtest_notes_strategy_check
  CHECK (strategy IN ('signals', 'liquidity', 'liquidity_fresh'));

ALTER TABLE backtest_auto_notes DROP CONSTRAINT IF EXISTS backtest_auto_notes_strategy_check;
ALTER TABLE backtest_auto_notes
  ADD CONSTRAINT backtest_auto_notes_strategy_check
  CHECK (strategy IN ('signals', 'liquidity', 'liquidity_fresh'));
