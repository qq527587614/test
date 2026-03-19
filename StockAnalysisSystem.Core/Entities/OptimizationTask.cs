using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace StockAnalysisSystem.Core.Entities;

/// <summary>
/// 策略优化任务表实体
/// </summary>
[Table("OptimizationTask")]
public class OptimizationTask
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    [StringLength(50)]
    public string StrategyType { get; set; } = null!;

    [Required]
    [Column(TypeName = "json")]
    public string ParameterRanges { get; set; } = "{}";

    [Required]
    public DateTime StartDate { get; set; }

    [Required]
    public DateTime EndDate { get; set; }

    [StringLength(50)]
    public string FitnessFunction { get; set; } = "AnnualReturn";

    [StringLength(20)]
    public string Status { get; set; } = "Pending";

    [Column(TypeName = "json")]
    public string? BestParameters { get; set; }

    [Column(TypeName = "json")]
    public string? BestResult { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime? CompletedAt { get; set; }
}
