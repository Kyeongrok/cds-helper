namespace CdsHelper.Support.Local.Models;

/// <summary>
/// 조선소에서 살 수 있는 선체 한 종류. 값은 게임 화면(조선소 → 구입)에서 그대로 옮겼다.
/// </summary>
/// <param name="Name">선체명.</param>
/// <param name="Hp">내구력.</param>
/// <param name="Speed">추진력.</param>
/// <param name="Capacity">적재용량.</param>
/// <param name="Tonnage">적재중량.</param>
/// <param name="Crew">필요승인.</param>
/// <param name="Guns">대포수.</param>
/// <param name="Price">값(닢).</param>
/// <param name="Skin">
/// 배 그림 벌(0~3). <c>asset/ship-g0</c> ~ <c>ship-g3</c> 와 짝이고, 큰 배일수록 큰 번호다.
/// </param>
public sealed record Hull(
    string Name, int Hp, int Speed, int Capacity, int Tonnage, int Crew, int Guns, int Price,
    int Skin)
{
    /// <summary>
    /// 살 수 있는 다섯 종류. 게임 표에 나오는 차례 그대로다(위가 큰 배).
    /// 값은 아래에서부터 100닢씩 올라간다.
    /// </summary>
    /// <remarks>
    /// 게임에서는 해가 가고 기술이 오르면 살 수 있는 선체가 늘지만, 여기서는 이 다섯을
    /// 고정으로 낸다.
    /// </remarks>
    public static readonly Hull[] All =
    [
        new("갤리온",     70, 55, 375, 3500, 40, 24, 500, 3),
        new("중카락",     60, 35, 400, 4000, 45, 24, 400, 2),
        new("카락",       30, 60, 200, 1750, 20,  6, 300, 2),
        new("대형카라벨", 35, 50, 250, 2000, 30,  8, 200, 1),
        new("카라벨",     20, 80, 125, 1250, 15,  2, 100, 0),
    ];

    /// <summary>가장 싼 것(카라벨). 처음에 타고 시작하는 배다.</summary>
    public static Hull Cheapest => All[^1];
}
