using System.IO;
using System.Text;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Support.Local.Helpers;

/// <summary>
/// SAVEDATA.CDS 파일 읽기 서비스
/// </summary>
public class SaveDataService
{
    private readonly CityService _cityService;
    private List<City> _cities = new();

    private const int CHARACTER_START_OFFSET = 0x924A;
    private const int CHARACTER_SIZE = 0x90;
    private const int CHARACTER_COUNT = 461;
    private const int YEAR_OFFSET = 0x15;
    private const int MONTH_OFFSET = 0x19;
    private const int DAY_OFFSET = 0x1A;

    private static readonly Dictionary<int, string> SkillsMap = new()
    {
        { 1, "항" }, { 2, "운" }, { 3, "검" }, { 4, "포" }, { 5, "사" },
        { 6, "의" }, { 7, "웅" }, { 8, "측" }, { 9, "역" }, { 10, "회" },
        { 11, "조" }, { 12, "신" }, { 13, "과" }, { 14, "스" }, { 15, "갈" },
        { 16, "로" }, { 17, "게" }, { 18, "슬" }, { 19, "랍" }, { 20, "페" },
        { 21, "중" }, { 22, "힌" }, { 23, "위" }, { 24, "아" }, { 25, "미" },
        { 26, "남" }, { 27, "동" },
    };

    public SaveDataService(CityService cityService)
    {
        _cityService = cityService;
    }

    public void SetCities(List<City> cities)
    {
        _cities = cities;
    }

    public SaveGameInfo ReadSaveFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"세이브 파일을 찾을 수 없습니다: {filePath}");
        }

        var saveInfo = new SaveGameInfo();
        var data = File.ReadAllBytes(filePath);

        if (data.Length > DAY_OFFSET)
        {
            saveInfo.Year = BitConverter.ToUInt16(data, YEAR_OFFSET);
            saveInfo.Month = data[MONTH_OFFSET];
            saveInfo.Day = data[DAY_OFFSET];
        }

        var characters = new List<CharacterData>();

        for (int i = 0; i < CHARACTER_COUNT; i++)
        {
            int offset = CHARACTER_START_OFFSET + (i * CHARACTER_SIZE);
            if (offset + CHARACTER_SIZE > data.Length)
                break;

            var character = ReadCharacterData(data, offset);

            if (character != null && character.Name != "???")
            {
                characters.Add(character);
            }
        }

        saveInfo.Characters = characters;
        return saveInfo;
    }

    private CharacterData? ReadCharacterData(byte[] data, int offset)
    {
        if (offset + CHARACTER_SIZE > data.Length)
            return null;

        var character = new CharacterData();

        // 이름 추출
        try
        {
            var name1Bytes = new ArraySegment<byte>(data, offset + 0x32, 20);
            var name2Bytes = new ArraySegment<byte>(data, offset + 0x45, 20);

            string name1 = ReadString(name1Bytes);
            string name2 = ReadString(name2Bytes);

            if (!string.IsNullOrEmpty(name1) && !string.IsNullOrEmpty(name2))
                character.Name = $"{name1}·{name2}";
            else if (!string.IsNullOrEmpty(name1))
                character.Name = name1;
            else if (!string.IsNullOrEmpty(name2))
                character.Name = name2;
            else
                character.Name = "???";
        }
        catch
        {
            character.Name = "???";
        }

        // 능력치
        character.HP = data[offset + 0x00];
        character.Intelligence = data[offset + 0x01];
        character.Strength = data[offset + 0x02];
        character.Charm = data[offset + 0x03];
        character.Luck = data[offset + 0x04];
        character.Available = data[offset + 0x0A];

        // 특기
        var skills = new List<string>();
        var rawSkills = new Dictionary<int, byte>();
        for (int i = 0; i < 28; i++)
        {
            int skillOffset = 0x0A + i;
            int skillId = i;
            if (skillOffset < CHARACTER_SIZE)
            {
                byte skillLevel = data[offset + skillOffset];
                if (skillLevel > 0 && SkillsMap.ContainsKey(skillId))
                {
                    skills.Add($"{SkillsMap[skillId]}:{skillLevel}");
                    rawSkills[skillId] = skillLevel;
                }
            }
        }
        character.Skills = string.Join(" ", skills);
        character.RawSkills = rawSkills;

        // 명성
        character.Fame = BitConverter.ToUInt16(data, offset + 0x26);

        // 소재
        byte locationIdx = data[offset + 0x2E];
        character.Location = _cityService.GetCityName(locationIdx, _cities);

        // 연령
        byte ageRaw = data[offset + 0x5C];
        character.Age = unchecked((sbyte)ageRaw);

        // 얼굴
        character.Face = data[offset + 0x60];

        // 성좌
        try
        {
            var constellationBytes = new ArraySegment<byte>(data, offset + 0x70, 20);
            character.Constellation = ReadString(constellationBytes);
        }
        catch
        {
            character.Constellation = "";
        }

        return character;
    }

    private string ReadString(ArraySegment<byte> bytes)
    {
        int nullPos = -1;
        for (int i = 0; i < bytes.Count; i++)
        {
            if (bytes[i] == 0)
            {
                nullPos = i;
                break;
            }
        }

        if (nullPos == 0)
            return "";

        int length = nullPos > 0 ? nullPos : bytes.Count;
        var validBytes = new byte[length];
        Array.Copy(bytes.Array!, bytes.Offset, validBytes, 0, length);

        try
        {
            var encoding = Encoding.GetEncoding(51949);
            return encoding.GetString(validBytes).Trim();
        }
        catch
        {
            return "";
        }
    }
}
