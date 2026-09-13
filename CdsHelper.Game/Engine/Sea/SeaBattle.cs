using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Engine.Sea;

/// <summary>
/// 해전 판 한 벌 — 격자·배·바람·이동 계획·동시 이동·충돌·퇴각·적의 길 짜기. 화면은 모른다.
/// </summary>
/// <remarks>
/// 볼트 <c>47.분석-해전</c> · <c>66.분석-해전 이동계획</c> · <c>83.분석-해전 적 AI와 들머리</c> 를 옮겼다.
/// <code>
///   격자        23 x 17 육각 — 짝수 X 줄에는 Y=16 이 없다(0x00441C5F)
///   배          열여섯 — 0~7 아군 · 8~15 적
///   처음 자리   아군 X=17 · 적 X=5, Y=7·8·9, 방향 아군 rand(2)+4 · 적 rand(2)+1(0x00442244)
///   걸음 하나   먼저 돈다(0 그대로 · 1 오른쪽 · 2 왼쪽) → <b>그리고 한 칸 나아간다</b>(0x0043AC00)
///   이동력      0x004349A0 — (바람세기+1) * 추진력 * 돛효율 / 100 + 1, 최대 6, 정면 역풍이면 1
///   한 턴       계획 → 결정 → 적의 길(0x0043B710) → 모든 배가 걸음 하나씩 나란히(0x0043CA60)
///   충돌        들어갈 칸에 배가 있으면 선다 — 다음 턴 한 번 못 움직인다(0x00439858 · 0x0043D7E7)
///   턴 끝       rand(10)==0 이면 바람이 (풍향+5)%6 으로 돈다
///   퇴각 지대   바람이 정한 가장자리 한 곳(0x0043E090)
/// </code>
/// <b>아직 없는 것</b> — 포격·접현·나포·일기토·전리품(다음 단계)과 부관 위임(<c>+0x944</c>).
/// </remarks>
public sealed class SeaBattle
{
    /// <summary>격자 가로 칸(X 0~22)과 세로 칸(Y 0~16).</summary>
    public const int Cols = 23, Rows = 17;

    /// <summary>한 편 배 수. 0~7 이 아군, 8~15 가 적이다.</summary>
    public const int PerSide = 8;

    /// <summary>방향 가짓수(육각).</summary>
    public const int Ways = 6;

    /// <summary>한 턴에 갈 수 있는 걸음의 끝.</summary>
    public const int MaxPower = 6;

    /// <summary>이동력이 하나 붙는 선수상 번호(<c>0x0044CA30</c> == 0x21).</summary>
    public const int SwiftFigurehead = 0x21;

    /// <summary>걸음 하나의 선회 — 0 그대로 · 1 오른쪽(방향+1) · 2 왼쪽(방향+5).</summary>
    public enum Move { Straight = 0, TurnRight = 1, TurnLeft = 2 }

    /// <summary>배 한 척의 상태. 원본 배 칸 <c>+0x24</c>(4 이상 = 떠 있음 · 3 퇴각 · 2 이하 잃음).</summary>
    public enum ShipState { Afloat, Retreated, Lost }

    /// <summary>판 위의 배 한 척.</summary>
    public sealed class Ship
    {
        public int Index { get; init; }
        public bool Mine => Index < PerSide;
        public bool Flagship => Index % PerSide == 0;
        public string Name { get; init; } = "";
        /// <summary>선박 종류 이름(<c>+0x314</c>) — 해전전황정보(선박)에 낸다.</summary>
        public string HullName { get; init; } = "";
        /// <summary>적재량(<c>+0x300</c>).</summary>
        public int Cargo { get; init; }
        /// <summary>대포 문수(<c>+0x31C</c>). 큰 한 방을 맞으면 준다.</summary>
        public int Guns { get; internal set; }
        public int X { get; internal set; }
        public int Y { get; internal set; }
        /// <summary>뱃머리 0~5. 0 이 위(Y−), 시계 방향이다.</summary>
        public int Way { get; internal set; }
        /// <summary>추진력(배 칸 <c>+0x14</c>).</summary>
        public int Speed { get; init; }
        /// <summary>마스트 셋의 돛(0 없음 · 1 삼각 · 2 사각) — 이동력 셈의 a·b·c.</summary>
        public int[] Sails { get; init; } = new int[3];
        /// <summary>선수상 번호. 0x21 이면 이동력 +1.</summary>
        public int Figurehead { get; init; } = -1;
        /// <summary>내구(<c>+0x304</c>).</summary>
        public int Hp { get; internal set; }
        /// <summary>승원(<c>+0x308</c>).</summary>
        public int Crew { get; internal set; }
        /// <summary>필요승원 — 적이 물러설지 볼 때 <c>선박표[0x4FC214]+10</c> 과 견준다.</summary>
        public int MinCrew { get; init; }
        /// <summary>대포 갈래(−1 없음 · 0 세이커 · 1 캘버린 · 2 페리에 · 3 카논).</summary>
        public int Gun { get; init; } = -1;
        /// <summary>이번 턴 이동력(<c>+0x2F8</c>).</summary>
        public int Power { get; internal set; }
        public ShipState State { get; internal set; } = ShipState.Afloat;
        /// <summary>그림 벌(SCOMBAT 파트 5~12).</summary>
        public int Art { get; init; }
        /// <summary>이번 턴 걸음(<c>+0x324</c> 여섯). 걸음마다 선회 하나 + 한 칸.</summary>
        public List<Move> Plan { get; } = [];
        /// <summary>지시를 마쳤는지(<c>+0x320</c> 1).</summary>
        public bool Ordered { get; internal set; }
        /// <summary>이번 턴에 부딪혀 섰는지(<c>+0x320</c> 3).</summary>
        public bool Crashed { get; internal set; }
        /// <summary>지난 턴에 부딪혀 이번 턴은 못 움직이는지(<c>+0x320</c> 2).</summary>
        public bool Stuck { get; internal set; }

        public bool CanAct => State == ShipState.Afloat;
    }

    private readonly Ship?[] _ships = new Ship?[PerSide * 2];
    private readonly Random _rng;

    /// <summary>격자 표시 — 비트 8 은 먼저 짠 배가 지나갈 칸, 4 는 위험 칸(<c>+0x96C</c>).</summary>
    private readonly int[,] _marks = new int[Cols, Rows];

    /// <summary>풍향 0~5(<c>+0x0860</c>). 0 이 북풍이다.</summary>
    public int Wind { get; private set; }

    /// <summary>바람 세기(<c>+0x0864</c>).</summary>
    public int WindStrength { get; private set; }

    public IEnumerable<Ship> Ships => _ships.OfType<Ship>();

    public Ship? At(int index) => index >= 0 && index < _ships.Length ? _ships[index] : null;

    public SeaBattle(Random rng, int wind, int windStrength)
    {
        _rng = rng;
        Wind = ((wind % Ways) + Ways) % Ways;
        WindStrength = Math.Max(0, windStrength);
    }

    /// <summary>
    /// 바다의 바람(16방위 · 세기)으로 판을 연다(<c>0x00441F1C</c>).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   방위 0·15 → 3      1~3 → 4      4 → 4 또는 5      5·6 → 5
    ///   방위 7~9  → 0      10·11 → 1    12 → 1 또는 2     13·14 → 2
    /// </code>
    /// 세기는 그대로 <c>+0x864</c> 에 든다.
    /// </remarks>
    public static SeaBattle FromSeaWind(Random rng, int dir16, int strength)
    {
        int d = ((dir16 % 16) + 16) % 16;
        int wind = d switch
        {
            0 or 15 => 3,
            >= 1 and <= 3 => 4,
            4 => rng.Next(2) == 0 ? 4 : 5,
            5 or 6 => 5,
            >= 7 and <= 9 => 0,
            10 or 11 => 1,
            12 => rng.Next(2) == 0 ? 1 : 2,
            _ => 2,
        };
        return new SeaBattle(rng, wind, strength);
    }

    // ── 격자 ───────────────────────────────────────────────────────────────

    /// <summary>판 안의 칸인지.</summary>
    public static bool OnBoard(int x, int y) =>
        x >= 0 && x < Cols && y >= 0 && y < Rows && !(y == Rows - 1 && (x & 1) == 0);

    /// <summary>그 방향으로 한 칸(<c>0x0043AD22</c>).</summary>
    public static (int X, int Y) Step(int x, int y, int way)
    {
        bool even = (x & 1) == 0;
        return (((way % Ways) + Ways) % Ways) switch
        {
            0 => (x, y - 1),
            1 => (x + 1, even ? y : y - 1),
            2 => (x + 1, even ? y + 1 : y),
            3 => (x, y + 1),
            4 => (x - 1, even ? y + 1 : y),
            _ => (x - 1, even ? y : y - 1),
        };
    }

    /// <summary>선회 하나를 먹인 방향.</summary>
    public static int Turn(int way, Move move) => move switch
    {
        Move.TurnRight => (way + 1) % Ways,
        Move.TurnLeft => (way + 5) % Ways,
        _ => way,
    };

    /// <summary>그 칸에 떠 있는 배. 없으면 null.</summary>
    public Ship? ShipAt(int x, int y) =>
        Ships.FirstOrDefault(s => s.CanAct && s.X == x && s.Y == y);

    /// <summary>두 칸 사이 걸음 거리(판 안에서). 판 밖으로 돌아가는 길은 안 센다.</summary>
    public static int Distance(int x1, int y1, int x2, int y2) => BfsDistance(x1, y1, x2, y2);

    /// <summary>걸음으로 잰 거리(판 안에서만). 칸이 적어 너비 우선으로 잰다.</summary>
    private static int BfsDistance(int x1, int y1, int x2, int y2)
    {
        if (x1 == x2 && y1 == y2) return 0;
        var seen = new bool[Cols, Rows];
        var queue = new Queue<(int X, int Y, int D)>();
        queue.Enqueue((x1, y1, 0));
        seen[x1, y1] = true;
        while (queue.Count > 0)
        {
            var (x, y, d) = queue.Dequeue();
            for (int w = 0; w < Ways; w++)
            {
                var (nx, ny) = Step(x, y, w);
                if (!OnBoard(nx, ny) || seen[nx, ny]) continue;
                if (nx == x2 && ny == y2) return d + 1;
                seen[nx, ny] = true;
                queue.Enqueue((nx, ny, d + 1));
            }
        }
        return int.MaxValue;
    }

    // ── 배 놓기 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 배를 판에 올린다. <paramref name="slot"/> 은 그 편 안의 차례(0~7), 0 이 기함이다.
    /// </summary>
    /// <remarks>
    /// 원본 <c>0x00442244</c> 은 아군 X=17 · 적 X=5 에 Y=7·8·9 로 세운다. 넷째부터의 자리는
    /// 아직 못 짚어 옆 줄(X∓1, X∓2 …)로 벌려 세운다.
    /// </remarks>
    public Ship Place(bool mine, int slot, string name, int speed, int[] sails, int art,
                      int hp = 50, int crew = 30, int minCrew = 10, int gun = -1, int figurehead = -1,
                      int formation = -1, string hullName = "", int cargo = 0, int guns = 0)
    {
        int index = (mine ? 0 : PerSide) + Math.Clamp(slot, 0, PerSide - 1);
        int baseX = mine ? 17 : 5;
        int x, y;
        if (formation is >= 0 and < FormationCount)
        {
            // 대열 — 기함 자리에 호위 (ΔX, ΔY) 를 더한다(0x004421F6). 적은 가로를 뒤집어 선다.
            int flagY = FlagshipRow(formation);
            if (slot == 0) { x = baseX; y = flagY; }
            else
            {
                var (dx, dy) = Formations[formation][Math.Min(slot, 7) - 1];
                x = baseX + (mine ? dx : -dx);
                y = flagY + dy;
            }
        }
        else
        {
            int rank = slot / 3, file = slot % 3;
            x = baseX + (mine ? rank : -rank);
            y = 7 + file;
        }
        (x, y) = FreeCellNear(x, y);

        var ship = new Ship
        {
            Index = index,
            Name = name,
            X = x,
            Y = y,
            Way = mine ? _rng.Next(2) + 4 : _rng.Next(2) + 1,
            Speed = speed,
            Sails = sails,
            Art = art,
            Hp = hp,
            Crew = crew,
            MinCrew = minCrew,
            Gun = gun,
            Figurehead = figurehead,
            HullName = hullName,
            Cargo = cargo,
            Guns = gun < 0 ? 0 : guns,
        };
        _ships[index] = ship;
        ship.Power = PowerOf(ship);
        return ship;
    }

    // ── 대열 ──────────────────────────────────────────────────────────────

    /// <summary>대열 가짓수.</summary>
    public const int FormationCount = 8;

    /// <summary>대열 이름 — 창에는 그림만 있어 우리가 붙인 이름이다.</summary>
    public static readonly string[] FormationNames =
        ["가로 한 줄", "세로 한 줄", "쐐기", "둘러싸기", "넓은 쐐기", "넓게 감싸기", "부채", "흩어짐"];

    /// <summary>
    /// 호위함 일곱의 (ΔX, ΔY) — 대열마다(볼트 84, <c>0x0044231E</c>~ 앞 벌).
    /// </summary>
    /// <remarks>
    /// 원본 스택 표는 두 벌이고 어느 벌이 아군인지 아직 못 짚었다. 앞 벌을 쓰고 적은 가로를 뒤집는다.
    /// </remarks>
    public static readonly (int Dx, int Dy)[][] Formations =
    [
        [(-2, 0), (2, 0), (-4, 0), (4, 0), (-6, 0), (6, 0), (-8, 0)],
        [(0, -2), (0, 2), (0, -4), (0, 4), (0, -6), (0, 6), (0, -8)],
        [(0, -3), (-2, -2), (2, -2), (-3, -1), (3, -1), (-1, 1), (1, 1)],
        [(2, 0), (1, -2), (1, 1), (0, -3), (0, 3), (-2, -1), (-2, 1)],
        [(0, -3), (2, -2), (-2, -2), (3, 0), (-3, -2), (5, 1), (-5, 1)],
        [(2, 0), (1, 1), (1, -2), (0, 3), (0, -3), (-2, 4), (-2, -4)],
        [(-1, -3), (1, -3), (-2, 1), (2, 1), (-4, -1), (4, -1), (0, 2)],
        [(2, -1), (2, 1), (-1, -3), (-1, 2), (0, -4), (0, 4), (-2, 0)],
    ];

    /// <summary>기함 Y — 대열 0·4·6 은 7, 5 는 9, 그 밖은 8(<c>0x004421F6</c>).</summary>
    public static int FlagshipRow(int formation) => formation is 0 or 4 or 6 ? 7 : formation == 5 ? 9 : 8;

    /// <summary>
    /// 판 안에서 비어 있는 가장 가까운 칸. 원본이 판 밖·겹침을 비키는 셈(<c>0x00442B0A</c> 뒤)은 아직
    /// 못 짚어, 판 안으로 당긴 뒤 걸음이 가까운 빈 칸으로 옮긴다.
    /// </summary>
    private (int X, int Y) FreeCellNear(int x, int y)
    {
        x = Math.Clamp(x, 0, Cols - 1);
        y = Math.Clamp(y, 0, Rows - 1);
        if (!OnBoard(x, y)) y = Rows - 2;
        if (ShipAt(x, y) is null) return (x, y);

        var seen = new bool[Cols, Rows];
        var queue = new Queue<(int, int)>();
        queue.Enqueue((x, y));
        seen[x, y] = true;
        while (queue.Count > 0)
        {
            var (cx, cy) = queue.Dequeue();
            for (int w = 0; w < Ways; w++)
            {
                var (nx, ny) = Step(cx, cy, w);
                if (!OnBoard(nx, ny) || seen[nx, ny]) continue;
                if (ShipAt(nx, ny) is null) return (nx, ny);
                seen[nx, ny] = true;
                queue.Enqueue((nx, ny));
            }
        }
        return (x, y);
    }

    // ── 이동력 ────────────────────────────────────────────────────────────

    /// <summary>돛 효율표 — 줄 k = a+2b+4c (3~14), 칸 바람각 갈래(0 / 1·5 / 2·4).</summary>
    private static readonly int[,] SailTable =
    {
        { 5, 8, 5 }, { 8, 9, 3 }, { 8, 8, 4 }, { 10, 9, 2 }, { 6, 11, 6 }, { 8, 11, 5 },
        { 9, 10, 5 }, { 9, 11, 4 }, { 9, 10, 5 }, { 9, 11, 4 }, { 9, 11, 4 }, { 10, 11, 3 },
    };

    /// <summary>이동력(<c>0x004349A0</c>).</summary>
    public int PowerOf(Ship ship)
    {
        int angle = ((Wind - ship.Way) % Ways + Ways) % Ways;
        int a = ship.Sails.ElementAtOrDefault(0), b = ship.Sails.ElementAtOrDefault(1), c = ship.Sails.ElementAtOrDefault(2);
        int band = angle == 0 ? 0 : angle is 1 or 5 ? 1 : 2;

        int e;
        if (angle == 3) e = 0;
        else if (b == 0 && c == 0)
            e = band switch { 0 => a <= 1 ? 6 : 9, 1 => a <= 1 ? 5 : 6, _ => a <= 1 ? 3 : 1 };
        else e = SailTable[Math.Clamp(a + 2 * b + 4 * c, 3, 14) - 3, band];

        int power = e == 0 ? 1 : (WindStrength + 1) * ship.Speed * e / 100 + 1;
        if (ship.Mine && ship.Figurehead == SwiftFigurehead) power++;
        return Math.Min(MaxPower, power);
    }

    // ── 길 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 걸음을 따라간 칸들 — 걸음마다 먼저 돌고 한 칸 나아간다. 판 밖으로 나가면 null.
    /// </summary>
    public static List<(int X, int Y, int Way)>? Trace(int x, int y, int way, IReadOnlyList<Move> plan)
    {
        var path = new List<(int, int, int)>(plan.Count);
        foreach (var move in plan)
        {
            way = Turn(way, move);
            (x, y) = Step(x, y, way);
            if (!OnBoard(x, y)) return null;
            path.Add((x, y, way));
        }
        return path;
    }

    /// <summary>
    /// 길 후보 전부(<c>0x0043AC00</c>) — 걸음 수 1 부터 이동력까지, 선회 갈래 셋을 모두 늘어놓는다.
    /// 짧은 길이 앞에 온다.
    /// </summary>
    /// <param name="avoidReserved">먼저 짠 배가 지나갈 칸(+8)과 배가 선 칸을 피할지(모드 2 이상).</param>
    /// <param name="avoidDanger">위험 칸(+4)도 피할지(모드 2).</param>
    private IEnumerable<(List<Move> Plan, int X, int Y, int Way)> Paths(Ship ship, bool avoidReserved, bool avoidDanger)
    {
        for (int len = 1; len <= ship.Power; len++)
        {
            int total = (int)Math.Pow(3, len);
            for (int code = 0; code < total; code++)
            {
                var plan = new List<Move>(len);
                int rest = code;
                for (int i = 0; i < len; i++) { plan.Add((Move)(rest % 3)); rest /= 3; }

                int x = ship.X, y = ship.Y, way = ship.Way;
                bool ok = true;
                foreach (var move in plan)
                {
                    way = Turn(way, move);
                    (x, y) = Step(x, y, way);
                    if (!OnBoard(x, y)) { ok = false; break; }
                    if (avoidReserved && ((_marks[x, y] & 8) != 0 || ShipAt(x, y) is not null)) { ok = false; break; }
                    if (avoidDanger && (_marks[x, y] & 4) != 0) { ok = false; break; }
                }
                if (ok) yield return (plan, x, y, way);
            }
        }
    }

    /// <summary>사람이 찍을 수 있는 끝 칸들 — 같은 칸이면 가장 짧은 길 하나만 남긴다(모드 0).</summary>
    public List<(List<Move> Plan, int X, int Y, int Way)> Options(Ship ship)
    {
        var list = new List<(List<Move>, int, int, int)>();
        if (!ship.CanAct || ship.Stuck) return list;
        var seen = new HashSet<(int, int)>();
        foreach (var option in Paths(ship, avoidReserved: false, avoidDanger: false))
            if (seen.Add((option.X, option.Y))) list.Add(option);
        return list;
    }

    /// <summary>지시를 적는다. 빈 걸음이면 「이동하지 않습니다」다.</summary>
    public void Order(Ship ship, IEnumerable<Move> plan)
    {
        ship.Plan.Clear();
        if (!ship.Stuck) ship.Plan.AddRange(plan.Take(ship.Power));
        ship.Ordered = true;
    }

    /// <summary>
    /// 계획을 마친다(<c>0x0043DDD0</c>) — 지시 안 한 배는 제자리로 두고, 적의 길을 짠다(<c>0x0043B710</c>).
    /// </summary>
    public void EndPlanning()
    {
        foreach (var ship in Ships.Where(s => s.Mine && s.CanAct && !s.Ordered))
        {
            ship.Plan.Clear();
            ship.Ordered = true;
        }
        PlanSide(mine: false);
    }

    // ── 적의 길 — 0x0043B710 ─────────────────────────────────────────────

    /// <summary>
    /// 한 편 여덟의 길을 짠다. 먼저 짠 배가 지나갈 칸을 표시해(+8) 뒤의 배가 비켜 가게 한다.
    /// </summary>
    private void PlanSide(bool mine)
    {
        Array.Clear(_marks);
        var side = Ships.Where(s => s.Mine == mine && s.CanAct).OrderBy(s => s.Index).ToList();
        var foes = Ships.Where(s => s.Mine != mine && s.CanAct).ToList();

        foreach (var ship in side)
        {
            ship.Plan.Clear();
            if (ship.Stuck) { ship.Ordered = true; continue; }       // 지난 턴 충돌 — 걸음 0

            List<Move>? plan = null;

            if (WantsRetreat(ship, mine))
            {
                if (IsRetreatCell(ship.X, ship.Y) && !mine)
                {
                    // 적도 퇴각 지대에 서 있으면 판을 뜬다(원본은 편을 가리지 않고 +0x8DC 로 본다).
                    ship.State = ShipState.Retreated;
                    ship.Ordered = true;
                    continue;
                }

                // 살아 있는 상대 배 둘레(X±3 · Y±2)를 위험 칸으로 칠하고 가장자리 쪽 길을 찾는다.
                foreach (var foe in foes)
                    for (int dx = -3; dx <= 3; dx++)
                        for (int dy = -2; dy <= 2; dy++)
                        {
                            int cx = foe.X + dx, cy = foe.Y + dy;
                            if (OnBoard(cx, cy)) _marks[cx, cy] |= 4;
                        }

                plan = BestTowardEdge(ship, avoidDanger: true) ?? BestTowardEdge(ship, avoidDanger: false);

                for (int x = 0; x < Cols; x++)
                    for (int y = 0; y < Rows; y++)
                        _marks[x, y] &= ~4;
            }
            else if (PickTarget(ship, foes) is { } target)
            {
                var (ax, ay) = AimPoint(target);
                plan = Broadside(ship, ax, ay);
            }
            else
            {
                plan = TowardFlagship(ship, foes);
            }

            // 길을 못 찾으면 선회 하나만 굴리고 걸음은 0 이다.
            if (plan == null)
            {
                ship.Plan.Clear();
            }
            else
            {
                ship.Plan.AddRange(plan);
                // 지나갈 칸을 +8 로 잡아 둔다(0x0043B5B0).
                var path = Trace(ship.X, ship.Y, ship.Way, plan);
                if (path != null)
                    foreach (var (px, py, _) in path) _marks[px, py] |= 8;
            }
            ship.Ordered = true;
        }

        Array.Clear(_marks);
    }

    /// <summary>
    /// 물러설 배인지 — 내구 10 이하, 승원이 필요승원+10 이하, 또는 아군 수/3 이 적 수 이상.
    /// </summary>
    private bool WantsRetreat(Ship ship, bool mine)
    {
        int ours = Ships.Count(s => s.Mine && s.CanAct);
        int theirs = Ships.Count(s => !s.Mine && s.CanAct);
        int enemyCount = mine ? ours : theirs;      // 배의 편
        int opposing = mine ? theirs : ours;        // 상대 편
        return ship.Hp <= 10 || ship.Crew <= ship.MinCrew + 10 || opposing / 3 >= enemyCount;
    }

    /// <summary>
    /// 노릴 배(<c>0x0043A880(5, 0, 편)</c>) — 걸음 1 부터 5 까지 고리를 넓혀, 처음 걸린 고리에서
    /// <b>기함이면 곧장</b>, 아니면 내구가 가장 낮은 배를 고른다. 없으면 null.
    /// </summary>
    private Ship? PickTarget(Ship ship, IReadOnlyList<Ship> foes)
    {
        for (int ring = 1; ring <= 5; ring++)
        {
            var hits = foes.Where(f => BfsDistance(ship.X, ship.Y, f.X, f.Y) == ring).ToList();
            if (hits.Count == 0) continue;
            return hits.FirstOrDefault(f => f.Flagship) ?? hits.OrderBy(f => f.Hp).First();
        }
        return null;
    }

    /// <summary>
    /// 노릴 자리 — 상대가 역풍이 아니면 바람 쪽으로 n 칸 앞질러 본다(<c>0x0043B7xx</c>).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   n = max(0, rand(2) + 상대이동력/2 − 1)
    ///   바람 0: Y −= n     바람 3: Y += n
    ///   X 짝수 && 바람 2·4: Y += (n+1)/2      X 홀수 && 바람 1·5: Y += (−1−n)/2
    ///   바람 4 이상: X −= n     바람 1·2: X += n
    ///   짝수 X 에 Y 16 이면 상대 자리 그대로
    /// </code>
    /// </remarks>
    private (int X, int Y) AimPoint(Ship target)
    {
        int ax = target.X, ay = target.Y;
        if ((target.Way - Wind + Ways) % Ways == 3) return (ax, ay);

        int n = Math.Max(0, _rng.Next(2) + target.Power / 2 - 1);
        if (Wind == 0) ay -= n;
        if (Wind == 3) ay += n;
        if ((ax & 1) == 0 && Wind is 2 or 4) ay += (n + 1) / 2;
        if ((ax & 1) == 1 && Wind is 1 or 5) ay += (-1 - n) / 2;
        if (Wind >= 4) ax -= n;
        if (Wind is 1 or 2) ax += n;
        if ((ax & 1) == 0 && ay == Rows - 1) return (target.X, target.Y);
        return (Math.Clamp(ax, 0, Cols - 1), Math.Clamp(ay, 0, Rows - 1));
    }

    /// <summary>대포 사거리(<c>0x0043699D</c> 벌) — 캘버린 4 · 카논 2 · 그 밖 3.</summary>
    public static int RangeOf(int gun) => gun switch { 1 => 4, 3 => 2, _ => 3 };

    /// <summary>
    /// 모드 3 — 끝 칸에서 노릴 자리가 <b>뱃전</b>(이물·고물 줄이 아닌 쪽) 거리 2~사거리에 들어오는
    /// 첫 길을 고른다. 없으면 노릴 자리 쪽으로 가장 가까이 가는 길이다.
    /// </summary>
    /// <remarks>
    /// 원본 뱃전 줄(<c>0x0043AFF4</c>~<c>0x0043B1CA</c>)은 끝 방향마다 두 줄을 따로 셈하는데,
    /// 여기서는 「이물 줄·고물 줄 위가 아닌 거리 d 칸」으로 갈음했다.
    /// </remarks>
    private List<Move>? Broadside(Ship ship, int ax, int ay)
    {
        int range = RangeOf(ship.Gun);
        List<Move>? closest = null;
        int best = int.MaxValue;

        foreach (var (plan, x, y, way) in Paths(ship, avoidReserved: true, avoidDanger: false))
        {
            int d = BfsDistance(x, y, ax, ay);
            if (d >= 2 && d <= range && !OnBowLine(x, y, way, ax, ay)) return plan;
            if (d < best) { best = d; closest = plan; }
        }
        return closest;
    }

    /// <summary>그 자리가 끝 방향의 이물 줄이나 고물 줄 위인지.</summary>
    private static bool OnBowLine(int x, int y, int way, int ax, int ay)
    {
        foreach (int w in new[] { way, (way + 3) % Ways })
        {
            int cx = x, cy = y;
            for (int i = 0; i < 6; i++)
            {
                (cx, cy) = Step(cx, cy, w);
                if (!OnBoard(cx, cy)) break;
                if (cx == ax && cy == ay) return true;
            }
        }
        return false;
    }

    /// <summary>모드 4 — 노릴 배가 없으면 상대 기함(0번)에 |dX|+|dY| 가 가장 작아지는 길.</summary>
    private List<Move>? TowardFlagship(Ship ship, IReadOnlyList<Ship> foes)
    {
        var flag = foes.FirstOrDefault(f => f.Flagship) ?? foes.FirstOrDefault();
        if (flag == null) return null;
        List<Move>? pick = null;
        int best = int.MaxValue;
        foreach (var (plan, x, y, _) in Paths(ship, avoidReserved: true, avoidDanger: false))
        {
            int score = Math.Abs(x - flag.X) + Math.Abs(y - flag.Y);
            if (score < best) { best = score; pick = plan; }
        }
        return pick;
    }

    /// <summary>
    /// 모드 2·5 — 퇴각 가장자리에 가장 가까워지는 길. 바람 0: Y 작게, |X−10| · 1·2: X 크게, |Y−7| ·
    /// 3: Y 크게, |X−10| · 4·5: X 작게, |Y−7|.
    /// </summary>
    private List<Move>? BestTowardEdge(Ship ship, bool avoidDanger)
    {
        List<Move>? pick = null;
        (int, int) best = (int.MaxValue, int.MaxValue);
        foreach (var (plan, x, y, _) in Paths(ship, avoidReserved: true, avoidDanger: avoidDanger))
        {
            var score = Wind switch
            {
                0 => (y, Math.Abs(x - 10)),
                1 or 2 => (-x, Math.Abs(y - 7)),
                3 => (-y, Math.Abs(x - 10)),
                _ => (x, Math.Abs(y - 7)),
            };
            if (score.CompareTo(best) < 0) { best = score; pick = plan; }
        }
        return pick;
    }

    // ── 실행 ──────────────────────────────────────────────────────────────

    /// <summary>한 박자에 일어난 일 — 부딪힘 · 포격 · 알림.</summary>
    public sealed record Beat(int Index, IReadOnlyList<(Ship Mover, Ship Hit)> Crashes,
                              IReadOnlyList<Volley> Volleys, IReadOnlyList<string> Notices);

    /// <summary>
    /// 한 턴을 실행한다 — 모든 배가 걸음 하나씩을 <b>나란히</b> 딛고, 그 박자에 쏠 수 있는 배는 쏜다
    /// (<c>0x0043CA60</c>).
    /// </summary>
    /// <param name="onBeat">박자 하나가 끝날 때마다 부른다.</param>
    /// <remarks>
    /// <code>
    ///   틱 0..59 x 하위단계 0..3 x 배 i
    ///     하위단계 1·2 는 (이동력*(틱+1)) % 60 == 0 일 때만 돈다 — 곧 한 턴에 <b>이동력 번</b>
    ///     하위단계 2 = 포격(0x00436900)
    /// </code>
    /// 여기서는 틱을 「박자」로 묶었다 — 박자 k 에 걸음 k 를 딛고, k &lt; 이동력인 배가 쏜다. 걸음이 아니라
    /// 이동력으로 세므로 서 있거나 부딪혀 멈춘 배도 쏜다(볼트 85).
    ///
    /// 들어갈 칸에 배가 있으면 부딪혀 선다(<c>0x00439858</c>). 부딪힌 배는 다음 턴 한 번 못 움직인다.
    /// </remarks>
    public void Execute(Action<Beat>? onBeat = null)
    {
        int beats = Ships.Where(s => s.CanAct)
                         .Select(s => Math.Max(s.Plan.Count, s.Power))
                         .DefaultIfEmpty(0).Max();

        for (int k = 0; k < beats; k++)
        {
            var crashes = new List<(Ship, Ship)>();
            foreach (var ship in Ships.Where(s => s.CanAct && !s.Crashed && k < s.Plan.Count).ToList())
            {
                int way = Turn(ship.Way, ship.Plan[k]);
                var (nx, ny) = Step(ship.X, ship.Y, way);
                ship.Way = way;
                if (!OnBoard(nx, ny)) { ship.Crashed = true; continue; }
                if (ShipAt(nx, ny) is { } hit)
                {
                    ship.Crashed = true;
                    crashes.Add((ship, hit));
                    continue;
                }
                ship.X = nx;
                ship.Y = ny;
            }

            var volleys = new List<Volley>();
            var notices = new List<string>();
            foreach (var ship in Ships.Where(s => s.CanAct && k < s.Power).ToList())
            {
                if (!ship.CanAct) continue;                 // 이 박자에 먼저 가라앉았다
                if (Fire(ship, notices) is { } volley) volleys.Add(volley);
            }

            onBeat?.Invoke(new Beat(k, crashes, volleys, notices));
            if (AllMineGone || AllEnemyGone) break;
        }

        EndTurn();
    }

    // ── 포격 — 0x00436900 ─────────────────────────────────────────────────

    /// <summary>한 편 제독의 싸움 능력 — 포술 자리 · 무력 · 방어(<c>[0x910]</c> 벌).</summary>
    public readonly record struct Side(int Gunnery, int Might, int Defense);

    /// <summary>아군 제독(<c>[0x904]</c>~).</summary>
    public Side MineSide { get; set; } = new(0, 50, 50);

    /// <summary>적 제독(<c>[0x924]</c>~).</summary>
    public Side EnemySide { get; set; } = new(0, 50, 50);

    /// <summary>
    /// 아군 탄약 — 들머리에 함대 보급품 탄약 x 10 이다. 0 이 되면 한 번 알리고 −1(더는 안 쏜다). 적은 안 본다.
    /// </summary>
    public int Ammo { get; set; }

    private bool _noGunsWarned;

    /// <summary>한 발 — 맞았는지, 피해, 큰 한 방인지.</summary>
    public readonly record struct Shot(bool Hit, int Damage, bool Big);

    /// <summary>한 번의 포격 — 쏜 배, 과녁, 발들, 과녁이 가라앉았는지.</summary>
    public sealed record Volley(Ship Shooter, Ship Target, IReadOnlyList<Shot> Shots, bool Sunk);

    /// <summary>한 번에 쏘는 발 수(속사포면 여덟).</summary>
    public const int ShotsPerVolley = 3;

    /// <summary>대포 위력 — 세이커 3 · 캘버린 4 · 페리에 5 · 카논 8(<c>0x0043699D</c> 벌).</summary>
    public static int GunPower(int gun) => gun switch { 0 => 3, 1 => 4, 2 => 5, 3 => 8, _ => 0 };

    /// <summary>
    /// 한 번 쏜다. 탄약·대포가 없거나 과녁이 없으면 null.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   발마다  명중 = 굴림(max(1, 6*포술 + 17*√대포수 − 24))
    ///           피해 = (무력/(4−포술) + 대포수*위력)/10 − rand(상대 방어/20) ,  ≤ 0 이면 rand(4)+1
    ///   큰 한 방 (대포 ≥ 10, 포격당 한 번, 확률 5*포술 + 제 방어/10)
    ///           대포 ≥ 25 → x2 (≥ 35 면 과녁 대포 −(rand(3)+3)) · 20~24 → x3/2 · 그 밖 → x6/5
    ///   과녁 내구 = max(0, 내구 − 피해) — 0 이면 남은 발을 거두고 가라앉는다
    ///   아군이면 탄약 −= 쏜 발 수
    /// </code>
    /// 선수상 보정(0x1D·0x22·0x23·0x1A)과 과녁 추진력 깎기, 속사포는 아직 안 옮겼다.
    /// </remarks>
    private Volley? Fire(Ship ship, List<string> notices)
    {
        if (ship.Mine)
        {
            if (Ammo == 0)
            {
                notices.Add("탄약이 떨어졌습니다! 공격할 수 없습니다!");
                Ammo = -1;
                return null;
            }
            if (Ammo < 0) return null;
        }

        if (ship.Gun < 0 || ship.Guns <= 0)
        {
            if (ship.Mine && !_noGunsWarned && FireTarget(ship, RangeOf(ship.Gun)) is not null)
            {
                _noGunsWarned = true;
                notices.Add("대포를 싣지 않은 함선은 발사할 수 없습니다!");
            }
            return null;
        }

        if (FireTarget(ship, RangeOf(ship.Gun)) is not { } target) return null;

        var me = ship.Mine ? MineSide : EnemySide;
        var them = ship.Mine ? EnemySide : MineSide;
        int power = GunPower(ship.Gun);
        int gunnery = Math.Clamp(me.Gunnery, 0, 3);

        var shots = new List<Shot>();
        bool bigUsed = false;
        for (int i = 0; i < ShotsPerVolley; i++)
        {
            int odds = Math.Max(1, 6 * gunnery + (int)(17 * Math.Sqrt(ship.Guns)) - 24);
            if (_rng.Next(100) >= odds)
            {
                shots.Add(new Shot(false, 0, false));
                continue;
            }

            int damage = (me.Might / Math.Max(1, 4 - gunnery) + ship.Guns * power) / 10
                         - _rng.Next(Math.Max(1, them.Defense / 20));
            if (damage <= 0) damage = _rng.Next(4) + 1;

            bool big = false;
            if (!bigUsed && ship.Guns >= 10 && _rng.Next(100) < 5 * gunnery + me.Defense / 10)
            {
                bigUsed = big = true;
                if (ship.Guns >= 25)
                {
                    damage *= 2;
                    if (ship.Guns >= 35) target.Guns = Math.Max(0, target.Guns - (_rng.Next(3) + 3));
                }
                else if (ship.Guns >= 20) damage = damage * 3 / 2;
                else damage = damage * 6 / 5;
            }

            target.Hp = Math.Max(0, target.Hp - damage);
            shots.Add(new Shot(true, damage, big));
            if (target.Hp == 0) break;
        }

        if (ship.Mine) Ammo = Math.Max(0, Ammo - shots.Count);

        bool sunk = target.Hp == 0;
        if (sunk) target.State = ShipState.Lost;
        return new Volley(ship, target, shots, sunk);
    }

    /// <summary>
    /// 과녁 — 거리 2 부터 사거리까지 <b>뱃전</b> 쪽(이물·고물 줄이 아닌) 맞은편 산 배를 찾아, 그 거리에서
    /// 기함이면 곧장, 아니면 내구가 가장 낮은 배(<c>0x004369E0</c>~).
    /// </summary>
    /// <remarks>원본의 대각 방향 뱃전 줄 셈을 다 못 풀어 「이물·고물 줄 위가 아닌 거리 d」로 갈음한다.</remarks>
    private Ship? FireTarget(Ship ship, int range)
    {
        for (int d = 2; d <= range; d++)
        {
            var hits = Ships.Where(s => s.CanAct && s.Mine != ship.Mine
                                        && BfsDistance(ship.X, ship.Y, s.X, s.Y) == d
                                        && !OnBowLine(ship.X, ship.Y, ship.Way, s.X, s.Y))
                            .ToList();
            if (hits.Count == 0) continue;
            return hits.FirstOrDefault(s => s.Flagship) ?? hits.OrderBy(s => s.Hp).First();
        }
        return null;
    }

    /// <summary>턴 끝 — 부딪힌 배는 다음 턴에 못 움직이고, 바람이 열에 하나로 돌고, 이동력을 다시 매긴다.</summary>
    private void EndTurn()
    {
        if (_rng.Next(10) == 0) Wind = (Wind + 5) % Ways;

        foreach (var ship in Ships)
        {
            ship.Stuck = ship.Crashed;
            ship.Crashed = false;
            ship.Plan.Clear();
            ship.Ordered = false;
            ship.Power = PowerOf(ship);
        }
    }

    // ── 퇴각 ──────────────────────────────────────────────────────────────

    /// <summary>그 칸이 퇴각 지대인지(<c>0x0043E090</c>).</summary>
    public bool IsRetreatCell(int x, int y) => Wind switch
    {
        0 => x is >= 9 and <= 13 && y == 0,
        1 or 2 => x == Cols - 1 && y is >= 5 and <= 10,
        3 => x is >= 9 and <= 13 && y == 15 + (x & 1),
        _ => x == 0 && y is >= 5 and <= 10,
    };

    /// <summary>퇴각 지대 칸 전부.</summary>
    public IEnumerable<(int X, int Y)> RetreatCells()
    {
        for (int x = 0; x < Cols; x++)
            for (int y = 0; y < Rows; y++)
                if (OnBoard(x, y) && IsRetreatCell(x, y)) yield return (x, y);
    }

    /// <summary>내 배를 퇴각시킨다 — 판에서 걷는다(<c>+0x24 = 3</c>).</summary>
    public void Retreat(Ship ship)
    {
        if (!ship.Mine || !ship.CanAct || !IsRetreatCell(ship.X, ship.Y)) return;
        ship.State = ShipState.Retreated;
    }

    /// <summary>내 배가 모두 판을 떴는지.</summary>
    public bool AllMineGone => Ships.Where(s => s.Mine).All(s => !s.CanAct);

    /// <summary>적이 모두 판을 떴는지.</summary>
    public bool AllEnemyGone => Ships.Where(s => !s.Mine).All(s => !s.CanAct);

    // ── 말 ────────────────────────────────────────────────────────────────

    /// <summary>「바람은 %s풍입니다. 퇴각지점은 바람이 부는 %s쪽에 있습니다.」(<c>0x0056B4E8</c>).</summary>
    public string WindNotice()
    {
        (string from, string to) = Wind switch
        {
            0 => ("북", "남"),
            1 or 2 => ("동", "서"),
            3 => ("남", "북"),
            _ => ("서", "동"),
        };
        return $"바람은 {from}풍입니다. 퇴각지점은 바람이 부는 {to}쪽에 있습니다.";
    }

    /// <summary>이동 지시를 재촉하는 세 벌(<c>0x0056B328</c> 벌).</summary>
    public string OrderPrompt() => _rng.Next(3) switch
    {
        0 => "제독, 각 함선에 이동 지시를!",
        1 => "준비는 완벽합니다! 이동 지시를 내려 주십시오!",
        _ => "각 함대에 이동 지시를 내려 주십시오.",
    };

    /// <summary>충돌 말(<c>0x00439858</c> 벌).</summary>
    public static string CrashWord(Ship mover, Ship hit) =>
        mover.Mine && hit.Mine ? "위험하다! 정지!…하마터면 아군끼리 부딪칠 뻔 했다." : "충돌했다!";

    /// <summary>다 빠져나갔을 때의 다섯 벌(<c>0x00435ABF</c>).</summary>
    public string EscapedWord(string foe)
    {
        string eul = Josa(foe, "을", "를");
        return _rng.Next(5) switch
        {
            0 => "휴, 간신히 도망쳐 나왔습니다.",
            1 => "휴, 아슬아슬했다···",
            2 => $"겨우 {foe}{eul} 물리쳤습니다.",
            3 => $"제독, {foe}에게서 도망쳐 나왔습니다.",
            _ => $"{foe}의 추격을 물리친 것 같습니다!",
        };
    }

    private static string Josa(string word, string batchim, string plain)
    {
        if (word.Length == 0) return plain;
        char last = word[^1];
        if (last < 0xAC00 || last > 0xD7A3) return plain;
        return (last - 0xAC00) % 28 != 0 ? batchim : plain;
    }
}
