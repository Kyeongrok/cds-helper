using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Engine.Land;

/// <summary>
/// 육상전 한 턴을 굴린다 — 차례를 매기고, 부대를 하나씩 움직이고, 피해를 먹인다.
/// </summary>
/// <remarks>
/// 게임의 <c>0x00449320</c>(한 턴) · <c>0x00448050</c>(부대 하나 움직이기) ·
/// <c>0x00449250</c>(한 대 때리기) · <c>0x00448360</c>(피해 매기기)를 옮긴 것이다.
/// 셈은 <see cref="LandUnits"/> 에 있고 여기서는 <b>차례</b>만 맡는다.
///
/// 한 턴이 남기는 것은 <see cref="Line"/> 목록이다 — 화면은 그것을 한 줄씩 읽어
/// 보여 준다. 그래야 셈과 그리기가 안 엉킨다.
/// </remarks>
public sealed class LandFight(LandBattle battle, GameRandom dice)
{
    /// <summary>한 턴에 일어난 일 한 줄.</summary>
    /// <param name="Text">화면에 낼 말. 비어 있으면 안 낸다.</param>
    /// <param name="Actor">움직인 부대. 없으면 −1.</param>
    /// <param name="Target">맞은 부대. 없으면 −1.</param>
    /// <param name="Damage">깎인 병사수.</param>
    /// <param name="Sound">낼 효과음 파트. −1 이면 없다.</param>
    public readonly record struct Line(string Text, int Actor = -1, int Target = -1,
                                       int Damage = 0, int Sound = -1);

    private readonly List<Line> _log = [];

    /// <summary>비가 오는지 — 주술사가 부르면 총·포가 죽는다(<c>+0x3C</c> 의 <c>0x20</c>).</summary>
    public bool Raining { get; private set; }

    /// <summary>표범이 춘 춤의 겹수(<c>+0x44</c>). 적 공격력이 겹마다 1.5배가 된다.</summary>
    public int Dances { get; private set; }

    /// <summary>다 빈치의 작렬탄 — 서 있으면 포가 비를 무시한다(<c>+0x3C</c> 의 <c>0x48</c>).</summary>
    public bool Shells { get; set; }

    /// <summary>
    /// 한 턴을 굴린다. 돌려주는 것은 그 턴에 일어난 일들이다.
    /// </summary>
    /// <param name="mine">아군이 고른 공격명령(<see cref="LandBattle.Normal"/> 따위).</param>
    /// <param name="theirs">적이 고른 공격명령.</param>
    public IReadOnlyList<Line> Turn(int mine, int theirs)
    {
        _log.Clear();

        // 차례는 행동속도가 빠른 쪽부터다(0x00447C20 → 0x004493BE 의 순서표).
        foreach (int slot in Order())
        {
            if (!battle.Units[slot].Standing) continue;
            Act(slot, slot < LandBattle.FirstFoe ? mine : theirs);
            if (Over != null) break;
        }

        // 비는 한 턴만 온다(0x00449403 이 그 비트를 끈다).
        Raining = false;
        return _log;
    }

    /// <summary>싸움이 끝났으면 이긴 쪽 — 참이면 아군, 거짓이면 적. 아직이면 null.</summary>
    public bool? Over { get; private set; }

    // ── 차례 ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// 행동속도(<c>0x00447C20</c>)로 매긴 차례. 빠른 쪽이 먼저다.
    /// </summary>
    /// <remarks>
    /// 갈래마다 보는 기능이 다르다.
    /// <code>
    ///   0 근접  rand(14 - 검술)   + 검술*2
    ///   1 사격  rand(14 - 사격술) + 사격술*2
    ///   2 포    rand(14 - 포술)   + 포술*2
    ///   3 지원  rand(2*(7 - 신학)) + 신학*3
    /// </code>
    /// </remarks>
    private int[] Order()
    {
        var speed = new int[LandBattle.Slots];
        for (int i = 0; i < LandBattle.Slots; i++)
        {
            var unit = battle.Units[i];
            if (!unit.Standing) { speed[i] = int.MinValue; continue; }

            int level = SkillFor(i, LandUnits.KindOf(unit.Kind));
            speed[i] = LandUnits.KindOf(unit.Kind) == LandUnits.Kind.Support
                ? dice.Next(Math.Max(1, 2 * (7 - level))) + level * 3
                : dice.Next(Math.Max(1, 14 - level)) + level * 2;
        }

        var order = new int[LandBattle.Slots];
        for (int i = 0; i < order.Length; i++) order[i] = i;
        Array.Sort(order, (a, b) => speed[b].CompareTo(speed[a]));
        return order;
    }

    /// <summary>그 갈래가 보는 기능 자리.</summary>
    private int SkillFor(int slot, LandUnits.Kind kind) => kind switch
    {
        LandUnits.Kind.Shot => battle.SkillAt(slot, Skill.Shooting),
        LandUnits.Kind.Cannon => battle.SkillAt(slot, Skill.Gunnery),
        LandUnits.Kind.Support => battle.SkillAt(slot, Skill.Theology),
        _ => battle.SkillAt(slot, Skill.Sword),
    };

    // ── 부대 하나 움직이기 — 0x00448050 ────────────────────────────────────────

    private void Act(int slot, int order)
    {
        var unit = battle.Units[slot];
        var kind = LandUnits.KindOf(unit.Kind);
        bool mine = slot < LandBattle.FirstFoe;

        switch (kind)
        {
            case LandUnits.Kind.Melee:
                // 창병만 앞열 하나와 그 뒤까지 둘을 친다(0x004487C0).
                int front = Pick(!mine, frontOnly: true);
                if (front < 0) { Done(); return; }
                Hit(slot, front, order);
                if (unit.Kind == LandUnits.Spear && Behind(front) is { } back && Alive(back))
                    Hit(slot, back, order);
                break;

            case LandUnits.Kind.Shot:
                if (Damp(unit.Kind)) { Say(slot, "비에 젖어 불이 붙지 않는다!"); break; }
                // 궁병만 아무나 하나를 노린다(0x00448880). 나머지는 앞열 전부대다.
                if (unit.Kind == LandUnits.Bow)
                {
                    int one = Pick(!mine, frontOnly: false);
                    if (one >= 0) Hit(slot, one, order);
                }
                else foreach (int at in All(!mine, frontOnly: true)) Hit(slot, at, order);
                break;

            case LandUnits.Kind.Cannon:
                if (Damp(unit.Kind)) { Say(slot, "비에 젖어 불이 붙지 않는다!"); break; }
                foreach (int at in All(!mine, frontOnly: false)) Hit(slot, at, order);
                break;

            default:
                Support(slot, unit.Kind);
                break;
        }
        Done();
    }

    /// <summary>
    /// 비에 죽는 병종인지 — <b>궁병만 빠져나간다</b>(<c>0x00448A30</c> 의 <c>cmp eax,0x11</c>).
    /// </summary>
    /// <remarks>다 빈치의 작렬탄이 서 있으면 포는 비를 무시한다(<c>0x00448DD0</c>).</remarks>
    private bool Damp(int unitKind)
    {
        if (!Raining || unitKind == LandUnits.Bow) return false;
        return !(Shells && LandUnits.KindOf(unitKind) == LandUnits.Kind.Cannon);
    }

    /// <summary>지원 병종 셋(<c>0x00448C80</c>).</summary>
    private void Support(int slot, int unitKind)
    {
        switch (unitKind)
        {
            case LandUnits.Shaman:
                if (Raining) return;                 // 이미 오면 아무것도 안 한다
                Raining = true;
                Say(slot, "주술사가 비를 부른다!");
                break;

            case LandUnits.Monk:
                // 한 부대 정원만큼의 2할을 되살린다(0x00448280).
                int side = slot < LandBattle.FirstFoe ? 0 : LandBattle.FirstFoe;
                int room = battle.RoomPerUnit(side);
                int healed = 0;
                for (int i = side; i < side + LandBattle.PerSide; i++)
                {
                    if (!Alive(i)) continue;
                    int was = battle.Units[i].Men;
                    int now = Math.Min(room, was + room * 2 / 10);
                    battle.SetMen(i, now);
                    healed += now - was;
                }
                if (healed > 0) Say(slot, $"고승의 기도로 {healed}명이 되살아났다!");
                break;

            case LandUnits.Leopard:
                Dances++;
                Say(slot, "표범이 춤을 춘다!");
                break;
        }
    }

    // ── 한 대 때리기 — 0x00449250 ──────────────────────────────────────────────

    private void Hit(int from, int to, int order)
    {
        if (!Alive(from) || !Alive(to)) return;

        int hurt = Worth(from, to, order);

        // 되받아치기 — 사무라이 15 · 하타모토 20 · 영주 25 (0x004481E0).
        int kick = battle.Units[to].Kind switch
        {
            LandUnits.Samurai => 15,
            LandUnits.Hatamoto => 20,
            LandUnits.Lord => 25,
            _ => 0,
        };
        if (kick > 0 && LandUnits.KindOf(battle.Units[from].Kind) == LandUnits.Kind.Melee
            && dice.Next(100) <= kick)
        {
            Say(to, "받아쳤다!");
            (from, to) = (to, from);
            hurt = Worth(from, to, order);
        }

        // 닌자의 변신술 — 비가 아닐 때 40%로 피해가 없다.
        if (battle.Units[to].Kind == LandUnits.Ninja && !Raining && dice.Next(100) < 40)
        {
            _log.Add(new Line("둔갑술의 하나, 변신술!", from, to, 0, Sound: 10));
            return;
        }

        hurt = Math.Min(hurt, battle.Units[to].Men);
        battle.SetMen(to, battle.Units[to].Men - hurt);

        _log.Add(new Line($"{Name(from)}의 공격 — {Name(to)} {hurt}명", from, to, hurt));
        Done();
    }

    /// <summary>
    /// 피해를 매긴다(<c>0x00448360</c>).
    /// </summary>
    private int Worth(int from, int to, int order)
    {
        var a = battle.Units[from];
        var d = battle.Units[to];

        int atk = LandUnits.Attack(a.Kind, battle.MightAt(from) + 1,
                                   battle.SkillAt(from, Skill.Sword),
                                   battle.SkillAt(from, Skill.Gunnery),
                                   battle.SkillAt(from, Skill.Shooting));
        int def = LandUnits.Defence(d.Kind, battle.MindAt(to) + 1,
                                    battle.SkillAt(to, Skill.Sword),
                                    battle.SkillAt(to, Skill.Gunnery),
                                    battle.SkillAt(to, Skill.Shooting),
                                    battle.SkillAt(to, Skill.Theology));

        atk = Bent(atk, order, attacking: true);
        def = Bent(def, order, attacking: false);

        // 춤 겹수는 <b>적 쪽 공격</b>에만 붙는다(0x00448360 이 슬롯 6 이상을 본다).
        if (from >= LandBattle.FirstFoe)
            for (int i = 0; i < Dances; i++) atk = atk * 3 / 2;

        int hurt = def + 1 >= atk ? 1 : (atk - def) / 3;

        switch (LandUnits.Match(LandUnits.KindOf(a.Kind), LandUnits.KindOf(d.Kind)))
        {
            case 0: hurt += dice.Next(4) + 3; break;
            case 2: hurt -= dice.Next(2) + 2; break;
        }
        if (hurt <= 0) hurt = 1;
        return hurt + dice.Next(3);
    }

    /// <summary>
    /// 공격명령이 셈을 굽히는 만큼(<c>0x00448120</c> · <c>0x00448180</c>).
    /// </summary>
    /// <remarks>통상 1.0 · 방어중시 공 0.7 방 1.5 · 돌격 공 1.5 방 0.7 이다.</remarks>
    private static int Bent(int value, int order, bool attacking) => order switch
    {
        LandBattle.Guarded => attacking ? value * 7 / 10 : value * 15 / 10,
        LandBattle.Charge => attacking ? value * 15 / 10 : value * 7 / 10,
        _ => value,
    };

    // ── 목표 고르기 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 그 편에서 노릴 부대 하나 — <b>병사수가 가장 적은</b> 부대다(<c>0x004475E0</c>).
    /// </summary>
    /// <remarks>
    /// 앞열이 살아 있으면 앞열에서만 고른다. 앞열이 다 쓰러지면 뒷열이 앞열이 된다 —
    /// 게임도 고를 것이 없으면 자리를 넓혀 잡는다.
    /// </remarks>
    private int Pick(bool foe, bool frontOnly)
    {
        int best = -1, fewest = int.MaxValue;
        foreach (int at in All(foe, frontOnly))
            if (battle.Units[at].Men < fewest) { fewest = battle.Units[at].Men; best = at; }
        return best;
    }

    /// <summary>그 편에서 노릴 수 있는 부대들.</summary>
    private IEnumerable<int> All(bool foe, bool frontOnly)
    {
        int side = foe ? LandBattle.FirstFoe : 0;
        var front = new List<int>();
        var back = new List<int>();
        for (int i = side; i < side + LandBattle.PerSide; i++)
        {
            if (!Alive(i)) continue;
            (LandUnits.IsFront(i) ? front : back).Add(i);
        }
        if (!frontOnly) return [.. front, .. back];
        return front.Count > 0 ? front : back;
    }

    /// <summary>그 앞열 자리의 뒤에 선 부대 — 창병이 꿰뚫는 자리다.</summary>
    private static int? Behind(int place)
    {
        int side = place < LandBattle.FirstFoe ? 0 : LandBattle.FirstFoe;
        int at = place - side;
        return at < 3 ? side + at + 3 : null;
    }

    private bool Alive(int slot) => battle.Units[slot].Standing;

    private string Name(int slot) =>
        $"{(slot < LandBattle.FirstFoe ? "아군" : "적")} {battle.Units[slot].Name}";

    private void Say(int slot, string text) => _log.Add(new Line(text, slot));

    /// <summary>한 쪽이 다 쓰러졌는지 본다.</summary>
    private void Done()
    {
        if (Over != null) return;

        bool mine = false, theirs = false;
        for (int i = 0; i < LandBattle.Slots; i++)
        {
            if (!battle.Units[i].Standing) continue;
            if (i < LandBattle.FirstFoe) mine = true; else theirs = true;
        }
        if (!theirs) Over = true;
        else if (!mine) Over = false;
    }
}
