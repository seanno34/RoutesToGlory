-- Routes to Glory — MySQL schema (ventures_rtg_test)
-- Coordinates stored as lat/lng DECIMAL; route paths as JSON arrays.

CREATE TABLE IF NOT EXISTS worlds (
  id CHAR(36) PRIMARY KEY,
  slug VARCHAR(64) NOT NULL UNIQUE,
  name VARCHAR(255) NOT NULL,
  status VARCHAR(32) NOT NULL DEFAULT 'active',
  difficulty VARCHAR(16) NOT NULL DEFAULT 'normal',
  config JSON NOT NULL,
  started_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS users (
  id CHAR(36) PRIMARY KEY,
  email VARCHAR(255) UNIQUE,
  display_name VARCHAR(255) NOT NULL,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS empires (
  id CHAR(36) PRIMARY KEY,
  world_id CHAR(36) NOT NULL,
  user_id CHAR(36) NULL,
  name VARCHAR(255) NOT NULL,
  color VARCHAR(16) NOT NULL DEFAULT '#3b82f6',
  power INT NOT NULL DEFAULT 100,
  gold INT NOT NULL DEFAULT 500,
  spawn_lat DECIMAL(10, 7) NULL,
  spawn_lng DECIMAL(10, 7) NULL,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  UNIQUE KEY uq_empire_world_user (world_id, user_id),
  CONSTRAINT fk_empires_world FOREIGN KEY (world_id) REFERENCES worlds(id) ON DELETE CASCADE,
  CONSTRAINT fk_empires_user FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS npc_empires (
  world_id CHAR(36) PRIMARY KEY,
  name VARCHAR(255) NOT NULL,
  difficulty VARCHAR(16) NOT NULL,
  growth_points INT NOT NULL DEFAULT 0,
  territory_count INT NOT NULL DEFAULT 1,
  hostility_phase VARCHAR(32) NOT NULL DEFAULT 'dormant',
  last_tick_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  CONSTRAINT fk_npc_world FOREIGN KEY (world_id) REFERENCES worlds(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS settlements (
  id CHAR(36) PRIMARY KEY,
  world_id CHAR(36) NOT NULL,
  slug VARCHAR(64) NOT NULL,
  name VARCHAR(255) NOT NULL,
  planet_display_name VARCHAR(255) NULL,
  terrestrial_label VARCHAR(255) NULL,
  tier VARCHAR(32) NOT NULL,
  alignment VARCHAR(32) NOT NULL DEFAULT 'neutral',
  is_goodie_hut TINYINT(1) NOT NULL DEFAULT 0,
  owner_empire_id CHAR(36) NULL,
  lat DECIMAL(10, 7) NOT NULL,
  lng DECIMAL(10, 7) NOT NULL,
  geofence_radius_m INT NOT NULL DEFAULT 250,
  base_defense INT NOT NULL DEFAULT 50,
  UNIQUE KEY uq_settlement_world_slug (world_id, slug),
  KEY idx_settlements_world (world_id),
  KEY idx_settlements_geo (world_id, lat, lng),
  CONSTRAINT fk_settlements_world FOREIGN KEY (world_id) REFERENCES worlds(id) ON DELETE CASCADE,
  CONSTRAINT fk_settlements_owner FOREIGN KEY (owner_empire_id) REFERENCES empires(id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS route_sessions (
  id CHAR(36) PRIMARY KEY,
  world_id CHAR(36) NOT NULL,
  empire_id CHAR(36) NOT NULL,
  user_id CHAR(36) NULL,
  status VARCHAR(32) NOT NULL DEFAULT 'active',
  origin_settlement_id CHAR(36) NULL,
  target_settlement_id CHAR(36) NULL,
  origin_lat DECIMAL(10, 7) NULL,
  origin_lng DECIMAL(10, 7) NULL,
  end_reason VARCHAR(32) NULL,
  point_count INT NOT NULL DEFAULT 0,
  distance_m DOUBLE NOT NULL DEFAULT 0,
  started_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  ended_at DATETIME NULL,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  KEY idx_route_sessions_empire (empire_id, status),
  CONSTRAINT fk_sessions_world FOREIGN KEY (world_id) REFERENCES worlds(id) ON DELETE CASCADE,
  CONSTRAINT fk_sessions_empire FOREIGN KEY (empire_id) REFERENCES empires(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS route_session_points (
  id BIGINT AUTO_INCREMENT PRIMARY KEY,
  session_id CHAR(36) NOT NULL,
  seq INT NOT NULL,
  lat DECIMAL(10, 7) NOT NULL,
  lng DECIMAL(10, 7) NOT NULL,
  accuracy_m DOUBLE NULL,
  speed_mps DOUBLE NULL,
  accepted TINYINT(1) NOT NULL DEFAULT 1,
  reject_reason VARCHAR(64) NULL,
  recorded_at DATETIME NOT NULL,
  received_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  UNIQUE KEY uq_session_seq (session_id, seq),
  KEY idx_points_session (session_id, seq),
  CONSTRAINT fk_points_session FOREIGN KEY (session_id) REFERENCES route_sessions(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS routes (
  id CHAR(36) PRIMARY KEY,
  world_id CHAR(36) NOT NULL,
  empire_id CHAR(36) NOT NULL,
  session_id CHAR(36) UNIQUE NULL,
  from_settlement_id CHAR(36) NOT NULL,
  to_settlement_id CHAR(36) NOT NULL,
  path_json JSON NOT NULL,
  distance_m DOUBLE NOT NULL,
  status VARCHAR(32) NOT NULL DEFAULT 'active',
  established_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  KEY idx_routes_world (world_id),
  CONSTRAINT fk_routes_world FOREIGN KEY (world_id) REFERENCES worlds(id) ON DELETE CASCADE,
  CONSTRAINT fk_routes_empire FOREIGN KEY (empire_id) REFERENCES empires(id) ON DELETE CASCADE,
  CONSTRAINT fk_routes_session FOREIGN KEY (session_id) REFERENCES route_sessions(id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS empire_stockpiles (
  empire_id CHAR(36) PRIMARY KEY,
  resources JSON NOT NULL,
  CONSTRAINT fk_stockpile_empire FOREIGN KEY (empire_id) REFERENCES empires(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS explored_tiles (
  world_id CHAR(36) NOT NULL,
  empire_id CHAR(36) NOT NULL,
  tile_id VARCHAR(64) NOT NULL,
  revealed_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (world_id, empire_id, tile_id),
  CONSTRAINT fk_explored_world FOREIGN KEY (world_id) REFERENCES worlds(id) ON DELETE CASCADE,
  CONSTRAINT fk_explored_empire FOREIGN KEY (empire_id) REFERENCES empires(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS map_resource_nodes (
  id CHAR(36) PRIMARY KEY,
  world_id CHAR(36) NOT NULL,
  tile_id VARCHAR(64) NOT NULL,
  resource_id VARCHAR(64) NOT NULL,
  lat DECIMAL(10, 7) NOT NULL,
  lng DECIMAL(10, 7) NOT NULL,
  richness VARCHAR(16) NOT NULL,
  yield_per_day INT NOT NULL,
  UNIQUE KEY uq_resource_tile (world_id, tile_id),
  KEY idx_resource_world (world_id),
  CONSTRAINT fk_resource_world FOREIGN KEY (world_id) REFERENCES worlds(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS build_jobs (
  id CHAR(36) PRIMARY KEY,
  world_id CHAR(36) NOT NULL,
  empire_id CHAR(36) NOT NULL,
  target_type VARCHAR(64) NOT NULL,
  target_key VARCHAR(64) NOT NULL,
  settlement_id CHAR(36) NULL,
  route_id CHAR(36) NULL,
  status VARCHAR(32) NOT NULL DEFAULT 'in_progress',
  duration_seconds INT NOT NULL,
  started_at DATETIME NULL,
  completes_at DATETIME NULL,
  resource_cost JSON NOT NULL,
  gold_rushed INT NOT NULL DEFAULT 0,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  CONSTRAINT fk_jobs_world FOREIGN KEY (world_id) REFERENCES worlds(id) ON DELETE CASCADE,
  CONSTRAINT fk_jobs_empire FOREIGN KEY (empire_id) REFERENCES empires(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS diplomacy_relations (
  world_id CHAR(36) NOT NULL,
  empire_a_id CHAR(36) NOT NULL,
  empire_b_id CHAR(36) NOT NULL,
  status VARCHAR(32) NOT NULL DEFAULT 'neutral',
  updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (world_id, empire_a_id, empire_b_id),
  CONSTRAINT fk_diplomacy_world FOREIGN KEY (world_id) REFERENCES worlds(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS world_events (
  id CHAR(36) PRIMARY KEY,
  world_id CHAR(36) NOT NULL,
  type VARCHAR(64) NOT NULL,
  payload JSON NOT NULL,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  KEY idx_events_world (world_id, created_at),
  CONSTRAINT fk_events_world FOREIGN KEY (world_id) REFERENCES worlds(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
