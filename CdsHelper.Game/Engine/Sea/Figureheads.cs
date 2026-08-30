namespace CdsHelper.Game.Engine.Sea;

/// <summary>
/// 선수상 규칙 — 뱃머리 조각 하나가 바다 재앙 하나를 막아 준다.
/// </summary>
/// <remarks>
/// 게임 표는 <c>0x0054A0A0</c> 이고 (이름 ptr, 등급) 여덟 바이트씩 <b>서른여섯</b>이다.
/// 소지품 <b>갈래 6</b>(아이템 번호 213~248)이 이 표와 <b>같은 차례</b>다 — 213 송골매상이
/// 0번이고 248 마왕상이 35번이다. 그래서 이름은 표에 적어 두지 않고 아이템 표에서 낸다.
/// <code>
///   막는 것      번호 % 4      0 쥐 · 1 병 · 2 반란 · 3 폭풍·눈보라
///   막을 확률    등급 * 30 - 20 (%)
///   다는 삯      0x0056E280[등급]   1: 200 · 2: 1000 · 3: 5000 · 0: 30000
/// </code>
/// 재앙마다 같은 자리에서 이 표를 탄다 — 쥐 <c>0x00474722</c> · 괴혈병 <c>0x0047489B</c> ·
/// 전염병 <c>0x00474A47</c> · 반란 <c>0x00474B78</c> · 폭풍 <c>0x00474C62</c>. 다섯 다
/// <c>[배+0x5C] % 4</c> 를 제 번호와 견주고, 맞으면 <c>등급*30-20 >= rand(100)</c> 으로 막는다.
///
/// <b>등급 0 은 저주받은 것</b>이다(사신상 · 마왕상). 확률이 <c>-20</c> 이라 아무것도 못
/// 막으면서 삯만 서른 배다 — 게임도 <c>0x00531CC0</c> 에서 "저주받아 풀 수가 없네" 한다.
///
/// 조선소가 <b>파는 것이 아니다</b>. 어디선가 얻어 지닌 것을 달아 줄 뿐이다
/// (<c>0x00495CF0</c> 이 소지품에서 갈래 6 을 찾는다).
/// </remarks>
public static class Figureheads
{
    /// <summary>선수상 아이템이 시작하는 번호(송골매상)와 가짓수.</summary>
    public const int FirstItemId = 213, Count = 36;

    /// <summary>막는 재앙 — <c>번호 % 4</c>.</summary>
    public const int GuardsRats = 0, GuardsSickness = 1, GuardsMutiny = 2, GuardsStorm = 3;

    /// <summary>번호마다의 등급. 1 이 열넷, 2 가 열둘, 3 이 여덟, 저주 둘이다.</summary>
    private static readonly int[] Grades =
    [
        1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,      //  0~13 송골매 … 코끼리
        2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2,            // 14~25 위인 … 바다뱀
        3, 3, 3, 3, 3, 3, 3, 3,                        // 26~33 여신 … 천마
        0, 0,                                          // 34~35 사신 · 마왕 — 저주
    ];

    /// <summary>등급마다의 삯(<c>0x0056E280</c>). 색인이 등급이다.</summary>
    private static readonly int[] Prices = [30000, 200, 1000, 5000];

    /// <summary>표 안의 번호인지.</summary>
    public static bool Known(int index) => index >= 0 && index < Count;

    /// <summary>아이템 번호를 선수상 번호로. 선수상이 아니면 -1.</summary>
    public static int FromItem(int itemId) =>
        Known(itemId - FirstItemId) ? itemId - FirstItemId : -1;

    /// <summary>선수상 번호를 아이템 번호로.</summary>
    public static int ToItem(int index) => FirstItemId + index;

    /// <summary>등급. 표 밖이면 -1.</summary>
    public static int GradeOf(int index) => Known(index) ? Grades[index] : -1;

    /// <summary>막는 재앙(<c>번호 % 4</c>). 표 밖이면 -1.</summary>
    public static int GuardOf(int index) => Known(index) ? index % 4 : -1;

    /// <summary>막을 확률(%). 저주받은 것은 음수라 아무것도 못 막는다.</summary>
    public static int BlockPercent(int index) =>
        Known(index) ? Grades[index] * 30 - 20 : 0;

    /// <summary>다는 삯. 표 밖이면 0.</summary>
    public static int PriceOf(int index) =>
        Known(index) ? Prices[Grades[index]] : 0;

    /// <summary>저주받았는지 — 등급 0 이다.</summary>
    public static bool Cursed(int index) => GradeOf(index) == 0;

    /// <summary>
    /// 그 문화권의 조선소가 <b>늘 갖춰 두는</b> 선수상들.
    /// </summary>
    /// <remarks>
    /// <c>0x00429DF0</c> 이 짓는 목록이다. 어디서나 <b>0 송골매상 · 1 요정상</b> 둘이 있고,
    /// 도시의 문화권(<c>[도시+0x58]</c>)에 따라 한둘이 더 붙는다.
    /// <code>
    ///   429e46  문화권 0 (이베리아)  + 2 제독상
    ///   429e4d  문화권 1 (북유럽)    + 3 백조상 · 4 말상
    ///   429e59  문화권 2 (지중해)    + 5 표범상
    ///   그 밖                        없다 — 늘 있는 둘뿐이다
    /// </code>
    /// 앱 DB 의 설명과 그대로 맞는다 — "이베리아 조선소에만 존재하는 선두상"(제독상),
    /// "이 선두상은 북유럽에만 존재한다"(말상), "지중해의 조선소에만 있는 선두상"(표범상).
    /// </remarks>
    public static IReadOnlyList<int> StockFor(int culture) => culture switch
    {
        0 => [0, 1, 2],
        1 => [0, 1, 3, 4],
        2 => [0, 1, 5],
        _ => [0, 1],
    };

    /// <summary>
    /// 이 선수상이 그 재앙을 막았는지.
    /// </summary>
    /// <param name="index">단 선수상 번호. 안 달았으면 -1 을 주면 된다.</param>
    /// <param name="guards">막아야 할 재앙(<see cref="GuardsRats"/> 벌).</param>
    /// <param name="rng">주사위. <c>rand(100)</c> 과 견준다.</param>
    public static bool Blocks(int index, int guards, Random rng) =>
        GuardOf(index) == guards && BlockPercent(index) >= rng.Next(100);
}
