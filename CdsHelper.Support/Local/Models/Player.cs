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

    private readonly List<Hull> _ships = [];

    /// <summary>카라벨 한 척과 시작 소지금으로 시작한다.</summary>
    public Player()
    {
        Gold = StartingGold;
        _ships.Add(Hull.Cheapest);
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
}
