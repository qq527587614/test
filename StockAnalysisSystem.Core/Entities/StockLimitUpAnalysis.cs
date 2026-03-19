using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StockAnalysisSystem.Core.Entities;

/// <summary>
/// 涨停分析表实体
/// </summary>
[Table("stock_limit_up_analysis")]
public class StockLimitUpAnalysis
{
    [Key]
    [Column("id")]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long id { get; set; }

    [Column("code")]
    [StringLength(20)]
    public string code { get; set; } = null!;

    [Column("name")]
    [StringLength(100)]
    public string name { get; set; } = null!;

    [Column("close", TypeName = "decimal(10,2)")]
    public decimal? close { get; set; }

    [Column("pct_chg", TypeName = "decimal(8,4)")]
    public decimal? pct_chg { get; set; }

    [Column("turn", TypeName = "decimal(8,4)")]
    public decimal? turn { get; set; }

    [Column("amount", TypeName = "decimal(15,2)")]
    public decimal? amount { get; set; }

    [Column("float_market_capital", TypeName = "decimal(15,2)")]
    public decimal? float_market_capital { get; set; }

    [Column("total_market_capital", TypeName = "decimal(15,2)")]
    public decimal? total_market_capital { get; set; }

    [Column("plate_code")]
    [StringLength(20)]
    public string? plate_code { get; set; }

    [Column("plate_name")]
    [StringLength(100)]
    public string? plate_name { get; set; }

    [Column("first_limit_up_time")]
    [StringLength(20)]
    public string? first_limit_up_time { get; set; }

    [Column("last_limit_up_time")]
    [StringLength(20)]
    public string? last_limit_up_time { get; set; }

    [Column("limit_up_times")]
    [StringLength(20)]
    public string? limit_up_times { get; set; }

    [Column("limit_up_type")]
    [StringLength(20)]
    public string? limit_up_type { get; set; }

    [Column("limit_up_strength", TypeName = "decimal(8,4)")]
    public decimal? limit_up_strength { get; set; }

    [Column("continuous_boards")]
    [StringLength(20)]
    public string continuous_boards { get; set; } = "0";

    [Column("days_limit_up_count")]
    [StringLength(20)]
    public string days_limit_up_count { get; set; } = "0";

    [Column("analysis_date")]
    public DateTime analysis_date { get; set; }

    [Column("created_time")]
    public DateTime created_time { get; set; } = DateTime.Now;

    [Column("updated_time")]
    public DateTime updated_time { get; set; } = DateTime.Now;

    // 兼容旧代码的属性（映射到实际字段）
    [NotMapped]
    public long Id => id;

    [NotMapped]
    public string StockCode => code;

    [NotMapped]
    public string StockName => name;

    [NotMapped]
    public DateTime TradeDate => analysis_date;

    [NotMapped]
    public decimal? ClosePrice => close;

    [NotMapped]
    public decimal? ChangePercent => pct_chg;

    [NotMapped]
    public decimal? TurnoverRate => turn;

    [NotMapped]
    public decimal? Volume => amount;

    [NotMapped]
    public decimal? CirculationValue => float_market_capital;
}
