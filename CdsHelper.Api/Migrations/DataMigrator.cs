using System.Text.Json;
using CdsHelper.Api.Controllers;
using CdsHelper.Api.Entities;

namespace CdsHelper.Api.Migrations;

public static class DataMigrator
{
    /// <summary>
    /// cities.json 파일에서 SQLite DB로 데이터 마이그레이션
    /// </summary>
    public static async Task MigrateCitiesFromJsonAsync(CityController controller, string jsonPath)
    {
        if (!File.Exists(jsonPath))
        {
            throw new FileNotFoundException($"cities.json 파일을 찾을 수 없습니다: {jsonPath}");
        }

        // DB에 이미 데이터가 있으면 스킵
        if (await controller.HasAnyDataAsync())
        {
            return;
        }

        var json = await File.ReadAllTextAsync(jsonPath);
        var jsonCities = JsonSerializer.Deserialize<List<JsonCityData>>(json);

        if (jsonCities == null || jsonCities.Count == 0)
        {
            return;
        }

        var entities = jsonCities.Select(c => new CityEntity
        {
            Id = c.Id,
            Name = c.Name,
            Latitude = c.Latitude,
            Longitude = c.Longitude,
            HasLibrary = c.HasLibrary,
            HasShipyard = c.HasShipyard,
            CulturalSphere = c.CulturalSphere,
            PixelX = c.PixelX,
            PixelY = c.PixelY
        }).ToList();

        await controller.AddCitiesAsync(entities);
    }

    private class JsonCityData
    {
        [System.Text.Json.Serialization.JsonPropertyName("id")]
        public byte Id { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("latitude")]
        public int? Latitude { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("longitude")]
        public int? Longitude { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("hasLibrary")]
        public bool HasLibrary { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("hasShipyard")]
        public bool HasShipyard { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("culturalSphere")]
        public string? CulturalSphere { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("pixelX")]
        public int? PixelX { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("pixelY")]
        public int? PixelY { get; set; }
    }
}
