using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StockAnalysisSystem.Core.Entities;

/// <summary>
/// 板块表
/// </summary>
[Table("plate")]
public class Plate
{
    [Key]
    [Column("id")]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long id { get; set; }

    [Column("plate_code")]
    [StringLength(20)]
    public string plate_code { get; set; } = null!;

    [Column("plate_name")]
    [StringLength(100)]
    public string plate_name { get; set; } = null!;

    [Column("plate_type")]
    [StringLength(20)]
    public string? plate_type { get; set; }

    [Column("created_time")]
    public DateTime created_time { get; set; } = DateTime.Now;

    [Column("updated_time")]
    public DateTime updated_time { get; set; } = DateTime.Now;

    // 导航属性
    public virtual ICollection<PlateStock> Stocks { get; set; } = new List<PlateStock>();
    public virtual ICollection<PlateDailyData> DailyData { get; set; } = new List<PlateDailyData>();
}

/// <summary>
/// 板块成分股表
/// </summary>
[Table("plate_stock")]
public class PlateStock
{
    [Key]
    [Column("id")]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long id { get; set; }

    [Column("plate_id")]
    public long plate_id { get; set; }

    [Column("stock_code")]
    [StringLength(20)]
    public string stock_code { get; set; } = null!;

    [Column("stock_name")]
    [StringLength(100)]
    public string stock_name { get; set; } = null!;

    [Column("join_date")]
    public DateTime join_date { get; set; } = DateTime.Now;

    [Column("created_time")]
    public DateTime created_time { get; set; } = DateTime.Now;

    // 导航属性
    [ForeignKey("plate_id")]
    public virtual Plate? Plate { get; set; }
}

/// <summary>
/// 板块日线表
/// </summary>
[Table("plate_daily_data")]
public class PlateDailyData
{
    [Key]
    [Column("id")]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long id { get; set; }

    [Column("plate_id")]
    public long plate_id { get; set; }

    [Column("trade_date")]
    public DateTime trade_date { get; set; }

    [Column("stock_count")]
    public int stock_count { get; set; }

    [Column("limit_up_count")]
    public int limit_up_count { get; set; }

    [Column("avg_pct_chg", TypeName = "decimal(10,4)")]
    public decimal? avg_pct_chg { get; set; }

    [Column("total_amount", TypeName = "decimal(18,2)")]
    public decimal? total_amount { get; set; }

    [Column("avg_turnover", TypeName = "decimal(10,4)")]
    public decimal? avg_turnover { get; set; }

    [Column("created_time")]
    public DateTime created_time { get; set; } = DateTime.Now;

    [Column("updated_time")]
    public DateTime updated_time { get; set; } = DateTime.Now;

    // 导航属性
    [ForeignKey("plate_id")]
    public virtual Plate? Plate { get; set; }
}