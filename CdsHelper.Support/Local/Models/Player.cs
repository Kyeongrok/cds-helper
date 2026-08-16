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

    /// <summary>놀이가 시작하는 날. 게임 화면에서 본 날짜를 그대로 쓴다.</summary>
    public static readonly DateTime StartDate = new(1499, 4, 15);

    private readonly List<Hull> _ships = [];
    private readonly Dictionary<string, int> _skills = [];

    /// <summary>카라벨 한 척과 시작 소지금으로 시작한다.</summary>
    public Player()
    {
        Gold = StartingGold;
        Date = StartDate;
        _ships.Add(Hull.Cheapest);
    }

    /// <summary>지금 날짜. 기술을 배우면 그만큼 달이 넘어간다.</summary>
    public DateTime Date { get; private set; }

    /// <summary>지금 들어와 있는 도시. 바다에 있으면 -1.</summary>
    public int CityId { get; private set; } = -1;

    /// <summary>지금 들어와 있는 도시 이름. 바다에 있으면 빈 문자열.</summary>
    public string CityName { get; private set; } = "";

    /// <summary>배운 기술과 그 자리.</summary>
    public IReadOnlyDictionary<string, int> Skills => _skills;

    /// <summary>
    /// 적어 둔 것을 되돌린다(불러오기). 배는 부르는 쪽이 그 도시 앞바다에 갖다 놓는다.
    /// </summary>
    public void Restore(int gold, DateTime date, int cityId, string cityName,
                        IEnumerable<KeyValuePair<string, int>> skills)
    {
        Gold = gold;
        Date = date;
        EnterCity(cityId, cityName);
        _skills.Clear();
        foreach (var (name, level) in skills) _skills[name] = Math.Clamp(level, 0, Skill.MaxLevel);
    }

    /// <summary>도시에 들어가거나(이름과 함께) 바다로 나온다(-1).</summary>
    public void EnterCity(int cityId, string cityName = "")
    {
        CityId = cityId;
        CityName = cityId >= 0 ? cityName : "";
    }

    /// <summary>소지금(닢).</summary>
    public int Gold { get; private set; }

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
