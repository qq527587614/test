using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StockAnalysisSystem.Core.Entities;

/// <summary>
/// 个股业绩（财务摘要/业绩报表）——按报告期维度存储。
/// 数据来源：东方财富 RPT_LICO_FN_CPD。扩展列需先执行 Scripts/stock_earnings_report_alter.sql。
/// </summary>
[Table("stock_earnings_report")]
public sealed class StockEarningsReport
{
    [Key]
    [Column("id")]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long id { get; set; }

    /// <summary>证券代码：同步优先 SECURITY_CODE；无则 SECUCODE。</summary>
    [Column("stock_code")]
    [StringLength(30)]
    public string stock_code { get; set; } = null!;

    [Column("stock_name")]
    [StringLength(100)]
    public string? stock_name { get; set; }

    [Column("report_date")]
    public DateTime report_date { get; set; }

    [Column("notice_date")]
    public DateTime? notice_date { get; set; }

    [Column("revenue", TypeName = "decimal(20,2)")]
    public decimal? revenue { get; set; }

    /// <summary>营收同比（%），接口多为 YSTZ。</summary>
    [Column("revenue_yoy", TypeName = "decimal(10,4)")]
    public decimal? revenue_yoy { get; set; }

    [Column("net_profit", TypeName = "decimal(20,2)")]
    public decimal? net_profit { get; set; }

    /// <summary>净利同比（%），接口多为 SJLTZ。</summary>
    [Column("net_profit_yoy", TypeName = "decimal(10,4)")]
    public decimal? net_profit_yoy { get; set; }

    [Column("net_profit_after_nrgal", TypeName = "decimal(20,2)")]
    public decimal? net_profit_after_nrgal { get; set; }

    [Column("eps", TypeName = "decimal(12,6)")]
    public decimal? eps { get; set; }

    /// <summary>加权 ROE（%），接口多为 WEIGHTAVG_ROE。</summary>
    [Column("roe", TypeName = "decimal(10,4)")]
    public decimal? roe { get; set; }

    /// <summary>营收环比（%），YSHZ。</summary>
    [Column("revenue_qoq", TypeName = "decimal(10,4)")]
    public decimal? revenue_qoq { get; set; }

    /// <summary>净利环比（%），SJLHZ。</summary>
    [Column("net_profit_qoq", TypeName = "decimal(10,4)")]
    public decimal? net_profit_qoq { get; set; }

    [Column("deduct_basic_eps", TypeName = "decimal(12,6)")]
    public decimal? deduct_basic_eps { get; set; }

    [Column("bps", TypeName = "decimal(14,6)")]
    public decimal? bps { get; set; }

    [Column("eps_operating_cf", TypeName = "decimal(14,6)")]
    public decimal? eps_operating_cf { get; set; }

    [Column("gross_margin", TypeName = "decimal(10,4)")]
    public decimal? gross_margin { get; set; }

    [Column("trade_market")]
    [StringLength(64)]
    public string? trade_market { get; set; }

    [Column("security_type")]
    [StringLength(64)]
    public string? security_type { get; set; }

    [Column("org_code")]
    [StringLength(32)]
    public string? org_code { get; set; }

    [Column("board_name")]
    [StringLength(128)]
    public string? board_name { get; set; }

    [Column("board_code")]
    [StringLength(32)]
    public string? board_code { get; set; }

    [Column("qdate")]
    [StringLength(16)]
    public string? qdate { get; set; }

    [Column("period_label")]
    [StringLength(64)]
    public string? period_label { get; set; }

    [Column("datayear")]
    [StringLength(8)]
    public string? datayear { get; set; }

    [Column("publish_name")]
    [StringLength(128)]
    public string? publish_name { get; set; }

    /// <summary>接口 UPDATE_DATE（完整时间）。</summary>
    [Column("update_date_api")]
    public DateTime? update_date_api { get; set; }

    [Column("created_time")]
    public DateTime created_time { get; set; } = DateTime.Now;

    [Column("updated_time")]
    public DateTime updated_time { get; set; } = DateTime.Now;
}
