using CdsHelper.Support.Local.Helpers;

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
/// <see cref="SpriteFolder"/> 를 준 배는 이 값을 안 본다.
/// </param>
/// <param name="MaxMasts">세울 수 있는 마스트 수(1~3).</param>
/// <param name="CanChangeSail">돛 종류를 바꿀 수 있는 배인지.</param>
/// <param name="SpriteFolder">
/// 8방향 그림이 든 폴더의 온 경로. 등록해 넣은 배가 제 그림을 들고 다니는 자리다.
/// null 이면 <see cref="Skin"/> 대로 <c>asset/ship-g*</c> 에서 읽는다.
/// </param>
public sealed record Hull(
    string Name, int Hp, int Speed, int Capacity, int Tonnage, int Crew, int Guns, int Price,
    int Skin, int MaxMasts = 3, bool CanChangeSail = true, string? SpriteFolder = null)
{
    /// <summary>
    /// 마스트 자리 수. 게임도 셋이 끝이다.
    /// </summary>
    /// <remarks>
    /// 위 <see cref="MaxMasts"/> 의 기본값에는 이 이름을 못 쓴다 — 매개변수 목록이 몸통보다
    /// 먼저 풀리기 때문이다. 그래서 거기만 3 을 그대로 적었다.
    /// </remarks>
    public const int MastLimit = 3;

    /// <summary>등록해 넣은 배인지 — 그림을 제 폴더에서 읽는 배다.</summary>
    public bool IsRegistered => SpriteFolder != null;

    /// <summary>
    /// 살 수 있는 다섯 종류. 게임 표에 나오는 차례 그대로다(위가 큰 배).
    /// 값은 아래에서부터 100닢씩 올라간다.
    /// </summary>
    /// <remarks>
    /// 게임에서는 해가 가고 기술이 오르면 살 수 있는 선체가 늘지만, 여기서는 이 다섯을
    /// 고정으로 낸다.
    /// </remarks>
    public static readonly Hull[] Builtin =
    [
        new("갤리온",     70, 55, 375, 3500, 40, 24, 500, 3),
        new("중카락",     60, 35, 400, 4000, 45, 24, 400, 2),
        new("카락",       30, 60, 200, 1750, 20,  6, 300, 2),
        new("대형카라벨", 35, 50, 250, 2000, 30,  8, 200, 1, CanChangeSail: false),
        new("카라벨",     20, 80, 125, 1250, 15,  2, 100, 0, MaxMasts: 2, CanChangeSail: false),
    ];

    private static Hull[]? _all;

    /// <summary>
    /// 조선소에 낼 선체 전부 — 붙박이 다섯에 등록해 넣은 배를 얹은 것이다. 값이 비싼 쪽이 위다.
    /// </summary>
    /// <remarks>
    /// 처음 볼 때 한 번 읽고 들고 있는다. 배를 등록·고침·지운 뒤에는 <see cref="Reload"/> 로 버린다.
    /// </remarks>
    public static Hull[] All => _all ??= ShipRegistry.BuildHulls();

    /// <summary>들고 있던 선체 목록을 버린다 — 다음에 볼 때 다시 읽는다.</summary>
    public static void Reload() => _all = null;

    /// <summary>
    /// 처음에 타고 시작하는 배(카라벨).
    /// </summary>
    /// <remarks>
    /// 붙박이 중에서 고른다 — 등록해 넣은 배가 더 싸다고 해서 시작하는 배까지 바뀌면 곤란하다.
    /// </remarks>
    public static Hull Cheapest => Builtin[^1];

    /// <summary>
    /// 조선소가 되사 주는 값 — 산 값의 <b>6할</b>이다. 시세는 부르는 쪽이 먹인다.
    /// </summary>
    /// <remarks>
    /// 게임은 선체 표(<c>0x004FC1E0</c>, 64바이트)의 <c>+0x38</c> 한 값으로 둘 다 낸다.
    /// <code>
    ///   구입  0x0044B450   c * 1000   (5c → 25c → 125c → &lt;&lt;3)
    ///   매각  0x00423A30   c *  600   (5c → 25c → 75c → 375c → &lt;&lt;4 → /10)
    /// </code>
    /// 그래서 매각은 늘 구입의 6할이고, 그 뒤에 도시 시세를 먹인다(<c>0x00429DC0</c> —
    /// <c>값 x 시세 / 100</c>, 적어도 1닢).
    ///
    /// <b>절대값은 게임과 다르다.</b> 게임의 <c>+0x38</c> 은 코구 7 · 카라벨 10 ·
    /// 대형카라벨 40 · 카락 50 · 대형카락 100 · 중카락 180 · 갤리온 250 이라 구입값이
    /// 만 닢에서 이십오만 닢까지다. 여기 <see cref="Price"/> 는 조선소 화면에서 옮긴
    /// 100~500 짜리 사다리라 자릿수가 다르다 — 비율만 게임 것을 쓴다.
    /// </remarks>
    public int SellPrice => Price * SellPercent / 100;

    /// <summary>되사 주는 비율(%). 게임의 600/1000 이다.</summary>
    public const int SellPercent = 60;
}
