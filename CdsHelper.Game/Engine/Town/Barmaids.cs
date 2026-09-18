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
        BarmaidTable.FortuneOf(player.Fortune, player.Age);

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

    /// <summary>선물로 낼 수 있는 아이템의 분류(<c>0x004B0A4D</c> 이 아이템 표 <c>+0x14</c> 를 1 과 견준다).</summary>
    public const int GiftCategory = 1;

    /// <summary>
    /// 선물 한 번에 오르는 친밀도(<c>0x00466B0B</c>) — <c>지금 친밀도 x (값 / 200) / 100</c> 이다.
    /// </summary>
    /// <remarks>
    /// <b>지금 친밀도에 비례한다</b> — 낯을 튼 지 얼마 안 되면(친밀도 3) 아무리 비싼 것을 줘도 한 칸 남짓이고,
    /// 절반쯤 든 사이(50)에 이만 닢짜리를 주면 한 번에 오십이 오른다. 나눗셈이 둘 다 버림이라
    /// 이천 닢 밑짜리는 아무 값도 안 올린다.
    /// </remarks>
    public static int GiftGain(int liking, int price) => liking * (price / 200) / 100;

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

    // ── 설득한다 — 0x00465A90 ──────────────────────────────────────────────────

    /// <summary>설득이 갈리는 친밀도 자리(<c>0x00465A9C</c>) — 30 · 60 · 90.</summary>
    public const int TalkStep = 30, TrustStep = 60, WooNeeded = 90;

    /// <summary>둘째 자리를 넘으려면 있어야 하는 명성과 소지금(<c>0x00465C2A</c> · <c>0x00465C3E</c>).</summary>
    public const int TrustFame = 3000, TrustGold = 50_000;

    /// <summary>그 나라 말을 이만큼 하면 여급이 더 좋아한다(<c>0x00465CA5</c>).</summary>
    public const int FluentTongue = 2;

    /// <summary>설득 한 번의 끝 — 무슨 말이 나오고 친밀도가 얼마나 오르는지.</summary>
    /// <param name="Words">여급이 하는 말.</param>
    /// <param name="Liking">그 자리에서 올라간 친밀도(안 오르면 지금 값 그대로).</param>
    /// <param name="Proposes">90 을 넘겨 여급이 먼저 물어 오는 자리인지.</param>
    public readonly record struct Talk(string Words, int Liking, bool Proposes = false);

    /// <summary>
    /// 「설득한다」 한 번(<c>0x00465A90</c>) — 친밀도 자리마다 딴 말을 하고 그 자리 위쪽까지만 오른다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   30 밑   친밀도 += rand(10)                     30 까지        「오늘은 정말 즐거웠어요. 또 봐요.」
    ///   60 밑   명성 3000 · 소지금 50000 이 있어야 오른다            없으면 「좀 더 재미있는 이야기가…」
    ///           그 나라 말 2 면 += rand(15)+5, 아니면 += rand(10)+2  60 까지
    ///   90 밑   선물을 준 적이 있어야 오른다                          없으면 「당신이 주는 선물이 받고 싶어요.」
    ///           말 2 면 += rand(20)+5, 아니면 += rand(10)+5           90 까지
    ///   90 위   여급이 먼저 물어 온다(아내가 있으면 그냥 떨어진다)
    /// </code>
    /// </remarks>
    public static Talk Persuade(Player player, in BarmaidTable.Barmaid her, int culture,
                                string tongue, Random dice)
    {
        int liking = player.LikingOf(her.Id);
        bool fluent = tongue.Length > 0 && player.TongueOf(tongue) == FluentTongue;

        if (liking < TalkStep)
            return new Talk("오늘은 정말 즐거웠어요. 또 봐요.",
                            Math.Min(liking + dice.Next(10), TalkStep));

        if (liking < TrustStep)
        {
            if (player.Fame < TrustFame || player.Gold < TrustGold)
                return new Talk("좀 더 재미있는 이야기가 듣고 싶어요. 자, 또 봐요.", liking);
            return fluent
                ? new Talk($"{tongue} 사람이군요. 그런 사람 싫어하지 않아요.",
                           Math.Min(liking + dice.Next(15) + 5, TrustStep))
                : new Talk("좀 더 당신에 대해 알고 싶어요.",
                           Math.Min(liking + dice.Next(10) + 2, TrustStep));
        }

        if (liking < WooNeeded)
        {
            if (!player.HasGifted(her.Id))
                return new Talk("당신이 주는 선물이 받고 싶어요.", liking);
            return fluent
                ? new Talk($"당신의 {tongue} 면이 좋아요.", Math.Min(liking + dice.Next(20) + 5, WooNeeded))
                : new Talk("저도 당신을 좋아해요.", Math.Min(liking + dice.Next(10) + 5, WooNeeded));
        }

        return new Talk("", liking, Proposes: true);
    }

    /// <summary>친밀도가 다 찼을 때 여급이 먼저 묻는 말 둘(<c>0x0055B7A8</c> · <c>0x0055B7D8</c>).</summary>
    public static readonly string[] Invitations =
    [
        "언제 당신의 고향에 데려가 주지 않겠어요?",
        "당신 배에 태워 줄 거예요.",
    ];

    /// <summary>그 물음에 아니오 했을 때 하는 말 둘(<c>0x0055B7F8</c> · <c>0x0055B840</c>).</summary>
    public static readonly string[] Jilted =
    [
        "너무해요, 여자인 나에게 이런 말까지 하게 해 놓고선, 장난이었군요.",
        "믿은 내가 바보였어요. 당신과는 이별이예요.",
    ];

    /// <summary>아니오 뒤에 뜨는 알림(<c>0x0055B870</c>).</summary>
    public const string JiltedNotice = "아무래도 퇴짜맞은 것 같습니다";

    /// <summary>설득이 떨어졌을 때(아내가 있거나 굴림이 모자랄 때) 하는 말(<c>0x0055B890</c>).</summary>
    public const string Fond = "나도 당신이 좋아요.";

    // ── 프로포즈 한다 — 0x00466150 ─────────────────────────────────────────────

    /// <summary>「프로포즈 한다」 줄이 서는 친밀도(<c>0x004668B4</c> 의 <c>cmp 0x50</c>).</summary>
    public const int ProposeNeeded = 80;

    /// <summary>
    /// 맺어질지 굴린다(<c>0x004661A0</c> · <c>0x00465ADB</c>) — 점수가 <c>rand(250)</c> 이상이면 맺어진다.
    /// </summary>
    public const int WooRoll = 250;

    /// <summary>
    /// 밑점수(<c>0x00465E10</c>) — 친밀도 + 매력 + 1 에 궁합·말·선물이 얹힌다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   궁합(얼굴 코드 차 1 이내)  +50      0x00465E70
    ///   그 여급의 말을 2 로 함      +50      0x00465EB0
    ///   선물을 준 적 있음           +10      0x00465E52
    /// </code>
    /// </remarks>
    public static int Score(Player player, in BarmaidTable.Barmaid her, bool destined, bool fluent) =>
        player.LikingOf(her.Id) + player.AbilityOf(Ability.Charm) + 1
        + (destined ? 50 : 0) + (fluent ? 50 : 0) + (player.HasGifted(her.Id) ? 10 : 0);

    /// <summary>
    /// 유혹어를 썼을 때 얹히는 점수(<c>0x0046621D</c>) — 지중해권(문화권 0~2)에서 더 잘 먹는다.
    /// </summary>
    public static int WooBonus(int culture, Random dice) =>
        culture is >= 0 and <= 2 ? dice.Next(25) + 25 : dice.Next(40) + 10;

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
    /// 유혹어 여덟 벌(<c>0x00465EF0</c>) — 소지품의 「지중해의 유혹어N」(아이템 277~284)을 고르면 그 벌을
    /// <b>한 줄씩 다</b> 읊는다. <c>{0}</c> 자리에 여급 이름이 든다.
    /// </summary>
    public static readonly string[][] WooLines =
    [
        [
            "세계중에 흩어져 있는 보석.",
            "최고의 장인이 심혈을 기울여 만든 드레스.",
            "먼 섬에 사는 극채색의 작은 새.",
            "그 전부를 당신에게 바치겠소···",
            "그 전부가 당신에게는 못미치지만.",
        ],
        [
            "일곱개의 바다를 넘어 당신이 있는 마을에 도착하였소.",
            "바닷바람에 등을 밀려 당신 가게에 들어왔소.",
            "당신 눈동자에 끌려 이렇게 술을 마시는 거요.",
            "그러니 {0}, 부탁이오.",
            "바람에 실려, 바다를 넘어···",
            "내 집에 와 주오.",
        ],
        [
            "오오, 세뇨리타! 내 마음은 지중해의 태양과 같이 불타오르고 있소.",
            "당신이 있는 한, 내 이 정열은 식지 않을 것이오.",
            "아아, 세뇨리타! 나와 함께 있어 주오!",
        ],
        [
            "대지는 몇 개의 선으로 나누어져 있고···",
            "사람들은 서로 다투어 영토를 빼앗소.",
            "그렇지만 {0}, 나는 알고 있소.",
            "바다는.... 이 광대한 바다는 어느 누구도 나누지는 못하오.",
            "그러니 {0}, 나와 하나의 바다가 되어···",
            "누구도 두 사람을 떼어 놓지 못하도록.",
        ],
        [
            "일곱색으로 변하는 에게해와 같이 아름다운 {0}.",
            "당신의 아름다운 목에 내가 만든 올리브 목걸이를 걸게 하여 주오.",
            "당신의 아버지가 오지 않도록.",
            "당신의 오빠가 오지 않도록.",
            "그리고 이 밤이 새지 않도록.",
        ],
        [
            "그대, 그 눈빛, 뜨거운 입김에 나는 흐물흐물해졌소.",
            "{0}, 귀여운 여인. 젖은 머리, 빨간 입술.",
            "이 검으로 그대를 베어버리겠소. 미래의 나의 황후여.",
            "그대는 나의 것.",
        ],
        [
            "만일 당신의 얼굴이 가려져 있어도, 나는 알 수 있소.",
            "그 눈썹은 마치 초승달과 같소. 흠, 지금 가려도 소용없소!",
            "그러니 {0}, 당신의 전부를 보여 주시오!",
        ],
        [
            "천지가 울리고 천사 강림하도다!",
            "파라다이스! 바로 파라다이스! 결국 낙원에 도착하였는가!",
            "아아, 뭐야 {0}였나.",
            "굉장한 착각을 했군···",
            "당신이 여신으로 보였소!",
        ],
    ];

    /// <summary>유혹어 아이템의 첫 번호(「지중해의 유혹어1」). 여덟이 이어 붙어 있다.</summary>
    public const int FirstWooItem = 277, WooItemCount = 8;

    /// <summary>유혹어를 안 쓸 때 고르는 줄(<c>0x0055BFA0</c>).</summary>
    public const string NoWooItem = "사용하지 않는다";

    /// <summary>맺어질 때 여급이 하는 말 셋(<c>0x0055B690</c> 부터, <c>0x00465920</c> 이 rand(3)).</summary>
    public static readonly string[] Yeses =
    [
        "기뻐요. 어디든지 당신을 따라가겠어요.",
        "저로 괜찮다면, 함께 가겠어요.",
        "기뻐요. 당신 고향에 데려가 주세요.",
    ];

    /// <summary>맺어질 때 여급이 하는 말(첫 줄).</summary>
    public const string Yes = "기뻐요. 어디든지 당신을 따라가겠어요.";

    /// <summary>혼인 그림(<c>0x00472FA0(7)</c>).</summary>
    public const int WeddingStill = 7;

    /// <summary>끼어드는 연적이 하는 말(<c>0x0055B700</c>) — 프로포즈가 먹힌 뒤 넷에 한 번이다.</summary>
    public const string RivalWord =
        "기다려라! 그 여자는 나도 좋아한다. 그녀를 절대로 넘겨 주지 않겠다! 결투다. 싫다고는 않겠지.";

    /// <summary>연적을 이겼을 때(<c>0x0055B760</c> · <c>0x0055B790</c>).</summary>
    public const string RivalBeaten = "···분하지만 내가 졌다. 목숨만은 살려주게.";
    public const string RivalFame = "명성치가 올라갔다!";

    /// <summary>연적을 이기면 오르는 명성, 지면 오르는 악명(<c>0x004659F3</c> · <c>0x00465A2C</c>).</summary>
    public const int RivalFameUp = 100, RivalInfamyUp = 500;

    /// <summary>맺어졌다고 알리는 서식(<c>0x00539080</c>). 게임 것 그대로다.</summary>
    public const string Married = "{0}는(은) {1}와(과) 결혼했습니다";

    /// <summary>그 유혹어 아이템이 읊는 말들. 번호가 표 밖이면 빈 벌이다.</summary>
    public static IReadOnlyList<string> WooWordsOf(int item)
    {
        int at = item - FirstWooItem;
        return at >= 0 && at < WooLines.Length ? WooLines[at] : [];
    }
}
