-- Nifty index options: Momentum V2 (breakout + score >= 4) accuracy key.

ALTER TABLE signal_outcomes DROP CONSTRAINT IF EXISTS signal_outcomes_strategy_check;
ALTER TABLE signal_outcomes ADD CONSTRAINT signal_outcomes_strategy_check CHECK (strategy IN (
  'signals', 'liquidity', 'liquidity_fresh', 'liquidity_v2', 'confluence',
  'trade_score', 'breakout', 'options_intraday', 'momentum_v2', 'momentum_v3',
  'nifty_orb', 'nifty_orb_liq_v2', 'nifty_liq_breakout', 'nifty_breakout_volume',
  'nifty_hero_zero', 'nifty_breakout_chain', 'nifty_momentum_v2'));
