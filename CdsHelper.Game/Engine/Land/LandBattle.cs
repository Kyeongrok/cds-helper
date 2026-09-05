using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Engine.Land;

/// <summary>
/// 육상전 한 판 — 열두 부대와 턴.
/// </summary>
/// <remarks>
/// 게임의 <c>CLandWar</c>(<c>0x005A47E8</c>)에 맞먹는다. 부대 열둘은 앞 여섯이 아군,
/// 뒤 여섯이 적이고 레코드가 40바이트씩이다(<c>+0xAC</c>부터).
///
/// <b>여기까지가 옮긴 데다.</b> 판을 세우고 첫 턴 차림표를 내는 데까지고,
/// 치고받는 셈(<c>0x00449320</c> · <c>0x00448360</c>)은 아직 남았다.
/// </remarks>
public sealed class LandBattle
{
    /// <summary>한 쪽 자리 수와 온 자리 수.</summary>
    public const int PerSide = LandRoster.SlotCount, Slots = PerSide * 2;

    /// <summary>적의 첫 자리.</summary>
    public const int FirstFoe = PerSide;

    /// <summary>턴은 열까지다(<c>0x00449C80</c> 의 <c>cmp 턴, 11</c>).</summary>
    public const int LastTurn = 10;

    /// <summary>부대 하나에 드는 최소 인원 — <c>0x004A1200</c> 의 15 다.</summary>
    private const int PerUnit = 15;

    /// <summary>부대 하나.</summary>
    /// <param name="Kind">병종 0~23. −1 이면 빈 자리다.</param>
    /// <param name="Men">병사수.</param>
    public readonly record struct Unit(int Kind, int Men)
    {
        /// <summary>선 부대인지.</summary>
        public bool Standing => Kind >= 0 && Men > 0;

        /// <summary>총대장 부대인지(<c>0x00446E90</c> 의 <c>+0xC4</c>).</summary>
        public bool IsLeader => Kind >= 0 && LandUnits.IsLeader(Kind);

        public string Name =>
            Kind >= 0 && Kind < LandUnits.Names.Length ? LandUnits.Names[Kind] : "";
    }

    private readonly Unit[] _units = new Unit[Slots];

    /// <summary>부대 열둘. 앞 여섯이 아군이다.</summary>
    public IReadOnlyList<Unit> Units => _units;

    /// <summary>지금 턴. 첫 턴이 1 이다.</summary>
    public int Turn { get; private set; } = 1;

    /// <summary>싸움터 그림 번호 — 0 도시 · 1 초지 · 2 숲 · 3 황무지.</summary>
    public int Terrain { get; }

    /// <summary>상대 도시의 문화권. 적 진형과 그림이 이것으로 갈린다.</summary>
    public int Culture { get; }

    /// <summary>적 대장의 능력 — 무력 · 지력 · 운 · 체력(<c>0x00449E50</c>).</summary>
    public int FoeMight { get; }
    public int FoeMind { get; }
    public int FoeLuck { get; }
    public int FoeBody { get; }

    /// <summary>
    /// 판을 세운다.
    /// </summary>
    /// <param name="mine">아군 여섯 자리의 병종. −1 이면 빈 자리다.</param>
    /// <param name="men">아군 총인원 — 선원 + 1.</param>
    /// <param name="scale">도시 규모(<c>도시 +0x08</c>). 적의 크기가 여기서 나온다.</param>
    public LandBattle(IReadOnlyList<int> mine, int men, int scale, int culture, int terrain,
                      GameRandom dice)
    {
        Culture = culture;
        Terrain = Math.Clamp(terrain, 0, 3);

        Split(mine, men);

        // 적 대장의 능력. 규모 0 / 1~2 / 3~4 / 5+ 마다 밑값이 다르다.
        int band = scale <= 0 ? 0 : scale <= 2 ? 1 : scale <= 4 ? 2 : 3;
        FoeMight = dice.Next(10) + new[] { 40, 60, 75, 90 }[band] - 1;
        FoeMind = dice.Next(10) + new[] { 30, 50, 70, 80 }[band] - 1;
        FoeLuck = dice.Next(10) + new[] { 20, 40, 65, 70 }[band] - 1;
        FoeBody = dice.Next(10) + new[] { 60, 75, 85, 90 }[band] - 1;

        Muster(scale, dice);
    }

    // ── 아군 ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// 아군 인원을 나눈다 — 고르게 나누고 <b>나머지는 대장 부대</b>가 갖는다.
    /// </summary>
    /// <remarks><c>0x0049F640</c> 이 배치 화면에서 하던 그 셈 그대로다.</remarks>
    private void Split(IReadOnlyList<int> mine, int men)
    {
        int units = 0;
        for (int i = 0; i < PerSide && i < mine.Count; i++) if (mine[i] >= 0) units++;
        if (units == 0) return;

        int each = Math.Max(1, men / units), over = men % units;
        for (int i = 0; i < PerSide && i < mine.Count; i++)
        {
            if (mine[i] < 0) continue;
            _units[i] = new Unit(mine[i], each + (LandUnits.IsLeader(mine[i]) ? over : 0));
        }
    }

    // ── 적 ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 적을 그 자리에서 지어낸다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   0x004A11D0  총 병사수 = 50 x 규모² + 100 + rand(50), 규모가 2 이하면 곱하기 2
    ///   0x004A1200  부대 수  = clamp(총인원 / 15, 1, 6) 에서 굴림으로 깎는다
    ///   0x004A1320  문화권으로 진형 여덟 중 하나를 고른다
    ///   0x004A04B0  인원을 고르게 나누고 나머지는 <b>첫 부대(대장)</b>가 갖는다
    /// </code>
    /// </remarks>
    private void Muster(int scale, GameRandom dice)
    {
        int men = 50 * scale * scale + 100 + dice.Next(50);
        if (scale <= 2) men *= 2;

        int units = Shrink(Math.Clamp(men / PerUnit, 1, PerSide), dice);
        var kinds = Formation(units, dice);

        int each = Math.Max(1, men / units), over = men % units;
        for (int i = 0; i < units && i < PerSide; i++)
            _units[FirstFoe + i] = new Unit(kinds[i], each + (i == 0 ? over : 0));
    }

    /// <summary>
    /// 부대 수를 굴림으로 깎는다(<c>0x004A1200</c>).
    /// </summary>
    /// <remarks>
    /// 그대로 둘 확률이 70%, 하나 깎을 확률이 60%… 스무 번째까지 다 빗나가면 처음부터
    /// 다시 굴린다. 그래서 여섯 부대가 다 나오는 판은 드물다.
    /// </remarks>
    private static int Shrink(int units, GameRandom dice)
    {
        int[] odds = [70, 60, 50, 40, 30, 20];
        for (int guard = 0; guard < 100; guard++)
            for (int cut = 0; cut < odds.Length; cut++)
            {
                if (units - cut <= 0) break;
                if (dice.Next(100) <= odds[cut]) return units - cut;
            }
        return Math.Max(1, units);
    }

    /// <summary>
    /// 그 문화권의 진형(<c>0x004A1320</c> 의 뜀표 <c>0x004A13C0</c>).
    /// </summary>
    /// <remarks>
    /// 문화권 열하나가 진형 여덟에 이렇게 맞물린다.
    /// <code>
    ///   0 · 1 · 2 → 0      3 · 8 → 1      4 → 2      5 → 3
    ///   6 → 4      7 → 5      9 → 6      10 → 7
    /// </code>
    /// <b>낱낱이 푼 것은 진형 2(이슬람)뿐이다</b> — <c>0x004A07C0</c> 이다. 나머지 일곱은
    /// 병종 몇을 못 짚어 그 진형이 쓰는 것으로 채운다. 어느 것이든 <b>첫 부대가 대장</b>이고
    /// 나머지가 원거리·근접으로 붙는 얼개는 같다.
    /// </remarks>
    private int[] Formation(int units, GameRandom dice)
    {
        var kinds = new int[Math.Max(units, 1)];
        int shape = Culture switch
        {
            0 or 1 or 2 => 0,
            3 or 8 => 1,
            4 => 2,
            5 => 3,
            6 => 4,
            7 => 5,
            9 => 6,
            _ => 7,
        };

        // 대장은 진형마다 다르다 — 12 족장 · 13 영주 · 14 장군.
        kinds[0] = shape switch
        {
            1 or 7 => LandUnits.Chief,
            6 => LandUnits.Lord,
            _ => LandUnits.General,
        };

        // 뒤따르는 병종. 진형 2 는 게임 그대로고(포술·검술 자리로 갈린다), 나머지는
        // 그 진형이 쓰는 병종을 돌려 쓴다.
        int[] rest = shape switch
        {
            1 => [LandUnits.Shaman, LandUnits.Bow, LandUnits.Spear, LandUnits.Light],
            2 => [Cannoneer(dice), Rider(dice), LandUnits.Bow, LandUnits.Bow, LandUnits.Light],
            3 => [LandUnits.Monk, LandUnits.Bow, LandUnits.Light],
            4 => [LandUnits.Bombard, LandUnits.Bow, LandUnits.Light],
            5 => [LandUnits.Bow, LandUnits.Light],
            6 => [LandUnits.Ninja, LandUnits.Bow, LandUnits.Light],
            7 => [LandUnits.Leopard, LandUnits.Indio, LandUnits.Bow],
            _ => [LandUnits.Gunner, LandUnits.Bow, LandUnits.Spear, LandUnits.Light],
        };
        for (int i = 1; i < kinds.Length; i++) kinds[i] = rest[(i - 1) % rest.Length];
        return kinds;
    }

    /// <summary>
    /// 진형 2 의 둘째 부대 — 적 대장의 <b>포술</b>이 정한다(<c>0x004A0835</c>).
    /// </summary>
    /// <remarks>3 이면 캐논포병, 2 면 반반, 그 밑이면 포병이다.</remarks>
    private int Cannoneer(GameRandom dice)
    {
        int gunnery = FoeSkill(Skill.Gunnery);
        bool big = gunnery >= 3 || (gunnery == 2 && dice.Next(2) == 0);
        return big ? LandUnits.Cannon : LandUnits.Gunner;
    }

    /// <summary>
    /// 진형 2 의 셋째 부대 — 적 대장의 <b>검술</b>이 정한다(<c>0x004A0872</c>).
    /// </summary>
    /// <remarks>3 이면 낙타병, 2 면 반반, 그 밑이면 경보병이다.</remarks>
    private int Rider(GameRandom dice)
    {
        int sword = FoeSkill(Skill.Sword);
        bool big = sword >= 3 || (sword == 2 && dice.Next(2) == 0);
        return big ? LandUnits.Camel : LandUnits.Light;
    }

    /// <summary>
    /// 적 대장의 기능. <b>적 대장 인물을 아직 안 들고 있어</b> 지력에서 어림한다.
    /// </summary>
    /// <remarks>
    /// 게임은 <c>0x00446F70(기능, 6)</c> 으로 적 대장 인물 레코드를 본다. 우리는 그 자리에
    /// 세울 인물을 안 만들어 두었으므로, 능력을 셋으로 갈라 자리를 매긴다.
    /// </remarks>
    private int FoeSkill(int slot)
    {
        int stat = slot == Skill.Sword ? FoeMight : FoeMind;
        return stat >= 80 ? 3 : stat >= 60 ? 2 : stat >= 40 ? 1 : 0;
    }

    // ── 턴 ─────────────────────────────────────────────────────────────────────

    /// <summary>턴 알림 글 — <c>0x0056D7B0</c> "제%2d턴" 이다.</summary>
    public string TurnWord => $"제{Turn,2}턴";

    /// <summary>다음 턴으로. 열 턴이 지나면 거짓.</summary>
    public bool NextTurn() => ++Turn <= LastTurn;

    /// <summary>공격명령 차림표의 제목(<c>0x0056D7A0</c>).</summary>
    public const string OrderTitle = "공격명령";

    /// <summary>공격명령 일곱(<c>0x00549D18</c>).</summary>
    public static readonly string[] Orders =
    [
        "통상공격", "방어중시공격", "돌격", "일기토", "퇴각", "묘책", "애니메이션",
    ];

    /// <summary>명령 번호.</summary>
    public const int Normal = 0, Guarded = 1, Charge = 2, Duel = 3, Retreat = 4,
                     Ruse = 5, Animate = 6;

    /// <summary>
    /// 지금 고를 수 있는 명령들. 꺼진 줄도 자리를 지킨다.
    /// </summary>
    /// <remarks>
    /// <b>일기토는 아무 때나 못 건다</b> — <c>0x00449BC5</c> 어름이 <c>+0x54</c> 가 −1 이나
    /// 4 가 아니면 끈다. 판을 세울 때 4 로 두므로(<c>0x0044A604</c>) 첫 턴에는 켜져 있고,
    /// 한 번 싸우고 나면 꺼진다. 묘책도 같은 자리의 <c>0x20</c> 비트로 갈린다.
    /// </remarks>
    public IReadOnlyList<(string Text, bool On)> OrderRows(bool canDuel, bool canRuse) =>
    [
        (Orders[Normal], true),
        (Orders[Guarded], true),
        (Orders[Charge], true),
        (Orders[Duel], canDuel),
        (Orders[Retreat], true),
        (Orders[Ruse], canRuse),
        (Orders[Animate], true),
    ];
}
