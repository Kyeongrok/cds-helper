using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

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

    // 플레이어 스킬 데이터 (읽기 가능 여부 판단용)
    [JsonIgnore]
    public Dictionary<string, byte>? PlayerSkills { get; set; }

    [JsonIgnore]
    public Dictionary<string, byte>? PlayerLanguages { get; set; }

    /// <summary>
    /// 읽기 가능 여부: 언어 레벨 3 이상 + 필요 스킬 충족
    /// </summary>
    [JsonIgnore]
    public bool CanRead
    {
        get
        {
            if (PlayerLanguages == null || PlayerSkills == null)
                return true; // 플레이어 데이터 없으면 기본 표시

            // 언어 체크 (3레벨 이상)
            if (!string.IsNullOrEmpty(Language))
            {
                if (!PlayerLanguages.TryGetValue(Language, out var langLevel) || langLevel < 3)
                    return false;
            }

            // 필요 스킬 체크
            if (!string.IsNullOrEmpty(Required))
            {
                // "역사학 2", "항해술 1" 등의 형식 파싱
                var match = Regex.Match(Required, @"(.+?)\s*(\d+)");
                if (match.Success)
                {
                    var skillName = match.Groups[1].Value.Trim();
                    var requiredLevel = byte.Parse(match.Groups[2].Value);

                    if (!PlayerSkills.TryGetValue(skillName, out var playerLevel) || playerLevel < requiredLevel)
                        return false;
                }
            }

            return true;
        }
    }
}
