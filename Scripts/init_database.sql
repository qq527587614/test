-- 股票分析系统数据库迁移脚本
-- 执行前请确保数据库已存在

-- 1. 策略表
CREATE TABLE IF NOT EXISTS `Strategy` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `Name` varchar(100) NOT NULL COMMENT '策略名称',
  `Description` text COMMENT '策略描述',
  `StrategyType` varchar(50) NOT NULL COMMENT '策略类型（如MovingAverageCross, MACDCross等）',
  `Parameters` json NOT NULL COMMENT '参数定义（如{"maShort":5,"maLong":20,"rsiThreshold":30}）',
  `IsActive` tinyint(1) DEFAULT '1',
  `CreatedAt` datetime DEFAULT CURRENT_TIMESTAMP,
  `UpdatedAt` datetime DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='策略配置表';

-- 2. 回测任务表
CREATE TABLE IF NOT EXISTS `BacktestTask` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `StrategyId` int NOT NULL COMMENT '关联的策略ID',
  `StartDate` date NOT NULL,
  `EndDate` date NOT NULL,
  `Parameters` json NOT NULL COMMENT '实际使用的参数快照',
  `InitialCapital` decimal(15,2) DEFAULT '1000000.00',
  `Status` varchar(20) DEFAULT 'Pending' COMMENT 'Pending, Running, Completed, Failed',
  `Result` json COMMENT '回测结果（总收益率、年化、最大回撤、夏普等）',
  `TradeLog` json COMMENT '交易记录列表',
  `CreatedAt` datetime DEFAULT CURRENT_TIMESTAMP,
  `CompletedAt` datetime DEFAULT NULL,
  PRIMARY KEY (`Id`),
  KEY `idx_strategy` (`StrategyId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='回测任务表';

-- 3. 策略优化任务表
CREATE TABLE IF NOT EXISTS `OptimizationTask` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `StrategyType` varchar(50) NOT NULL COMMENT '策略类型',
  `ParameterRanges` json NOT NULL COMMENT '参数搜索范围（如{"maShort":{"min":5,"max":20,"step":5}}）',
  `StartDate` date NOT NULL,
  `EndDate` date NOT NULL,
  `FitnessFunction` varchar(50) DEFAULT 'AnnualReturn' COMMENT '适应度函数（AnnualReturn, SharpeRatio等）',
  `Status` varchar(20) DEFAULT 'Pending',
  `BestParameters` json COMMENT '最优参数',
  `BestResult` json COMMENT '最优绩效',
  `CreatedAt` datetime DEFAULT CURRENT_TIMESTAMP,
  `CompletedAt` datetime DEFAULT NULL,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='策略优化任务表';

-- 4. 每日选股结果表
CREATE TABLE IF NOT EXISTS `DailyPick` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `TradeDate` date NOT NULL COMMENT '选股日期',
  `StockId` varchar(36) NOT NULL,
  `StockCode` varchar(20) NOT NULL,
  `StockName` varchar(100) NOT NULL,
  `StrategyId` int NOT NULL,
  `SignalType` varchar(10) NOT NULL COMMENT 'Buy/Sell',
  `Reason` text COMMENT '触发条件',
  `DeepSeekScore` decimal(3,2) DEFAULT NULL COMMENT 'DeepSeek评分（0-100）',
  `FinalScore` decimal(3,2) DEFAULT NULL COMMENT '最终排序分数',
  `CreatedAt` datetime DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`Id`),
  UNIQUE KEY `uk_date_stock_strategy` (`TradeDate`,`StockId`,`StrategyId`),
  KEY `idx_date` (`TradeDate`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='每日选股结果表';

-- 5. DeepSeek调用日志表
CREATE TABLE IF NOT EXISTS `DeepSeekLog` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `RequestData` json NOT NULL,
  `ResponseData` json NOT NULL,
  `UsedFor` varchar(50) NOT NULL,
  `CreatedAt` datetime DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='DeepSeek API调用日志表';

-- 6. 技术指标存储表
CREATE TABLE IF NOT EXISTS `StockDailyIndicator` (
  `Id` bigint NOT NULL AUTO_INCREMENT,
  `StockId` varchar(36) NOT NULL,
  `TradeDate` date NOT NULL,
  `MA5` decimal(10,4) DEFAULT NULL,
  `MA10` decimal(10,4) DEFAULT NULL,
  `MA20` decimal(10,4) DEFAULT NULL,
  `MACD` json DEFAULT NULL COMMENT '存储DIF,DEA,MACD',
  `KDJ` json DEFAULT NULL COMMENT '存储K,D,J',
  `RSI6` decimal(8,4) DEFAULT NULL,
  `RSI12` decimal(8,4) DEFAULT NULL,
  `BOLL` json DEFAULT NULL COMMENT '存储Upper,Middle,Lower',
  `VolumeMA5` decimal(15,2) DEFAULT NULL,
  `VolumeMA10` decimal(15,2) DEFAULT NULL,
  `CreatedAt` datetime DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`Id`),
  UNIQUE KEY `uk_stock_date` (`StockId`,`TradeDate`),
  KEY `idx_date` (`TradeDate`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='技术指标存储表';

-- 插入默认策略
INSERT INTO `Strategy` (`Name`, `Description`, `StrategyType`, `Parameters`, `IsActive`) VALUES
('均线交叉策略-5日20日', '短期均线上穿长期均线买入，下穿卖出', 'MovingAverageCross', '{"ShortPeriod": 5, "LongPeriod": 20}', 1),
('MACD金叉死叉策略', 'MACD金叉买入，死叉卖出', 'MACDCross', '{"FastPeriod": 12, "SlowPeriod": 26, "SignalPeriod": 9}', 1),
('RSI超卖策略', 'RSI低于30买入，高于70卖出', 'RSIOverSold', '{"Period": 6, "OversoldThreshold": 30, "OverboughtThreshold": 70}', 1);

-- 7. 自选股表
CREATE TABLE IF NOT EXISTS `stockfavorite` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `StockCode` varchar(30) NOT NULL COMMENT '股票代码',
  `AddedDate` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '添加日期',
  `Remark` varchar(200) DEFAULT NULL COMMENT '备注',
  PRIMARY KEY (`Id`),
  UNIQUE KEY `uk_stockcode` (`StockCode`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='自选股表';

-- 8. 板块表
CREATE TABLE IF NOT EXISTS `plate` (
  `id` bigint NOT NULL AUTO_INCREMENT,
  `plate_code` varchar(20) NOT NULL COMMENT '板块代码',
  `plate_name` varchar(100) NOT NULL COMMENT '板块名称',
  `plate_type` varchar(20) DEFAULT NULL COMMENT '板块类型',
  `created_time` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_time` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_plate_code` (`plate_code`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='板块表';

-- 9. 板块成分股表
CREATE TABLE IF NOT EXISTS `plate_stock` (
  `id` bigint NOT NULL AUTO_INCREMENT,
  `plate_id` bigint NOT NULL COMMENT '板块ID',
  `stock_code` varchar(20) NOT NULL COMMENT '股票代码',
  `stock_name` varchar(100) NOT NULL COMMENT '股票名称',
  `join_date` date NOT NULL COMMENT '加入日期',
  `created_time` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_plate_stock` (`plate_id`,`stock_code`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='板块成分股表';

-- 10. 板块日线数据表
CREATE TABLE IF NOT EXISTS `plate_daily_data` (
  `id` bigint NOT NULL AUTO_INCREMENT,
  `plate_id` bigint NOT NULL COMMENT '板块ID',
  `trade_date` date NOT NULL COMMENT '交易日期',
  `stock_count` int NOT NULL COMMENT '股票数量',
  `limit_up_count` int NOT NULL DEFAULT '0' COMMENT '涨停数量',
  `avg_pct_chg` decimal(10,4) DEFAULT NULL COMMENT '平均涨跌幅',
  `total_amount` decimal(18,2) DEFAULT NULL COMMENT '总成交额',
  `avg_turnover` decimal(10,4) DEFAULT NULL COMMENT '平均换手率',
  `created_time` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_time` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_plate_date` (`plate_id`,`trade_date`),
  KEY `idx_trade_date` (`trade_date`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='板块日线数据表';
