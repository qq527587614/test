-- 添加120日量能均线字段
-- 执行时间: 2026-04-02
-- 兼容 MySQL 5.7

USE StockAnalysisSystem;

-- 为 StockDailyIndicator 表添加 VolumeMA120 字段
ALTER TABLE `StockDailyIndicator`
ADD COLUMN `VolumeMA120` decimal(15,2) DEFAULT NULL COMMENT '120日量能均线'
AFTER `VolumeMA10`;

-- 验证字段是否添加成功
SELECT COLUMN_NAME, COLUMN_TYPE, COLUMN_DEFAULT, COLUMN_COMMENT
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA = 'StockAnalysisSystem'
  AND TABLE_NAME = 'StockDailyIndicator'
  AND COLUMN_NAME = 'VolumeMA120';
