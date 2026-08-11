-- In-app alerts when a high-probability Nifty index option strike is recommended.

CREATE TABLE IF NOT EXISTS index_option_notifications (
  id                    uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id               uuid NOT NULL REFERENCES users (id) ON DELETE CASCADE,
  recommendation_id     uuid REFERENCES nifty_orb_recommendations (id) ON DELETE SET NULL,
  signal_source         text NOT NULL,
  side                  signal_side NOT NULL,
  as_of_date            date NOT NULL,
  contract_strike       numeric(18, 4) NOT NULL,
  contract_option_type  text NOT NULL,
  premium_ltp           numeric(18, 4) NOT NULL,
  premium_stop_loss     numeric(18, 4),
  premium_target_t1     numeric(18, 4),
  confidence_score      int NOT NULL DEFAULT 0,
  title                 text NOT NULL,
  body                  text NOT NULL,
  read_at               timestamptz,
  created_at            timestamptz NOT NULL DEFAULT now()
);

-- One alert per user / strategy / side / session / strike (avoid Worker poll spam).
CREATE UNIQUE INDEX IF NOT EXISTS index_option_notif_dedup_idx
  ON index_option_notifications (user_id, signal_source, side, as_of_date, contract_strike);

CREATE INDEX IF NOT EXISTS index_option_notif_user_unread_idx
  ON index_option_notifications (user_id, created_at DESC)
  WHERE read_at IS NULL;

ALTER TABLE index_option_notifications ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS index_option_notif_isolation ON index_option_notifications;
CREATE POLICY index_option_notif_isolation ON index_option_notifications
  USING (user_id = current_app_user_id())
  WITH CHECK (user_id = current_app_user_id());
