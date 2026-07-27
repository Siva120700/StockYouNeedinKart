-- Distinguish auto historical backtest rows from manual journal notes.

ALTER TABLE backtest_notes
  ADD COLUMN IF NOT EXISTS source text NOT NULL DEFAULT 'manual'
    CHECK (source IN ('manual', 'auto'));

CREATE INDEX IF NOT EXISTS backtest_notes_user_auto_idx
  ON backtest_notes (user_id, instrument_id, strategy)
  WHERE source = 'auto';

COMMENT ON COLUMN backtest_notes.source IS
  'manual = user journal; auto = Run 1Y historical backtest.';
