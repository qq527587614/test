-- 业绩表扩展列（与 StockEarningsReport 实体一致）。执行一次即可；重复执行会跳过已存在的列。
-- MySQL 5.7+ / 8.0+

DELIMITER $$

DROP PROCEDURE IF EXISTS add_earnings_column$$
CREATE PROCEDURE add_earnings_column(IN col_name VARCHAR(64), IN ddl VARCHAR(2048))
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'stock_earnings_report'
      AND COLUMN_NAME = col_name
  ) THEN
    SET @s = ddl;
    PREPARE stmt FROM @s;
    EXECUTE stmt;
    DEALLOCATE PREPARE stmt;
  END IF;
END$$

DELIMITER ;

CALL add_earnings_column('revenue_qoq', 'ALTER TABLE stock_earnings_report ADD COLUMN revenue_qoq DECIMAL(10,4) NULL COMMENT ''YSHZ 营收环比%''');
CALL add_earnings_column('net_profit_qoq', 'ALTER TABLE stock_earnings_report ADD COLUMN net_profit_qoq DECIMAL(10,4) NULL COMMENT ''SJLHZ 净利环比%''');
CALL add_earnings_column('deduct_basic_eps', 'ALTER TABLE stock_earnings_report ADD COLUMN deduct_basic_eps DECIMAL(12,6) NULL COMMENT ''DEDUCT_BASIC_EPS''');
CALL add_earnings_column('bps', 'ALTER TABLE stock_earnings_report ADD COLUMN bps DECIMAL(14,6) NULL COMMENT ''BPS''');
CALL add_earnings_column('eps_operating_cf', 'ALTER TABLE stock_earnings_report ADD COLUMN eps_operating_cf DECIMAL(14,6) NULL COMMENT ''MGJYXJJE''');
CALL add_earnings_column('gross_margin', 'ALTER TABLE stock_earnings_report ADD COLUMN gross_margin DECIMAL(10,4) NULL COMMENT ''XSMLL 销售毛利率%''');
CALL add_earnings_column('trade_market', 'ALTER TABLE stock_earnings_report ADD COLUMN trade_market VARCHAR(64) NULL COMMENT ''TRADE_MARKET''');
CALL add_earnings_column('security_type', 'ALTER TABLE stock_earnings_report ADD COLUMN security_type VARCHAR(64) NULL COMMENT ''SECURITY_TYPE''');
CALL add_earnings_column('org_code', 'ALTER TABLE stock_earnings_report ADD COLUMN org_code VARCHAR(32) NULL COMMENT ''ORG_CODE''');
CALL add_earnings_column('board_name', 'ALTER TABLE stock_earnings_report ADD COLUMN board_name VARCHAR(128) NULL COMMENT ''BOARD_NAME''');
CALL add_earnings_column('board_code', 'ALTER TABLE stock_earnings_report ADD COLUMN board_code VARCHAR(32) NULL COMMENT ''BOARD_CODE''');
CALL add_earnings_column('qdate', 'ALTER TABLE stock_earnings_report ADD COLUMN qdate VARCHAR(16) NULL COMMENT ''QDATE''');
CALL add_earnings_column('period_label', 'ALTER TABLE stock_earnings_report ADD COLUMN period_label VARCHAR(64) NULL COMMENT ''DATATYPE''');
CALL add_earnings_column('datayear', 'ALTER TABLE stock_earnings_report ADD COLUMN datayear VARCHAR(8) NULL COMMENT ''DATAYEAR''');
CALL add_earnings_column('publish_name', 'ALTER TABLE stock_earnings_report ADD COLUMN publish_name VARCHAR(128) NULL COMMENT ''PUBLISHNAME''');
CALL add_earnings_column('update_date_api', 'ALTER TABLE stock_earnings_report ADD COLUMN update_date_api DATETIME NULL COMMENT ''UPDATE_DATE''');

DROP PROCEDURE IF EXISTS add_earnings_column;
