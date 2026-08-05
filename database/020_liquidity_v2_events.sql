-- Liquidity V2 event taxonomy (external / internal / cluster / delayed / multi).

ALTER TABLE liquidity_signals
  ADD COLUMN IF NOT EXISTS event_type text;

ALTER TABLE liquidity_signals DROP CONSTRAINT IF EXISTS liquidity_signals_event_type_check;
ALTER TABLE liquidity_signals
  ADD CONSTRAINT liquidity_signals_event_type_check
  CHECK (
    event_type IS NULL
    OR event_type IN (
      'external_sweep',
      'internal_liquidity',
      'liquidity_cluster',
      'delayed_reclaim',
      'multi_sweep'
    )
  );

COMMENT ON COLUMN liquidity_signals.event_type IS
  'V2 liquidity event: external_sweep | internal_liquidity | liquidity_cluster | delayed_reclaim | multi_sweep';
