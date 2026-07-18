-- User PIN for POC multi-tester isolation (4-digit numeric).

SET @db = DATABASE();

SET @col = (SELECT COUNT(*) FROM information_schema.COLUMNS
  WHERE TABLE_SCHEMA = @db AND TABLE_NAME = 'users' AND COLUMN_NAME = 'pin');
SET @sql = IF(@col = 0,
  'ALTER TABLE users ADD COLUMN pin CHAR(4) NULL AFTER display_name',
  'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @idx = (SELECT COUNT(*) FROM information_schema.STATISTICS
  WHERE TABLE_SCHEMA = @db AND TABLE_NAME = 'users' AND INDEX_NAME = 'uq_users_pin');
SET @sql = IF(@idx = 0,
  'ALTER TABLE users ADD UNIQUE KEY uq_users_pin (pin)',
  'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
