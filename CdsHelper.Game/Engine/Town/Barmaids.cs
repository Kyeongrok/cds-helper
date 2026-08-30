using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Engine.Town;

/// <summary>
/// 술집 여급과의 사이 — 궁합 · 친밀도 · 유혹.
/// </summary>
/// <remarks>
/// 표와 궁합 잣대는 <see cref="BarmaidTable"/> 가 알고, 여기서는 <b>그 값으로 무엇이
/// 갈리는지</b>만 든다. 자세한 것은 볼트 <c>58.분석-술집 여급과 궁합</c>.
///
/// <b>궁합은 얼굴 코드다.</b> 주인공의 표시 얼굴 코드(신상에서 고른 초상화 열여섯 중
/// 하나에, 서른여섯 살부터 16 을 더한 것)와 여급의 <see cref="BarmaidTable.Barmaid.Fortune"/>
/// 가 같거나 하나 차이면 맞는 것이다. 맞으면 첫 대화에서만 친밀도가 <b>50</b> 오르고
/// 아니면 <b>3</b> 이다.
/// </remarks>
public static class Barmaids
{
    /// <summary>주인공의 표시 얼굴 코드. 서른여섯부터 열여섯이 더 붙는다.</summary>
    public static int FortuneOf(Player player) =>
        BarmaidTable.FortuneOf(player.Face, player.Age);

    /// <summary>이 여급과 궁합이 맞는지.</summary>
    public static bool Destined(Player player, in BarmaidTable.Barmaid her) =>
        BarmaidTable.Destined(FortuneOf(player), her.Fortune);

    /// <summary>첫 대화가 올리는 친밀도 — 궁합이 열일곱 배를 가른다.</summary>
    public static int FirstMeet(Player player, in BarmaidTable.Barmaid her) =>
        BarmaidTable.LikingGain(Destined(player, her));

    /// <summary>말이 안 통하는 마을에서 오르는 친밀도(<c>0x0046651A</c>).</summary>
    public const int StrangerLike = 20;

    /// <summary>잡담 한 번에 오르는 폭. 궁합이 맞으면 갑절이다.</summary>
    /// <remarks>
    /// 게임은 잡담마다 딴 폭을 주는데 그 표는 아직 못 짚었다. 첫 대화의 50 대 3 을
    /// 결로 삼아 <b>맞으면 4, 아니면 2</b> 로 둔다 — 우리가 정한 값이다.
    /// </remarks>
    public static int ChatLike(bool destined) => destined ? 4 : 2;

    /// <summary>선물 한 번에 오르는 폭. 값이 비쌀수록 많이 오른다.</summary>
    /// <remarks>
    /// 게임은 물건 값을 그대로 먹이는 자리가 있는데(<c>0x00466B33</c> 의 나눗셈) 나누는
    /// 수를 아직 못 짚었다. <b>천 닢에 한 칸</b>으로 두되 적어도 하나는 오르게 한다 —
    /// 우리가 정한 값이다.
    /// </remarks>
    public static int GiftLike(int price) => Math.Clamp(price / 1000, 1, 20);

    /// <summary>선물 반응이 갈리는 자리(<c>0x00466B70</c>).</summary>
    public static readonly int[] GiftSteps = [30, 50, 70, 90];

    /// <summary>선물 받고 하는 말. 친밀도가 높을수록 기뻐한다.</summary>
    public static readonly string[] GiftWords =
    [
        "고마워요. 그런데, 이런 것 받아도 괜찮아요?",
        "고마워요! 기뻐요.",
        "고마워요! 정말 기뻐요.",
        "정말 기뻐요. 소중히 할께요.",
        "고마워요. 내 보물로 하겠어요.",
    ];

    /// <summary>그 친밀도에서 선물 받고 하는 말.</summary>
    public static string GiftWord(int liking)
    {
        int step = 0;
        foreach (int at in GiftSteps) if (liking >= at) step++;
        return GiftWords[step];
    }

    /// <summary>
    /// 청을 받아 주는 친밀도. 여기에 차야 맺어진다.
    /// </summary>
    /// <remarks>
    /// 게임에서 이 문턱을 아직 못 짚었다. 친밀도가 0~100 이고 선물 반응의 마지막 자리가
    /// 90 이라 <b>가득 찼을 때</b>로 둔다 — 우리가 정한 값이다.
    /// </remarks>
    public const int WooNeeded = BarmaidTable.MaxLiking;

    /// <summary>
    /// 유혹이 물렸을 때 하는 말 셋(<c>0x0055BFB0</c> 벌).
    /// </summary>
    public static readonly string[] Refusals =
    [
        "미안해요. 당신과는 좋은 친구로 있고 싶어요.",
        "미안해요. 솔직히 말하면, 좋아하는 사람이 있어요.",
        "미안해요. 기쁘지만 난 이 마을을 떠날 수는 없어요.",
    ];

    /// <summary>
    /// 문화권마다 다른 유혹의 말. 게임은 <c>0x0055B9B8</c> 부터 벌마다 여러 줄을 잇는데,
    /// 여기서는 벌마다 한 줄만 옮겼다.
    /// </summary>
    /// <remarks>
    /// 게임 문자열에 "지중해의 유혹어" 라는 이름이 그대로 박혀 있다(<c>0x0055B988</c>).
    /// </remarks>
    public static readonly string[] Wooing =
    [
        "일곱개의 바다를 넘어 당신이 있는 마을에 도착하였소. 그러니 내 집에 와 주오.",
        "오오, 세뇨리타! 내 마음은 지중해의 태양과 같이 불타오르고 있소. 나와 함께 있어 주오!",
        "바다는.... 이 광대한 바다는 어느 누구도 나누지는 못하오. 나와 하나의 바다가 되어···",
        "일곱색으로 변하는 에게해와 같이 아름다운 그대. 내가 만든 올리브 목걸이를 걸게 하여 주오.",
        "그대, 그 눈빛, 뜨거운 입김에 나는 흐물흐물해졌소. 그대는 나의 것.",
    ];

    /// <summary>맺어질 때 여급이 하는 말(<c>0x0055B690</c>).</summary>
    public const string Yes = "기뻐요. 어디든지 당신을 따라가겠어요.";

    /// <summary>맺어졌다고 알리는 서식(<c>0x00539080</c>). 게임 것 그대로다.</summary>
    public const string Married = "{0}는(은) {1}와(과) 결혼했습니다";

    /// <summary>그 문화권의 유혹의 말. 벌이 모자라면 앞에서부터 돌려 쓴다.</summary>
    public static string WooWord(int culture) =>
        Wooing[Math.Abs(culture) % Wooing.Length];
}
