-- 插入首板后回落策略到数据库
-- 首板后回落策略：识别首板后回落到首板最低价附近的股票

USE gudata;

INSERT INTO strategy (name, strategy_type, parameters, description, is_active, created_time, updated_time)
VALUES (
    '首板后回落策略',
    'FirstBoardPullback',
    '{
        "LimitUpThreshold": 9.95,
        "PullbackRange": 0.05,
        "MaxDaysAfterLimitUp": 30,
        "MinDaysAfterLimitUp": 3
    }',
    '首板后回落策略：识别首次涨停后，股价回落到首板最低价附近的买入机会。参数包括：
    - LimitUpThreshold: 涨停阈值（9.95%）
    - PullbackRange: 回落范围（±5%）
    - MaxDaysAfterLimitUp: 首板后最大有效天数（30天）
    - MinDaysAfterLimitUp: 首板后最小等待天数（3天）

    策略逻辑：
    1. 从行情表（stockdailydata）识别首次涨停（首板）并记录最低价
    2. 在首板后3-30天内，股价回落到首板最低价±5%范围内时发出买入信号
    3. 根据距离首板的天数评分，天数越少评分越高（0.5-1.0分）',
    1,
    NOW(),
    NOW()
);

-- 验证插入结果
SELECT id, name, strategy_type, is_active, created_time
FROM strategy
WHERE strategy_type = 'FirstBoardPullback';
