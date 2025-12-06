using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CdsHelper.Api.Entities;

[Table("Cities")]
public class CityEntity
{
    [Key]
    public byte Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string Name { get; set; } = string.Empty;

    public int? Latitude { get; set; }

    public int? Longitude { get; set; }

    public bool HasLibrary { get; set; }

    public bool HasShipyard { get; set; }

    [MaxLength(50)]
    public string? CulturalSphere { get; set; }

    public int? PixelX { get; set; }

    public int? PixelY { get; set; }
}
