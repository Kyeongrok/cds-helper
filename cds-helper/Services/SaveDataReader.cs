using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using cds_helper.Models;

namespace cds_helper.Services;

/// <summary>
/// SAVEDATA.CDS 파일 읽기 서비스
/// </summary>
public class SaveDataReader
{
    private const int CHARACTER_START_OFFSET = 0x924A; // 37,450 바이트
    private const int CHARACTER_SIZE = 0x90; // 144 바이트
    private const int CHARACTER_COUNT = 461; // 최대 캐릭터 수

    // 세이브 파일 헤더 오프셋
    private const int YEAR_OFFSET = 0x15; // 년도 (2바이트, little-endian)
    private const int MONTH_OFFSET = 0x19; // 월 (1바이트)
    private const int DAY_OFFSET = 0x1A; // 일 (1바이트)

    private static readonly Dictionary<int, string> SkillsMap = new()
    {
        { 1, "항" },   // 항해술
        { 2, "운" },   // 운용술
        { 3, "검" },   // 검술
        { 4, "포" },   // 포술
        { 5, "사" },   // 사격술
        { 6, "의" },   // 의학
        { 7, "웅" },   // 웅변술
        { 8, "측" },   // 측량술
        { 9, "역" },   // 역사학
        { 10, "회" },  // 회피
        { 11, "조" },  // 조선술
        { 12, "신" },  // 신학
        { 13, "과" },  // 과학
        { 14, "스" },  // 스페인어
        { 15, "갈" },  // 포르투갈어
        { 16, "로" },  // 로망스어
        { 17, "게" },  // 게르만어
        { 18, "슬" },  // 슬라브어
        { 19, "랍" },  // 아랍어
        { 20, "페" },  // 페르시아어
        { 21, "중" },  // 중국어
        { 22, "힌" },  // 힌두어
        { 23, "위" },  // 위그르어
        { 24, "아" },  // 아프리카 토착어
        { 25, "미" },  // 아메리카 토착어
        { 26, "남" },  // 동남아시아 토착어
        { 27, "동" },  // 동아시아 토착어
    };

    public SaveGameInfo ReadSaveFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"세이브 파일을 찾을 수 없습니다: {filePath}");
        }

        var saveInfo = new SaveGameInfo();
        var data = File.ReadAllBytes(filePath);

        System.Diagnostics.Debug.WriteLine($"파일 크기: {data.Length} 바이트");

        // 세이브 날짜 읽기
        if (data.Length > DAY_OFFSET)
        {
            saveInfo.Year = BitConverter.ToUInt16(data, YEAR_OFFSET);
            saveInfo.Month = data[MONTH_OFFSET];
            saveInfo.Day = data[DAY_OFFSET];
            System.Diagnostics.Debug.WriteLine($"세이브 날짜: {saveInfo.Year}년 {saveInfo.Month}월 {saveInfo.Day}일");
        }

        var characters = new List<CharacterData>();
        int totalRead = 0;
        int validCount = 0;

        for (int i = 0; i < CHARACTER_COUNT; i++)
        {
            int offset = CHARACTER_START_OFFSET + (i * CHARACTER_SIZE);
            if (offset + CHARACTER_SIZE > data.Length)
                break;

            var character = ReadCharacterData(data, offset);
            totalRead++;

            if (character != null)
            {
                if (i < 20) // 처음 20개만 로그 출력
                {
                    System.Diagnostics.Debug.WriteLine($"[{i}] 이름: {character.Name}, HP: {character.HP}, 소재: {character.Location}");
                }

                if (character.Name != "???")
                {
                    characters.Add(character);
                    validCount++;
                }
            }
        }

        System.Diagnostics.Debug.WriteLine($"총 읽은 캐릭터: {totalRead}, 유효한 캐릭터: {validCount}");

        saveInfo.Characters = characters;
        return saveInfo;
    }

    private static int _debugCounter = 0;

    private CharacterData? ReadCharacterData(byte[] data, int offset)
    {
        if (offset + CHARACTER_SIZE > data.Length)
            return null;

        var charData = new ArraySegment<byte>(data, offset, CHARACTER_SIZE);
        var character = new CharacterData();

        bool isDebug = _debugCounter < 3; // 처음 3개만 디버그
        _debugCounter++;

        // 이름 추출 (오프셋 0x32와 0x45)
        try
        {
            var name1Bytes = new ArraySegment<byte>(data, offset + 0x32, 20);
            var name2Bytes = new ArraySegment<byte>(data, offset + 0x45, 20);

            if (isDebug)
            {
                System.Diagnostics.Debug.WriteLine($"\n캐릭터 이름 읽기 (offset: 0x{offset:X}):");
                System.Diagnostics.Debug.WriteLine($"  이름1 오프셋: 0x{offset + 0x32:X}");
            }

            string name1 = ReadString(name1Bytes, isDebug);

            if (isDebug)
            {
                System.Diagnostics.Debug.WriteLine($"  이름2 오프셋: 0x{offset + 0x45:X}");
            }

            string name2 = ReadString(name2Bytes, isDebug);

            if (!string.IsNullOrEmpty(name1) && !string.IsNullOrEmpty(name2))
                character.Name = $"{name1}·{name2}";
            else if (!string.IsNullOrEmpty(name1))
                character.Name = name1;
            else if (!string.IsNullOrEmpty(name2))
                character.Name = name2;
            else
                character.Name = "???";

            if (isDebug)
            {
                System.Diagnostics.Debug.WriteLine($"  최종 이름: {character.Name}");
            }
        }
        catch (Exception ex)
        {
            if (isDebug)
            {
                System.Diagnostics.Debug.WriteLine($"  이름 읽기 오류: {ex.Message}");
            }
            character.Name = "???";
        }

        // 능력치 추출
        character.HP = data[offset + 0x00];
        character.Intelligence = data[offset + 0x01];
        character.Strength = data[offset + 0x02];
        character.Charm = data[offset + 0x03];
        character.Luck = data[offset + 0x04];

        // 등장 여부 플래그
        character.Available = data[offset + 0x0A];

        // 특기 추출 (0x0A~0x24, 총 27개)
        var skills = new List<string>();
        var rawSkills = new Dictionary<int, byte>();
        for (int i = 0; i < 28; i++)
        {
            int skillOffset = 0x0A + i;
            int skillId = i; // 스킬 ID는 1부터 시작
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

        // 명성 (0x26, little endian 2바이트)
        character.Fame = BitConverter.ToUInt16(data, offset + 0x26);

        // 소재 (0x2E)
        byte locationIdx = data[offset + 0x2E];
        character.Location = LocationMap.GetCityName(locationIdx);

        // 연령 (0x5C)
        // 부호 있는 바이트로 변환 (0x7F보다 크면 음수)
        byte ageRaw = data[offset + 0x5C];
        character.Age = unchecked((sbyte)ageRaw);

        // 얼굴 (0x60)
        character.Face = data[offset + 0x60];

        // 성좌 (0x70~)
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

    private string ReadString(ArraySegment<byte> bytes, bool debug = false)
    {
        // null 종료 문자 찾기
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
            // EUC-KR 인코딩으로 디코드 (코드페이지 51949)
            var encoding = Encoding.GetEncoding(51949);
            var result = encoding.GetString(validBytes).Trim();

            // 디버그 출력
            if (debug && validBytes.Length > 0)
            {
                var hexStr = BitConverter.ToString(validBytes).Replace("-", " ");
                System.Diagnostics.Debug.WriteLine($"    바이트: [{hexStr}] => '{result}'");
            }

            return result;
        }
        catch (Exception ex)
        {
            if (debug)
            {
                System.Diagnostics.Debug.WriteLine($"    인코딩 오류: {ex.Message}");
            }
            return "";
        }
    }
}
