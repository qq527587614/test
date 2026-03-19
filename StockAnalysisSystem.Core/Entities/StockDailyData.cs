using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StockAnalysisSystem.Core.Entities;

/// <summary>
/// 股票日线数据表实体
/// </summary>
[Table("stockdailydata")]
public class StockDailyData
{
    [Key]
    [Column("ID")]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long ID { get; set; }

    [Column("StockID")]
    [StringLength(36)]
    public string StockID { get; set; } = null!;

    [Column("StockCode")]
    [StringLength(30)]
    public string StockCode { get; set; } = null!;

    [Column("TradeDate")]
    public DateTime TradeDate { get; set; }

    [Column("OpenPrice", TypeName = "decimal(10,4)")]
    public decimal OpenPrice { get; set; }

    [Column("ClosePrice", TypeName = "decimal(10,4)")]
    public decimal ClosePrice { get; set; }

    [Column("HighPrice", TypeName = "decimal(10,4)")]
    public decimal HighPrice { get; set; }

    [Column("LowPrice", TypeName = "decimal(10,4)")]
    public decimal LowPrice { get; set; }

    [Column("Volume", TypeName = "decimal(15,2)")]
    public decimal Volume { get; set; }

    [Column("Amount", TypeName = "decimal(15,2)")]
    public decimal Amount { get; set; }

    [Column("ChangePercent", TypeName = "decimal(8,4)")]
    public decimal? ChangePercent { get; set; }

    [Column("TurnoverRate", TypeName = "decimal(10,2)")]
    public decimal? TurnoverRate { get; set; }

    [Column("CurrentPrice", TypeName = "decimal(10,4)")]
    public decimal? CurrentPrice { get; set; }

    [Column("BeforDate")]
    public DateTime? BeforDate { get; set; }

    [Column("CreatedTime")]
    public DateTime CreatedTime { get; set; } = DateTime.Now;

    // 兼容旧代码的属性（映射到实际字段）
    [NotMapped]
    public string StockId => StockID;

    [NotMapped]
    public long Id => ID;

    // 导航属性
    [ForeignKey("StockID")]
    public virtual StockInfo? Stock { get; set; }
}
