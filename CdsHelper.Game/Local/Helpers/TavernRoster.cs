namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 인물 표에서 <b>그 도시 술집·여관에 앉아 있는 사람</b>만 골라 낸 것.
/// 술집 화면에 세울 항해사·부하 후보가 여기서 온다.
/// </summary>
/// <remarks>
/// 게임은 시설을 열 때 그 도시에 있는 인물을 자리에 앉히고, 남는 자리는 지나가는 사람으로
/// 채운다(볼트 <c>14.분석-술집 화면과 대사</c> 의 "누가 이름 있는 인물이 되는가").
/// 인물이 어느 도시 어느 건물에 있는지는 표에 그대로 적혀 있다.
///
/// <b>세이브를 뜯지 않는다.</b> 예전에는 <c>SAVEDATA.CDS</c> 를 그때그때 읽었는데 그러면
/// 놀이가 남의 세이브에 매인다. 지금은 <see cref="PersonTable"/>(<c>인물표.json</c>)이 원본이고
/// 이 클래스는 그 위의 <b>보기</b>일 뿐이다 — 자리 값은 <see cref="PersonFile"/> 한 곳만 안다.
/// </remarks>
public sealed class TavernRoster
{
    /// <summary>건물 번호.</summary>
    public const byte Tavern = PersonTable.Tavern, Inn = PersonTable.Inn;

    /// <summary>고용 상태. 2 라야 부하로 삼을 수 있다.</summary>
    public const byte TalkOnly = PersonTable.TalkOnly,
                      Hireable = PersonTable.Hireable,
                      Hired = PersonTable.Hired;

    /// <summary>술집·여관에 앉아 있는 사람 하나.</summary>
    /// <param name="Index">인물 번호. 그림을 고르는 씨앗으로도 쓴다.</param>
    /// <param name="City">소재 도시. -1 이면 어느 도시에도 없다.</param>
    /// <param name="Body">체력 · <paramref name="Mind"/> 지력 · <paramref name="Might"/> 무력
    /// · <paramref name="Charm"/> 매력. 레코드 맨 앞 다섯 바이트가 능력치다(운까지).</param>
    public readonly record struct Person(int Index, string Name, int Fame, int Age,
                                         byte Hire, int FaceCode, int City, byte Building,
                                         byte Body, byte Mind, byte Might, byte Charm, byte Luck,
                                         byte Sword, byte Shooting = 0, byte Gunnery = 0);

    private readonly List<Person> _people;

    private TavernRoster(List<Person> people) => _people = people;

    /// <summary>왜 못 읽었는지. 잘 읽었으면 빈 문자열.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>
    /// 인물 표를 열어 술집·여관에 앉은 사람만 추린다. 표가 비면 null 이다.
    /// </summary>
    public static TavernRoster? Open()
    {
        var table = PersonTable.Open();
        if (table.IsEmpty)
        {
            LastError = PersonTable.LastError.Length > 0
                ? PersonTable.LastError
                : "인물 표가 비어 있습니다";
            return null;
        }
        return From(table);
    }

    /// <summary>이미 연 표에서 추린다.</summary>
    public static TavernRoster From(PersonTable table) => From(table.People);

    /// <summary>
    /// 줄 목록에서 추린다 — <see cref="PersonWorld"/> 처럼 사람이 옮겨 다니는 쪽이 쓴다.
    /// </summary>
    public static TavernRoster From(IReadOnlyList<PersonTable.Row> rows)
    {
        LastError = "";
        var people = new List<Person>();

        foreach (var r in rows)
        {
            if (r.Appear == 0) continue;                          // 아직 등장하지 않은 인물
            if (r.Building is not (Tavern or Inn)) continue;
            if (r.Name == "???") continue;

            people.Add(new Person(
                r.Id, r.Name, r.Fame, r.Age,
                Byte(r.Hire), r.Face, r.City, Byte(r.Building),
                Stat(r, 0), Stat(r, 1), Stat(r, 2), Stat(r, 3), Stat(r, 4),
                Level(r, Support.Local.Models.Skill.Sword),
                Level(r, Support.Local.Models.Skill.Shooting),
                Level(r, Support.Local.Models.Skill.Gunnery)));
        }
        return new TavernRoster(people);
    }

    /// <summary>그 도시 그 건물에 앉아 있는 사람들. 표에 적힌 차례 그대로다.</summary>
    public IReadOnlyList<Person> At(int city, byte building)
    {
        if (city < 0) return [];                            // -1 은 "어디에도 없다" 다

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
    /// 얼굴을 다시 구하려면 이렇게 되짚어야 한다. 부하가 되어도 표의 소재지는 그대로라
    /// 목록에서 빠지지 않는다.
    /// </remarks>
    public Person? Find(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        foreach (var p in _people)
            if (p.Name == name) return p;
        return null;
    }

    private static byte Byte(int value) => (byte)Math.Clamp(value, 0, byte.MaxValue);

    private static byte Stat(PersonTable.Row row, int slot) =>
        slot < row.Stats.Length ? Byte(row.Stats[slot]) : (byte)0;

    private static byte Level(PersonTable.Row row, int slot) =>
        slot < row.Skills.Length
            ? Byte(Math.Min(row.Skills[slot], Support.Local.Models.Skill.MaxLevel))
            : (byte)0;
}
