-- Pattern type for chart-pattern breakouts (range, triangles, double top/bottom).

ALTER TABLE breakout_confirmations
  ADD COLUMN IF NOT EXISTS pattern_type text;

COMMENT ON COLUMN breakout_confirmations.pattern_type IS
  'Chart pattern: range_breakout, ascending_triangle, descending_triangle, double_bottom, double_top';

COMMENT ON COLUMN breakout_confirmations.level_20d IS
  'Pattern breakout level (range boundary, triangle line, or neckline)';
