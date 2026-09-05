using CdsHelper.Game.Engine;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 인물이 도시 사이를 옮겨 다니는 시늉 — 매월 1일에 목적지를 뽑고, 닿으면 예순 날 쉰다.
/// </summary>
/// <remarks>
/// 게임의 손 넷을 그대로 옮긴 것이다(볼트 <c>72.분석-인물 이동(역사 항해사와 매달 굴림)</c>).
/// <code>
///   0x004327F0  달 넘김   14~200 만 굴린다. 활동 중 · 쉬는 날이 끝났고 · 가는 데가 없고
///                         · rand(5)==0 이라야 226곳에서 후보를 모아 하나를 뽑는다
///   0x00432740  하루 넘김 날 셈에 1 을 더한다
///   0x00432470  자리 재기 날 셈 x 24 가 거리에 닿으면 도착
///   0x004325F0  도착      소재 도시 = 목적지, 날 셈 = -60
/// </code>
///
/// <b>상태를 적어 두지 않는다.</b> 굴림의 주사위를 <c>(인물 번호, 해, 달)</c> 로 씨를 뿌려
/// 굴리므로 같은 날짜면 늘 같은 세상이 된다 — 그래서 세이브에 넣을 것이 없고, 불러온 판도
/// <see cref="Advance"/> 한 번으로 그 날짜까지 따라잡는다. 마흔 해를 따라잡아도 굴림이
/// 한 달에 서른일곱 번쯤이라 사백만 셈이 채 안 된다.
///
/// <b>게임과 다른 것 하나</b> — 나이를 안 먹인다. 표의 나이는 구워 온 판의 값이고 우리
/// 놀이는 1480년에 시작하므로, 해를 더하면 스무 해 만에 죄다 예순을 넘겨 아무도 안
/// 움직이게 된다. 그래서 활동 판정(18~60)은 표에 적힌 나이를 그대로 본다.
/// </remarks>
public sealed class PersonWorld
{
    /// <summary>하루에 나아가는 거리. <c>0x00432587</c> 의 <c>x24</c> 다.</summary>
    private const int SpeedPerDay = 24;

    /// <summary>닿고 나서 쉬는 날. <c>0x00432617</c> 의 <c>push -0x3c</c> 다.</summary>
    private const int RestDays = 60;

    /// <summary>세계가 감기는 너비. <c>0x9C4</c> 다.</summary>
    private const int WorldWidth = 0x9C4;

    /// <summary>움직일 수 있는 나이. <c>0x004322B0</c> 의 <c>0x12</c> ~ <c>0x3C</c> 다.</summary>
    private const int Youngest = 18, Oldest = 60;

    /// <summary>몇 달에 한 번꼴로 움직이는가. <c>0x0043284A</c> 의 <c>push 5</c> 다.</summary>
    private const int Odds = 5;

    /// <summary>
    /// 아직 세워지지 않은 도시 — 갈래 3(같은 나라)이 목적지로 삼지 않는다.
    /// </summary>
    /// <remarks>
    /// 언제 어느 도시가 서는지는 <see cref="CityFounding"/> 에 모아 두었다 —
    /// <c>HIST_EV.CDS</c> 의 신도시 이벤트 스무 벌이다. 날짜가 가면 하나씩 열리므로
    /// 이 목록도 달마다 달라진다.
    /// </remarks>
    private HashSet<int> NotFoundedYet =>
        [.. CityFounding.Hidden.Where(c => !CityFounding.FoundedBy(_asOf).Contains(c))];

    private readonly List<PersonTable.Row> _rows;
    private readonly CityExeTable? _cities;
    private readonly bool[] _harbor;

    private DateTime _asOf;

    /// <summary>
    /// 표를 받아 세상을 연다.
    /// </summary>
    /// <param name="start">놀이가 시작하는 날. 여기서부터 따라잡는다.</param>
    public PersonWorld(PersonTable table, CityExeTable? cities, CityBuildingTable? buildings,
                       DateTime start)
    {
        _rows = [.. table.People];
        _cities = cities;
        _harbor = Harbors(buildings);
        _asOf = start;

        // 구워 온 표에는 길 위에 있던 사람이 그대로 들어 있는데(1517년 판에 쉰 명쯤)
        // 떠나 온 도시가 없어 거리를 잴 수가 없다. 길에서 걷어 제자리에 세운다.
        foreach (var row in _rows)
            if (row.Dest >= 0 && row.From < 0) row.Dest = -1;
    }

    /// <summary>지금 인물들. 표를 연 그 줄을 그대로 옮겨 다닌다.</summary>
    public IReadOnlyList<PersonTable.Row> People => _rows;

    /// <summary>누가 움직일 때마다 하나씩 오른다 — 술집 목록을 다시 짤 때가 언제인지 알린다.</summary>
    public int Revision { get; private set; }

    /// <summary>어느 날까지 따라잡았는지.</summary>
    public DateTime AsOf => _asOf;

    /// <summary>지금 길 위에 있는 사람 수.</summary>
    public int Walking => _rows.Count(r => r.Dest >= 0);

    /// <summary>그 날짜까지 따라잡는다. 이미 지난 날이면 아무것도 안 한다.</summary>
    public void Advance(DateTime today)
    {
        if (today <= _asOf) return;

        // 굴림은 매월 1일에 한 번이라 달 경계마다 끊어 나아간다.
        var at = _asOf;
        while (at < today)
        {
            var nextMonth = new DateTime(at.Year, at.Month, 1).AddMonths(1);
            var step = nextMonth <= today ? nextMonth : today;

            Walk((step - at).Days);
            at = step;
            if (at == nextMonth) Roll(at);
        }
        _asOf = today;
    }

    // ── 하루 넘김과 도착 ───────────────────────────────────────────────────────

    private void Walk(int days)
    {
        if (days <= 0) return;

        foreach (var row in _rows)
        {
            row.Wait += days;
            if (row.Dest >= 0) Arrive(row);
        }
    }

    private void Arrive(PersonTable.Row row)
    {
        int far = Distance(row);
        if (far < 0) return;                          // 자리를 모르면 그 자리에 둔다
        if (row.Wait * SpeedPerDay < far) return;     // 아직 가는 중

        row.City = row.Dest;
        row.From = -1;
        row.Dest = -1;
        row.Wait = -RestDays;
        Revision++;
    }

    /// <summary>출발 도시에서 목적지까지. 자리를 모르면 -1.</summary>
    private int Distance(PersonTable.Row row)
    {
        if (_cities is not { } cities) return -1;
        if (!cities.TryCell(row.From, out int fx, out int fy, out _)) return -1;
        if (!cities.TryCell(row.Dest, out int tx, out int ty, out _)) return -1;

        int dx = tx - fx, dy = ty - fy;
        if (Math.Abs(dx) >= WorldWidth / 2) dx += dx > 0 ? -WorldWidth : WorldWidth;
        return (int)Math.Sqrt((double)dx * dx + (double)dy * dy);
    }

    // ── 달 넘김 ────────────────────────────────────────────────────────────────

    private void Roll(DateTime when)
    {
        foreach (var row in _rows)
        {
            if (row.Id < PersonTable.VoyagerCount) continue;   // 역사 항해사는 각본이 옮긴다
            if (row.Id >= PersonTable.MovingEnd) continue;     // 이벤트 인물은 안 움직인다
            if (!Active(row)) continue;
            if (row.Wait < 0) continue;                        // 아직 쉬는 중
            if (row.Dest >= 0) continue;                       // 이미 가는 중

            var dice = new GameRandom(Seed(row.Id, when));
            if (dice.Next(Odds) != 0) continue;

            var picks = Candidates(row);
            if (picks.Count == 0) continue;

            row.From = row.City;
            row.Dest = picks[dice.Next(picks.Count)];
            row.City = -1;                                     // 도시에서 빠진다
            row.Wait = 0;
            Revision++;
        }
    }

    /// <summary>등장했고 열여덟에서 예순 사이인가.</summary>
    private static bool Active(PersonTable.Row row) =>
        row.Appear != 0 && row.Age >= Youngest && row.Age <= Oldest;

    /// <summary>갈 만한 도시를 모은다. 하나도 없으면 그 달은 안 움직인다.</summary>
    private List<int> Candidates(PersonTable.Row row)
    {
        var got = new List<int>();
        if (_cities is not { } cities || row.City < 0) return got;

        int region = cities.RegionOf(row.City);
        int culture = cities.CultureOf(row.City);
        int nation = cities.NationOf(row.City);

        for (int city = 0; city < PersonTable.CityCount; city++)
        {
            bool ok = row.Kind switch
            {
                0 => cities.RegionOf(city) == region && Harbor(city),
                1 => cities.CultureOf(city) == culture && Harbor(city),
                3 => cities.NationOf(city) == nation && !NotFoundedYet.Contains(city),
                _ => false,                       // 갈래 2 는 후보를 못 담아 영영 안 움직인다
            };
            if (ok) got.Add(city);
        }
        return got;
    }

    /// <summary>
    /// 그 달 그 사람의 주사위. <c>(번호, 해, 달)</c> 로 씨를 뿌려 언제 따라잡아도 같게 나온다.
    /// </summary>
    private static int Seed(int id, DateTime when) =>
        (id * 10007) ^ (when.Year * 137 + when.Month * 11);

    // ── 항구 ───────────────────────────────────────────────────────────────────

    private bool Harbor(int city) =>
        city >= 0 && city < _harbor.Length && _harbor[city];

    /// <summary>
    /// 도시마다 항구가 있는지. 건물 표를 못 읽으면 <b>다 있는 셈</b> 친다 —
    /// 게임 폴더를 모르는 자리에서도 사람이 돌아다니게 해 두는 편이 낫다.
    /// </summary>
    /// <remarks>항구는 건물 코드 0 이다(<see cref="CityBuildingTable.Building.Code"/>).</remarks>
    private static bool[] Harbors(CityBuildingTable? buildings)
    {
        var got = new bool[PersonTable.CityCount];
        if (buildings == null)
        {
            Array.Fill(got, true);
            return got;
        }
        foreach (var building in buildings.Buildings)
            if (building.Code == 0 && building.City >= 0 && building.City < got.Length)
                got[building.City] = true;
        return got;
    }
}
