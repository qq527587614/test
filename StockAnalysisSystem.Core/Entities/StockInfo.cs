using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StockAnalysisSystem.Core.Entities;

/// <summary>
/// 股票基本信息表实体
/// </summary>
[Table("stockinfo")]
public class StockInfo
{
    [Key]
    [Column("StockID")]
    [StringLength(36)]
    public string StockID { get; set; } = null!;

    [Column("StockName")]
    [StringLength(100)]
    public string StockName { get; set; } = null!;

    [Column("StockCode")]
    [StringLength(50)]
    public string StockCode { get; set; } = null!;

    [Column("Market")]
    [StringLength(10)]
    public string Market { get; set; } = null!;

    [Column("Industry")]
    [StringLength(50)]
    public string? Industry { get; set; }

    [Column("ListingDate")]
    public DateTime? ListingDate { get; set; }

    [Column("CreatedTime")]
    public DateTime CreatedTime { get; set; } = DateTime.Now;

    // 兼容旧代码的属性（映射到实际字段）
    [NotMapped]
    public string Id => StockID;

    [NotMapped]
    public DateTime? ListDate => ListingDate;

    [NotMapped]
    public string? Sector { get; set; }  // 兼容旧代码，但数据库表中没有此字段

    [NotMapped]
    public decimal? CirculationValue { get; set; }  // 兼容旧代码，但数据库表中没有此字段

    // 导航属性
    public virtual ICollection<StockDailyData> DailyData { get; set; } = new List<StockDailyData>();
    public virtual ICollection<StockDailyIndicator> Indicators { get; set; } = new List<StockDailyIndicator>();
}
