-- 插入均线多头策略
-- 执行时间: 2026-04-02
-- 兼容 MySQL 5.7

USE StockAnalysisSystem;

-- 检查策略是否已存在
SELECT * FROM `Strategy` WHERE `StrategyType` = 'MultiMovingAverage';

-- 如果不存在，则插入
INSERT IGNORE INTO `Strategy` (
    `Name`,
    `Description`,
    `StrategyType`,
    `Parameters`,
    `IsActive`,
    `CreatedAt`
) VALUES (
    '均线多头策略',
    '5日线大于10日线，当天收盘价大于5日线，最近5天至少有两天量能大于120日量能均线3倍',
    'MultiMovingAverage',
    '{"ShortPeriod":5,"MediumPeriod":10,"VolumeMaPeriod":120,"VolumeMultiplier":3.0,"CheckDays":5,"RequiredExpansionDays":2}',
    1,
    NOW()
);

-- 验证插入结果
SELECT * FROM `Strategy` WHERE `StrategyType` = 'MultiMovingAverage';
