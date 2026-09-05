using CdsHelper.Game.Engine;
using CdsHelper.Game.Engine.Discovery;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 인물이 도시 사이를 옮겨 다니는 시늉 — 매월 1일에 목적지를 뽑고, 닿으면 예순 날 쉰다.
/// </summary>
/// <remarks>
/// 게임의 손 넷을 그대로 옮긴 것이다(볼트 <c>72.분석-인물 이동(역사 항해사와 매달 굴림)</c>).
/// <code>
///   0x004327F0  달 넘김   14~200 만 굴린다. 활동 중 · 쉬는 날이 끝났고 · 가는 데가 없고
///                         · rand(5)==0 이라야 226곳에서 후보를 모아 하나를 뽑는다
///   0x00432995  달 넘김   0~13 은 굴리지 않고 제 대본(HISTCHR.CDS)을 돌린다 —
///                         그 안의 3C 08 <도시> 가 사람을 떠나보낸다
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
/// <b>게임과 다른 것 둘.</b>
/// <list type="number">
///   <item><b>나이를 안 먹인다.</b> 표의 나이는 구워 온 판의 값이고 우리 놀이는
///   1480년에 시작하므로, 해를 더하면 스무 해 만에 죄다 예순을 넘겨 아무도 안 움직이게
///   된다. 그래서 활동 판정(18~60)은 표에 적힌 나이를 그대로 본다.</item>
///
///   <item><b>역사 항해자 열넷은 활동 판정을 안 본다.</b> 게임은
///   <c>0x004327F0</c> 첫 줄에서 활동 판정을 먼저 하지만, 우리 표가 <b>1517년 판</b>에서
///   구운 것이라 그때 이미 죽은 다섯(디아스 · 아르메이다 · 알브켈케 · 코론 · 캐벗)이
///   「등장 안 함」으로 적혀 있다. 그대로 보면 1480년에 시작하는 판에서 정작 초반의
///   주인공들이 얼어붙는다. <b>대본이 곧 그 사람의 한살이</b>라 — 디아스는 1480~1500,
///   코론은 1485~1506 이 전부다 — 날짜를 대본에 맡기는 편이 오히려 판에 맞는다.</item>
/// </list>
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

    /// <summary>역사 항해자 열넷의 대본. 없으면 그들은 안 움직인다.</summary>
    private readonly HistoryVoyages? _script;

    /// <summary>발견물 자리를 재려면 그 표가 있어야 한다.</summary>
    private readonly DiscoveryTable? _places;

    /// <summary>
    /// <b>도시가 아닌 자리</b>로 가는(또는 가 있는) 사람들 — 인물 번호 → 세계 좌표.
    /// </summary>
    /// <remarks>
    /// <c>3C 0B</c> 는 도시가 아니라 발견물 자리로 보내므로 목적지를 도시 번호로 적을 수가
    /// 없다. 인물 줄은 표에 구워 두는 것이라 여기에 곁으로 들고 있는다 —
    /// <see cref="PersonWorld"/> 는 어차피 아무것도 적어 두지 않고 날짜만으로 다시 셈한다.
    /// </remarks>
    private readonly Dictionary<int, (int X, int Y)> _bound = [];

    /// <summary>
    /// 목적지가 도시가 아니라 좌표일 때 <see cref="PersonTable.Row.Dest"/> 에 박는 값.
    /// </summary>
    public const int SpotDest = -2;

    private DateTime _asOf;

    /// <summary>
    /// 표를 받아 세상을 연다.
    /// </summary>
    /// <param name="start">놀이가 시작하는 날. 여기서부터 따라잡는다.</param>
    /// <param name="script">
    /// 역사 항해자 대본(<c>HISTCHR.CDS</c>). 없으면 0~13번은 제자리에 앉아 있는다.
    /// </param>
    /// <param name="places">
    /// 발견물 표. <c>3C 0B</c> 이 보내는 자리를 여기서 잰다 — 없으면 그 수는 건너뛴다.
    /// </param>
    public PersonWorld(PersonTable table, CityExeTable? cities, CityBuildingTable? buildings,
                       DateTime start, HistoryVoyages? script = null,
                       DiscoveryTable? places = null)
    {
        _rows = [.. table.People];
        _cities = cities;
        _harbor = Harbors(buildings);
        _script = script;
        _places = places;
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
    public int Walking => _rows.Count(Moving);

    /// <summary>길 위에 있는가 — 도시로 가든 발견물 자리로 가든.</summary>
    public static bool Moving(PersonTable.Row row) => row.Dest >= 0 || row.Dest == SpotDest;

    /// <summary>
    /// 도시 밖에 서 있는 사람의 세계 좌표. 도시에 앉아 있으면 null.
    /// </summary>
    /// <remarks>발견물 자리로 간 사람은 닿은 뒤에도 그 자리에 머문다 — 다음 수까지다.</remarks>
    public (int X, int Y)? SpotOf(int person) =>
        _bound.TryGetValue(person, out var at) ? at : null;

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
            if (Moving(row)) Arrive(row);
        }
    }

    private void Arrive(PersonTable.Row row)
    {
        int far = Distance(row);
        if (far < 0) return;                          // 자리를 모르면 그 자리에 둔다
        if (row.Wait * SpeedPerDay < far) return;     // 아직 가는 중

        // 발견물 자리로 간 사람은 앉을 도시가 없다 — 그 좌표에 그대로 선다.
        row.City = row.Dest == SpotDest ? -1 : row.Dest;
        row.From = -1;
        row.Dest = -1;
        row.Wait = -RestDays;
        Revision++;
    }

    /// <summary>출발 도시에서 목적지까지. 자리를 모르면 -1.</summary>
    private int Distance(PersonTable.Row row)
    {
        if (Leg(row) is not { } leg) return -1;
        return (int)Math.Sqrt((double)leg.Dx * leg.Dx + (double)leg.Dy * leg.Dy);
    }

    /// <summary>
    /// 지금 가고 있는 다리 — 떠난 자리와 <b>거기서부터 잰 어긋남</b>. 못 재면 null.
    /// </summary>
    /// <remarks>
    /// 세계가 <see cref="WorldWidth"/> 폭으로 감기므로 반 바퀴를 넘으면 짧은 쪽으로 돌린다
    /// (<c>0x004324E9</c> 의 <c>0x4E2</c> 견줌).
    /// </remarks>
    private (int Fx, int Fy, int Dx, int Dy)? Leg(PersonTable.Row row)
    {
        if (_cities is not { } cities) return null;
        if (!cities.TryCell(row.From, out int fx, out int fy, out _)) return null;

        int tx, ty;
        if (row.Dest == SpotDest)
        {
            if (!_bound.TryGetValue(row.Id, out var to)) return null;
            (tx, ty) = to;
        }
        else if (!cities.TryCell(row.Dest, out tx, out ty, out _)) return null;

        int dx = tx - fx, dy = ty - fy;
        if (Math.Abs(dx) >= WorldWidth / 2) dx += dx > 0 ? -WorldWidth : WorldWidth;
        return (fx, fy, dx, dy);
    }

    /// <summary>
    /// 그 사람이 <b>지금 서 있는 세계 칸</b>. 도시에 앉아 있으면 null.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x00432470</c> 이다 — 하루하루 좌표를 옮겨 적지 않고 <b>물어볼 때 셈해
    /// 낸다</b>. 떠난 자리에서 목표 쪽으로 <c>날 셈 x 24 / 거리</c> 만큼 간 데다.
    /// 게임은 칸을 열여섯으로 쪼개고 하루를 마흔여덟 눈금으로 다시 쪼개 그 안에서도
    /// 부드럽게 움직이는데, 우리 셈은 하루가 가장 잘게 나눈 단위라 <b>날 단위</b>다.
    ///
    /// 발견물 자리로 갔던 사람은 <b>닿은 뒤에도 그 자리에 서 있다</b> — 앉을 도시가 없다.
    /// </remarks>
    public (double X, double Y)? CellOf(PersonTable.Row row)
    {
        if (!Moving(row))
            return _bound.TryGetValue(row.Id, out var stood) ? (stood.X, stood.Y) : null;

        if (Leg(row) is not { } leg) return null;

        int far = (int)Math.Sqrt((double)leg.Dx * leg.Dx + (double)leg.Dy * leg.Dy);
        double gone = far <= 0 ? 1 : Math.Clamp(row.Wait * (double)SpeedPerDay / far, 0, 1);

        double x = leg.Fx + leg.Dx * gone, y = leg.Fy + leg.Dy * gone;
        if (x < 0) x += WorldWidth;
        else if (x >= WorldWidth) x -= WorldWidth;
        return (x, y);
    }

    /// <summary>
    /// 그 사람의 뱃머리 — 16방위(0 북 · 4 서 · 8 남 · 12 동). 서 있으면 남쪽을 본다.
    /// </summary>
    /// <remarks>
    /// 게임은 네 쪽만 쓴다(<c>0x00432596</c>) — 어긋남이 큰 축을 골라 그 부호로 정한다.
    /// </remarks>
    public int HeadingOf(PersonTable.Row row)
    {
        if (Leg(row) is not { } leg) return 8;
        return Math.Abs(leg.Dy) > Math.Abs(leg.Dx)
            ? (leg.Dy < 0 ? 0 : 8)
            : (leg.Dx < 0 ? 4 : 12);
    }

    /// <summary>
    /// 지도에 세울 사람들 — 도시 밖에 자리가 있는 이들이다.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x00426790</c> 은 281명을 통째로 훑어 <b>자리가 있는 사람</b>만 골라
    /// 앞에서부터 열여섯 칸을 채운다. 번호로 거르지 않으므로 역사 항해자도 그대로
    /// 걸린다 — 바다에서 마주치는 것이 이 때문이다. 몇을 낼지는 부르는 쪽이 정한다.
    /// </remarks>
    public IEnumerable<(PersonTable.Row Who, double X, double Y, int Heading)> Afloat()
    {
        foreach (var row in _rows)
        {
            if (!Active(row) && row.Id >= PersonTable.VoyagerCount) continue;
            if (CellOf(row) is not { } at) continue;
            yield return (row, at.X, at.Y, HeadingOf(row));
        }
    }

    // ── 달 넘김 ────────────────────────────────────────────────────────────────

    private void Roll(DateTime when)
    {
        foreach (var row in _rows)
        {
            // 역사 항해자는 여기서 갈린다 — 활동 판정보다 앞이다(위 <b>다른 것 둘</b>).
            if (row.Id < PersonTable.VoyagerCount) { Sail(row, when); continue; }
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

    /// <summary>
    /// 역사 항해자 하나를 제 대본대로 떠나보낸다(<c>0x00432995</c>).
    /// </summary>
    /// <remarks>
    /// 이들은 주사위를 안 굴린다 — 대본에 적힌 달이 오면 <b>쉬는 중이든 가는 중이든</b>
    /// 그 자리에서 다시 떠난다. 게임의 <c>3C 08</c>(<c>0x0040AB77</c>)이 목적지를 박기 전에
    /// 아무 문턱도 안 보기 때문이다.
///
    /// 출발 자리는 지금 앉은 도시고, 이미 길 위였으면 <b>떠나 온 도시를 그대로 물려받는다</b>.
    /// 게임은 그때 지금 좌표를 재어 새 출발점으로 삼는데(<c>0x0040AC17</c>), 우리는 좌표를
    /// 안 들고 있어 도시 둘 사이로만 거리를 잰다 — 대본이 한 달에 한 수씩이라 어긋나 봐야
    /// 한 다리다.
///
    /// <b><c>3C 0B</c>(발견물 자리로)는 원본을 안 따르고 <b>고쳐서</b> 옮긴다.</b> 게임이
    /// 내는 자리가 <c>((x2-x1)/2, (y2-y1)/2)</c> 라 <b>넓이의 절반</b>이지 가운데가 아니다
    /// (<c>0x0040AD15</c> 의 <c>sub eax, edi</c> — 바이트가 <c>2B C7</c> 이라 <c>03 C7</c>
    /// 의 오타로 보인다). 발견물 6번이면 <c>(0, 0)</c> 이 나와 지도 왼쪽 위 모서리로
    /// 날아간다. 그래서 <c>(x1+x2)/2</c> 로 <b>진짜 가운데</b>를 낸다 — 마가랴네스·엘카노가
    /// 제 항로에 뜬다. 열두 수뿐이고 발견물도 <c>6 · 185 · 186</c> 셋뿐이다.
    /// </remarks>
    private void Sail(PersonTable.Row row, DateTime when)
    {
        if (_script is not { } script) return;

        foreach (var move in script.MovesOn(row.Id, when.Year, when.Month))
        {
            if (move.ToCity && move.City == row.City) continue;   // 이미 그 도시에 앉아 있다

            (int X, int Y)? spot = move.ToCity ? null : Middle(move.Discovery);
            if (!move.ToCity && spot == null) continue;           // 자리를 못 재면 그냥 둔다

            int from = row.City >= 0 ? row.City : row.From;
            if (from < 0)
            {
                // 떠나는 도시를 모른다. 두 가지다 — 구워 온 표에 자리가 없는 사람 셋
                // (디아스 · 엘카노 · 칼티에)의 첫 수이거나, 발견물 자리에 서 있다가
                // 다시 떠나는 수다. 거리는 도시 둘 사이로만 재므로 길 없이 그 자리에
                // 세우고, 다음 수부터 제대로 항해한다.
                if (move.ToCity) { row.City = move.City; _bound.Remove(row.Id); }
                else _bound[row.Id] = spot!.Value;

                row.Wait = 0;
                Revision++;
                return;
            }

            row.From = from;
            row.City = -1;                                       // 도시에서 빠진다
            row.Wait = 0;

            if (move.ToCity) { row.Dest = move.City; _bound.Remove(row.Id); }
            else { row.Dest = SpotDest; _bound[row.Id] = spot!.Value; }

            Revision++;
            return;                                              // 한 달에 한 수면 넉넉하다
        }
    }

    /// <summary>
    /// 그 발견물이 놓인 네모의 <b>가운데</b>. 자리가 없는 발견물이면 null.
    /// </summary>
    /// <remarks>
    /// 게임은 여기서 <c>(x2-x1)/2</c> 를 내는데 그것은 넓이의 절반이다 — 위 주석 참고.
    /// </remarks>
    private (int X, int Y)? Middle(int discovery)
    {
        if (_places?.Find(discovery) is not { } spot || !spot.HasPlace) return null;
        return ((spot.X1 + spot.X2) / 2, (spot.Y1 + spot.Y2) / 2);
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
