-- 首板后回落策略诊断脚本
-- 用于检查数据库中的涨停数据，验证策略的有效性

USE stock_analysis;

-- 1. 查询最近30天的首板股票
SELECT
    code AS 股票代码,
    name AS 股票名称,
    close AS 收盘价,
    pct_chg AS 涨跌幅,
    analysis_date AS 交易日期,
    first_limit_up_time AS 首板时间,
    last_limit_up_time AS 最后涨停时间,
    limit_up_times AS 涨停次数,
    continuous_boards AS 连板数
FROM stock_limit_up_analysis
WHERE analysis_date >= DATE_SUB(CURDATE(), INTERVAL 30 DAY)
  AND limit_up_times = '1'
  AND pct_chg >= 9.95
ORDER BY analysis_date DESC, pct_chg DESC
LIMIT 50;

-- 2. 统计最近30天的首板数量
SELECT
    DATE(analysis_date) AS 日期,
    COUNT(*) AS 首板数量,
    AVG(pct_chg) AS 平均涨幅,
    MIN(close) AS 最低价,
    MAX(close) AS 最高价
FROM stock_limit_up_analysis
WHERE analysis_date >= DATE_SUB(CURDATE(), INTERVAL 30 DAY)
  AND limit_up_times = '1'
  AND pct_chg >= 9.95
GROUP BY DATE(analysis_date)
ORDER BY 日期 DESC;

-- 3. 查询特定股票的首板及后续走势
-- 替换 '000001' 为要查询的股票代码
SELECT
    sdd.StockCode AS 股票代码,
    sdd.TradeDate AS 交易日期,
    sdd.OpenPrice AS 开盘价,
    sdd.ClosePrice AS 收盘价,
    sdd.HighPrice AS 最高价,
    sdd.LowPrice AS 最低价,
    sdd.ChangePercent AS 涨跌幅,
    DATEDIFF(sdd.TradeDate, (
        SELECT MIN(analysis_date)
        FROM stock_limit_up_analysis
        WHERE code = sdd.StockCode
          AND limit_up_times = '1'
          AND pct_chg >= 9.95
    )) AS 距首板天数
FROM stockdailydata sdd
WHERE sdd.StockCode = '000001'
  AND sdd.TradeDate >= (
      SELECT MIN(analysis_date)
      FROM stock_limit_up_analysis
      WHERE code = '000001'
        AND limit_up_times = '1'
        AND pct_chg >= 9.95
  )
  AND sdd.TradeDate <= DATE_ADD((
      SELECT MIN(analysis_date)
      FROM stock_limit_up_analysis
      WHERE code = '000001'
        AND limit_up_times = '1'
        AND pct_chg >= 9.95
  ), INTERVAL 30 DAY)
ORDER BY sdd.TradeDate;

-- 4. 模拟策略信号（最近30天）
-- 查找在首板后3-30天内回落到首板最低价±5%的股票
WITH first_boards AS (
    SELECT
        code,
        analysis_date AS first_board_date,
        MIN(close) AS first_board_low,
        MIN(low) as day_low
    FROM stock_limit_up_analysis
    WHERE limit_up_times = '1'
      AND pct_chg >= 9.95
      AND analysis_date >= DATE_SUB(CURDATE(), INTERVAL 60 DAY)
    GROUP BY code, analysis_date
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
    INNER JOIN first_boards fb ON sdd.StockCode = fb.code
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
            code,
            MIN(analysis_date) AS first_board_date,
            MIN(close) AS first_board_low
        FROM stock_limit_up_analysis
        WHERE limit_up_times = '1'
          AND pct_chg >= 9.95
          AND analysis_date >= DATE_SUB(CURDATE(), INTERVAL 90 DAY)
        GROUP BY code
    ) fb ON sdd.StockCode = fb.code
    WHERE sdd.TradeDate > fb.first_board_date
      AND sdd.TradeDate <= DATE_ADD(fb.first_board_date, INTERVAL 30 DAY)
      AND DATEDIFF(sdd.TradeDate, fb.first_board_date) BETWEEN 3 AND 30
) dp;

-- 6. 检查数据完整性
SELECT
    '检查1: 涨停表中是否有数据' AS 检查项,
    COUNT(*) AS 结果
FROM stock_limit_up_analysis
UNION ALL
SELECT
    '检查2: 首板数据数量' AS 检查项,
    COUNT(*) AS 结果
FROM stock_limit_up_analysis
WHERE limit_up_times = '1'
UNION ALL
SELECT
    '检查3: 最近30天首板数量' AS 检查项,
    COUNT(*) AS 结果
FROM stock_limit_up_analysis
WHERE limit_up_times = '1'
  AND analysis_date >= DATE_SUB(CURDATE(), INTERVAL 30 DAY)
UNION ALL
SELECT
    '检查4: 日线数据完整性' AS 检查项,
    COUNT(DISTINCT StockCode) AS 结果
FROM stockdailydata
WHERE TradeDate >= DATE_SUB(CURDATE(), INTERVAL 30 DAY);
