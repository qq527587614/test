using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace StockAnalysisSystem.Core.Entities;

/// <summary>
/// 策略表实体
/// </summary>
[Table("Strategy")]
public class Strategy
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    [StringLength(100)]
    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    [Required]
    [StringLength(50)]
    public string StrategyType { get; set; } = null!;

    [Required]
    [Column(TypeName = "json")]
    public string Parameters { get; set; } = "{}";

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    // 辅助方法：获取参数字典
    [NotMapped]
    public Dictionary<string, object> ParametersDict
    {
        get => JsonSerializer.Deserialize<Dictionary<string, object>>(Parameters) ?? new Dictionary<string, object>();
        set => Parameters = JsonSerializer.Serialize(value);
    }
}
