-- 首板后回落策略诊断脚本
-- 从行情表（stockdailydata）识别首板，验证策略的有效性

USE gudata;

-- 1. 查询最近30天的首板股票（从行情表中识别）
SELECT
    StockCode AS 股票代码,
    TradeDate AS 首板日期,
    ClosePrice AS 收盘价,
    ChangePercent AS 涨跌幅,
    LowPrice AS 最低价,
    OpenPrice AS 开盘价
FROM stockdailydata
WHERE ChangePercent >= 9.95
  AND TradeDate >= DATE_SUB(CURDATE(), INTERVAL 30 DAY)
ORDER BY TradeDate DESC, ChangePercent DESC
LIMIT 50;

-- 2. 统计最近30天的首板数量
SELECT
    DATE(TradeDate) AS 日期,
    COUNT(*) AS 首板数量,
    AVG(ChangePercent) AS 平均涨幅,
    MIN(ClosePrice) AS 最低价,
    MAX(ClosePrice) AS 最高价
FROM stockdailydata
WHERE ChangePercent >= 9.95
  AND TradeDate >= DATE_SUB(CURDATE(), INTERVAL 30 DAY)
GROUP BY DATE(TradeDate)
ORDER BY 日期 DESC;

-- 3. 查询特定股票的首板及后续走势
-- 替换 '000001' 为要查询的股票代码
WITH first_board AS (
    SELECT
        StockCode,
        MIN(TradeDate) AS first_board_date,
        MIN(LowPrice) AS first_board_low
    FROM stockdailydata
    WHERE StockCode = '000001'
      AND ChangePercent >= 9.95
    GROUP BY StockCode
)
SELECT
    sdd.StockCode AS 股票代码,
    sdd.TradeDate AS 交易日期,
    sdd.OpenPrice AS 开盘价,
    sdd.ClosePrice AS 收盘价,
    sdd.HighPrice AS 最高价,
    sdd.LowPrice AS 最低价,
    sdd.ChangePercent AS 涨跌幅,
    fb.first_board_date AS 首板日期,
    fb.first_board_low AS 首板最低价,
    DATEDIFF(sdd.TradeDate, fb.first_board_date) AS 距首板天数
FROM stockdailydata sdd
INNER JOIN first_board fb ON sdd.StockCode = fb.StockCode
WHERE sdd.TradeDate >= fb.first_board_date
  AND sdd.TradeDate <= DATE_ADD(fb.first_board_date, INTERVAL 30 DAY)
ORDER BY sdd.TradeDate;

-- 4. 模拟策略信号（最近30天）
-- 查找在首板后3-30天内回落到首板最低价±5%的股票
WITH first_boards AS (
    SELECT
        StockCode,
        MIN(TradeDate) AS first_board_date,
        MIN(LowPrice) AS first_board_low
    FROM stockdailydata
    WHERE ChangePercent >= 9.95
      AND TradeDate >= DATE_SUB(CURDATE(), INTERVAL 60 DAY)
    GROUP BY StockCode
),
daily_prices AS (
    SELECT
        sdd.StockCode,
        sdd.TradeDate,
        sdd.ClosePrice,
        sdd.LowPrice,
        fb.first_board_date,
        fb.first_board_low,
        DATEDIFF(sdd.TradeDate, fb.first_board_date) AS days_after
    FROM stockdailydata sdd
    INNER JOIN first_boards fb ON sdd.StockCode = fb.StockCode
    WHERE sdd.TradeDate > fb.first_board_date
      AND sdd.TradeDate <= DATE_ADD(fb.first_board_date, INTERVAL 30 DAY)
)
SELECT
    dp.StockCode AS 股票代码,
    dp.first_board_date AS 首板日期,
    dp.days_after AS 距首板天数,
    dp.ClosePrice AS 当前收盘价,
    dp.first_board_low AS 首板最低价,
    ROUND((dp.ClosePrice - dp.first_board_low) / dp.first_board_low * 100, 2) AS 偏差百分比,
    CASE
        WHEN ABS((dp.ClosePrice - dp.first_board_low) / dp.first_board_low) <= 0.05 THEN '✓ 符合'
        ELSE '✗ 不符合'
    END AS 是否符合策略
FROM daily_prices dp
WHERE dp.days_after BETWEEN 3 AND 30
  AND dp.TradeDate >= DATE_SUB(CURDATE(), INTERVAL 30 DAY)
ORDER BY dp.TradeDate DESC, dp.days_after
LIMIT 100;

-- 5. 统计策略命中率（基于历史数据模拟）
SELECT
    COUNT(*) AS 总信号数,
    SUM(CASE
        WHEN ABS((dp.ClosePrice - dp.first_board_low) / dp.first_board_low) <= 0.05 THEN 1
        ELSE 0
    END) AS 符合策略数,
    ROUND(SUM(CASE
        WHEN ABS((dp.ClosePrice - dp.first_board_low) / dp.first_board_low) <= 0.05 THEN 1
        ELSE 0
    END) * 100.0 / COUNT(*), 2) AS 命中率
FROM (
    SELECT
        sdd.StockCode,
        sdd.TradeDate,
        sdd.ClosePrice,
        fb.first_board_low,
        DATEDIFF(sdd.TradeDate, fb.first_board_date) AS days_after
    FROM stockdailydata sdd
    INNER JOIN (
        SELECT
            StockCode,
            MIN(TradeDate) AS first_board_date,
            MIN(LowPrice) AS first_board_low
        FROM stockdailydata
        WHERE ChangePercent >= 9.95
          AND TradeDate >= DATE_SUB(CURDATE(), INTERVAL 90 DAY)
        GROUP BY StockCode
    ) fb ON sdd.StockCode = fb.StockCode
    WHERE sdd.TradeDate > fb.first_board_date
      AND sdd.TradeDate <= DATE_ADD(fb.first_board_date, INTERVAL 30 DAY)
      AND DATEDIFF(sdd.TradeDate, fb.first_board_date) BETWEEN 3 AND 30
) dp;

-- 6. 检查数据完整性
SELECT
    '检查1: 最近30天首板数量' AS 检查项,
    COUNT(*) AS 结果
FROM stockdailydata
WHERE ChangePercent >= 9.95
  AND TradeDate >= DATE_SUB(CURDATE(), INTERVAL 30 DAY)
UNION ALL
SELECT
    '检查2: 日线数据完整性' AS 检查项,
    COUNT(DISTINCT StockCode) AS 结果
FROM stockdailydata
WHERE TradeDate >= DATE_SUB(CURDATE(), INTERVAL 30 DAY)
UNION ALL
SELECT
    '检查3: 有涨跌幅数据的天数' AS 检查项,
    COUNT(*) AS 结果
FROM stockdailydata
WHERE TradeDate >= DATE_SUB(CURDATE(), INTERVAL 30 DAY)
  AND ChangePercent IS NOT NULL;
