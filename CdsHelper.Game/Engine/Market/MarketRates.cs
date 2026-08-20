using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Engine.Market;

/// <summary>
/// 도시별 시세. 아이템 값이 도시마다 다른 것은 이 값 하나로 갈린다.
/// </summary>
/// <remarks>
/// 시세는 <b>백분율</b>이다 — 100 이 정가고, 130 이면 정가의 1.3 배를 부른다.
/// 게임 실물은 도시마다 125~134 쯤에 흩어져 있다(cds95-mod 의 시세 일람으로 확인:
/// 리스본 128 · 바르셀로나 125 · 빌바오 134). 그 값이 어디서 오고 어떻게 움직이는지는
/// 아직 안 밝혔으므로 <b>지금은 전부 100</b> 으로 둔다.
///
/// 값 셈은 이미 시세를 거치게 해 두었다(<see cref="BuyPrice"/>). 그래서 나중에 시세를
/// 구현할 때 <see cref="Open"/> 이 표를 채우도록 고치기만 하면 값이 저절로 따라 움직인다 —
/// 부르는 쪽은 한 줄도 안 고쳐도 된다.
///
/// 게임 실물에서 재어 본 것:
/// <code>
///   바스타드소드 정가 12000, 시세 130 인 도시 -> 15600   (12000 * 130 / 100)
/// </code>
/// 곱하는 코드를 EXE 에서 짚지는 못했다. 도시 구조체를 절대주소가 아니라 포인터로 잡아서
/// 정적으로는 안 걸린다 — 셈이 딱 떨어지는 것과 시세가 백분율 꼴인 것으로 세운 것이다.
/// </remarks>
public sealed class MarketRates
{
    /// <summary>기준 시세. 이 값이면 정가 그대로다.</summary>
    public const int Par = 100;

    /// <summary>
    /// 정가에서 벗어날 수 있는 폭. 게임 실물이 125~134 라 넉넉히 잡았다 —
    /// 표에 엉뚱한 값이 들어와도 값이 터무니없어지지 않게 막는 자리다.
    /// </summary>
    public const int MinRate = 1, MaxRate = 1000;

    /// <summary>정가에서 벗어난 도시만 담는다. 없는 도시는 <see cref="Par"/> 다.</summary>
    private readonly Dictionary<int, int> _rates = [];

    /// <summary>그 도시의 시세. 모르는 도시는 100.</summary>
    public int Of(int cityId) => _rates.TryGetValue(cityId, out int rate) ? rate : Par;

    /// <summary>시세를 적어 넣는다. 100 이면 표에서 지운다 — 기본값과 같으니 들 까닭이 없다.</summary>
    public void Set(int cityId, int rate)
    {
        rate = Math.Clamp(rate, MinRate, MaxRate);
        if (rate == Par) _rates.Remove(cityId);
        else _rates[cityId] = rate;
    }

    /// <summary>정가에서 벗어나 있는 도시들. 지금은 늘 비어 있다.</summary>
    public IReadOnlyDictionary<int, int> Adjusted => _rates;

    /// <summary>살 때 내는 값 — 정가에 그 도시 시세를 먹인 것이다.</summary>
    public int BuyPrice(int listPrice, int cityId) => Apply(listPrice, Of(cityId));

    /// <summary>팔 때 받는 값.</summary>
    public int SellPrice(int listPrice, int cityId) => Apply(listPrice, Of(cityId));

    /// <summary>
    /// <c>item.json</c> 의 아이템으로 셈하는 길.
    /// </summary>
    /// <remarks>
    /// <see cref="Item.SellPrice"/> 가 <b>가게가 파는</b> 정가다 — 이름이 반대로 읽히지만
    /// 가게 쪽에서 붙인 이름이다. 시장은 EXE 표(<c>ItemTable</c>)를 쓰는 쪽이 원본이라
    /// 이 두 줄은 옛 부르는 곳을 위해 남겨 둔 것이다.
    /// </remarks>
    public int BuyPrice(Item item, int cityId) => Apply(item.SellPrice, Of(cityId));

    /// <inheritdoc cref="BuyPrice(Item, int)"/>
    public int SellPrice(Item item, int cityId) => Apply(item.BuyPrice, Of(cityId));

    /// <summary>
    /// 이 값부터는 100 단위로 내린다. 그 밑은 한 닢까지 그대로 부른다
    /// (수수 경단은 정가가 2닢이다).
    /// </summary>
    private const int RoundFrom = 1000;

    /// <summary>
    /// 정가에 시세를 먹인다. 값이 커도 넘치지 않게 <see cref="long"/> 으로 셈한다 —
    /// 가장 비싼 것이 50만이라 시세를 곱하면 int 한 줄로는 아슬아슬하다.
    /// </summary>
    private static int Apply(int listPrice, int rate) =>
        listPrice <= 0 ? 0
        : Round((int)Math.Min(int.MaxValue, (long)listPrice * rate / Par));

    /// <summary>
    /// 1000 닢부터는 100 단위로 <b>내린다</b>. 게임 매각 본체(<c>0x004B3D1C</c>)가 그렇게 한다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   cmp  eax, 1000
    ///   jl   그대로
    ///   cdq
    ///   mov  ecx, 100
    ///   idiv ecx          ; / 100
    ///   shl  eax, 2       ; x4
    ///   lea  edx, [eax + eax*4]   ; x5  -> x20
    ///   lea  eax, [edx + edx*4]   ; x5  -> x100
    /// </code>
    /// 파는 쪽에서 눈으로 확인한 규칙이다. 사는 쪽도 같은 함수를 거치는 것으로 보고 함께
    /// 걸었다 — 지금까지 본 값(바스타드소드 15600)은 이미 100 의 배수라 이 규칙이
    /// 걸리든 안 걸리든 같다. 100 의 배수가 아닌 값이 나오면 그때 갈라 주면 된다.
    /// </remarks>
    private static int Round(int price) =>
        price >= RoundFrom ? price / 100 * 100 : price;

    /// <summary>
    /// 시세 표를 연다. <b>지금은 모든 도시가 100</b> 이다 — 시세를 구현하면 여기서 채운다.
    /// </summary>
    public static MarketRates Open() => new();
}
