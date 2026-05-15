namespace StockAnalysisSystem.SyncWorker;

/// <summary>盘后定时同步配置（与「数据管理」手工步骤对齐）。</summary>
public sealed class PostMarketSyncOptions
{
    public const string SectionName = "PostMarketSync";

    /// <summary>触发时刻：小时（本地时间，默认 15）。</summary>
    public int RunHour { get; set; } = 15;

    /// <summary>触发时刻：分钟（默认 5，即 15:05）。</summary>
    public int RunMinute { get; set; } = 5;

    /// <summary>为 true 时跳过周六、周日（节假日仍会在工作日尝试同步）。</summary>
    public bool RunOnlyWeekdays { get; set; } = true;

    public bool EnableRealtimeDaily { get; set; } = true;
    public bool EnableClsLimitUp { get; set; } = true;
    public bool EnablePlateSync { get; set; } = true;
    public bool EnablePlateDailyCalc { get; set; } = true;

    public int RealtimeBatchSize { get; set; } = 100;
}
