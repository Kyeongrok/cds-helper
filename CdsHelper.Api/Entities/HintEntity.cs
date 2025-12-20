using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CdsHelper.Api.Entities;

[Table("Hints")]
public class HintEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    // Navigation property
    public ICollection<BookHintEntity> BookHints { get; set; } = new List<BookHintEntity>();
}
