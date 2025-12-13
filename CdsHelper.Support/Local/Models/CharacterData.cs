namespace CdsHelper.Support.Local.Models;

/// <summary>
/// 캐릭터 데이터 모델
/// </summary>
public class CharacterData
{
    public int Index { get; set; }  // 캐릭터 인덱스 (세이브 파일 내 순서)
    public string Name { get; set; } = "";
    public byte HP { get; set; }
    public byte Intelligence { get; set; }
    public byte Strength { get; set; }
    public byte Charm { get; set; }
    public byte Luck { get; set; }
    public string Skills { get; set; } = "";

    // 언어 특기 목록 (축약형)
    // 스=스페인어, 갈=포르투갈어, 로=로망스어, 게=게르만어, 슬=슬라브어
    // 랍=아랍어, 페=페르시아어, 중=중국어, 힌=힌두어, 위=위그르어
    // 아=아프리카어, 미=아메리카어, 남=동남아시아어, 동=동아시아어
    private static readonly HashSet<string> LanguageSkills = new()
    {
        "스", "갈", "로", "게", "슬", "랍", "페", "중", "힌", "위", "아", "미", "남", "동"
    };

    /// <summary>
    /// 일반 특기만 (항해술, 포술 등)
    /// </summary>
    public string GeneralSkills => string.Join(" ", Skills.Split(' ')
        .Where(s => !string.IsNullOrEmpty(s) && !IsLanguageSkill(s)));

    /// <summary>
    /// 언어 특기만 (스페인어, 중국어 등)
    /// </summary>
    public string LanguageSkillsText => string.Join(" ", Skills.Split(' ')
        .Where(s => !string.IsNullOrEmpty(s) && IsLanguageSkill(s)));

    /// <summary>
    /// 언어 특기인지 확인 (예: "스:3" -> "스"가 언어목록에 있는지)
    /// </summary>
    private static bool IsLanguageSkill(string skill)
    {
        var skillName = skill.Split(':')[0];
        return LanguageSkills.Contains(skillName);
    }
    public ushort Fame { get; set; }
    public string Location { get; set; } = "";
    public byte Face { get; set; }
    public sbyte Age { get; set; }
    public string Constellation { get; set; } = "";
    public byte Available { get; set; }

    /// <summary>
    /// 고용 상태: 1=대화만, 2=고용가능, 3=고용완료
    /// </summary>
    public byte HireStatus { get; set; }

    /// <summary>
    /// 고용 상태 표시 텍스트
    /// </summary>
    public string HireStatusText => HireStatus switch
    {
        1 => "대화만",
        2 => "고용가능",
        3 => "고용중",
        _ => "-"
    };

    /// <summary>
    /// 특기별 레벨 (특기 인덱스 -> 레벨)
    /// </summary>
    public Dictionary<int, byte> RawSkills { get; set; } = new();

    /// <summary>
    /// 회색 표시 여부: 18세 미만 또는 60세 초과
    /// </summary>
    public bool IsGray => Age < 18 || Age > 60;

    /// <summary>
    /// 플레이어 명성 (비교용)
    /// </summary>
    public ushort PlayerFame { get; set; }

    /// <summary>
    /// 고용 가능 여부: 캐릭터 명성이 플레이어 명성 이하
    /// </summary>
    public bool CanRecruit => Fame <= PlayerFame;

    /// <summary>
    /// 특정 특기가 특정 레벨인지 확인
    /// </summary>
    public bool HasSkill(int skillIndex, byte level)
    {
        return RawSkills.TryGetValue(skillIndex, out var skillLevel) && skillLevel == level;
    }
}
