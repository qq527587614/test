using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StockAnalysisSystem.Core.Entities;

/// <summary>
/// 技术指标存储表实体
/// </summary>
[Table("StockDailyIndicator")]
public class StockDailyIndicator
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    [Required]
    [StringLength(36)]
    public string StockId { get; set; } = null!;

    [Required]
    public DateTime TradeDate { get; set; }

    [Column("MA5", TypeName = "decimal(10,4)")]
    public decimal? MA5 { get; set; }

    [Column("MA10", TypeName = "decimal(10,4)")]
    public decimal? MA10 { get; set; }

    [Column("MA20", TypeName = "decimal(10,4)")]
    public decimal? MA20 { get; set; }

    [Column(TypeName = "json")]
    public string? MACD { get; set; }

    [Column(TypeName = "json")]
    public string? KDJ { get; set; }

    [Column("RSI6", TypeName = "decimal(8,4)")]
    public decimal? RSI6 { get; set; }

    [Column("RSI12", TypeName = "decimal(8,4)")]
    public decimal? RSI12 { get; set; }

    [Column(TypeName = "json")]
    public string? BOLL { get; set; }

    [Column("VolumeMA5", TypeName = "decimal(15,2)")]
    public decimal? VolumeMA5 { get; set; }

    [Column("VolumeMA10", TypeName = "decimal(15,2)")]
    public decimal? VolumeMA10 { get; set; }

    /// <summary>
    /// 120日量能均线
    /// </summary>
    [Column("VolumeMA120", TypeName = "decimal(15,2)")]
    public decimal? VolumeMA120 { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    // 映射到实际数据库字段
    [NotMapped]
    public string StockID => StockId;

    // 导航属性
    [ForeignKey("StockId")]
    public virtual StockInfo? Stock { get; set; }
}
