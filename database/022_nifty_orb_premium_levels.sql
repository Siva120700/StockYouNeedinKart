-- Option-premium SL / targets for Index Options (Nifty ORB).

ALTER TABLE nifty_orb_recommendations
  ADD COLUMN IF NOT EXISTS premium_stop_loss numeric(18, 4);

ALTER TABLE nifty_orb_recommendations
  ADD COLUMN IF NOT EXISTS premium_target_t1 numeric(18, 4);

ALTER TABLE nifty_orb_recommendations
  ADD COLUMN IF NOT EXISTS premium_target_t2 numeric(18, 4);

ALTER TABLE nifty_orb_recommendations
  ADD COLUMN IF NOT EXISTS premium_target_t3 numeric(18, 4);
