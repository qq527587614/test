using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StockAnalysisSystem.Core.Entities;

/// <summary>
/// 每日选股结果表实体
/// </summary>
[Table("DailyPick")]
public class DailyPick
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    public DateTime TradeDate { get; set; }

    [Required]
    [StringLength(36)]
    public string StockId { get; set; } = null!;

    [Required]
    [StringLength(20)]
    public string StockCode { get; set; } = null!;

    [Required]
    [StringLength(100)]
    public string StockName { get; set; } = null!;

    public int StrategyId { get; set; }

    [Required]
    [StringLength(10)]
    public string SignalType { get; set; } = null!;

    public string? Reason { get; set; }

    [Column("DeepSeekScore", TypeName = "decimal(5,2)")]
    public decimal? DeepSeekScore { get; set; }

    [Column("FinalScore", TypeName = "decimal(5,2)")]
    public decimal? FinalScore { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    // 映射到实际数据库字段
    [NotMapped]
    public string StockID => StockId;

    // 导航属性
    [ForeignKey("StrategyId")]
    public virtual Strategy? Strategy { get; set; }
}
