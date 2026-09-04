namespace CdsHelper.Support.Local.Models;

/// <summary>배를 산 결과.</summary>
public enum PurchaseResult
{
    /// <summary>샀다.</summary>
    Ok,

    /// <summary>소지금이 모자란다.</summary>
    NotEnoughGold,

    /// <summary>배가 이미 <see cref="Player.MaxShips"/> 척이다.</summary>
    FleetFull,

    /// <summary>소지품 칸이 꽉 찼다("이 이상 가질 수 없습니다!").</summary>
    BagFull,
}

/// <summary>기술을 배운 결과.</summary>
public enum LearnResult
{
    /// <summary>배웠다.</summary>
    Ok,

    /// <summary>소지금이 모자란다.</summary>
    NotEnoughGold,

    /// <summary>이미 <see cref="Skill.MaxLevel"/> 자리다.</summary>
    Mastered,
}

/// <summary>
/// 함대 창의 주인공. 소지금과 가진 배를 들고 있는다 — 조선소에서 배를 사면 여기서 돈이 빠진다.
/// </summary>
/// <remarks>
/// 세이브 파일에서 읽는 <see cref="PlayerData"/> 와는 다르다. 그쪽은 게임이 적어 둔 값을
/// 보여 주는 것이고, 이쪽은 함대 창에서 우리가 굴리는 값이다.
/// </remarks>
public sealed class Player
{
    /// <summary>
    /// 함대에 둘 수 있는 배의 수. 넘으면 더 못 산다.
    /// </summary>
    /// <remarks>
    /// 게임도 여덟이다 — 항구 함대편성의 "선박 편입" 이 <c>0x0046A24A</c> 에서
    /// <c>cmp eax, 8</c> 으로 막고, 함대 객체도 여덟 칸이다.
    /// </remarks>
    public const int MaxShips = 8;

    /// <summary>시작 소지금(닢).</summary>
    public const int StartingGold = 1000;

    /// <summary>시작 명성. 이만큼이면 만나 주는 후원자가 제법 있다.</summary>
    public const int StartingFame = 1700;

    /// <summary>
    /// 놀이가 시작하는 날 — <b>1480년 1월 1일</b>이다.
    /// </summary>
    /// <remarks>
    /// 새 놀이는 늘 이 날이다. 여급 표의 등장년도도 이 해를 바닥으로 삼고
    /// (<c>max(1480, 1495 - 표값)</c>), 도서관 책도 이 해부터 하나씩 나온다.
    ///
    /// 예전에는 <c>1499년 4월 15일</c> 로 두었는데, 그것은 <b>이어서 하던 판</b>의 갈무리에서
    /// 본 날짜였다.
    /// </remarks>
    public static readonly DateTime StartDate = new(1480, 1, 1);

    private readonly List<Ship> _ships = [];
    private readonly Dictionary<string, int> _skills = [];
    private readonly HashSet<int> _hints = [];

    /// <summary>카라벨 한 척과 시작 소지금으로 시작한다.</summary>
    public Player()
    {
        Gold = StartingGold;
        Fame = StartingFame;
        Date = StartDate;
        _ships.Add(new Ship(Hull.Cheapest, name: ShipNames.All[0]));
        Crew = MinCrew;   // 배는 최저 승원을 채우고 시작한다
    }

    /// <summary>
    /// 주인공 이름. 사람들이 이 이름으로 부른다("각하, 에르네스토를 데리고 왔습니다").
    /// </summary>
    /// <remarks>
    /// 세이브에서 읽어 오지 않고 여기서 들고 있는다 — 함대 창은 세이브와 따로 굴러가기
    /// 때문이다(<see cref="PlayerData"/> 참고). <b>처음에는 비어 있다</b> — 게임도 신상
    /// 창을 빈 칸으로 열고 사람이 적어 넣는다.
    /// </remarks>
    public string Name { get; set; } = "";

    // ── 신상 (NEW GAME 첫 걸음) ────────────────────────────────────────────────

    /// <summary>성. 게임 화면의 첫 칸이다.</summary>
    public string Family { get; set; } = "";

    /// <summary>명. 화면에는 <c>"%s·%s"</c>(<c>0x00571B08</c>) 로 성과 붙여 낸다.</summary>
    public string Given { get; set; } = "";

    /// <summary>나이. 게임은 25로 시작한다.</summary>
    public int Age { get; set; } = 25;

    /// <summary>생일(달·날).</summary>
    public int BirthMonth { get; set; } = 1;

    /// <summary>생일의 날.</summary>
    public int BirthDay { get; set; } = 1;

    /// <summary>혈액형(<see cref="BloodTypes"/> 의 번호).</summary>
    public int Blood { get; set; }

    /// <summary>고를 수 있는 혈액형. 게임 화면 차례 그대로다.</summary>
    public static readonly string[] BloodTypes = ["A", "B", "O", "AB"];

    /// <summary>국적(<see cref="Nations"/> 의 번호).</summary>
    public int Nation { get; set; }

    /// <summary>고를 수 있는 국적. 게임 화면에 둘만 뜬다.</summary>
    public static readonly string[] Nations = ["포르투갈 왕국", "에스파니아 왕국"];

    /// <summary>얼굴 번호(MALE.CDS 의 파트). 화면 왼쪽 초상화다.</summary>
    public int Face { get; set; }

    /// <summary>
    /// <b>운명 코드</b> — 여급과의 궁합이 이 값 하나로 갈린다(0~15).
    /// </summary>
    /// <remarks>
    /// 게임은 이것을 주인공 객체의 <c>+0x08</c> 에 따로 들고 있다(<c>0x0047CB10</c>). 새 놀이가
    /// 앞의 열여섯 초상화만 고르게 해서 값이 <see cref="Face"/> 와 늘 같았을 뿐,
    /// <b>초상화 번호와 같은 것이 아니다</b>.
    ///
    /// 그래서 칸을 갈라 두었다. 붙여 두면 초상화를 더 넣거나 차례를 바꾸는 순간, 또는
    /// 열여섯 밖의 얼굴을 가진 세이브를 읽는 순간 궁합이 조용히 어긋난다.
    /// 새로 지을 때는 고른 초상화 자리를 그대로 넣고(<see cref="SetFortune"/>),
    /// 그 값을 안 적어 둔 옛 세이브만 얼굴 번호로 물러선다.
    /// </remarks>
    public int Fortune { get; private set; }

    /// <summary>운명 코드를 넣는다. 0~15 를 벗어나면 잘라 넣는다.</summary>
    public void SetFortune(int fortune) => Fortune = Math.Clamp(fortune, 0, MaxFortune);

    /// <summary>운명 코드의 위. 게임도 젊은 얼굴 열여섯 벌만 쓴다.</summary>
    public const int MaxFortune = 15;

    /// <summary>국적 이름. 번호가 표 밖이면 첫째다.</summary>
    public string NationName => Nations[Math.Clamp(Nation, 0, Nations.Length - 1)];

    /// <summary>혈액형 이름.</summary>
    public string BloodName => BloodTypes[Math.Clamp(Blood, 0, BloodTypes.Length - 1)];

    /// <summary>
    /// 생일이 드는 별자리. 게임 표(<c>0x005609D8</c>, 목양좌부터 열둘)와 같은 이름이다.
    /// </summary>
    public string Zodiac => ZodiacOf(BirthMonth, BirthDay);

    /// <summary>별자리 이름 열둘. 목양좌(양자리)부터 돈다.</summary>
    public static readonly string[] Zodiacs =
    [
        "목양좌", "목우좌", "쌍둥이좌", "게좌", "사자좌", "처녀좌",
        "천칭좌", "전갈좌", "궁수좌", "산양좌", "물병좌", "물고기좌",
    ];

    /// <summary>그 날짜가 드는 별자리. 경계 날은 흔히 쓰는 자리를 따른다.</summary>
    public static string ZodiacOf(int month, int day)
    {
        // 자리마다 "그 달 며칟날부터"다. 목양좌(양자리)는 3월 21일부터.
        int[] from = [21, 20, 21, 22, 23, 23, 23, 23, 22, 22, 20, 19];
        int at = (month + 9) % 12;                 // 3월이 0(목양좌)이 되게 민다
        if (day < from[at]) at = (at + 11) % 12;    // 아직 안 넘었으면 앞자리
        return Zodiacs[at];
    }

    /// <summary>신상을 한꺼번에 박는다. NEW GAME 의 첫 걸음이 부른다.</summary>
    public void SetProfile(string family, string given, int age, int month, int day,
                           int blood, int nation, int face)
    {
        Family = family.Trim();
        Given = given.Trim();
        Name = Family.Length > 0 ? $"{Given}·{Family}" : Given;
        Age = Math.Clamp(age, MinAge, MaxAge);
        BirthYear = Date.Year - Age;
        BirthMonth = Math.Clamp(month, 1, 12);
        BirthDay = Math.Clamp(day, 1, 31);
        Blood = Math.Clamp(blood, 0, BloodTypes.Length - 1);
        Nation = Math.Clamp(nation, 0, Nations.Length - 1);
        Face = Math.Max(0, face);
        // 새로 지을 때는 고른 초상화 자리가 곧 운명 코드다 — 게임도 그렇게 넣는다.
        SetFortune(Face);
    }

    /// <summary>고를 수 있는 나이.</summary>
    public const int MinAge = 15;

    /// <summary>고를 수 있는 가장 많은 나이.</summary>
    public const int MaxAge = 40;

    /// <summary>악명치 — 인물정보 판의 명성 맞은편 칸이다.</summary>
    /// <remarks>
    /// 게임은 나쁜 짓(해적질·약탈)으로 올린다. 우리 쪽에는 아직 올릴 길이 없어 늘 0 이다.
    /// </remarks>
    public int Infamy { get; set; }

    /// <summary>빚(닢). 아직 빌려 주는 데가 없어 늘 0 이다.</summary>
    public int Debt { get; set; }

    /// <summary>태어난 해. 인물정보 판이 생년월일로 적는다.</summary>
    public int BirthYear { get; set; } = StartDate.Year - 25;

    /// <summary>직업 번호(<see cref="Job.All"/>).</summary>
    public int JobIndex { get; set; }

    /// <summary>직업.</summary>
    public Job Work => Job.Of(JobIndex);

    /// <summary>능력치 여섯(체력·지력·무력·매력·운·신앙심).</summary>
    public int[] Abilities { get; private set; } = [50, 50, 50, 50, 50, 50];

    /// <summary>그 능력치.</summary>
    public int AbilityOf(int which) =>
        which >= 0 && which < Abilities.Length ? Abilities[which] : 0;

    /// <summary>능력치를 통째로 박는다.</summary>
    public void SetAbilities(IReadOnlyList<int> values)
    {
        var next = new int[Ability.Names.Length];
        for (int i = 0; i < next.Length; i++)
            next[i] = i < values.Count ? values[i] : Ability.Base;
        Abilities = next;
    }

    /// <summary>
    /// 몸이 상한다 — 일기토를 치르고 나면 그만큼 체력이 준다.
    /// </summary>
    /// <remarks>
    /// 게임은 <c>0x004AA5BB</c> 에서 <b>남은 부위 셋의 평균</b>만큼 깎는다. 1 아래로는
    /// 안 내려간다 — 0 이 되면 셈이 무너지는 자리가 여럿이다.
    /// </remarks>
    public void Hurt(int amount)
    {
        if (amount <= 0) return;
        Abilities[Ability.Body] = Math.Max(1, Abilities[Ability.Body] - amount);
    }

    /// <summary>언어마다의 자리(0~<see cref="Skill.MaxLevel"/>).</summary>
    private readonly Dictionary<string, int> _tongues = [];

    /// <summary>배운 언어.</summary>
    public IReadOnlyDictionary<string, int> Tongues => _tongues;

    /// <summary>그 언어의 자리.</summary>
    public int TongueOf(string language) => _tongues.GetValueOrDefault(language);

    /// <summary>언어 자리를 박는다.</summary>
    public void SetTongue(string language, int level) =>
        _tongues[language] = Math.Clamp(level, 0, Skill.MaxLevel);

    /// <summary>
    /// 세이브에서 언어 자리를 되돌린다.
    /// </summary>
    /// <remarks>
    /// 언어는 기술과 <b>딴 칸</b>에 있어서 <see cref="Restore"/> 의 기술 사전에 안 실린다.
    /// 판 24 앞 세이브에는 언어가 아예 안 적혀 있어 그때는 아무 일도 안 한다 —
    /// 그 판까지는 갈무리를 불러오면 <b>배운 언어가 다 0 이 되었다</b>.
    /// </remarks>
    public void RestoreTongues(IEnumerable<KeyValuePair<string, int>>? tongues)
    {
        if (tongues == null) return;
        _tongues.Clear();
        foreach (var (name, level) in tongues)
            _tongues[name] = Math.Clamp(level, 0, Skill.MaxLevel);
    }

    /// <summary>기술 자리를 박는다(새 놀이에서 찍어 줄 때).</summary>
    public void SetSkill(string skill, int level) =>
        _skills[skill] = Math.Clamp(level, 0, Skill.MaxLevel);

    /// <summary>
    /// 명성. 후원자를 만나려면 그 사람이 요구하는 만큼 있어야 한다.
    /// </summary>
    /// <remarks>
    /// 게임은 알현의 첫 관문에서 이것을 본다(<c>0x004AE1F0</c> → <c>0x0044E740</c>) —
    /// 모자라면 집사가 "…님은 바쁘셔서 만나실 수 없습니다" 로 돌려보낸다.
    /// 요구치는 후원자마다 다르다(<c>patrons.json</c> 의 fame, 0 부터 9900 까지).
    ///
    /// 항구에서 발견물을 <b>발표</b>하면 오른다 — 그 발견물의 보수를 70 으로 나눈 만큼이고
    /// 적어도 10 이다(<c>0x0047E849</c>). <see cref="StartingFame"/> 이면 여든한 명 가운데
    /// 열몇이 만나 주고, 알릴수록 문이 열린다.
    /// </remarks>
    public int Fame { get; set; }

    /// <summary>지금 날짜. 기술을 배우면 그만큼 달이 넘어간다.</summary>
    public DateTime Date { get; private set; }

    /// <summary>지금 들어와 있는 도시. 바다에 있으면 -1.</summary>
    public int CityId { get; private set; } = -1;

    /// <summary>지금 들어와 있는 도시 이름. 바다에 있으면 빈 문자열.</summary>
    public string CityName { get; private set; } = "";

    /// <summary>배운 기술과 그 자리.</summary>
    public IReadOnlyDictionary<string, int> Skills => _skills;

    /// <summary>얻은 힌트 번호. 책을 읽으면 는다.</summary>
    public IReadOnlyCollection<int> Hints => _hints;

    /// <summary>
    /// 부하 자리 이름. 게임 EXE 의 표(<c>0x00571038</c>) 차례 그대로다.
    /// </summary>
    public static readonly string[] MateRoles = ["부관", "항해사", "측량사", "통역"];

    /// <summary>부하로 삼을 수 있는 사람 수. 자리마다 하나씩이다.</summary>
    public static readonly int MaxMates = MateRoles.Length;

    /// <summary>자리별 부하. 빈 자리는 빈 문자열이다.</summary>
    private readonly string[] _mates = ["", "", "", ""];
    private readonly HashSet<string> _met = [];

    /// <summary>낯을 튼 사람. 이 사람들만 이름이 보이고 말을 걸 수 있다.</summary>
    /// <remarks>
    /// 게임은 인물 객체의 <c>vtbl[0x34]</c> 로 이것을 가른다 — 참이면 이름 대신 "남자"·"여"
    /// 로 부르고 한잔 사는 것만 되고, 거짓이면 "[이름]이 있다" 로 부르고 말을 걸 수 있다
    /// (볼트 <c>14.분석-술집 화면과 대사</c>).
    /// </remarks>
    public IReadOnlyCollection<string> Met => _met;

    /// <summary>그 사람과 낯을 텄는지.</summary>
    public bool HasMet(string name) => _met.Contains(name);

    /// <summary>낯을 튼다. 처음이면 true.</summary>
    public bool Meet(string name) => !string.IsNullOrEmpty(name) && _met.Add(name);

    private readonly Dictionary<string, int> _closeness = [];

    /// <summary>친밀도의 위(<c>0x00478530</c> 이 0~100 으로 자른다).</summary>
    public const int MaxCloseness = 100;

    /// <summary>
    /// 후원자마다의 친밀도(0~<see cref="MaxCloseness"/>). 움직인 적 있는 사람만 들어 있다.
    /// </summary>
    /// <remarks>
    /// 게임은 후원자 객체 <c>+0x20</c> 에 들고 <b>0 에서 시작한다</b> — 후원자 정보 창의
    /// 「친밀도」가 이 값이다. 후원자 표의 <c>+0x30</c> 은 이름은 같아도 딴 값이다:
    /// 그쪽은 낼 자금을 가르는 밑값이고(<c>0x004AF086</c> 이 표를 읽는다) 놀이 내내 안 바뀐다.
    ///
    /// 여급의 친밀도(<see cref="Liking"/>)와 같은 함수로 오르내리지만 자리는 따로다.
    /// </remarks>
    public IReadOnlyDictionary<string, int> Closeness => _closeness;

    /// <summary>그 후원자와의 친밀도. 아직 움직인 적이 없으면 0 이다.</summary>
    public int ClosenessOf(string name) =>
        !string.IsNullOrEmpty(name) && _closeness.TryGetValue(name, out int now) ? now : 0;

    /// <summary>
    /// 친밀도를 움직인다(<c>0x00478530</c>). 돌려주는 것은 움직인 뒤의 값이다.
    /// </summary>
    public int Endear(string name, int by)
    {
        if (string.IsNullOrEmpty(name)) return 0;
        int now = Math.Clamp(ClosenessOf(name) + by, 0, MaxCloseness);
        _closeness[name] = now;
        return now;
    }

    /// <summary>적어 둔 친밀도를 되돌린다.</summary>
    public void RestoreCloseness(Dictionary<string, int>? closeness)
    {
        _closeness.Clear();
        if (closeness == null) return;
        foreach (var (name, value) in closeness)
            if (!string.IsNullOrEmpty(name))
                _closeness[name] = Math.Clamp(value, 0, MaxCloseness);
    }

    /// <summary>
    /// 자리별 부하. 색인이 <see cref="MateRoles"/> 의 자리고, 빈 자리는 빈 문자열이다.
    /// </summary>
    /// <remarks>
    /// 게임은 부하를 든 차례가 아니라 <b>자리</b>로 든다 — 부관·항해사·측량사·통역이다.
    /// 여관·술집의 "부하편성" 에서 두 줄을 눌러 자리를 맞바꾼다.
    /// </remarks>
    public IReadOnlyList<string> Mates => _mates;

    /// <summary>그 자리에 앉은 사람. 비었으면 빈 문자열.</summary>
    public string MateAt(int slot) =>
        slot >= 0 && slot < _mates.Length ? _mates[slot] : "";

    /// <summary>든 부하 수(빈 자리는 빼고).</summary>
    public int MateCount => _mates.Count(m => m.Length > 0);

    /// <summary>이미 부하로 든 사람인지.</summary>
    public bool HasMate(string name) => _mates.Contains(name);

    /// <summary>
    /// 부하로 삼는다. 앞에서부터 빈 자리에 앉힌다. 처음 드는 사람이고 자리가 남았으면 true.
    /// </summary>
    public bool Hire(string name)
    {
        if (string.IsNullOrEmpty(name) || HasMate(name)) return false;

        for (int i = 0; i < _mates.Length; i++)
            if (_mates[i].Length == 0)
            {
                _mates[i] = name;
                return true;
            }
        return false;
    }

    /// <summary>그 자리에 그 사람을 앉힌다. 자리를 되돌릴 때 쓴다.</summary>
    public void SetMate(int slot, string name)
    {
        if (slot >= 0 && slot < _mates.Length) _mates[slot] = name ?? "";
    }

    /// <summary>
    /// 부하 하나의 됨됨이. 자리에는 이름만 앉히고 자료는 여기에 따로 적어 둔다.
    /// </summary>
    /// <remarks>
    /// 술집에서 사람을 들일 때 <b>그 자리에서 베껴 둔다</b>. 이름만 들고 있으면 나중에
    /// 인물정보를 낼 때마다 게임 세이브(SAVEDATA.CDS)를 다시 뒤져야 하는데, 그 파일은
    /// 우리 것이 아니라 이름이 바뀌거나 없어질 수 있다 — 그러면 제 부하를 두고도
    /// "자료를 찾지 못했다" 가 뜬다.
    /// </remarks>
    /// <param name="Sword">검술(0~3). 일기토에서 부관을 내보낼 때 이것을 본다.
    /// 예전 갈무리에는 없던 칸이라 없으면 0 이다.</param>
    public readonly record struct MateInfo(string Name, int Face, int Fame, int Age,
                                           int Body, int Mind, int Might, int Charm, int Luck,
                                           int Sword = 0);

    private readonly Dictionary<string, MateInfo> _mateBook = [];

    /// <summary>적어 둔 부하 자료. 세이브에 그대로 적힌다.</summary>
    public IReadOnlyCollection<MateInfo> MateBook => _mateBook.Values;

    /// <summary>부하 자료를 적어 둔다. 같은 이름이면 새것으로 갈아 낸다.</summary>
    public void RememberMate(MateInfo who)
    {
        if (!string.IsNullOrEmpty(who.Name)) _mateBook[who.Name] = who;
    }

    /// <summary>그 이름으로 적어 둔 자료. 없으면 null.</summary>
    public MateInfo? MateInfoOf(string name) =>
        _mateBook.TryGetValue(name ?? "", out var who) ? who : null;

    /// <summary>세이브에서 부하 자료를 되돌린다.</summary>
    public void RestoreMateBook(IEnumerable<MateInfo>? book)
    {
        _mateBook.Clear();
        if (book == null) return;
        foreach (var who in book) RememberMate(who);
    }

    /// <summary>
    /// 부관이 다친다 — 일기토를 대신 치른 뒤에 부른다.
    /// </summary>
    /// <remarks>게임도 부관을 내보내면 그 사람의 체력에서 깎는다(<c>0x004AA5F8</c>).</remarks>
    public void HurtMate(string name, int amount)
    {
        if (amount <= 0 || !_mateBook.TryGetValue(name ?? "", out var who)) return;
        _mateBook[who.Name] = who with { Body = Math.Max(1, who.Body - amount) };
    }

    /// <summary>두 자리를 맞바꾼다. 빈 자리와도 바꿀 수 있다.</summary>
    public void SwapMates(int a, int b)
    {
        if (a == b || a < 0 || b < 0 || a >= _mates.Length || b >= _mates.Length) return;
        (_mates[a], _mates[b]) = (_mates[b], _mates[a]);
    }

    private readonly List<int> _items = [];

    /// <summary>
    /// 소지품 — 산 것·주운 것이 든 차례대로다. 값은 아이템 번호(<c>item.json</c> 의 id)다.
    /// </summary>
    /// <remarks>
    /// 게임은 플레이어 객체 <c>+0x118</c> 에 <b>열여섯 칸</b>을 두고 빈 칸은 -1 로 둔다
    /// (읽기 <c>0x0047CDD0</c> · 쓰기 <c>0x0047CDB0</c>). 꽉 차면 "이 이상 가질 수
    /// 없습니다!"(<c>0x00544830</c>) 로 물린다.
    ///
    /// 같은 것을 여럿 들 수 있게 두었다. 게임 소지품 일람에도 같은 이름이 두 줄 나온다.
    /// </remarks>
    public IReadOnlyList<int> Items => _items;

    /// <summary>소지품 칸 수. 게임도 열여섯이다.</summary>
    public const int MaxItems = 16;

    /// <summary>소지품이 꽉 찼는지.</summary>
    public bool IsBagFull => _items.Count >= MaxItems;

    /// <summary>그 아이템을 지녔는지.</summary>
    public bool HasItem(int itemId) => _items.Contains(itemId);

    /// <summary>소지품에 넣는다. 칸이 꽉 찼으면 아무것도 하지 않고 false.</summary>
    public bool Take(int itemId)
    {
        if (itemId < 0 || IsBagFull) return false;
        _items.Add(itemId);
        return true;
    }

    /// <summary>소지품에서 하나 뺀다. 없었으면 false.</summary>
    public bool Drop(int itemId) => _items.Remove(itemId);

    private readonly List<int> _stored = [];

    /// <summary>
    /// 자택에 보관해 둔 것. 소지품과 같은 아이템 번호다.
    /// </summary>
    /// <remarks>
    /// 게임은 소지품 열여섯 칸 바로 뒤(<c>+0x158</c>)에 <b>아흔아홉 칸</b>을 둔다
    /// (읽기 <c>0x0047CE70</c> · 쓰기 <c>0x0047CE50</c>). 자택의 "보관" 이 두 칸을 주고받는다.
    /// </remarks>
    public IReadOnlyList<int> Stored => _stored;

    /// <summary>보관 칸 수. 게임도 아흔아홉이다.</summary>
    public const int MaxStored = 99;

    /// <summary>보관 칸이 꽉 찼는지.</summary>
    public bool IsStoreFull => _stored.Count >= MaxStored;

    /// <summary>소지품 한 칸을 자택에 맡긴다. 자리가 없거나 칸이 없으면 false.</summary>
    public bool Store(int index)
    {
        if (index < 0 || index >= _items.Count || IsStoreFull) return false;
        _stored.Add(_items[index]);
        _items.RemoveAt(index);
        return true;
    }

    /// <summary>보관해 둔 한 칸을 도로 든다. 소지품이 꽉 찼으면 false.</summary>
    public bool Fetch(int index)
    {
        if (index < 0 || index >= _stored.Count || IsBagFull) return false;
        _items.Add(_stored[index]);
        _stored.RemoveAt(index);
        return true;
    }

    /// <summary>
    /// 소지품과 보관 칸을 통째로 갈아 끼운다. 자택 <b>아이템 교환</b> 창이 다 마치고
    /// 한 번 쓴다.
    /// </summary>
    /// <remarks>
    /// 게임은 두 칸을 열여섯·아흔아홉 자리 배열로 들고 빈 자리를 -1 로 남긴다. 교환 창도
    /// 그 배열을 그대로 주무르다가 끝날 때 되쓴다(<c>0x0047CDB0</c> · <c>0x0047CE50</c>).
    /// 우리 쪽은 빈 자리를 안 들고 다니므로 <b>여기서 추려</b> 넣는다.
    /// </remarks>
    public void ReplaceBelongings(IEnumerable<int> items, IEnumerable<int> stored)
    {
        _items.Clear();
        foreach (int id in items)
            if (id >= 0 && _items.Count < MaxItems) _items.Add(id);

        _stored.Clear();
        foreach (int id in stored)
            if (id >= 0 && _stored.Count < MaxStored) _stored.Add(id);
    }

    /// <summary>그 값을 치를 수 있는지.</summary>
    public bool CanAfford(int price) => Gold >= price;

    /// <summary>
    /// 소지금과 저금의 위쪽 끝. 게임도 백만 닢에서 자른다.
    /// </summary>
    /// <remarks>
    /// 돈을 더하는 자리(<c>0x0047CBC0</c> 소지금 · <c>0x0047CC00</c> 저금)가 둘 다
    /// <c>0x0049E5A0(지금, 더할값, 0, 0xF4240)</c> 으로 0~1000000 사이에 가둔다.
    /// 넘치면 "금화는 더 이상 늘릴 수 없습니다"(<c>0x0055A488</c>) 가 뜬다.
    /// </remarks>
    public const int MaxGold = 1_000_000;

    /// <summary>
    /// 돈을 받는다(매각·사례). 소지금은 <see cref="MaxGold"/> 에서 잘린다.
    /// </summary>
    public void Earn(int amount)
    {
        if (amount <= 0) return;
        Gold = (int)Math.Min(MaxGold, (long)Gold + amount);
    }

    /// <summary>
    /// 자택에 맡겨 둔 돈. 소지금과 따로 두며 백만 닢까지 담긴다.
    /// </summary>
    /// <remarks>게임은 플레이어 객체의 소지금(<c>+0xF4</c>) 옆에 나란히 둔다.</remarks>
    public int Savings { get; private set; }

    /// <summary>
    /// 그만큼 저금한다. 소지금이 모자라거나 저금 칸이 다 찼으면 할 수 있는 만큼만 한다.
    /// </summary>
    /// <returns>실제로 맡긴 돈.</returns>
    public int Deposit(int amount)
    {
        int moved = Math.Min(Math.Max(0, amount), Math.Min(Gold, MaxGold - Savings));
        Gold -= moved;
        Savings += moved;
        return moved;
    }

    /// <summary>
    /// 저금에서 그만큼 꺼낸다. 저금이 모자라거나 소지금이 다 찼으면 되는 만큼만 꺼낸다.
    /// </summary>
    /// <returns>실제로 꺼낸 돈.</returns>
    public int Withdraw(int amount)
    {
        int moved = Math.Min(Math.Max(0, amount), Math.Min(Savings, MaxGold - Gold));
        Savings -= moved;
        Gold += moved;
        return moved;
    }

    /// <summary>
    /// 있는 만큼만 치른다 — 모자라도 물리지 않고 0 까지만 깎인다.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x0047CBC0</c> 이 이렇게 한다 — 소지금을 <c>0x0049E5A0</c> 으로
    /// <c>0 ~ 1,000,000</c> 사이에 가둔다. 뭍을 걷는 하루 여행비가 이 길로 나간다.
    /// 물건 값처럼 <b>못 사면 안 사야 하는</b> 자리는 <see cref="Pay"/> 를 쓴다.
    /// </remarks>
    /// <returns>실제로 나간 돈.</returns>
    public int Spend(int amount)
    {
        int paid = Math.Clamp(amount, 0, Gold);
        Gold -= paid;
        return paid;
    }

    /// <summary>값을 치른다. 모자라면 아무것도 하지 않고 false.</summary>
    public bool Pay(int amount)
    {
        if (amount < 0 || !CanAfford(amount)) return false;
        Gold -= amount;
        return true;
    }

    /// <summary>
    /// 달을 넘긴다(여관 숙박·수련). 날짜는 놀이 안에서만 흐르므로 밖에서 박지 않는다.
    /// </summary>
    public void AdvanceMonths(int months)
    {
        if (months <= 0) return;
        Date = Date.AddMonths(months);
        Recover(months * DaysPerMonth);
    }

    /// <summary>게임이 달을 날로 셀 때 쓰는 날수. 달력 달이 아니라 서른 날이다.</summary>
    public const int DaysPerMonth = 30;

    /// <summary>
    /// 날을 넘긴다(자택 휴양). 게임은 달을 셀 때도 <b>서른 날</b>로 세므로 이쪽을 쓴다.
    /// </summary>
    /// <remarks>
    /// 휴양은 <c>0x004A2AD0(개월 x 30, 1)</c> 로 날수를 넘긴다 — 달력 달이 아니라 30일이다.
    /// </remarks>
    public void AdvanceDays(int days)
    {
        if (days <= 0) return;
        Date = Date.AddDays(days);
        Recover(days);
    }

    /// <summary>
    /// 마을에서 날을 넘긴 값을 몸에 먹인다 — <b>하루에 피로 -1, 사기 +3</b>.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x004A2AD0(날수, 모드)</c> 가 하는 일이다. 휴양만이 아니라 숙박·수련처럼
    /// <b>마을에서 날이 가는 모든 자리</b>가 이 하나를 거친다.
    /// <code>
    /// 4a2b06  0x474030(함대, -날수)      ; 피로도 -= 날수     (+0x28)
    /// 4a2b14  0x474060(함대, 날수 * 3)   ; 사기   += 날수 x 3 (+0x2C)
    /// 4a2b1f  0x0044AFD0(달력, 날수)     ; 날짜를 넘긴다
    /// </code>
    /// 그래서 한 달 휴양이면 피로가 서른, 사기가 아흔 움직인다 — 한 번에 다 푼다.
    /// <b>바다에서는 이 길을 안 거친다</b>(<see cref="PassDayAtSea"/>) — 항해 중에는
    /// 지치기만 하고 안 풀린다.
    /// </remarks>
    private void Recover(int days)
    {
        Tire(-days);
        Cheer(days * MoralePerRestDay);
    }

    /// <summary>마을에서 하루를 나면 오르는 사기(<c>lea (%esi,%esi,2),%ecx</c>).</summary>
    public const int MoralePerRestDay = 3;

    // ── 피로도와 항해일 ───────────────────────────────────────────────────────

    /// <summary>피로도가 더 못 올라가는 자리.</summary>
    /// <remarks>
    /// 게임은 함대 객체 <c>+0x28</c> 에 0~100 으로 든다(<c>0x00474030</c> 이 그 폭으로 자른다).
    /// </remarks>
    public const int MaxFatigue = 100;

    /// <summary>선원들이 지친 만큼(0~<see cref="MaxFatigue"/>).</summary>
    /// <remarks>
    /// 폭풍을 맞으면 20~30 오르고 자택에서 휴양하면 도로 0 이 된다. 게임은 이 값이
    /// 80 을 넘으면 반란을 굴린다(<c>0x00474B5B</c> 의 <c>cmpl $0x50</c>) — 그쪽은
    /// 아직 안 옮겼다.
    /// </remarks>
    public int Fatigue { get; private set; }

    /// <summary>그만큼 지친다.</summary>
    public void Tire(int amount) => Fatigue = Math.Clamp(Fatigue + amount, 0, MaxFatigue);

    /// <summary>피로도를 그대로 박는다. 세이브를 되돌릴 때와 개발용 창에서 쓴다.</summary>
    public void SetFatigue(int fatigue) => Fatigue = Math.Clamp(fatigue, 0, MaxFatigue);

    /// <summary>사기가 더 못 올라가는 자리.</summary>
    public const int MaxMorale = 100;

    /// <summary>선원들의 사기(0~<see cref="MaxMorale"/>). 꽉 찬 채로 시작한다.</summary>
    /// <remarks>
    /// 게임은 함대 객체 <c>+0x2C</c> 에 든다(<c>0x00474060</c> 이 0~100 으로 자른다).
    /// 폭풍이 10~20 깎고(<c>0x00474D2D</c>) 반란을 눌러 앉히면 30 오른다
    /// (<c>0x004753EA</c>). 반란 대표와 이야기하는 자리도 이 값이 50 을 넘는지로 갈린다
    /// (<c>0x004754FB</c>) — 그쪽은 아직 안 옮겼다.
    /// </remarks>
    public int Morale { get; private set; } = MaxMorale;

    /// <summary>사기를 그만큼 올린다(음수면 깎는다).</summary>
    public void Cheer(int amount) => Morale = Math.Clamp(Morale + amount, 0, MaxMorale);

    /// <summary>사기를 그대로 박는다. 세이브를 되돌릴 때와 개발용 창에서 쓴다.</summary>
    public void SetMorale(int morale) => Morale = Math.Clamp(morale, 0, MaxMorale);

    /// <summary>마을을 떠난 뒤로 바다에서 지낸 날수.</summary>
    /// <remarks>
    /// 게임의 <c>0x005A4D40</c> 자리다 — 사건은 이 값이 <b>열을 넘어야</b> 굴러가고
    /// (<c>0x00474680</c>), 반란은 이레마다 본다(<c>0x00474B4F</c> 의 <c>idiv 7</c>).
    /// 마을에 들어가면 0 으로 돌아간다.
    /// </remarks>
    public int DaysAtSea { get; private set; }

    /// <summary>
    /// 밝힌 바다. 항해지도가 이것으로 그려진다 — 안 밝힌 곳은 양피지로 남는다.
    /// </summary>
    /// <remarks>배가 지나며 저절로 칠해진다(<see cref="ExploredMap.Mark"/>).</remarks>
    public ExploredMap Explored { get; } = new();

    /// <summary>
    /// 아내 이름. 없으면 빈 문자열이다.
    /// </summary>
    /// <remarks>
    /// 게임은 <c>0x005B61B0</c> 에 아내 번호를 들고 <b>-1 이면 없는 것</b>으로 본다
    /// (<c>0x00460650</c> 이 그 값 하나로 "후손을 남긴다" 줄의 켜짐을 정한다).
    /// 우리는 아직 사람 표를 안 들고 있어 이름만 든다.
    /// </remarks>
    public string Spouse { get; private set; } = "";

    /// <summary>얻은 후손들. 차례가 곧 태어난 차례다.</summary>
    public IReadOnlyList<string> Heirs => _heirs;

    private readonly List<string> _heirs = [];

    /// <summary>
    /// 맺어진 여급의 번호. 없으면 -1.
    /// </summary>
    /// <remarks>
    /// 게임 세이브는 배우자 자리(오프셋 173, 2바이트)에 <c>0x2000 | 여급번호</c> 를 적고
    /// 빈 자리는 <c>0xFFFF</c> 다. 우리는 번호만 든다.
    /// </remarks>
    public int SpouseId { get; private set; } = -1;

    /// <summary>여급마다의 친밀도(0~100). 말을 걸어야 생긴다.</summary>
    public IReadOnlyDictionary<int, int> Liking => _liking;

    private readonly Dictionary<int, int> _liking = [];

    /// <summary>그 여급과의 친밀도. 아직 말을 안 걸었으면 0.</summary>
    public int LikingOf(int barmaid) => _liking.GetValueOrDefault(barmaid);

    /// <summary>친밀도를 올리고 내린다. 0~100 을 벗어나지 않는다.</summary>
    /// <returns>더한 뒤의 값.</returns>
    public int AddLiking(int barmaid, int amount)
    {
        int now = Math.Clamp(LikingOf(barmaid) + amount, 0, MaxLiking);
        _liking[barmaid] = now;
        return now;
    }

    /// <summary>친밀도의 위. 게임도 0~100 으로 자른다.</summary>
    public const int MaxLiking = 100;

    /// <summary>아내를 맞는다. 빈 이름을 주면 홀로 돌아간다.</summary>
    public void Marry(string? name, int barmaid = -1)
    {
        Spouse = (name ?? "").Trim();
        SpouseId = Spouse.Length == 0 ? -1 : barmaid;
    }

    /// <summary>후손을 하나 얻는다.</summary>
    public void AddHeir(string name)
    {
        string given = (name ?? "").Trim();
        if (given.Length > 0) _heirs.Add(given);
    }

    /// <summary>적어 둔 것을 되돌린다.</summary>
    public void RestoreFamily(string? spouse, IEnumerable<string>? heirs, int spouseId = -1,
                              IReadOnlyDictionary<int, int>? liking = null)
    {
        Spouse = (spouse ?? "").Trim();
        SpouseId = Spouse.Length == 0 ? -1 : spouseId;
        _heirs.Clear();
        foreach (string h in heirs ?? []) AddHeir(h);

        _liking.Clear();
        foreach (var (id, value) in liking ?? new Dictionary<int, int>())
            _liking[id] = Math.Clamp(value, 0, MaxLiking);
    }

    /// <summary>바다에서 하루를 넘긴다.</summary>
    public void PassDayAtSea()
    {
        DaysAtSea++;
        Date = Date.AddDays(1);
    }

    /// <summary>항해일을 그대로 박는다. 세이브를 되돌릴 때 쓴다.</summary>
    public void SetDaysAtSea(int days) => DaysAtSea = Math.Max(0, days);

    /// <summary>
    /// 아이템을 산다. 값을 치르고 소지품에 넣는다. 돈이 모자라면 아무것도 하지 않는다.
    /// </summary>
    /// <remarks>
    /// 게임도 돈 검사를 <b>YES 를 고른 뒤</b>에 한다(구입 본체 0x004B3AAD 에서 소지금과
    /// 값을 견준다). 그래서 여기서도 물어보는 것과 치르는 것을 갈라 두었다 —
    /// 부르는 쪽이 물음창을 먼저 띄우고 이것을 마지막에 부른다.
    /// </remarks>
    public PurchaseResult BuyItem(int itemId, int price)
    {
        if (itemId < 0) return PurchaseResult.NotEnoughGold;
        if (IsBagFull) return PurchaseResult.BagFull;
        if (!CanAfford(price)) return PurchaseResult.NotEnoughGold;

        Gold -= price;
        Take(itemId);
        return PurchaseResult.Ok;
    }

    private readonly Dictionary<int, int> _hostility = [];
    private readonly HashSet<int> _openedGates = [];
    private readonly HashSet<int> _talksLost = [];

    /// <summary>
    /// 나라마다의 <b>적대도</b>. 0 이면 여느 나라다.
    /// </summary>
    /// <remarks>
    /// 게임은 나라마다 열여섯 바이트짜리 형편 레코드를 <c>0x005859C0</c> 에 두고 적대도를
    /// <c>+0x0C</c> 에 적는다(<c>0x00429D90</c>). 그 판은 <c>.bss</c> 라 켤 때는 죄다 0 이고
    /// 세이브에 통째로 실린다(<c>0x0047858E</c>, 일흔여덟 칸).
    ///
    /// <b>무엇이 처음 적대도를 올리는지는 아직 못 짚었다</b> — 우리는 마을을 치거나
    /// 숨어들다 잡혔을 때 올린다.
    /// </remarks>
    public IReadOnlyDictionary<int, int> Hostility => _hostility;

    /// <summary>그 나라의 적대도. 모르는 나라면 0.</summary>
    public int HostilityOf(int nation) => _hostility.GetValueOrDefault(nation);

    /// <summary>그 나라를 성나게 한다.</summary>
    public void Anger(int nation, int by = 1)
    {
        if (nation < 0 || by <= 0) return;
        _hostility[nation] = HostilityOf(nation) + by;
    }

    /// <summary>그 나라를 달랜다.</summary>
    public void Calm(int nation) => _hostility.Remove(nation);

    /// <summary>
    /// 적대 도시 가운데 <b>문이 열린</b> 곳. 공략·잠입·교섭에 성공하면 는다.
    /// </summary>
    /// <remarks>
    /// 게임의 「제독, 이것으로 마을에 들어갈 수 있습니다」(<c>0x00551C28</c>) 다. 한 번
    /// 열리면 그 도시는 적대도와 상관없이 그냥 들어간다.
    /// </remarks>
    public IReadOnlyCollection<int> OpenedGates => _openedGates;

    /// <summary>그 적대 도시의 문이 이미 열렸는지.</summary>
    public bool IsGateOpen(int city) => _openedGates.Contains(city);

    /// <summary>적대 도시의 문을 연다. 처음 여는 것이면 true.</summary>
    public bool OpenGate(int city) => city >= 0 && _openedGates.Add(city);

    /// <summary>
    /// 교섭이 어그러진 자리. <b>마을 쪽과 항구 쪽을 따로</b> 센다.
    /// </summary>
    /// <remarks>
    /// 게임도 도시 레코드에 <c>+0xB0</c>(항구)와 <c>+0xB4</c>(마을) 두 자리를 둔다
    /// (<c>0x004A56A7</c>). 한 번 어그러지면 그 자리에서는 <b>「떠난다」가 꺼져</b>
    /// 물러설 수 없다.
    /// </remarks>
    public bool TalkLostAt(int city, bool byLand) => _talksLost.Contains(GateKey(city, byLand));

    /// <summary>교섭이 어그러졌다고 적어 둔다.</summary>
    public void MarkTalkLost(int city, bool byLand) => _talksLost.Add(GateKey(city, byLand));

    /// <summary>적어 둔 자리 — 마을 쪽과 항구 쪽이 다른 칸이다.</summary>
    private static int GateKey(int city, bool byLand) => city * 2 + (byLand ? 0 : 1);

    /// <summary>적어 둔 형편을 통째로 되돌린다(세이브를 읽을 때).</summary>
    public void RestoreStandings(IEnumerable<KeyValuePair<int, int>>? hostility,
                                 IEnumerable<int>? openedGates,
                                 IEnumerable<int>? talksLost)
    {
        _hostility.Clear();
        foreach (var (nation, level) in hostility ?? []) _hostility[nation] = level;

        _openedGates.Clear();
        foreach (int city in openedGates ?? []) _openedGates.Add(city);

        _talksLost.Clear();
        foreach (int key in talksLost ?? []) _talksLost.Add(key);
    }

    /// <summary>세이브에 적을 교섭 실패 자리(칸 번호 그대로).</summary>
    public IReadOnlyCollection<int> TalksLost => _talksLost;

    /// <summary>그 힌트를 이미 얻었는지.</summary>
    public bool HasHint(int hint) => _hints.Contains(hint);

    /// <summary>힌트를 얻는다. 처음 얻는 것이면 true.</summary>
    public bool GainHint(int hint) => hint >= 0 && _hints.Add(hint);

    private readonly HashSet<int> _found = [];

    /// <summary>
    /// 발견한 발견물 번호. 게임 발견물 표(274줄)의 줄 번호다.
    /// </summary>
    /// <remarks>
    /// 게임은 발견물마다 164바이트 칸을 두고 발견자·발표자 이름과 그 연월까지 적는다.
    /// 여기서는 주인공이 하나뿐이라 번호만 든다 — 발표(왕궁 보고)는 아직 흉내내지 않는다.
    /// </remarks>
    public IReadOnlyCollection<int> Discoveries => _found;

    /// <summary>그것을 이미 발견했는지.</summary>
    public bool HasFound(int discovery) => _found.Contains(discovery);

    /// <summary>발견한 것으로 적는다. 처음 발견하는 것이면 true.</summary>
    /// <remarks>
    /// 계약 중이면 그 계약에도 얹는다 — 계약 정보 창의 "발견물" 칸이 그것이다.
    /// </remarks>
    public bool Discover(int discovery)
    {
        if (discovery < 0 || !_found.Add(discovery)) return false;
        Contract?.Add(discovery);
        return true;
    }

    /// <summary>
    /// 지금 맺고 있는 계약. 없으면 null — 도시 커맨드의 "계약 정보" 가 이것을 낸다.
    /// </summary>
    /// <remarks>게임도 계약을 하나만 든다(<c>0x0061D1D0</c>).</remarks>
    public Contract? Contract { get; private set; }

    /// <summary>
    /// 계약을 맺는다. 선금은 그 자리에서 받는다 — 게임도 그렇다(<c>0x004ADF3E</c>).
    /// 이미 맺은 것이 있으면 갈아 끼운다(게임도 계약을 하나만 든다).
    /// </summary>
    public void Sign(Contract contract)
    {
        Contract = contract;
        Earn(contract.Advance);
    }

    /// <summary>계약을 지운다(기한 넘김·파기). 돈은 건드리지 않는다.</summary>
    public void EndContract() => Contract = null;

    private readonly HashSet<int> _announced = [];

    /// <summary>발표한 발견물 번호.</summary>
    /// <remarks>
    /// 게임은 발견물 인스턴스의 깃발 <c>0x80</c> 으로 든다. 발표하면 명성이 오르고, 한 번
    /// 발표한 것은 다시 못 한다.
    /// </remarks>
    public IReadOnlyCollection<int> Announced => _announced;

    /// <summary>그것을 이미 발표했는지.</summary>
    public bool HasAnnounced(int discovery) => _announced.Contains(discovery);

    /// <summary>발표한 것으로 적는다. 발견한 적 없거나 이미 발표했으면 false.</summary>
    public bool Announce(int discovery) =>
        HasFound(discovery) && _announced.Add(discovery);

    /// <summary>세이브를 되돌릴 때 계약을 그대로 박는다. 선금을 다시 주지 않는다.</summary>
    public void RestoreContract(Contract? contract) => Contract = contract;

    /// <summary>
    /// 적어 둔 것을 되돌린다(불러오기). 배는 부르는 쪽이 그 도시 앞바다에 갖다 놓는다.
    /// </summary>
    public void Restore(int gold, DateTime date, int cityId, string cityName,
                        IEnumerable<KeyValuePair<string, int>> skills,
                        IEnumerable<int>? hints = null,
                        IEnumerable<string>? mates = null,
                        IEnumerable<string>? met = null,
                        IEnumerable<int>? items = null,
                        IEnumerable<int>? supplies = null,
                        IEnumerable<int>? discoveries = null,
                        int? crew = null,
                        IEnumerable<int>? announced = null,
                        IEnumerable<int>? stored = null,
                        int? savings = null,
                        bool supplyInBarrels = false)
    {
        Gold = gold;
        Date = date;
        EnterCity(cityId, cityName);
        _skills.Clear();
        foreach (var (name, level) in skills) _skills[name] = Math.Clamp(level, 0, Skill.MaxLevel);
        _hints.Clear();
        if (hints != null) foreach (int hint in hints) _hints.Add(hint);
        // 보급은 자리째로 되돌린다. 옛 세이브에는 없으므로 그때는 빈 채로 둔다.
        // 판 16 앞의 세이브는 식량·물도 <b>통</b>으로 적혀 있어 열 배로 펴 준다.
        Array.Clear(_supplies);
        if (supplies != null)
        {
            int slot = 0;
            foreach (int value in supplies)
            {
                if (slot >= _supplies.Length) break;
                var kind = (SupplyKind)slot;
                _supplies[slot++] = Math.Max(0,
                    supplyInBarrels && Supply.Of(kind).IsDaily
                        ? Supply.UnitsOf(value) : value);
            }
        }
        // 자리째로 되돌린다 — 빈 자리가 섞여 있어도 차례가 어긋나지 않게.
        for (int i = 0; i < _mates.Length; i++) _mates[i] = "";
        if (mates != null)
        {
            int at = 0;
            foreach (var name in mates)
            {
                if (at >= _mates.Length) break;
                _mates[at++] = name ?? "";
            }
        }
        _met.Clear();
        if (met != null) foreach (var name in met) _met.Add(name);
        _items.Clear();
        if (items != null) foreach (int id in items) Take(id);
        _found.Clear();
        if (discoveries != null) foreach (int id in discoveries) _found.Add(id);
        _announced.Clear();
        if (announced != null) foreach (int id in announced) _announced.Add(id);
        _stored.Clear();
        if (stored != null) foreach (int id in stored) _stored.Add(id);
        Savings = Math.Clamp(savings ?? 0, 0, MaxGold);
        // 선원을 안 적어 둔 옛 세이브는 최저 승원으로 채운다 — 그 전까지 쓰던 값이 그것이다.
        SetCrew(crew ?? MinCrew);
    }

    /// <summary>도시에 들어가거나(이름과 함께) 바다로 나온다(-1).</summary>
    public void EnterCity(int cityId, string cityName = "")
    {
        CityId = cityId;
        CityName = cityId >= 0 ? cityName : "";
        // 마을에 들면 항해일이 끊긴다 — 게임도 입항하면 0x5A4D40 을 도로 0 으로 둔다.
        if (cityId >= 0) DaysAtSea = 0;
    }

    /// <summary>소지금(닢).</summary>
    public int Gold { get; private set; }

    /// <summary>
    /// 소지금을 그대로 박는다. 놀이 안에서 쓰는 길은 아니고 개발용 창에서만 부른다 —
    /// 돈이 도는 길(교역)을 아직 흉내내지 않아 시험하려면 넣어 줄 데가 있어야 한다.
    /// </summary>
    public void SetGold(int gold) => Gold = Math.Clamp(gold, 0, MaxGold);

    /// <summary>
    /// 아직 안 쓴 배 이름 하나. 함대에 있는 것도 맡겨 둔 것도 다 피한다.
    /// </summary>
    public string SuggestShipName() =>
        ShipNames.Suggest(_ships.Select(s => s.Name)
                                .Concat(_docked.Values.SelectMany(l => l).Select(s => s.Name)));

    /// <summary>가지고 있는 배. 산 차례대로다.</summary>
    public IReadOnlyList<Ship> Ships => _ships;

    /// <summary>
    /// 기함이 함대에서 몇째 자리인지. 배가 없으면 -1 이다.
    /// </summary>
    /// <remarks>
    /// 항구 함대편성의 "기함 변경" 으로 바꾼다. 게임은 배가 <b>두 척 이상</b>일 때만 그 줄을
    /// 켠다(<c>0x0046A220</c>) — 한 척뿐이면 바꿀 것이 없다.
    /// </remarks>
    public int Flagship { get; private set; }

    /// <summary>기함. 배가 없으면 null.</summary>
    public Ship? FlagshipHull =>
        Flagship >= 0 && Flagship < _ships.Count ? _ships[Flagship] : _ships.FirstOrDefault();

    /// <summary>
    /// 배와 선원을 다 걷는다 — <b>새 주인공은 배도 선원도 없이 시작한다</b>.
    /// </summary>
    /// <remarks>
    /// 게임도 새 놀이를 열면 함대가 비어 있고, 조선소에서 첫 배를 사야 바다에 나간다.
    /// 생성자가 카라벨 한 척을 얹어 두는 것은 세이브를 안 읽고 함대 창만 여는 길
    /// (도구 쪽) 때문이라 그대로 두고, 새 놀이만 여기서 걷는다.
    /// </remarks>
    public void ClearShips()
    {
        _ships.Clear();
        Flagship = 0;
        // 배가 없으면 정원이 0 이라 선원도 0 이다. 배를 사도 선원은 안 붙는다 —
        // 게임도 항구에서 고용해야 는다(<see cref="Buy"/> 가 선원을 안 건드린다).
        Crew = 0;
    }

    /// <summary>기함을 그 자리의 배로 바꾼다. 자리가 이상하면 false.</summary>
    public bool SetFlagship(int index)
    {
        if (index < 0 || index >= _ships.Count) return false;
        Flagship = index;
        return true;
    }

    private readonly Dictionary<int, List<Ship>> _docked = [];

    /// <summary>
    /// 그 마을에 맡겨 둔 배. 함대에서 <b>삭제</b>하면 여기로 오고, <b>편입</b>하면 도로 나간다.
    /// </summary>
    /// <remarks>
    /// 게임도 마을마다 배를 맡아 둔다 — 편입·삭제가 그 마을의 수를 세어 줄을 켠다
    /// (<c>0x0040E280(도시, 0)</c>).
    /// </remarks>
    public IReadOnlyList<Ship> DockedAt(int cityId) =>
        _docked.TryGetValue(cityId, out var list) ? list : [];

    /// <summary>한 마을에 맡길 수 있는 배의 수. 게임도 여덟이다.</summary>
    public const int MaxDocked = 8;

    /// <summary>
    /// 함대의 배를 그 마을에 맡긴다(선박 삭제). 기함만 남는 상태로는 못 만든다.
    /// </summary>
    public bool Dock(int index, int cityId)
    {
        if (_ships.Count <= 1 || index < 0 || index >= _ships.Count) return false;
        if (DockedAt(cityId).Count >= MaxDocked) return false;

        if (!_docked.TryGetValue(cityId, out var list)) _docked[cityId] = list = [];
        list.Add(_ships[index]);
        RemoveShip(index);
        return true;
    }

    /// <summary>맡겨 둔 배를 함대에 넣는다(선박 편입).</summary>
    public bool Undock(int cityId, int index)
    {
        if (IsFleetFull) return false;
        if (!_docked.TryGetValue(cityId, out var list)) return false;
        if (index < 0 || index >= list.Count) return false;

        _ships.Add(list[index]);
        list.RemoveAt(index);
        return true;
    }

    /// <summary>배를 없앤다(선박 파기). 마지막 한 척은 못 없앤다.</summary>
    public bool Scrap(int index)
    {
        if (_ships.Count <= 1 || index < 0 || index >= _ships.Count) return false;
        RemoveShip(index);
        return true;
    }

    /// <summary>
    /// 적어 둔 함대를 되돌린다(불러오기). 이름으로 <see cref="Hull.All"/> 에서 찾는다.
    /// </summary>
    /// <remarks>
    /// 배는 선체 다섯 가지 중 하나라 이름만 적어 두면 된다. 모르는 이름은 버린다 —
    /// 선체 표가 갈려도 세이브가 통째로 깨지지는 않게.
    /// </remarks>
    public void RestoreFleet(IEnumerable<string>? ships, int flagship,
                             IEnumerable<KeyValuePair<int, List<string>>>? docked,
                             IReadOnlyList<int>? shipHp = null,
                             IReadOnlyDictionary<int, List<int>>? dockedHp = null,
                             IReadOnlyList<Ship.Stats>? shipStats = null,
                             IReadOnlyDictionary<int, List<Ship.Stats>>? dockedStats = null,
                             IReadOnlyList<string>? shipNames = null,
                             IReadOnlyDictionary<int, List<string>>? dockedNames = null,
                             bool gunsInStats = true,
                             bool sailsInStats = true)
    {
        static Hull? Find(string name) => Hull.All.FirstOrDefault(h => h.Name == name);

        List<Ship> Build(IEnumerable<string> hulls, IReadOnlyList<int>? hps,
                         IReadOnlyList<Ship.Stats>? stats, IReadOnlyList<string>? names)
        {
            var list = new List<Ship>();
            int at = 0;
            foreach (var hull in hulls)
            {
                int? hp = hps != null && at < hps.Count ? hps[at] : null;
                var st = stats != null && at < stats.Count ? stats[at] : null;
                var nm = names != null && at < names.Count ? names[at] : null;
                at++;
                // 판 18·19 앞 세이브에는 포탑·대포·돛 칸이 없다 — 선체 기본값으로 되살린다.
                if (st != null && !gunsInStats)
                    st = st with { Turrets = Find(hull)?.Guns ?? 0, Gun = -1, Guns = 0 };
                if (st != null && !sailsInStats)
                    st = st with { Sails = [Ship.Lateen, Ship.NoSail, Ship.NoSail] };
                if (Find(hull) is { } found) list.Add(new Ship(found, hp, st, nm));
            }
            return list;
        }

        if (ships != null)
        {
            _ships.Clear();
            _ships.AddRange(Build(ships, shipHp, shipStats, shipNames));
            if (_ships.Count == 0) _ships.Add(new Ship(Hull.Cheapest, name: ShipNames.All[0]));
        }
        Flagship = Math.Clamp(flagship, 0, Math.Max(0, _ships.Count - 1));

        _docked.Clear();
        if (docked == null) return;
        foreach (var (city, names) in docked)
        {
            var list = Build(names,
                dockedHp != null && dockedHp.TryGetValue(city, out var h) ? h : null,
                dockedStats != null && dockedStats.TryGetValue(city, out var t) ? t : null,
                dockedNames != null && dockedNames.TryGetValue(city, out var n) ? n : null);
            if (list.Count > 0) _docked[city] = list;
        }
    }

    /// <summary>맡겨 둔 배를 마을별로. 세이브에 적을 때 쓴다.</summary>
    public IReadOnlyDictionary<int, List<Ship>> Docked => _docked;

    /// <summary>배를 폭풍에 놓친다. 마지막 한 척과 기함은 안 없어진다.</summary>
    /// <remarks>게임의 <c>0x00473E60</c> 자리다 — 함대에서 빼기만 하고 마을에 안 맡긴다.</remarks>
    public bool LoseShip(int index)
    {
        if (_ships.Count <= 1 || index < 0 || index >= _ships.Count) return false;
        if (index == Flagship) return false;
        RemoveShip(index);
        SetCrew(Crew);   // 배가 줄면 정원도 줄어 선원이 넘칠 수 있다
        return true;
    }

    /// <summary>함대에서 한 척을 뺀다. 기함 자리가 밀리지 않게 같이 손본다.</summary>
    private void RemoveShip(int index)
    {
        _ships.RemoveAt(index);
        if (Flagship > index) Flagship--;
        else if (Flagship == index) Flagship = 0;
        Flagship = Math.Clamp(Flagship, 0, Math.Max(0, _ships.Count - 1));
    }

    // ── 보급 ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// 실어 둔 보급품. 색인은 <see cref="SupplyKind"/> 고, <b>값은 게임 원값</b>이다 —
    /// 식량·물은 <b>단위</b>, 자재·탄약은 통이다.
    /// </summary>
    /// <remarks>
    /// 게임도 식량·물만 열 배로 들고 화면에 낼 때 <c>(값 + 9) / 10</c> 으로 통을 낸다
    /// (<c>0x0040EA15</c>). 하루 소모가 통보다 잘아서 통으로만 들면 셀 수가 없다.
    /// </remarks>
    private readonly int[] _supplies = new int[Supply.Count];

    /// <summary>그 보급품을 몇 통 실었는지 — <b>화면에 내는 값</b>이다.</summary>
    public int SupplyOf(SupplyKind kind) =>
        Supply.Of(kind).IsDaily ? Supply.BarrelsOf(_supplies[(int)kind]) : _supplies[(int)kind];

    /// <summary>속으로 든 값 그대로. 하루 소모와 세이브가 이것을 쓴다.</summary>
    public int SupplyUnitsOf(SupplyKind kind) => _supplies[(int)kind];

    /// <summary>보급품을 그만큼 싣는다(통, 음수면 던다). 0 밑으로는 안 내려간다.</summary>
    public void AddSupply(SupplyKind kind, int barrels) =>
        AddSupplyUnits(kind, Supply.Of(kind).IsDaily ? barrels * Supply.UnitsPerBarrel : barrels);

    /// <summary>보급품을 원값으로 그만큼 더한다(음수면 던다).</summary>
    public void AddSupplyUnits(SupplyKind kind, int units) =>
        _supplies[(int)kind] = Math.Max(0, _supplies[(int)kind] + units);

    /// <summary>실어 둔 것을 통 수로 박는다.</summary>
    public void SetSupply(SupplyKind kind, int barrels) =>
        SetSupplyUnits(kind, Supply.Of(kind).IsDaily ? Supply.UnitsOf(barrels) : barrels);

    /// <summary>실어 둔 것을 원값으로 박는다. 세이브를 되돌릴 때 쓴다.</summary>
    public void SetSupplyUnits(SupplyKind kind, int units) =>
        _supplies[(int)kind] = Math.Max(0, units);

    /// <summary>실어 둔 보급품을 원값으로 통째로. 세이브에 적을 때 쓴다.</summary>
    public IReadOnlyList<int> Supplies => _supplies;

    /// <summary>함대가 실을 수 있는 통 수(용량). 배마다의 적재량을 더한 것이다.</summary>
    public int Capacity => _ships.Sum(s => s.Capacity);

    /// <summary>함대가 견디는 무게(중량 한도).</summary>
    public int Tonnage => _ships.Sum(s => s.Tonnage);

    /// <summary>
    /// 지금 태우고 있는 선원 수. 식량·물이 며칠 가는지가 이것으로 갈린다.
    /// </summary>
    /// <remarks>
    /// 게임도 배 여덟 칸의 선원수를 더한다(<c>0x004745F0</c>). 항구의 "선원편성" 에서
    /// 모집하고 해고한다 — 배를 사도 선원이 따라오지는 않는다.
    /// </remarks>
    public int Crew { get; private set; }

    /// <summary>
    /// 최저 승원 수 — 배마다의 필요승인을 더한 것. 이보다 적으면 배가 제대로 안 간다.
    /// </summary>
    /// <remarks>
    /// 게임은 배 레코드 <c>+0x30</c> 에 <b>필요승인에서 10을 뺀</b> 값을 담고 읽을 때 도로
    /// 더한다(<c>0x0044C780</c>). 선체 표(<c>0x004FC1E0</c>, 64바이트 x 8)의 <c>+0x34</c> 가
    /// 그 값이다.
    /// </remarks>
    public int MinCrew => _ships.Sum(s => s.Crew);

    /// <summary>
    /// 태울 수 있는 선원 수(정원). 필요승인의 <b>다섯 배</b>다.
    /// </summary>
    /// <remarks>
    /// 게임은 선체 표 <c>+0x34</c> 에 5 를 곱하고 50 을 더한다(<c>0x0044C790</c>).
    /// <c>+0x34</c> 가 필요승인 - 10 이므로 <c>(필요승인-10)*5 + 50 = 필요승인*5</c> 로 같다 —
    /// 카라벨 15명이면 75명, 갤리온 40명이면 200명이다.
    /// </remarks>
    public int MaxCrew => _ships.Sum(s => s.Crew) * 5;

    /// <summary>
    /// 선원을 그만큼 태운다(음수면 내린다). 0 과 정원 사이로 잘린다.
    /// </summary>
    /// <returns>실제로 늘거나 준 수.</returns>
    /// <remarks>게임의 <c>0x0040E3F0</c> 과 같다 — 그쪽도 0~정원으로 자른 뒤 싣는다.</remarks>
    public int AddCrew(int count)
    {
        int before = Crew;
        Crew = Math.Clamp(Crew + count, 0, MaxCrew);
        return Crew - before;
    }

    /// <summary>선원 수를 그대로 박는다. 세이브를 되돌릴 때 쓴다.</summary>
    public void SetCrew(int crew) => Crew = Math.Clamp(crew, 0, MaxCrew);

    /// <summary>식량과 물이 며칠 갈지. 적은 쪽이 정한다.</summary>
    public int SupplyDaysLeft =>
        Supply.DaysLeft(SupplyOf(SupplyKind.Food), SupplyOf(SupplyKind.Water), Crew);

    /// <summary>지금 실은 통 수.</summary>
    public int LoadedBarrels => Supply.All.Sum(s => SupplyOf(s.Kind));

    /// <summary>
    /// 지금 실은 무게 — 보급품과 <b>대포</b>를 센다. 소지품 무게는 아직 안 센다.
    /// </summary>
    public int LoadedWeight =>
        Supply.All.Sum(s => SupplyOf(s.Kind) * s.UnitWeight) + GunWeight;

    /// <summary>함대가 실은 대포의 무게.</summary>
    public int GunWeight => _ships.Sum(s => s.GunWeight);

    /// <summary>함대의 대포 문수.</summary>
    public int Guns => _ships.Sum(s => s.Guns);

    /// <summary>함대의 포탑 수.</summary>
    public int Turrets => _ships.Sum(s => s.Turrets);

    /// <summary>
    /// 바다에서 하루치 식량과 물을 축낸다.
    /// </summary>
    /// <returns>축내기 <b>앞</b>의 (물, 식량) 단위 수. 알림을 가리는 데 쓴다.</returns>
    public (int Water, int Food) UseDailySupply()
    {
        var before = (SupplyUnitsOf(SupplyKind.Water), SupplyUnitsOf(SupplyKind.Food));
        int use = Supply.DailyUse(Crew);
        AddSupplyUnits(SupplyKind.Water, -use);
        AddSupplyUnits(SupplyKind.Food, -use);
        return before;
    }

    /// <summary>배가 꽉 찼는지.</summary>
    public bool IsFleetFull => _ships.Count >= MaxShips;

    /// <summary>그 배를 살 돈이 있는지.</summary>
    public bool CanAfford(Hull hull) => Gold >= hull.Price;

    /// <summary>그 배를 지금 살 수 있는지 — 살 수 없으면 까닭을 낸다.</summary>
    public PurchaseResult CanBuy(Hull hull) =>
        IsFleetFull ? PurchaseResult.FleetFull
      : !CanAfford(hull) ? PurchaseResult.NotEnoughGold
      : PurchaseResult.Ok;

    /// <summary>배를 산다. 살 수 없으면 아무것도 하지 않고 까닭을 낸다.</summary>
    /// <param name="name">
    /// 새 배에 붙일 이름. 안 주면 <see cref="SuggestShipName"/> 이 골라 준다 —
    /// 조선소 창은 「선명입력」 에서 받은 것을 넘긴다.
    /// </param>
    public PurchaseResult Buy(Hull hull, string? name = null)
    {
        var can = CanBuy(hull);
        if (can != PurchaseResult.Ok) return can;

        Gold -= hull.Price;
        _ships.Add(new Ship(hull, name: string.IsNullOrWhiteSpace(name) ? SuggestShipName() : name.Trim()));
        return PurchaseResult.Ok;
    }

    /// <summary>
    /// 값 없이 배를 한 척 받는다 — 스폰서가 계약을 맺으며 대 주는 배다.
    /// </summary>
    /// <remarks>
    /// 게임은 세상 배 표(<c>0x005A4E18</c>)에 이미 있는 배를 골라 <c>+0x64</c> 에 1 을
    /// 박아 「대출」로 표시할 뿐 새 배를 짓지 않는다(<c>0x0040FA00</c>). 우리 쪽에는 그런
    /// 배 무리가 없어 새로 세운다.
    ///
    /// <b>함대에 곧장 들어가지 않는다.</b> 항구에 「대출 · 계류」로 대 놓일 뿐이라, 쓰려면
    /// 함대편성 → 선박 편입을 해야 한다. 그래서 맡긴 배와 같은 자리에 넣는다.
    /// <b>돌려주는 자리는 아직 없다.</b>
    /// </remarks>
    /// <returns>그 마을이 더 못 맡으면 false.</returns>
    public bool Give(Hull hull, int cityId, string? name = null)
    {
        if (DockedAt(cityId).Count >= MaxDocked) return false;

        if (!_docked.TryGetValue(cityId, out var list)) _docked[cityId] = list = [];
        list.Add(new Ship(hull, name: string.IsNullOrWhiteSpace(name) ? SuggestShipName() : name.Trim()));
        return true;
    }

    /// <summary>그 기술의 지금 자리(0~<see cref="Skill.MaxLevel"/>).</summary>
    public int LevelOf(string skill) => _skills.GetValueOrDefault(skill);

    /// <summary>그 기술을 지금 배울 수 있는지 — 없으면 까닭을 낸다.</summary>
    public LearnResult CanLearn(string skill) =>
        LevelOf(skill) >= Skill.MaxLevel ? LearnResult.Mastered
      : Gold < Skill.Price ? LearnResult.NotEnoughGold
      : LearnResult.Ok;

    /// <summary>
    /// 조합에서 한 자리 배운다. 값을 치르고 자리를 올린 뒤, 그 자리에 걸리는 만큼 달이 간다
    /// (0→1 석 달 · →2 여섯 달 · →3 열두 달).
    /// </summary>
    public LearnResult Learn(string skill)
    {
        var can = CanLearn(skill);
        if (can != LearnResult.Ok) return can;

        int next = LevelOf(skill) + 1;
        Gold -= Skill.Price;
        _skills[skill] = next;
        Date = Date.AddMonths(Skill.MonthsFor(next));
        return LearnResult.Ok;
    }
}
