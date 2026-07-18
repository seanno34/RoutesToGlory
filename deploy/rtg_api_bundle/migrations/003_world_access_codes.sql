-- Human-readable resume codes for cross-device game restore (v1 testing).

SET @db = DATABASE();

SET @col = (SELECT COUNT(*) FROM information_schema.COLUMNS
  WHERE TABLE_SCHEMA = @db AND TABLE_NAME = 'worlds' AND COLUMN_NAME = 'access_code');
SET @sql = IF(@col = 0,
  'ALTER TABLE worlds ADD COLUMN access_code VARCHAR(8) NULL AFTER slug',
  'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @idx = (SELECT COUNT(*) FROM information_schema.STATISTICS
  WHERE TABLE_SCHEMA = @db AND TABLE_NAME = 'worlds' AND INDEX_NAME = 'uq_world_access_code');
SET @sql = IF(@idx = 0,
  'ALTER TABLE worlds ADD UNIQUE KEY uq_world_access_code (access_code)',
  'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
