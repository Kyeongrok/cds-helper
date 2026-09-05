using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Engine.Land;

/// <summary>
/// 육상전 한 판 — 열두 부대와 턴.
/// </summary>
/// <remarks>
/// 게임의 <c>CLandWar</c>(<c>0x005A47E8</c>)에 맞먹는다. 부대 열둘은 앞 여섯이 아군,
/// 뒤 여섯이 적이고 레코드가 40바이트씩이다(<c>+0xAC</c>부터).
///
/// 판을 세우고 값을 치르는 것이 이 클래스고, 한 턴을 굴리는 것은
/// <see cref="LandFight"/> 다.
///
/// <b>아직 안 옮긴 것</b> — 묘책 넷(<c>0x004490D0</c>) · 일기토(<c>0x004478A0</c>) ·
/// 증원 2차전(<c>0x00449930</c>) · 다 빈치의 작렬탄(<c>0x00448DD0</c>) · 적 AI 의
/// 명령 고르기(<c>0x00447A60</c>).
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
    /// <param name="scale">도시 규모(<c>도시 +0x08</c>). 적의 크기가 여기서 나온다.</param>
    public LandBattle(IReadOnlyList<int> mine, Player player, Player.MateInfo? aide,
                      int scale, int nation, int culture, int terrain, GameRandom dice)
    {
        Nation = nation;
        Culture = culture;
        Terrain = Math.Clamp(terrain, 0, 3);
        Scale = Math.Max(0, scale);
        _me = player;
        _aide = aide;

        int men = player.Crew + 1;
        MyFirst = men;
        Split(mine, men);

        // 적 대장의 능력. 규모 0 / 1~2 / 3~4 / 5+ 마다 밑값이 다르다.
        int band = scale <= 0 ? 0 : scale <= 2 ? 1 : scale <= 4 ? 2 : 3;
        FoeMight = dice.Next(10) + new[] { 40, 60, 75, 90 }[band] - 1;
        FoeMind = dice.Next(10) + new[] { 30, 50, 70, 80 }[band] - 1;
        FoeLuck = dice.Next(10) + new[] { 20, 40, 65, 70 }[band] - 1;
        FoeBody = dice.Next(10) + new[] { 60, 75, 85, 90 }[band] - 1;

        Muster(scale, dice);
        Shell(player, dice);
        for (int i = FirstFoe; i < Slots; i++) FoeFirst += _units[i].Men;
        FoeRoom = FoeUnits > 0 ? FoeFirst / FoeUnits : FoeFirst;
        MyRoom = MyUnits > 0 ? MyFirst / MyUnits : MyFirst;
    }

    private readonly Player _me;
    private readonly Player.MateInfo? _aide;

    /// <summary>도시 규모. 전리품 셈이 이것을 본다.</summary>
    public int Scale { get; }

    /// <summary>도시가 딸린 나라. 증원이 붙을지를 이것도 본다.</summary>
    public int Nation { get; }

    // ── 증원 2차전 — 0x00449930 ────────────────────────────────────────────────

    /// <summary>증원이 붙는 나라(<c>0x00449956</c> 의 <c>cmp eax, 7</c>).</summary>
    private const int ReinforcingNation = 7;

    /// <summary>증원이 붙는 도시 규모.</summary>
    private const int ReinforcingScale = 3;

    /// <summary>이미 한 번 붙었는지(<c>+0x88</c>). 한 판에 딱 한 번이다.</summary>
    private bool _reinforced;

    /// <summary>증원이 왔을 때 나오는 말(<c>0x0056D0B8</c> 벌).</summary>
    public const string ReinforceWord = "제독, 적의 새 병력입니다!";

    /// <summary>
    /// 이겼을 때 적의 새 병력이 붙는지 — <b>마을 공략에서 딱 한 번</b>이다.
    /// </summary>
    /// <remarks>
    /// 도시 규모가 셋 위이거나, 작아도 나라가 <b>7</b> 이면 붙는다. 붙으면 적을 다시 짜고
    /// 턴을 처음으로 돌린다(<c>0x0044998E</c> 가 3 을 내어 고리를 되돌린다).
    /// </remarks>
    public bool Reinforce(GameRandom dice)
    {
        if (_reinforced) return false;
        if (Scale < ReinforcingScale && Nation != ReinforcingNation) return false;

        _reinforced = true;
        for (int i = FirstFoe; i < Slots; i++) _units[i] = default;

        Muster(Scale, dice);
        for (int i = FirstFoe; i < Slots; i++) FoeFirst += _units[i].Men;
        FoeRoom = FoeUnits > 0 ? MenOn(foe: true) / FoeUnits : FoeFirst;
        Turn = 1;
        return true;
    }

    /// <summary>판을 열 때의 인원.</summary>
    public int MyFirst { get; }
    public int FoeFirst { get; private set; }

    /// <summary>부대 하나의 정원 — 고승의 회복이 이것을 본다(<c>0x00448280</c>).</summary>
    public int MyRoom { get; private set; }
    public int FoeRoom { get; private set; }

    private int MyUnits => Standing(0);
    private int FoeUnits => Standing(FirstFoe);

    private int Standing(int side)
    {
        int n = 0;
        for (int i = side; i < side + PerSide; i++) if (_units[i].Standing) n++;
        return n;
    }

    /// <summary>지금 그 편에 남은 병사수.</summary>
    public int MenOn(bool foe)
    {
        int side = foe ? FirstFoe : 0, n = 0;
        for (int i = side; i < side + PerSide; i++) n += Math.Max(0, _units[i].Men);
        return n;
    }

    /// <summary>부대 하나의 정원.</summary>
    public int RoomPerUnit(int side) => side >= FirstFoe ? FoeRoom : MyRoom;

    /// <summary>그 부대의 병사수를 고쳐 넣는다. 0 이 되면 쓰러진 것이다.</summary>
    public void SetMen(int slot, int men)
    {
        if (slot < 0 || slot >= Slots) return;
        _units[slot] = _units[slot] with { Men = Math.Max(0, men) };
    }

    /// <summary>
    /// 그 자리 부대가 쓰는 <b>기능</b> 자리(0~3).
    /// </summary>
    /// <remarks>
    /// 아군은 제독과 부관 중 큰 쪽이고(<c>0x00446F70</c>), 적은 대장 인물을 아직 안
    /// 들고 있어 능력에서 어림한다.
    /// </remarks>
    public int SkillAt(int slot, int skill)
    {
        if (slot >= FirstFoe) return FoeSkill(skill);

        int mine = _me.LevelOf(Skill.Names[skill]);
        int mate = _aide is not { } who ? 0 : skill switch
        {
            Skill.Sword => who.Sword,
            Skill.Shooting => who.Shooting,
            Skill.Gunnery => who.Gunnery,
            _ => 0,
        };
        return Math.Max(mine, mate);
    }

    /// <summary>그 자리 부대의 무력.</summary>
    public int MightAt(int slot) =>
        slot >= FirstFoe ? FoeMight : _me.AbilityOf(Ability.Might);

    /// <summary>그 자리 부대의 지력.</summary>
    public int MindAt(int slot) =>
        slot >= FirstFoe ? FoeMind : _me.AbilityOf(Ability.Mind);

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
    /// 그 문화권의 진형이 낼 병종들. 세트 자체는 <see cref="LandFormations"/> 에 있다.
    /// </summary>
    /// <remarks>
    /// 놀이도 고치는 창(<c>LandFormationDialog</c>)도 같은 표를 본다 — 사람이 편성을
    /// 갈아 두면 여기에도 그대로 먹는다.
    /// </remarks>
    private int[] Formation(int units, GameRandom dice) =>
        LandFormations.Muster(LandFormations.ShapeOf(Culture), units,
                              FoeSkill(Skill.Sword), FoeSkill(Skill.Gunnery),
                              FoeSkill(Skill.Shooting), dice);

    /// <summary>
    /// 적 대장의 기능 자리. <b>적 대장 인물을 아직 안 들고 있어</b> 능력에서 어림한다.
    /// </summary>
    /// <remarks>
    /// 게임은 <c>0x00446F70(기능, 6)</c> 으로 적 대장 인물 레코드를 그대로 본다. 우리는
    /// 그 자리에 세울 인물을 안 만들어 두었으므로 검술은 무력에서, 포술·사격술은 지력에서
    /// 넷으로 갈라 매긴다.
    /// </remarks>
    private int FoeSkill(int slot)
    {
        int stat = slot == Skill.Sword ? FoeMight : FoeMind;
        return stat >= 80 ? 3 : stat >= 60 ? 2 : stat >= 40 ? 1 : 0;
    }

    // ── 끝맺음 — 0x00449870 ────────────────────────────────────────────────────

    /// <summary>싸움이 끝나고 치르는 값.</summary>
    /// <param name="Loot">전리품 금화(<c>0x00449490</c>).</param>
    /// <param name="Back">복귀한 부상병(<c>0x00449570</c>).</param>
    /// <param name="Fame">오른 명성 · <paramref name="Infamy"/> 오른 악명(<c>0x00449600</c>).</param>
    /// <param name="Might">오른 무력(<c>0x00449730</c>).</param>
    public readonly record struct Spoils(int Loot, int Back, int Fame, int Infamy, int Might);

    /// <summary>
    /// 이기거나 물러난 뒤를 치른다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   전리품  ((적 처음 - 적 남은) x (규모+1)) / 10 + rand(50)
    ///   복귀    율 = 운*5/100 + 의학*2
    ///           min(처음 - 1, 생존 + 율*(처음 - 생존 - 1)/10)
    ///   명성    이김 밑 100 · 악명 밑 200 / 짐 악명 밑 300, 명성 += 밑 + rand(11)
    ///   무력    rand(20) == 0 일 때만 rand(2)+1
    /// </code>
    /// 마을 공략(갈래 2)이라 나라가 같을 일이 드물어 <b>이기면 명성 +10</b> 쪽을 쓴다.
    /// </remarks>
    public Spoils Finish(bool won, GameRandom dice)
    {
        int loot = won ? (FoeFirst - MenOn(foe: true)) * (Scale + 1) / 10 + dice.Next(50) : 0;

        int alive = Math.Max(0, MenOn(foe: false) - 1);
        int rate = _me.AbilityOf(Ability.Luck) * 5 / 100
                   + _me.LevelOf(Skill.Names[Skill.Medicine]) * 2;
        int back = Math.Min(MyFirst - 1, alive + rate * (MyFirst - alive - 1) / 10) - alive;
        back = Math.Max(0, back);

        int fameBase = won ? 100 : 0;
        int infamy = won ? 200 : 300;
        int fame = fameBase + (won ? 10 : 0) + dice.Next(11);
        int might = dice.Next(20) == 0 ? dice.Next(2) + 1 : 0;

        return new Spoils(loot, back, fame, infamy, might);
    }

    // ── 턴 ─────────────────────────────────────────────────────────────────────

    /// <summary>턴 알림 글 — <c>0x0056D7B0</c> "제%2d턴" 이다.</summary>
    public string TurnWord => $"제{Turn,2}턴";

    /// <summary>다음 턴으로. 열 턴이 지나면 거짓.</summary>
    public bool NextTurn() => ++Turn <= LastTurn;

    /// <summary>
    /// 다 빈치의 작렬탄(<c>0x00448DD0</c>) — 판이 열릴 때 한 번 굴린다.
    /// </summary>
    /// <remarks>
    /// 아이템 2 를 지녔으면 40%로 <c>0x0056D340</c> "다 빈치 선생의 작렬탄을 받아라!"
    /// 가 뜨고 그 뒤로 <b>포가 비를 안 탄다</b>. 그 아이템은 그 자리에서 없어진다.
    /// </remarks>
    public const int ShellItem = 2, ShellOdds = 40;

    /// <summary>작렬탄을 받았는지. 서 있으면 포가 비를 안 탄다.</summary>
    public bool Shells { get; private set; }

    /// <summary>작렬탄을 받았을 때 나오는 말(<c>0x0056D340</c>). 안 받았으면 빈 글.</summary>
    public string ShellWord { get; private set; } = "";

    /// <summary>
    /// 작렬탄을 굴린다 — 아이템을 지녔으면 <see cref="ShellOdds"/> 로 받는다.
    /// </summary>
    /// <remarks>받으면 그 아이템은 그 자리에서 없어진다(<c>0x0047CDB0</c>).</remarks>
    private void Shell(Player player, GameRandom dice)
    {
        if (!player.HasItem(ShellItem)) return;
        if (dice.Next(100) >= ShellOdds) return;

        player.Drop(ShellItem);
        Shells = true;
        ShellWord = "다 빈치 선생의 작렬탄을 받아라!";
    }

    // ── 적 AI — 0x00447A60 ─────────────────────────────────────────────────────

    /// <summary>
    /// 전투 갈래(<c>+0x34</c>). 마을 공략이 <b>2</b> 다.
    /// </summary>
    public const int VillageRaid = 2;

    /// <summary>
    /// 적이 고르는 공격명령(<c>0x00447A60</c>).
    /// </summary>
    /// <remarks>
    /// 양쪽 병사수를 견주고 턴이 여덟을 넘었는지로 갈린다. 마을 공략(갈래 2)만 옮겼다 —
    /// 함대전 갈래는 우리 쪽에 아직 그 판이 없다.
    /// <code>
    ///   턴 &gt;= 8  이기고 있으면  rand(4) != 0 ? 방어중시 : 통상
    ///            지고  있으면  rand(9) != 0 ? 방어중시 : 통상
    ///   턴 &lt;  8  이기고 있으면  rand(7) &gt; 3 ? 돌격     : 통상
    ///            지고  있으면  rand(7) &gt; 2 ? 방어중시 : 통상
    /// </code>
    /// <b>적은 마을 공략에서 일기토를 안 건다</b>(<c>0x004479D7</c> 이 갈래 2·4 를
    /// 먼저 걸러 낸다). 퇴각도 갈래 1 에서만 고른다.
    /// </remarks>
    public int FoeOrder(GameRandom dice)
    {
        bool ahead = MenOn(foe: true) >= MenOn(foe: false);

        if (Turn >= LateTurn)
            return dice.Next(ahead ? 4 : 9) != 0 ? Guarded : Normal;

        return ahead ? (dice.Next(7) > 3 ? Charge : Normal)
                     : (dice.Next(7) > 2 ? Guarded : Normal);
    }

    /// <summary>적이 셈을 바꾸는 턴(<c>0x00447A7C</c> 의 <c>cmp 턴, 8</c>).</summary>
    private const int LateTurn = 8;

    // ── 묘책 — 0x004490D0 ──────────────────────────────────────────────────────

    /// <summary>묘책 넷의 이름(<c>0x0056D438</c>). 제목은 「기습명령」이다.</summary>
    public static readonly string[] Ruses = ["기습", "함정", "암살자", "심판"];

    /// <summary>묘책 번호.</summary>
    public const int Ambush = 0, Trap = 1, Assassin = 2, Judgement = 3;

    /// <summary>묘책 차림표의 제목(<c>0x0056D458</c>).</summary>
    public const string RuseTitle = "기습명령";

    /// <summary>
    /// 묘책이 먹힐 확률 — <b>문화권 x 세 묘책</b> 표(<c>0x00549B80</c>)다.
    /// </summary>
    /// <remarks>
    /// <c>0x00449080</c> 이 <c>표[문화권*3 + 묘책] &gt;= rand(100)</c> 으로 가른다.
    /// 값은 20 · 40 · 60 · 80 넷뿐이다. 심판은 표 밖이라(디버그 전용) 안 쓴다.
    /// </remarks>
    private static readonly int[] RuseOdds =
    [
        40, 80, 60,   40, 80, 60,   40, 80, 60,   60, 20, 40,
        60, 40, 80,   40, 80, 20,   80, 60, 20,   20, 60, 40,
        60, 20, 40,   80, 20, 60,   40, 20, 60,
    ];

    /// <summary>한 판에 한 번씩만 쓴다(<c>+0x50</c> 의 비트).</summary>
    private int _usedRuses;

    /// <summary>그 묘책을 아직 안 썼는지.</summary>
    public bool RuseLeft(int ruse) => (_usedRuses & (1 << ruse)) == 0;

    /// <summary>아직 쓸 수 있는 묘책이 하나라도 있는지.</summary>
    public bool AnyRuseLeft =>
        RuseLeft(Ambush) || RuseLeft(Trap) || RuseLeft(Assassin);

    /// <summary>묘책 차림표의 줄들. 쓴 것은 꺼진다.</summary>
    public IReadOnlyList<(string Text, bool On)> RuseRows() =>
    [
        (Ruses[Ambush], RuseLeft(Ambush)),
        (Ruses[Trap], RuseLeft(Trap)),
        (Ruses[Assassin], RuseLeft(Assassin)),
        // 심판은 디버그 아이템(0xB8)이 있어야 열린다 — 우리는 안 낸다.
        (Ruses[Judgement], false),
    ];

    /// <summary>그 묘책을 걸어 본다. 먹혔으면 참이고, 어느 쪽이든 한 번 쓰면 없어진다.</summary>
    public bool TryRuse(int ruse, GameRandom dice)
    {
        _usedRuses |= 1 << ruse;

        int at = Math.Clamp(Culture, 0, 10) * 3 + ruse;
        return at < RuseOdds.Length && RuseOdds[at] >= dice.Next(100);
    }

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
