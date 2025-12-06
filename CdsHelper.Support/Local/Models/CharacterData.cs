namespace CdsHelper.Support.Local.Models;

/// <summary>
/// 캐릭터 데이터 모델
/// </summary>
public class CharacterData
{
    public string Name { get; set; } = "";
    public byte HP { get; set; }
    public byte Intelligence { get; set; }
    public byte Strength { get; set; }
    public byte Charm { get; set; }
    public byte Luck { get; set; }
    public string Skills { get; set; } = "";
    public ushort Fame { get; set; }
    public string Location { get; set; } = "";
    public byte Face { get; set; }
    public sbyte Age { get; set; }
    public string Constellation { get; set; } = "";
    public byte Available { get; set; }

    /// <summary>
    /// 특기별 레벨 (특기 인덱스 -> 레벨)
    /// </summary>
    public Dictionary<int, byte> RawSkills { get; set; } = new();

    /// <summary>
    /// 회색 표시 여부: 18세 미만 또는 60세 초과
    /// </summary>
    public bool IsGray => Age < 18 || Age > 60;

    /// <summary>
    /// 특정 특기가 특정 레벨인지 확인
    /// </summary>
    public bool HasSkill(int skillIndex, byte level)
    {
        return RawSkills.TryGetValue(skillIndex, out var skillLevel) && skillLevel == level;
    }
}
