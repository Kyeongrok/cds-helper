using System.IO;
using System.Text;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 게임 세이브(SAVEDATA.CDS)에 적힌 인물 중 <b>그 도시 술집·여관에 앉아 있는 사람</b>.
/// 술집 화면에 세울 항해사·부하 후보가 여기서 온다.
/// </summary>
/// <remarks>
/// 게임은 시설을 열 때 그 도시에 있는 인물을 자리에 앉히고, 남는 자리는 지나가는 사람으로
/// 채운다(볼트 <c>14.분석-술집 화면과 대사</c> 의 "누가 이름 있는 인물이 되는가").
/// 인물이 어느 도시 어느 건물에 있는지는 세이브 레코드에 그대로 적혀 있다.
///
/// <code>
///   표 시작 0x924A, 레코드 0x90 바이트, 최대 461개
///   +0x0A  등장 여부      +0x26  명성(u16)
///   +0x2E  소재 도시      +0x30  건물(4 주점 · 5 여관)
///   +0x32  이름(20)       +0x45  성(19, cp949)
///   +0x58  얼굴코드(u16)  +0x5C  연령(부호 있음)   +0x62  고용상태(1~3)
/// </code>
///
/// <b>읽기만 한다.</b> <see cref="SaveDataService"/> 를 안 쓰고 직접 뜯는 까닭이 이것이다 —
/// 그쪽은 <see cref="Models.CharacterData"/> 의 속성을 건드리면 세이브에 곧바로 되쓰는
/// 콜백을 걸어 두므로, 놀이 화면에서 잘못 만지면 남의 진짜 세이브가 바뀐다.
///
/// 얼굴코드는 <b>+0x58</b> 이다. <see cref="SaveDataService"/> 는 +0x60 을 얼굴로 보는데
/// 그 자리는 값이 0~11 뿐인 성좌다(모드 쪽에서 세이브 247건으로 확인).
/// </remarks>
public sealed class TavernRoster
{
    private const int TableStart = 0x924A, RecordSize = 0x90, MaxRecords = 461;

    /// <summary>건물 번호. 세이브 <c>+0x30</c> 에 이 값이 들어 있다.</summary>
    public const byte Tavern = 4, Inn = 5;

    /// <summary>고용 상태. 2 라야 부하로 삼을 수 있다.</summary>
    public const byte TalkOnly = 1, Hireable = 2, Hired = 3;

    /// <summary>술집·여관에 앉아 있는 사람 하나.</summary>
    /// <param name="Index">세이브 안 인물 번호. 그림을 고르는 씨앗으로도 쓴다.</param>
    /// <param name="Body">체력 · <paramref name="Mind"/> 지력 · <paramref name="Might"/> 무력
    /// · <paramref name="Charm"/> 매력. 레코드 맨 앞 다섯 바이트가 능력치다(운까지).</param>
    public readonly record struct Person(int Index, string Name, int Fame, int Age,
                                         byte Hire, int FaceCode, byte City, byte Building,
                                         byte Body, byte Mind, byte Might, byte Charm, byte Luck);

    private readonly List<Person> _people;

    private TavernRoster(List<Person> people) => _people = people;

    /// <summary>왜 못 읽었는지. 잘 읽었으면 빈 문자열.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>세이브에서 술집·여관에 있는 사람만 골라 읽는다. 못 읽으면 null.</summary>
    public static TavernRoster? Open(string saveFilePath)
    {
        LastError = "";
        if (!File.Exists(saveFilePath)) { LastError = $"{saveFilePath} 가 없습니다"; return null; }

        byte[] data;
        try { data = File.ReadAllBytes(saveFilePath); }
        catch (IOException ex) { LastError = ex.Message; return null; }

        if (data.Length < TableStart + RecordSize)
        {
            LastError = "세이브가 너무 짧습니다";
            return null;
        }

        var people = new List<Person>();
        for (int i = 0; i < MaxRecords; i++)
        {
            int at = TableStart + i * RecordSize;
            if (at + RecordSize > data.Length) break;

            if (data[at + 0x0A] == 0) continue;                 // 아직 등장하지 않은 인물
            byte building = data[at + 0x30];
            if (building is not (Tavern or Inn)) continue;

            string name = Name(data, at);
            if (name.Length == 0) continue;

            people.Add(new Person(
                i, name,
                BitConverter.ToUInt16(data, at + 0x26),
                unchecked((sbyte)data[at + 0x5C]),
                data[at + 0x62],
                BitConverter.ToUInt16(data, at + 0x58),
                data[at + 0x2E], building,
                data[at + 0x00], data[at + 0x01], data[at + 0x02],
                data[at + 0x03], data[at + 0x04]));
        }
        return new TavernRoster(people);
    }

    /// <summary>그 도시 그 건물에 앉아 있는 사람들. 세이브 차례 그대로다.</summary>
    public IReadOnlyList<Person> At(int city, byte building)
    {
        if (city < 0 || city > byte.MaxValue) return [];

        var found = new List<Person>();
        foreach (var p in _people)
            if (p.City == city && p.Building == building) found.Add(p);
        return found;
    }

    /// <summary>
    /// 그 이름으로 적힌 사람. 없으면 null 이다.
    /// </summary>
    /// <remarks>
    /// 부하로 삼은 사람은 이름만 적어 두므로(<see cref="Support.Local.Models.Player.Mates"/>)
    /// 얼굴을 다시 구하려면 이렇게 되짚어야 한다. 부하가 되어도 세이브의 소재지는 그대로라
    /// 표에서 빠지지 않는다.
    /// </remarks>
    public Person? Find(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        foreach (var p in _people)
            if (p.Name == name) return p;
        return null;
    }

    /// <summary>이름은 "이름·성" 으로 잇는다. 세이브가 cp949 다.</summary>
    private static string Name(byte[] data, int at)
    {
        string first = Text(data, at + 0x32, 20);
        string last = Text(data, at + 0x45, 19);
        if (first.Length > 0 && last.Length > 0) return $"{first}·{last}";
        return first.Length > 0 ? first : last;
    }

    private static Encoding? _cp949;

    private static string Text(byte[] data, int at, int max)
    {
        int len = 0;
        while (len < max && at + len < data.Length && data[at + len] != 0) len++;
        if (len == 0) return "";

        if (_cp949 == null)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            _cp949 = Encoding.GetEncoding(949);
        }
        return _cp949.GetString(data, at, len).Trim();
    }
}
