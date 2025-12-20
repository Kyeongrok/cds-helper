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

    /// <summary>
    /// 현재 로드된 세이브 게임 정보
    /// </summary>
    public SaveGameInfo? CurrentSaveGameInfo { get; private set; }

    /// <summary>
    /// 현재 로드된 플레이어 데이터
    /// </summary>
    public PlayerData? CurrentPlayerData { get; private set; }

    /// <summary>
    /// 현재 로드된 파일 경로
    /// </summary>
    public string? CurrentFilePath { get; private set; }

    private const int CHARACTER_START_OFFSET = 0x924A;
    private const int CHARACTER_SIZE = 0x90;
    private const int CHARACTER_COUNT = 461;
    private const int YEAR_OFFSET = 0x15;
    private const int MONTH_OFFSET = 0x19;
    private const int DAY_OFFSET = 0x1A;

    // 힌트 관련 상수
    private const int HINT_START_OFFSET = 0x1A625;
    private const int HINT_SIZE = 6;
    private const int HINT_STATUS_OFFSET = 4;  // 6바이트 블록 내 상태 바이트 위치
    private const int HINT_COUNT = 186;

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
                character.Index = i;  // 캐릭터 인덱스 저장
                characters.Add(character);
            }
        }

        saveInfo.Characters = characters;

        // 힌트 데이터 읽기
        saveInfo.Hints = ReadHintData(data);

        // 현재 로드된 데이터 캐싱
        CurrentSaveGameInfo = saveInfo;
        CurrentFilePath = filePath;

        return saveInfo;
    }

    /// <summary>
    /// 힌트 획득 데이터 읽기 (1~186)
    /// </summary>
    private List<HintData> ReadHintData(byte[] data)
    {
        var hints = new List<HintData>();

        for (int i = 0; i < HINT_COUNT; i++)
        {
            int offset = HINT_START_OFFSET + (i * HINT_SIZE) + HINT_STATUS_OFFSET;
            if (offset >= data.Length)
                break;

            hints.Add(new HintData
            {
                Index = i + 1,
                Value = data[offset]
            });
        }

        return hints;
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

        // 고용 상태 (나이 + 6바이트 = 0x62)
        character.HireStatus = data[offset + 0x62];

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

    /// <summary>
    /// 플레이어(주인공) 데이터 읽기
    /// </summary>
    public PlayerData? ReadPlayerData(string filePath)
    {
        if (!File.Exists(filePath))
            return null;

        var data = File.ReadAllBytes(filePath);
        if (data.Length < 0xC0)
            return null;

        var player = new PlayerData();

        // 이름 (0x5F: 이름, 0x72: 성)
        try
        {
            var firstName = ReadString(new ArraySegment<byte>(data, 0x5F, 14));
            var lastName = ReadString(new ArraySegment<byte>(data, 0x72, 14));
            player.FirstName = firstName;
            player.LastName = lastName;
        }
        catch { }

        // 기능 스킬 (0x38~0x44)
        player.Navigation = data[0x38];      // 항해술
        player.Seamanship = data[0x39];      // 운용술
        player.Swordsmanship = data[0x3A];   // 검술
        player.Gunnery = data[0x3B];         // 포술
        player.Shooting = data[0x3C];        // 사격술
        player.Medicine = data[0x3D];        // 의학
        player.Eloquence = data[0x3E];       // 웅변술
        player.Surveying = data[0x3F];       // 측량술
        player.History = data[0x40];         // 역사학
        player.Accounting = data[0x41];      // 회계
        player.Shipbuilding = data[0x42];    // 조선기술
        player.Theology = data[0x43];        // 신학
        player.Science = data[0x44];         // 과학

        // 언어 스킬 (0x45~0x52)
        player.Spanish = data[0x45];         // 스페인어
        player.Portuguese = data[0x46];      // 포르투갈어
        player.Romance = data[0x47];         // 로망스어
        player.Germanic = data[0x48];        // 게르만어
        player.Slavic = data[0x49];          // 슬라브어
        player.Arabic = data[0x4A];          // 아랍어
        player.Persian = data[0x4B];         // 페르시아어
        player.Chinese = data[0x4C];         // 중국어
        player.Hindi = data[0x4D];           // 힌두어
        player.Uyghur = data[0x4E];          // 위그르어
        player.African = data[0x4F];         // 아프리카어
        player.American = data[0x50];        // 아메리카어
        player.SoutheastAsian = data[0x51];  // 동남아시아어
        player.EastAsian = data[0x52];       // 동아시아어

        // 명성 (0x53-0x54)
        player.Fame = BitConverter.ToUInt16(data, 0x53);

        // 악명 (0x55-0x56)
        player.Notoriety = BitConverter.ToUInt16(data, 0x55);

        // 현재 도시 (0x57)
        player.CurrentCity = data[0x57];
        player.CurrentCityName = _cityService.GetCityName(player.CurrentCity, _cities);

        // 동료 (0xA5-0xA8) - 캐릭터 인덱스
        player.Adjutant = data[0xA5];      // 부관
        player.Navigator = data[0xA7];     // 항해사
        player.Surveyor = data[0xA9];      // 측량사
        player.Interpreter = data[0xAB];   // 통역

        // 동료 캐릭터 데이터 조회
        player.AdjutantData = ReadCharacterByIndex(data, player.Adjutant);
        player.NavigatorData = ReadCharacterByIndex(data, player.Navigator);
        player.SurveyorData = ReadCharacterByIndex(data, player.Surveyor);
        player.InterpreterData = ReadCharacterByIndex(data, player.Interpreter);

        // 동료 이름 설정
        player.AdjutantName = player.AdjutantData?.Name ?? "없음";
        player.NavigatorName = player.NavigatorData?.Name ?? "없음";
        player.SurveyorName = player.SurveyorData?.Name ?? "없음";
        player.InterpreterName = player.InterpreterData?.Name ?? "없음";

        // 소지금 (추후 확인 필요)
        // player.Gold = ...;

        // 현재 로드된 플레이어 데이터 캐싱
        CurrentPlayerData = player;

        return player;
    }

    /// <summary>
    /// 캐릭터 인덱스로 캐릭터 데이터 조회
    /// </summary>
    private CharacterData? ReadCharacterByIndex(byte[] data, byte characterIndex)
    {
        // 0 또는 0xFF(255)는 미고용 상태
        if (characterIndex == 0 || characterIndex == 0xFF)
            return null;

        int offset = CHARACTER_START_OFFSET + (characterIndex * CHARACTER_SIZE);
        if (offset + CHARACTER_SIZE > data.Length)
            return null;

        var character = ReadCharacterData(data, offset);
        if (character != null)
        {
            character.Index = characterIndex;
        }
        return character;
    }
}
