using System.Text.Json.Serialization;

namespace cds_helper.Models;

public class Book
{
    [JsonPropertyName("도서명")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("언어")]
    public string Language { get; set; } = string.Empty;

    [JsonPropertyName("소재 도서관")]
    public string Library { get; set; } = string.Empty;

    [JsonPropertyName("게제 힌트")]
    public string Hint { get; set; } = string.Empty;

    [JsonPropertyName("필요")]
    public string Required { get; set; } = string.Empty;

    [JsonPropertyName("개제조건")]
    public string Condition { get; set; } = string.Empty;
}
