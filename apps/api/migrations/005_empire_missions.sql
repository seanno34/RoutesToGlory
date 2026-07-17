-- POC sequential missions (A→B→C) progress per empire.
-- Idempotent: safe to re-run if a prior attempt partially applied.

CREATE TABLE IF NOT EXISTS empire_missions (
  empire_id CHAR(36) PRIMARY KEY,
  world_id CHAR(36) NOT NULL,
  mission_a_completed_at DATETIME NULL,
  mission_b_completed_at DATETIME NULL,
  mission_c_started_at DATETIME NULL,
  mission_c_completes_at DATETIME NULL,
  mission_c_completed_at DATETIME NULL,
  base_camp_settlement_id CHAR(36) NULL,
  reserve_target INT NOT NULL DEFAULT 0,
  reserve_baseline INT NOT NULL DEFAULT 0,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  KEY idx_missions_world (world_id),
  CONSTRAINT fk_missions_empire FOREIGN KEY (empire_id) REFERENCES empires(id) ON DELETE CASCADE,
  CONSTRAINT fk_missions_world FOREIGN KEY (world_id) REFERENCES worlds(id) ON DELETE CASCADE,
  CONSTRAINT fk_missions_base_camp FOREIGN KEY (base_camp_settlement_id) REFERENCES settlements(id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
