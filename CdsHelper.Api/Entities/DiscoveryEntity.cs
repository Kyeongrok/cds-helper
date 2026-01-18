using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CdsHelper.Api.Entities;

[Table("Discoveries")]
public class DiscoveryEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 게재힌트 ID (FK → Hints)
    /// </summary>
    public int? HintId { get; set; }

    /// <summary>
    /// 게재힌트 Navigation property
    /// </summary>
    [ForeignKey(nameof(HintId))]
    public HintEntity? Hint { get; set; }

    /// <summary>
    /// 등장 조건
    /// </summary>
    [MaxLength(50)]
    public string? AppearCondition { get; set; }

    /// <summary>
    /// 관련 도서명
    /// </summary>
    [MaxLength(200)]
    public string? BookName { get; set; }

    // Navigation properties
    public ICollection<DiscoveryParentEntity> Parents { get; set; } = new List<DiscoveryParentEntity>();
    public ICollection<DiscoveryParentEntity> Children { get; set; } = new List<DiscoveryParentEntity>();
}
