namespace StockAnalysisSystem.Core.Entities;

/// <summary>
/// 股票分析建议
/// </summary>
public enum AnalysisRecommendation
{
    强烈买入 = 5,
    建议买入 = 4,
    持有观望 = 3,
    建议卖出 = 2,
    强烈卖出 = 1
}

/// <summary>
/// 股票分析结果
/// </summary>
public class StockAnalysisResult
{
    public string StockCode { get; set; } = "";
    public string StockName { get; set; } = "";
    public string Market { get; set; } = "";

    /// <summary>
    /// 综合评分 (0-100)
    /// </summary>
    public int Score { get; set; }

    /// <summary>
    /// 建议
    /// </summary>
    public AnalysisRecommendation Recommendation { get; set; }

    /// <summary>
    /// 分析时间
    /// </summary>
    public DateTime AnalysisTime { get; set; } = DateTime.Now;

    /// <summary>
    /// 各指标信号详情
    /// </summary>
    public string SignalDetails { get; set; } = "";

    // === 各指标评分 ===
    public int MaScore { get; set; }         // MA评分 (20分)
    public int MacdScore { get; set; }       // MACD评分 (25分)
    public int RsiScore { get; set; }        // RSI评分 (20分)
    public int KdjScore { get; set; }        // KDJ评分 (15分)
    public int BollScore { get; set; }       // BOLL评分 (10分)
    public int VolumeScore { get; set; }     // 成交量评分 (10分)

    // === 指标详情 ===
    public decimal? ClosePrice { get; set; }     // 最新收盘价
    public decimal? Ma5 { get; set; }
    public decimal? Ma10 { get; set; }
    public decimal? Ma20 { get; set; }
    public decimal? MacdDif { get; set; }
    public decimal? MacdDea { get; set; }
    public decimal? MacdValue { get; set; }
    public decimal? Rsi6 { get; set; }
    public decimal? Rsi12 { get; set; }
    public decimal? K { get; set; }
    public decimal? D { get; set; }
    public decimal? J { get; set; }
    public decimal? BollUpper { get; set; }
    public decimal? BollMiddle { get; set; }
    public decimal? BollLower { get; set; }
    public decimal? Volume { get; set; }
    public decimal? VolumeMa { get; set; }
    public decimal? ChangePercent { get; set; }

    /// <summary>
    /// 获取建议文本
    /// </summary>
    public string GetRecommendationText()
    {
        return Recommendation switch
        {
            AnalysisRecommendation.强烈买入 => "强烈买入 ⭐⭐⭐",
            AnalysisRecommendation.建议买入 => "建议买入 ⭐⭐",
            AnalysisRecommendation.持有观望 => "持有观望",
            AnalysisRecommendation.建议卖出 => "建议卖出 ⭐",
            AnalysisRecommendation.强烈卖出 => "强烈卖出 ⭐⭐⭐",
            _ => "未知"
        };
    }

    /// <summary>
    /// 获取评分描述
    /// </summary>
    public string GetScoreDescription()
    {
        return Score switch
        {
            >= 80 => "强势信号，建议买入",
            >= 60 => "偏多信号，可以考虑买入",
            >= 40 => "中性信号，建议观望",
            >= 20 => "偏空信号，注意风险",
            _ => "弱势信号，建议卖出",
        };
    }
}
