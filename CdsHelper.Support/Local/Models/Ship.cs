namespace CdsHelper.Support.Local.Models;

/// <summary>
/// 가지고 있는 배 한 척 — 선체와, 그 배만의 값.
/// </summary>
/// <remarks>
/// <see cref="Models.Hull"/> 은 선체 <b>종류</b>라 배마다 다른 것을 담을 수 없다. 조선소
/// 수리가 배마다의 손상을 보고 <b>개조</b>가 배마다 성능을 바꾸므로 한 겹을 두었다 —
/// 값은 선체 것으로 시작해서 개조로 갈린다.
///
/// 게임은 배 레코드(<c>0x005A4E18</c>, 108바이트)에 이렇게 담는다.
/// <code>
///   +0x30  필요승원-10  (0x0044C770 / 0x0044C780)
///   +0x38  지금 추진력  +0x3C  최대 추진력   (0x0044C810·20 / 0x0044C830·40)
///   +0x48  지금 내구    +0x4C  최대 내구     (0x0044C850·60 / 0x0044C870·80)
///   +0x50  적재중량                          (0x0044C890 / 0x0044C8B0)
///   +0x58  적재용량                          (0x0044C8F0 / 0x0044C910)
/// </code>
/// 수리비는 <b>두 손상(추진력·내구)을 더한 값</b>이다(<c>0x0044BBF0</c>). 어느 짝이 내구고
/// 어느 짝이 추진력인지는 개조 결과 상자(<c>0x00495558</c>)가 이름표와 함께 찍어 준다.
/// </remarks>
public sealed class Ship
{
    /// <summary>
    /// 개조로 값을 얼마까지 올릴 수 있는지 — 선체 기본값의 <b>몇 배</b>.
    /// </summary>
    /// <remarks>
    /// <b>이 수는 게임 것이 아니다.</b> 게임은 선체 표(<c>0x004FC1E0</c>)에 값마다 상한을
    /// 따로 들고(내구 상한은 <c>+0x18</c>) 거기서 자른다. 우리 선체 표에는 그 칸이 없어
    /// 기본값의 배로 갈음한다.
    /// </remarks>
    public const int RefitCeiling = 2;

    /// <param name="hull">선체 종류.</param>
    /// <param name="hp">지금 내구. 안 주면 성한 채로 시작한다.</param>
    /// <param name="stats">개조로 갈린 값. 안 주면 선체 것 그대로다.</param>
    /// <param name="name">배 이름. 안 주면 선체 이름을 쓴다.</param>
    public Ship(Hull hull, int? hp = null, Stats? stats = null, string? name = null)
    {
        Hull = hull;
        Name = Trim(name) is { Length: > 0 } given ? given : hull.Name;
        var s = stats ?? Stats.Of(hull);
        MaxHp = Bound(s.MaxHp, hull.Hp);
        Speed = Bound(s.Speed, hull.Speed);
        Capacity = Bound(s.Capacity, hull.Capacity);
        Tonnage = Bound(s.Tonnage, hull.Tonnage);
        Crew = Bound(s.Crew, hull.Crew);
        Turrets = Math.Clamp(s.Turrets, 0, Math.Max(0, hull.Guns));
        for (int i = 0; i < MastSlots; i++)
            _sails[i] = i < (s.Sails?.Count ?? 0) ? Math.Clamp(s.Sails![i], NoSail, Square) : NoSail;
        // 물음표를 붙여 둔 것은 <b>옛 세이브</b> 때문이다 — 그 칸이 없으면 0(송골매상)이
        // 아니라 null 로 들어와야 안 단 것이 된다.
        Figurehead = s.Figurehead is { } carved && carved >= 0 && carved < FigureheadCount
            ? carved : -1;
        Gun = Cannon.Of(s.Gun) == null ? -1 : s.Gun;
        Guns = Gun < 0 ? 0 : Math.Clamp(s.Guns, 0, Turrets);
        if (Guns == 0) Gun = -1;
        Hp = Math.Clamp(hp ?? MaxHp, 0, MaxHp);
    }

    private static int Bound(int value, int basis) =>
        Math.Clamp(value, 1, Math.Max(1, basis * RefitCeiling));

    /// <summary>선체 종류. 값·그림 같은 것은 여기에 있다.</summary>
    public Hull Hull { get; }

    /// <summary>지금 내구.</summary>
    public int Hp { get; private set; }

    /// <summary>성할 때의 내구. 개조 "보강" 이 올린다.</summary>
    public int MaxHp { get; private set; }

    /// <summary>
    /// 뱃머리에 단 선수상의 번호. 안 달았으면 -1.
    /// </summary>
    /// <remarks>
    /// 게임은 배 레코드의 <c>+0x5C</c> 에 든다(<c>0x0044CA30</c> 이 그 자리를 읽는다).
    /// 바다 재앙 판정이 이 값으로 표 <c>0x0054A0A0</c> 을 타고 하나를 막아 준다 —
    /// 자세한 것은 <see cref="Models.Figurehead"/>.
    /// </remarks>
    public int Figurehead { get; private set; }

    /// <summary>선수상 가짓수(게임 표 <c>0x0054A0A0</c> 의 줄 수).</summary>
    public const int FigureheadCount = 36;

    /// <summary>선수상을 갈아 단다. 표 밖이면 떼어 낸 셈이 된다.</summary>
    public void Carve(int index) =>
        Figurehead = index >= 0 && index < FigureheadCount ? index : -1;

    /// <summary>최대 추진력. 개조가 깎는다.</summary>
    public int Speed { get; private set; }

    /// <summary>적재용량. 개조 "용량증가" 가 올린다.</summary>
    public int Capacity { get; private set; }

    /// <summary>적재중량. 개조 "부력증가" 가 올린다.</summary>
    public int Tonnage { get; private set; }

    /// <summary>필요승원. 용량을 늘리면 하나씩 는다.</summary>
    public int Crew { get; private set; }

    /// <summary>
    /// 포탑 수 — 대포를 걸 수 있는 자리. 개조 "포탑수변경" 이 늘리고 줄인다.
    /// </summary>
    /// <remarks>
    /// 게임 배 레코드의 <c>0x0044C9B0</c>(세터) · <c>0x0044C9C0</c>(게터) 자리다.
    /// 조선소 구입 화면의 "대포수" 가 <b>이 값의 상한</b>이라 <see cref="Models.Hull.Guns"/>
    /// 를 그대로 쓴다(게임은 선체 표 <c>+0x30</c> 에 따로 든다).
    /// </remarks>
    public int Turrets { get; private set; }

    /// <summary>실은 대포 갈래(<see cref="Cannon.All"/> 의 번호). 안 실었으면 -1.</summary>
    /// <remarks>게임의 <c>0x0044C9D0</c>·<c>0x0044C9E0</c> 자리다.</remarks>
    public int Gun { get; private set; } = -1;

    /// <summary>실은 대포 문수. 포탑 수를 넘을 수 없다.</summary>
    /// <remarks>게임의 <c>0x0044C950</c>·<c>0x0044C960</c> 자리다.</remarks>
    public int Guns { get; private set; }

    /// <summary>실은 대포의 무게. 적재중량을 먹는다.</summary>
    public int GunWeight => (Cannon.Of(Gun)?.Weight ?? 0) * Guns;

    // ── 마스트와 돛 ───────────────────────────────────────────────────────────

    /// <summary>마스트 자리 셋. 게임도 셋이다.</summary>
    public const int MastSlots = 3;

    /// <summary>마스트 이름. 게임 것 그대로다(<c>0x005314B0</c> 벌).</summary>
    public static readonly string[] MastNames = ["메인마스트", "세브마스트", "선미마스트"];

    /// <summary>돛 이름. 번호가 곧 <see cref="Sails"/> 의 값이다(<c>0x00531498</c> 벌).</summary>
    public static readonly string[] SailNames = ["없음", "삼각돛", "사각돛"];

    /// <summary>돛 번호.</summary>
    public const int NoSail = 0, Lateen = 1, Square = 2;

    private readonly int[] _sails = new int[MastSlots];

    /// <summary>
    /// 마스트마다 달린 돛(0 없음 · 1 삼각돛 · 2 사각돛).
    /// </summary>
    /// <remarks>
    /// 게임은 배 레코드 <c>+0x68</c> 의 16비트에 <b>2비트씩 셋</b>으로 담는다 —
    /// 메인 0~1, 세브 2~3, 선미 4~5 (<c>0x00494AE0</c> 이 그 자리를 쓴다).
    /// </remarks>
    public IReadOnlyList<int> Sails => _sails;

    /// <summary>서 있는 마스트 수(돛이 달린 자리). 게임의 <c>0x00422CE0</c> 이다.</summary>
    public int Masts => _sails.Count(v => v != NoSail);

    /// <summary>
    /// 이 배에 세울 수 있는 마스트 수.
    /// </summary>
    /// <remarks>
    /// 게임(<c>0x00494A50</c>)은 선체 종류로 가른다 — 코구(0)·다우(7)는 못 늘리고,
    /// 카라벨(1)은 둘까지, 그 밖은 셋까지다. 우리 붙박이 다섯에는 코구도 다우도 없으므로
    /// <b>카라벨만 둘, 나머지는 셋</b>이다. 등록해 넣은 배는 제가 들고 있는 값을 쓴다.
    /// </remarks>
    public int MaxMasts => Math.Clamp(Hull.MaxMasts, 1, MastSlots);

    /// <summary>마스트를 더 세울 수 있는지.</summary>
    public bool CanAddMast => Masts < MaxMasts;

    /// <summary>
    /// 돛 종류를 바꿀 수 있는 배인지.
    /// </summary>
    /// <remarks>
    /// 게임은 못 바꾸는 배에 "안됐지만, 이 타입은 돛의 종류를 바꿀 수 없네."
    /// (<c>0x00531678</c>) 를 낸다. 마스트를 달 때 <b>삼각돛으로 못 박는</b> 배가 그쪽이다
    /// (<c>0x00494C9A</c> 의 선체 종류 1·2) — 카라벨과 대형카라벨이다.
    /// 등록해 넣은 배는 제가 들고 있는 값을 쓴다.
    /// </remarks>
    public bool CanChangeSail => Hull.CanChangeSail;

    /// <summary>
    /// 마스트 하나를 세운다 — 적재용량이 25 줄고 필요승원이 둘 는다.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x00494AB0</c> 이다.
    /// <code>
    /// 494b52  적재용량 = max(1, 적재용량 - 25)
    /// 494b67  필요승원 += 2
    /// </code>
    /// </remarks>
    /// <param name="sail">달 돛(<see cref="Lateen"/> · <see cref="Square"/>).</param>
    /// <returns>세운 자리 번호. 못 세웠으면 -1.</returns>
    public int AddMast(int sail)
    {
        if (!CanAddMast || sail is not (Lateen or Square)) return -1;

        int at = Array.FindIndex(_sails, v => v == NoSail);
        if (at < 0) return -1;

        _sails[at] = sail;
        Capacity = Math.Max(1, Capacity - MastCapacityCost);
        Crew += MastCrewCost;
        return at;
    }

    /// <summary>마스트 하나가 먹는 적재용량과 승원.</summary>
    public const int MastCapacityCost = 25, MastCrewCost = 2;

    /// <summary>그 자리의 돛을 삼각↔사각으로 바꾼다.</summary>
    public bool SwapSail(int at)
    {
        if (!CanChangeSail || at < 0 || at >= MastSlots || _sails[at] == NoSail) return false;
        _sails[at] = _sails[at] == Lateen ? Square : Lateen;
        return true;
    }

    /// <summary>돛을 더 달 수 있는지 — 최대 추진력이 상한에 안 닿았으면.</summary>
    /// <remarks>게임은 선체 표 <c>+0x10</c>(추진력 상한)과 견준다(<c>0x004951C0</c>).</remarks>
    public bool CanAddSail => Masts > 0 && Speed < Hull.Speed * RefitCeiling;

    /// <summary>돛 한 벌을 더 달면 오르는 추진력.</summary>
    public const int SailSpeedStep = 10;

    /// <summary>
    /// 돛 추가 — 추진력을 올리고 그만큼 배가 여려진다.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x00495200</c> 이다.
    /// <code>
    /// 495212  새 추진력 = min(추진력 + 10, 선체 표 +0x10)
    /// 49527f  최대내구 -= 늘어난 / 2       (적어도 1)
    /// 4952bc  필요승원 += 1
    /// </code>
    /// 물음도 그렇게 이른다 — "마스트에 부담이 되어 배가 조그마한 충격에도 약해지지만,
    /// 괜찮겠나?"
    /// </remarks>
    public Refit AddSail()
    {
        var was = Snapshot();
        int grown = Math.Min(Speed + SailSpeedStep, Hull.Speed * RefitCeiling) - Speed;

        Speed += grown;
        MaxHp = Math.Max(1, MaxHp - grown / 2);
        Hp = Math.Min(Hp, MaxHp);
        Crew++;
        return Refit.Between(was, Snapshot());
    }

    /// <summary>
    /// 포탑을 이만큼까지 달 수 있다.
    /// </summary>
    /// <remarks>
    /// 게임은 <c>min(선체 표 +0x30, 지금 포탑 + 적재용량)</c> 으로 자른다
    /// (<c>0x004961D6</c>). 우리 선체 표의 "대포수" 가 그 <c>+0x30</c> 자리다.
    /// </remarks>
    public int MaxTurrets => Math.Min(Hull.Guns, Turrets + Capacity);

    /// <summary>
    /// 포탑 수를 바꾼다. 줄여서 대포가 넘치면 그만큼 내린다.
    /// </summary>
    /// <returns>넘쳐서 내린 대포 문수. 없으면 0.</returns>
    /// <remarks>게임의 <c>0x004960A0</c> 이다.</remarks>
    public int SetTurrets(int count)
    {
        Turrets = Math.Clamp(count, 0, MaxTurrets);
        if (Guns <= Turrets) return 0;

        int spilled = Guns - Turrets;
        Guns = Turrets;
        if (Guns == 0) Gun = -1;
        return spilled;
    }

    /// <summary>
    /// 대포를 싣는다. 갈래가 갈리면 실려 있던 것은 부르는 쪽이 되사 준 뒤에 부른다.
    /// </summary>
    /// <remarks>게임의 <c>0x004962E0</c> 이다 — 갈래를 넣고 문수를 넣는다.</remarks>
    public void Load(int kind, int count)
    {
        Gun = Cannon.Of(kind) == null ? -1 : kind;
        Guns = Gun < 0 ? 0 : Math.Clamp(count, 0, Turrets);
        if (Guns == 0) Gun = -1;
    }

    /// <summary>
    /// 그 대포를 몇 문까지 실을 수 있는지 — 포탑 수와 남는 무게가 가른다.
    /// </summary>
    /// <param name="kind">실을 대포 갈래.</param>
    /// <param name="free">
    /// 쓸 수 있는 무게. 게임은 <b>지금 실린 대포를 다 내렸다 치고</b> 잰다
    /// (<c>0x004964FF</c>) — 갈래를 바꿔 실을 때 앞엣것이 자리를 막지 않게. 그래서 부르는
    /// 쪽이 <c>남는 중량 + <see cref="GunWeight"/></c> 를 준다.
    ///
    /// 게임은 <b>배마다</b> 짐을 싣지만 우리는 함대가 통째로 싣는다 — 그래서 이 값도
    /// 함대 것으로 잰다.
    /// </param>
    public int RoomFor(int kind, int free)
    {
        if (Cannon.Of(kind) is not { } gun || gun.Weight <= 0) return 0;
        return Math.Clamp(Math.Max(0, free) / gun.Weight, 0, Turrets);
    }

    /// <summary>상한 만큼. 성하면 0 이다.</summary>
    public int Damage => Math.Max(0, MaxHp - Hp);

    /// <summary>손볼 데가 있는지.</summary>
    public bool NeedsRepair => Damage > 0;

    /// <summary>
    /// 그만큼 상한다. <paramref name="floor"/> 밑으로는 안 내려간다.
    /// </summary>
    /// <param name="amount">깎을 만큼. 음수는 0 으로 본다.</param>
    /// <param name="floor">
    /// 더 못 내려가는 바닥. 폭풍이 <b>기함</b>을 칠 때 1 을 준다 — 게임도 기함 자리의
    /// 내구를 1 밑으로 안 내려 기함만은 안 잃는다(<c>0x00474F4E</c>).
    /// </param>
    public void Hurt(int amount, int floor = 0) =>
        Hp = Math.Clamp(Hp - Math.Max(0, amount), Math.Clamp(floor, 0, MaxHp), MaxHp);

    /// <summary>말끔히 고친다.</summary>
    public void Repair() => Hp = MaxHp;

    /// <summary>
    /// 배 이름. 게임 문구에서는 뒤에 "호" 가 붙는다("산타마리아호").
    /// </summary>
    /// <remarks>
    /// 조선소 개조의 "선명변경" 으로 바꾼다. 고를 수 있는 이름은
    /// <see cref="ShipNames.All"/> 이고, 글자를 하나씩 찍어 지어 넣을 수도 있다.
    /// </remarks>
    public string Name { get; private set; }

    /// <summary>이름을 바꾼다. 빈 이름은 안 받는다.</summary>
    /// <returns>바꿨으면 true.</returns>
    public bool Rename(string name)
    {
        if (Trim(name) is not { Length: > 0 } given || given == Name) return false;
        Name = given;
        return true;
    }

    /// <summary>앞뒤 빈칸을 떼고 너무 길면 자른다.</summary>
    private static string Trim(string? name)
    {
        string text = (name ?? "").Trim();
        return text.Length > ShipNames.MaxLength ? text[..ShipNames.MaxLength] : text;
    }

    // ── 개조 ─────────────────────────────────────────────────────────────────

    /// <summary>용량을 더 늘릴 수 있는지(<c>0x004953F0</c>).</summary>
    public bool CanGrowCapacity => Capacity < Hull.Capacity * RefitCeiling;

    /// <summary>중량을 더 늘릴 수 있는지(<c>0x004956A0</c>).</summary>
    public bool CanGrowTonnage => Tonnage < Hull.Tonnage * RefitCeiling;

    /// <summary>더 보강할 수 있는지(<c>0x00495920</c>).</summary>
    public bool CanReinforce => MaxHp < Hull.Hp * RefitCeiling;

    /// <summary>개조 한 번에 늘어나는 적재용량·적재중량.</summary>
    public const int GrowStep = 50;

    /// <summary>보강 한 번에 늘어나는 내구.</summary>
    public const int ReinforceStep = 10;

    /// <summary>
    /// 용량증가 — 적재용량을 올린다. 게임의 <c>0x00495420</c> 이다.
    /// </summary>
    /// <remarks>
    /// <code>
    /// 49542b  늘어난만큼 = min(용량 + 50, 상한) - 용량
    /// 49544b  d = 늘어난만큼 * 10
    /// 4954a5  적재중량   += d / 3          (상한까지)
    /// 4954d1  최대추진력 -= d / 100        (적어도 1)
    /// 495510  최대내구   -= d / 100        (적어도 1)
    /// 495544  필요승원   += 1
    /// </code>
    /// 게임 물음도 그렇게 알려 준다 — "용량과 함께 적재용량도 조금 올라가지만, 스피드와
    /// 내구력이 조금 떨어지네."
    /// </remarks>
    public Refit GrowCapacity()
    {
        var was = Snapshot();
        int grown = Math.Min(Capacity + GrowStep, Hull.Capacity * RefitCeiling) - Capacity;
        int d = grown * 10;

        Capacity += grown;
        Tonnage = Math.Min(Tonnage + d / 3, Hull.Tonnage * RefitCeiling);
        Wear(d / 100);
        Crew++;
        return Refit.Between(was, Snapshot());
    }

    /// <summary>
    /// 부력증가 — 적재중량을 올린다. 게임의 <c>0x004956D0</c> 이다. 용량증가와 같은 꼴인데
    /// 늘어나는 것과 딸려 오는 것이 뒤바뀌고, 필요승원은 안 는다.
    /// </summary>
    public Refit GrowTonnage()
    {
        var was = Snapshot();
        int grown = Math.Min(Tonnage + GrowStep, Hull.Tonnage * RefitCeiling) - Tonnage;
        int d = grown * 10;

        Tonnage += grown;
        Capacity = Math.Min(Capacity + d / 3, Hull.Capacity * RefitCeiling);
        Wear(d / 100);
        return Refit.Between(was, Snapshot());
    }

    /// <summary>
    /// 보강 — 최대 내구를 올리고 <b>그 자리에서 성하게 해 준다</b>. 게임의
    /// <c>0x00495960</c> 이다.
    /// </summary>
    /// <remarks>
    /// <code>
    /// 49599f  늘어난만큼 = min(최대내구 + 10, 선체표+0x18) - 최대내구
    /// 4959c9  지금내구 = 새 최대내구            ; 꽉 채운다
    /// 4959e6  최대추진력 -= 늘어난만큼 / 3
    /// 495a36  적재중량   -= 늘어난만큼 * 50 / 3
    /// </code>
    /// 그래서 보강은 <b>수리를 겸한다</b> — 폭풍에 상한 배를 여기서 한꺼번에 되돌린다.
    /// </remarks>
    public Refit Reinforce()
    {
        var was = Snapshot();
        int grown = Math.Min(MaxHp + ReinforceStep, Hull.Hp * RefitCeiling) - MaxHp;

        MaxHp += grown;
        Hp = MaxHp;
        Speed = Math.Max(1, Speed - grown / 3);
        Tonnage = Math.Max(1, Tonnage - grown * 50 / 3);
        return Refit.Between(was, Snapshot());
    }

    /// <summary>추진력과 내구를 그만큼 깎는다. 지금 내구는 새 상한까지 잘린다.</summary>
    private void Wear(int amount)
    {
        Speed = Math.Max(1, Speed - amount);
        MaxHp = Math.Max(1, MaxHp - amount);
        Hp = Math.Min(Hp, MaxHp);
    }

    /// <summary>세이브에 적고 되돌릴 때 쓰는 값 묶음.</summary>
    /// <param name="MaxHp">최대 내구.</param>
    /// <param name="Speed">최대 추진력.</param>
    /// <param name="Capacity">적재용량.</param>
    /// <param name="Tonnage">적재중량.</param>
    /// <param name="Crew">필요승원.</param>
    /// <param name="Sails">마스트 셋에 달린 돛. 안 주면 메인마스트에 삼각돛 하나다.</param>
    public sealed record Stats(int MaxHp, int Speed, int Capacity, int Tonnage, int Crew,
                               int Turrets = 0, int Gun = -1, int Guns = 0,
                               IReadOnlyList<int>? Sails = null, int? Figurehead = null)
    {
        /// <summary>
        /// 선체 기본값 그대로. 포탑은 다 달린 채로 나오고 대포는 안 실려 있으며,
        /// 마스트는 <b>메인 하나에 삼각돛</b>만 서 있다.
        /// </summary>
        public static Stats Of(Hull hull) =>
            new(hull.Hp, hull.Speed, hull.Capacity, hull.Tonnage, hull.Crew, hull.Guns,
                Sails: [Lateen, NoSail, NoSail]);
    }

    /// <summary>지금 값을 통째로.</summary>
    public Stats Snapshot() =>
        new(MaxHp, Speed, Capacity, Tonnage, Crew, Turrets, Gun, Guns, [.. _sails], Figurehead);

    /// <summary>개조로 값이 갈렸는지.</summary>
    public bool IsRefitted => Snapshot() != Stats.Of(Hull);
}

/// <summary>
/// 개조 한 번이 바꾼 값들 — 게임이 개조 뒤에 띄우는 <c>"%-12s%4d → %4d"</c> 상자다.
/// </summary>
/// <param name="Lines">바뀐 줄. 이름과 앞뒤 값이다.</param>
public sealed record Refit(IReadOnlyList<Refit.Line> Lines)
{
    /// <param name="Name">값 이름("적재용량" 같은).</param>
    /// <param name="Before">개조 앞.</param>
    /// <param name="After">개조 뒤.</param>
    public sealed record Line(string Name, int Before, int After);

    /// <summary>바뀐 것이 하나라도 있는지.</summary>
    public bool Any => Lines.Count > 0;

    /// <summary>
    /// 두 값 묶음을 견줘 바뀐 줄만 낸다. 이름과 차례는 게임 상자 그대로다
    /// (<c>0x005318D0</c> 벌 — 적재용량 · 적재중량 · 최대추진력 · 최대내구력 · 최저승원수).
    /// </summary>
    public static Refit Between(Ship.Stats was, Ship.Stats now)
    {
        static int Standing(IReadOnlyList<int>? sails) => sails?.Count(v => v != 0) ?? 0;

        var lines = new List<Line>();
        void Add(string name, int a, int b) { if (a != b) lines.Add(new Line(name, a, b)); }

        Add("적재용량", was.Capacity, now.Capacity);
        Add("적재중량", was.Tonnage, now.Tonnage);
        Add("최대추진력", was.Speed, now.Speed);
        Add("최대내구력", was.MaxHp, now.MaxHp);
        Add("최저승원수", was.Crew, now.Crew);
        Add("포탑수", was.Turrets, now.Turrets);
        Add("대포수", was.Guns, now.Guns);
        Add("마스트수", Standing(was.Sails), Standing(now.Sails));
        return new Refit(lines);
    }
}
