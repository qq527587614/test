using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace StockAnalysisSystem.Core.Entities;

/// <summary>
/// 回测任务表实体
/// </summary>
[Table("BacktestTask")]
public class BacktestTask
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int StrategyId { get; set; }

    [Required]
    public DateTime StartDate { get; set; }

    [Required]
    public DateTime EndDate { get; set; }

    [Required]
    [Column(TypeName = "json")]
    public string Parameters { get; set; } = "{}";

    [Column("InitialCapital", TypeName = "decimal(15,2)")]
    public decimal InitialCapital { get; set; } = 1000000m;

    [StringLength(20)]
    public string Status { get; set; } = "Pending";

    [Column(TypeName = "json")]
    public string? Result { get; set; }

    [Column(TypeName = "json")]
    public string? TradeLog { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime? CompletedAt { get; set; }

    // 导航属性
    [ForeignKey("StrategyId")]
    public virtual Strategy? Strategy { get; set; }

    // 辅助属性
    [NotMapped]
    public Dictionary<string, object>? ResultDict
    {
        get => Result != null ? JsonSerializer.Deserialize<Dictionary<string, object>>(Result) : null;
        set => Result = value != null ? JsonSerializer.Serialize(value) : null;
    }
}
