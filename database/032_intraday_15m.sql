-- Allow 15-minute candles in the shared intraday bar cache (spike scanner + momentum RSI).

ALTER TABLE market_intraday_bars
  DROP CONSTRAINT IF EXISTS market_intraday_bars_interval_check;

ALTER TABLE market_intraday_bars
  ADD CONSTRAINT market_intraday_bars_interval_check
  CHECK (interval IN ('1h', '4h', '15m'));

COMMENT ON TABLE market_intraday_bars IS
  'Intraday OHLCV (1h liquidity, 15m spike/momentum, 4h optional aggregate).';
