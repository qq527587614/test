using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StockAnalysisSystem.Core.Entities;

/// <summary>
/// DeepSeek API调用日志表实体
/// </summary>
[Table("DeepSeekLog")]
public class DeepSeekLog
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    [Column(TypeName = "json")]
    public string RequestData { get; set; } = "{}";

    [Required]
    [Column(TypeName = "json")]
    public string ResponseData { get; set; } = "{}";

    [Required]
    [StringLength(50)]
    public string UsedFor { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
