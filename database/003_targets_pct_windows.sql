-- Targets T1/T2/T3 map to 5d/3d/2d avg % moves — price order is not guaranteed.
ALTER TABLE analysis_signals DROP CONSTRAINT IF EXISTS analysis_signals_targets_order_buy;
ALTER TABLE analysis_signals DROP CONSTRAINT IF EXISTS analysis_signals_targets_order_sell;

ALTER TABLE analysis_signals ADD CONSTRAINT analysis_signals_targets_side_buy CHECK (
  side <> 'buy'
  OR (
    (target_t1 IS NULL OR target_t1 > entry_price)
    AND (target_t2 IS NULL OR target_t2 > entry_price)
    AND (target_t3 IS NULL OR target_t3 > entry_price)
  )
);

ALTER TABLE analysis_signals ADD CONSTRAINT analysis_signals_targets_side_sell CHECK (
  side <> 'sell'
  OR (
    (target_t1 IS NULL OR target_t1 < entry_price)
    AND (target_t2 IS NULL OR target_t2 < entry_price)
    AND (target_t3 IS NULL OR target_t3 < entry_price)
  )
);
