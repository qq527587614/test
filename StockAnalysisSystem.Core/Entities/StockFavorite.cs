using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StockAnalysisSystem.Core.Entities;

/// <summary>
/// 自选股表实体
/// </summary>
[Table("stockfavorite")]
public class StockFavorite
{
    [Key]
    [Column("Id")]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Column("StockCode")]
    [Required]
    [StringLength(30)]
    public string StockCode { get; set; } = null!;

    [Column("AddedDate")]
    public DateTime AddedDate { get; set; } = DateTime.Now;

    [Column("Remark")]
    [StringLength(200)]
    public string? Remark { get; set; }

    // 兼容旧代码的属性
    [NotMapped]
    public string? StockName { get; set; }

    [NotMapped]
    public decimal? CurrentPrice { get; set; }

    [NotMapped]
    public decimal? ChangePercent { get; set; }

    [NotMapped]
    public decimal? TurnoverRate { get; set; }
}
