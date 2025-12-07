using System.Text.Json.Serialization;

namespace cds_helper.Models;

public class City
{
    [JsonPropertyName("id")]
    public byte Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("latitude")]
    public int? Latitude { get; set; }

    [JsonPropertyName("longitude")]
    public int? Longitude { get; set; }

    [JsonPropertyName("hasLibrary")]
    public bool HasLibrary { get; set; }

    [JsonPropertyName("hasShipyard")]
    public bool HasShipyard { get; set; }

    [JsonPropertyName("culturalSphere")]
    public string? CulturalSphere { get; set; }

    [JsonPropertyName("pixelX")]
    public int? PixelX { get; set; }

    [JsonPropertyName("pixelY")]
    public int? PixelY { get; set; }

    // 위도 표시용 (북위/남위)
    public string LatitudeDisplay
    {
        get
        {
            if (!Latitude.HasValue)
                return "-";

            var absLat = Math.Abs(Latitude.Value);
            var direction = Latitude.Value >= 0 ? "북위" : "남위";
            return $"{direction}{absLat}";
        }
    }

    // 경도 표시용 (동경/서경)
    public string LongitudeDisplay
    {
        get
        {
            if (!Longitude.HasValue)
                return "-";

            var absLon = Math.Abs(Longitude.Value);
            var direction = Longitude.Value >= 0 ? "동경" : "서경";
            return $"{direction}{absLon}";
        }
    }
}
