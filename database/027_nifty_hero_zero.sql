-- Accuracy strategy key for Nifty Hero Zero (far OTM lottery) index options.

ALTER TABLE signal_outcomes DROP CONSTRAINT IF EXISTS signal_outcomes_strategy_check;
ALTER TABLE signal_outcomes ADD CONSTRAINT signal_outcomes_strategy_check CHECK (strategy IN (
  'signals', 'liquidity', 'liquidity_fresh', 'liquidity_v2', 'confluence',
  'trade_score', 'breakout', 'options_intraday', 'nifty_orb', 'nifty_orb_liq_v2',
  'nifty_liq_breakout', 'nifty_breakout_volume', 'nifty_hero_zero'));
