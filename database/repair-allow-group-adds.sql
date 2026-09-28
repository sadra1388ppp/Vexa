-- Repair an existing MariaDB Vexa database whose Users.AllowGroupAdds
-- was created as a boolean/TINYINT instead of the schema-defined VARCHAR(5).
--
-- IMPORTANT:
-- 1. This intentionally matches Vexa.Server/Data/AppDbContext.Partial.cs.
-- 2. The ALTER runs first so existing 0/1 values become text safely.
-- 3. The UPDATE then normalizes those values to the application values
--    "true" or "false".

ALTER TABLE `Users`
    MODIFY COLUMN `AllowGroupAdds` VARCHAR(5) NOT NULL DEFAULT 'true';

UPDATE `Users`
SET `AllowGroupAdds` = CASE
    WHEN LOWER(TRIM(`AllowGroupAdds`)) IN ('1', 'true', 'yes') THEN 'true'
    ELSE 'false'
END;

-- Verification:
-- SHOW COLUMNS FROM `Users` LIKE 'AllowGroupAdds';
-- SELECT `AllowGroupAdds`, LENGTH(`AllowGroupAdds`), HEX(`AllowGroupAdds`)
-- FROM `Users`;
-- Expected values: "true" (74727565) or "false" (66616C7365).
