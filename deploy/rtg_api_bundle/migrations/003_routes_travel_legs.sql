-- Travel legs may not start/end at a game node; node connection bonuses come later.
ALTER TABLE routes
  MODIFY from_settlement_id CHAR(36) NULL,
  MODIFY to_settlement_id CHAR(36) NULL;
