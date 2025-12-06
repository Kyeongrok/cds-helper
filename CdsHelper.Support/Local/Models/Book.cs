using System.Text.Json.Serialization;

namespace CdsHelper.Support.Local.Models;

public class Book
{
    public int Id { get; set; }

    [JsonPropertyName("도서명")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("언어")]
    public string Language { get; set; } = string.Empty;

    [JsonPropertyName("게제 힌트")]
    public string Hint { get; set; } = string.Empty;

    [JsonPropertyName("필요")]
    public string Required { get; set; } = string.Empty;

    [JsonPropertyName("개제조건")]
    public string Condition { get; set; } = string.Empty;

    // 소재 도서관 도시 ID 목록 (정규화된 데이터)
    public List<byte> LibraryCityIds { get; set; } = new();

    // 소재 도서관 도시명 목록 (표시용)
    public List<string> LibraryCityNames { get; set; } = new();

    // 기존 호환성용 (쉼표 구분 문자열)
    [JsonPropertyName("소재 도서관")]
    public string Library
    {
        get => string.Join(", ", LibraryCityNames);
        set { } // JSON 역직렬화용 (무시)
    }
}
