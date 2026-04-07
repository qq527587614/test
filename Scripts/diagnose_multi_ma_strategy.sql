-- 诊断均线多头策略未选股问题
-- 执行时间: 2026-04-02

USE StockAnalysisSystem;

-- ========================================
-- 问题1: 检查策略是否存在
-- ========================================
SELECT '=== 问题1: 检查策略是否存在 ====' AS diagnosis;
SELECT
    `Id`,
    `Name`,
    `StrategyType`,
    `IsActive`,
    `Parameters`
FROM `Strategy`
WHERE `StrategyType` = 'MultiMovingAverage';

-- ========================================
-- 问题2: 检查股票数据量（最近几天）
-- ========================================
SELECT '=== 问题2: 检查股票数据量 ====' AS diagnosis;
SELECT
    COUNT(DISTINCT `StockId`) as total_stocks,
    COUNT(*) as total_records
FROM `StockDailyData`
WHERE `TradeDate` >= DATE_SUB(CURDATE(), INTERVAL 150 DAY);

-- ========================================
-- 问题3: 检查技术指标数据量
-- ========================================
SELECT '=== 问题3: 检查技术指标数据量 ====' AS diagnosis;
SELECT
    COUNT(DISTINCT `StockId`) as total_stocks,
    COUNT(*) as total_records,
    COUNT(CASE WHEN `VolumeMA120` IS NOT NULL THEN 1 END) as has_volume_ma120
FROM `StockDailyIndicator`
WHERE `TradeDate` >= DATE_SUB(CURDATE(), INTERVAL 150 DAY);

-- ========================================
-- 问题4: 抽样检查几只股票的具体数据
-- ========================================
SELECT '=== 问题4: 抽样检查具体数据（随机5只股票）===' AS diagnosis;
SELECT
    d.`StockCode`,
    d.`TradeDate`,
    d.`ClosePrice`,
    d.`Volume`,
    i.`MA5`,
    i.`MA10`,
    i.`VolumeMA120`,
    CASE WHEN i.`MA5` > i.`MA10` THEN 1 ELSE 0 END as ma5_gt_ma10,
    CASE WHEN d.`ClosePrice` > i.`MA5` THEN 1 ELSE 0 END as price_gt_ma5,
    CASE WHEN i.`VolumeMA120` IS NOT NULL AND d.`Volume` > i.`VolumeMA120` * 3 THEN 1 ELSE 0 END as volume_expansion_3x,
    i.`VolumeMA120`,
    d.`Volume` / i.`VolumeMA120` as volume_ratio
FROM `StockDailyData` d
LEFT JOIN `StockDailyIndicator` i
    ON d.`StockID` = i.`StockId` AND d.`TradeDate` = i.`TradeDate`
WHERE d.`StockCode` IN (
    SELECT `StockCode`
    FROM `StockDailyData`
    WHERE `TradeDate` >= DATE_SUB(CURDATE(), INTERVAL 150 DAY)
    GROUP BY `StockCode`
    ORDER BY `TradeDate` DESC
    LIMIT 10
)
ORDER BY d.`TradeDate` DESC
LIMIT 50;

-- ========================================
-- 问题5: 统计有多少股票满足选股条件
-- ========================================
SELECT '=== 问题5: 统计满足条件的股票数量（最近30天）===' AS diagnosis;
SELECT
    COUNT(DISTINCT d.`StockCode`) as qualified_stocks
FROM `StockDailyData` d
INNER JOIN `StockDailyIndicator` i
    ON d.`StockID` = i.`StockId` AND d.`TradeDate` = i.`TradeDate`
WHERE d.`TradeDate` >= DATE_SUB(CURDATE(), INTERVAL 30 DAY)
  AND i.`MA5` > i.`MA10`
  AND d.`ClosePrice` > i.`MA5`
  AND i.`VolumeMA120` IS NOT NULL
  AND d.`Volume` > i.`VolumeMA120` * 3;

-- ========================================
-- 问题6: 查看最近选股记录
-- ========================================
SELECT '=== 问题6: 查看最近选股记录 ====' AS diagnosis;
SELECT
    `TradeDate`,
    COUNT(*) as picked_count
FROM `DailyPick`
GROUP BY `TradeDate`
ORDER BY `TradeDate` DESC
LIMIT 10;
