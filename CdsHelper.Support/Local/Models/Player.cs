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
    /// <summary>가질 수 있는 배의 수. 넘으면 더 못 산다.</summary>
    public const int MaxShips = 10;

    /// <summary>시작 소지금(닢).</summary>
    public const int StartingGold = 1000;

    /// <summary>시작 명성. 이만큼이면 만나 주는 후원자가 제법 있다.</summary>
    public const int StartingFame = 1700;

    /// <summary>놀이가 시작하는 날. 게임 화면에서 본 날짜를 그대로 쓴다.</summary>
    public static readonly DateTime StartDate = new(1499, 4, 15);

    private readonly List<Hull> _ships = [];
    private readonly Dictionary<string, int> _skills = [];
    private readonly HashSet<int> _hints = [];

    /// <summary>카라벨 한 척과 시작 소지금으로 시작한다.</summary>
    public Player()
    {
        Gold = StartingGold;
        Fame = StartingFame;
        Date = StartDate;
        _ships.Add(Hull.Cheapest);
        Crew = MinCrew;   // 배는 최저 승원을 채우고 시작한다
    }

    /// <summary>
    /// 주인공 이름. 사람들이 이 이름으로 부른다("각하, 에르네스토를 데리고 왔습니다").
    /// </summary>
    /// <remarks>
    /// 세이브에서 읽어 오지 않고 여기서 들고 있는다 — 함대 창은 세이브와 따로 굴러가기
    /// 때문이다(<see cref="PlayerData"/> 참고). 게임 화면에서 본 이름을 기본값으로 둔다.
    /// </remarks>
    public string Name { get; set; } = "에르네스토";

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
        if (months > 0) Date = Date.AddMonths(months);
    }

    /// <summary>
    /// 날을 넘긴다(자택 휴양). 게임은 달을 셀 때도 <b>서른 날</b>로 세므로 이쪽을 쓴다.
    /// </summary>
    /// <remarks>
    /// 휴양은 <c>0x004A2AD0(개월 x 30, 1)</c> 로 날수를 넘긴다 — 달력 달이 아니라 30일이다.
    /// </remarks>
    public void AdvanceDays(int days)
    {
        if (days > 0) Date = Date.AddDays(days);
    }

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
                        int? savings = null)
    {
        Gold = gold;
        Date = date;
        EnterCity(cityId, cityName);
        _skills.Clear();
        foreach (var (name, level) in skills) _skills[name] = Math.Clamp(level, 0, Skill.MaxLevel);
        _hints.Clear();
        if (hints != null) foreach (int hint in hints) _hints.Add(hint);
        // 보급은 자리째로 되돌린다. 옛 세이브에는 없으므로 그때는 빈 채로 둔다.
        Array.Clear(_supplies);
        if (supplies != null)
        {
            int slot = 0;
            foreach (int barrels in supplies)
            {
                if (slot >= _supplies.Length) break;
                _supplies[slot++] = Math.Max(0, barrels);
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
    }

    /// <summary>소지금(닢).</summary>
    public int Gold { get; private set; }

    /// <summary>
    /// 소지금을 그대로 박는다. 놀이 안에서 쓰는 길은 아니고 개발용 창에서만 부른다 —
    /// 돈이 도는 길(교역)을 아직 흉내내지 않아 시험하려면 넣어 줄 데가 있어야 한다.
    /// </summary>
    public void SetGold(int gold) => Gold = Math.Clamp(gold, 0, MaxGold);

    /// <summary>가지고 있는 배. 산 차례대로다.</summary>
    public IReadOnlyList<Hull> Ships => _ships;

    // ── 보급 ─────────────────────────────────────────────────────────────────

    /// <summary>실어 둔 보급품(통). 색인은 <see cref="SupplyKind"/> 다.</summary>
    private readonly int[] _supplies = new int[Supply.Count];

    /// <summary>그 보급품을 몇 통 실었는지.</summary>
    public int SupplyOf(SupplyKind kind) => _supplies[(int)kind];

    /// <summary>보급품을 그만큼 싣는다(음수면 던다). 0 밑으로는 안 내려간다.</summary>
    public void AddSupply(SupplyKind kind, int barrels) =>
        _supplies[(int)kind] = Math.Max(0, _supplies[(int)kind] + barrels);

    /// <summary>실어 둔 것을 그대로 박는다. 세이브를 되돌릴 때 쓴다.</summary>
    public void SetSupply(SupplyKind kind, int barrels) =>
        _supplies[(int)kind] = Math.Max(0, barrels);

    /// <summary>실어 둔 보급품을 통째로. 세이브에 적을 때 쓴다.</summary>
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
    public int LoadedBarrels => _supplies.Sum();

    /// <summary>지금 실은 무게. 보급품만 센다 — 소지품 무게는 아직 안 센다.</summary>
    public int LoadedWeight =>
        Supply.All.Sum(s => _supplies[(int)s.Kind] * s.UnitWeight);

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
    public PurchaseResult Buy(Hull hull)
    {
        var can = CanBuy(hull);
        if (can != PurchaseResult.Ok) return can;

        Gold -= hull.Price;
        _ships.Add(hull);
        return PurchaseResult.Ok;
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
