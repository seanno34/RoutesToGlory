-- Claimed map resources become permanent extractor mines tied to routes.
-- Idempotent: safe to re-run if a prior attempt partially applied (MySQL DDL auto-commits).

SET @db = DATABASE();

SET @col = (SELECT COUNT(*) FROM information_schema.COLUMNS
  WHERE TABLE_SCHEMA = @db AND TABLE_NAME = 'map_resource_nodes' AND COLUMN_NAME = 'owner_empire_id');
SET @sql = IF(@col = 0,
  'ALTER TABLE map_resource_nodes ADD COLUMN owner_empire_id CHAR(36) NULL AFTER yield_per_day',
  'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @col = (SELECT COUNT(*) FROM information_schema.COLUMNS
  WHERE TABLE_SCHEMA = @db AND TABLE_NAME = 'map_resource_nodes' AND COLUMN_NAME = 'route_id');
SET @sql = IF(@col = 0,
  'ALTER TABLE map_resource_nodes ADD COLUMN route_id CHAR(36) NULL AFTER owner_empire_id',
  'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @col = (SELECT COUNT(*) FROM information_schema.COLUMNS
  WHERE TABLE_SCHEMA = @db AND TABLE_NAME = 'map_resource_nodes' AND COLUMN_NAME = 'claimed_at');
SET @sql = IF(@col = 0,
  'ALTER TABLE map_resource_nodes ADD COLUMN claimed_at DATETIME NULL AFTER route_id',
  'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @col = (SELECT COUNT(*) FROM information_schema.COLUMNS
  WHERE TABLE_SCHEMA = @db AND TABLE_NAME = 'map_resource_nodes' AND COLUMN_NAME = 'last_yield_at');
SET @sql = IF(@col = 0,
  'ALTER TABLE map_resource_nodes ADD COLUMN last_yield_at DATETIME NULL AFTER claimed_at',
  'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @idx = (SELECT COUNT(*) FROM information_schema.STATISTICS
  WHERE TABLE_SCHEMA = @db AND TABLE_NAME = 'map_resource_nodes' AND INDEX_NAME = 'idx_resource_owner');
SET @sql = IF(@idx = 0,
  'ALTER TABLE map_resource_nodes ADD KEY idx_resource_owner (world_id, owner_empire_id)',
  'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @fk = (SELECT COUNT(*) FROM information_schema.TABLE_CONSTRAINTS
  WHERE TABLE_SCHEMA = @db AND TABLE_NAME = 'map_resource_nodes' AND CONSTRAINT_NAME = 'fk_resource_owner');
SET @sql = IF(@fk = 0,
  'ALTER TABLE map_resource_nodes ADD CONSTRAINT fk_resource_owner FOREIGN KEY (owner_empire_id) REFERENCES empires(id) ON DELETE SET NULL',
  'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @fk = (SELECT COUNT(*) FROM information_schema.TABLE_CONSTRAINTS
  WHERE TABLE_SCHEMA = @db AND TABLE_NAME = 'map_resource_nodes' AND CONSTRAINT_NAME = 'fk_resource_route');
SET @sql = IF(@fk = 0,
  'ALTER TABLE map_resource_nodes ADD CONSTRAINT fk_resource_route FOREIGN KEY (route_id) REFERENCES routes(id) ON DELETE SET NULL',
  'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
