-- NSE F&O underlying equities (stock futures universe).

DO $$ BEGIN
  ALTER TYPE universe_code ADD VALUE 'nifty_fno';
EXCEPTION
  WHEN duplicate_object THEN NULL;
END $$;

COMMENT ON TYPE universe_code IS
  'Scan universes: nifty_50, nifty_100, nifty_fno (all F&O underlyings), watchlist.';
