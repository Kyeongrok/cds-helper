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
    /// <b>아직 오르지 않는다.</b> 게임은 발견물을 발표하면 명성이 붙는데 그쪽을 흉내내지
    /// 않아서, <see cref="StartingFame"/> 에서 멈춰 있다. 그 값이면 여든한 명 가운데
    /// 열몇은 만나 준다 — 문을 다 닫아 두지도, 다 열어 두지도 않는 자리로 잡았다.
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

    /// <summary>부하로 삼을 수 있는 사람 수. 게임의 부관 자리와 같다.</summary>
    public const int MaxMates = 3;

    private readonly List<string> _mates = [];
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

    /// <summary>술집·여관에서 부하로 삼은 사람. 든 차례대로다.</summary>
    public IReadOnlyList<string> Mates => _mates;

    /// <summary>부하로 삼는다. 처음 드는 사람이고 자리가 남았으면 true.</summary>
    public bool Hire(string name)
    {
        if (string.IsNullOrEmpty(name) || _mates.Count >= MaxMates || _mates.Contains(name))
            return false;
        _mates.Add(name);
        return true;
    }

    private readonly List<int> _items = [];

    /// <summary>
    /// 소지품 — 산 것·주운 것이 든 차례대로다. 값은 아이템 번호(<c>item.json</c> 의 id)다.
    /// </summary>
    /// <remarks>
    /// 게임에는 들 수 있는 개수에 한계가 있다("이 이상 가질 수 없습니다!" · "이대로는 %d개
    /// 들을 수 없습니다"). 그 수가 얼마인지는 아직 안 밝혀서 여기서는 막지 않는다 —
    /// 알아내면 <see cref="Take"/> 에 걸면 된다.
    ///
    /// 같은 것을 여럿 들 수 있게 두었다. 게임 소지품 일람에도 같은 이름이 두 줄 나온다.
    /// </remarks>
    public IReadOnlyList<int> Items => _items;

    /// <summary>그 아이템을 지녔는지.</summary>
    public bool HasItem(int itemId) => _items.Contains(itemId);

    /// <summary>소지품에 넣는다.</summary>
    public void Take(int itemId)
    {
        if (itemId >= 0) _items.Add(itemId);
    }

    /// <summary>소지품에서 하나 뺀다. 없었으면 false.</summary>
    public bool Drop(int itemId) => _items.Remove(itemId);

    /// <summary>그 값을 치를 수 있는지.</summary>
    public bool CanAfford(int price) => Gold >= price;

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
        if (!CanAfford(price)) return PurchaseResult.NotEnoughGold;

        Gold -= price;
        Take(itemId);
        return PurchaseResult.Ok;
    }

    /// <summary>그 힌트를 이미 얻었는지.</summary>
    public bool HasHint(int hint) => _hints.Contains(hint);

    /// <summary>힌트를 얻는다. 처음 얻는 것이면 true.</summary>
    public bool GainHint(int hint) => hint >= 0 && _hints.Add(hint);

    /// <summary>
    /// 적어 둔 것을 되돌린다(불러오기). 배는 부르는 쪽이 그 도시 앞바다에 갖다 놓는다.
    /// </summary>
    public void Restore(int gold, DateTime date, int cityId, string cityName,
                        IEnumerable<KeyValuePair<string, int>> skills,
                        IEnumerable<int>? hints = null,
                        IEnumerable<string>? mates = null,
                        IEnumerable<string>? met = null,
                        IEnumerable<int>? items = null)
    {
        Gold = gold;
        Date = date;
        EnterCity(cityId, cityName);
        _skills.Clear();
        foreach (var (name, level) in skills) _skills[name] = Math.Clamp(level, 0, Skill.MaxLevel);
        _hints.Clear();
        if (hints != null) foreach (int hint in hints) _hints.Add(hint);
        _mates.Clear();
        if (mates != null) foreach (var name in mates) Hire(name);
        _met.Clear();
        if (met != null) foreach (var name in met) _met.Add(name);
        _items.Clear();
        if (items != null) foreach (int id in items) Take(id);
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
    public void SetGold(int gold) => Gold = Math.Max(0, gold);

    /// <summary>가지고 있는 배. 산 차례대로다.</summary>
    public IReadOnlyList<Hull> Ships => _ships;

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
