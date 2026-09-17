using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Engine.Town;

/// <summary>
/// 자택에서 쉬는 규칙. 값은 안 든다 — 내 집이다.
/// </summary>
/// <remarks>
/// 게임은 <c>0x004A2AD0(개월 x 30, 1)</c> 로 <b>날수</b>를 넘긴다 — 달력 달이 아니라
/// 서른 날이다. 쉬면 하루에 피로 -1 · 사기 +3 씩 돌아오는데, 그것은 날을 넘기는 자리가
/// 함께 하므로 <see cref="Support.Local.Models.Player.AdvanceDays"/> 가 맡는다.
/// 그래서 한 달만 쉬어도 폭풍 몇 번 분이 한꺼번에 풀린다.
/// </remarks>
public static class Home
{
    /// <summary>장기 휴양으로 고를 수 있는 가장 긴 달수(<c>0x00460782</c> 의 <c>push 0xC</c>).</summary>
    public const int MaxRestMonths = 12;

    /// <summary>휴양 한 달을 며칠로 세는지. 게임도 서른 날이다.</summary>
    public const int DaysPerMonth = 30;

    /// <summary>그만큼 쉬면 며칠이 가는지.</summary>
    public static int RestDays(int months) => DaysPerMonth * months;

    /// <summary>쉬고 나서 나오는 지문 셋. 게임 것 그대로다(<c>0x00539840</c> 벌).</summary>
    /// <remarks>
    /// 게임은 아내가 있으면 아내가, 없으면 이 셋 가운데 하나를 낸다
    /// (<c>0x004607FE</c> 의 <c>rand(3)</c>). 우리 쪽에는 아내가 없어 지문만 쓴다.
    /// </remarks>
    public static readonly string[] RestWords =
        ["피로가 풀렸다!", "체력이 회복되었다!", "기분이 상쾌하다!"];

    /// <summary>쉬고 나서 건네는 한마디.</summary>
    public static string RestWord(Random random) => RestWords[random.Next(RestWords.Length)];

    // ── 후손을 남긴다 ────────────────────────────────────────────────────────

    /// <summary>
    /// 후손 하나를 얻는 데 드는 날.
    /// </summary>
    /// <remarks>게임도 끝에 <c>0x00469850(5)</c> 로 닷새를 넘긴다(<c>0x00461401</c>).</remarks>
    public const int HeirDays = 5;

    /// <summary>주사위 폭과 되는 눈(<c>0x004613CC</c> 의 <c>rand(8) &lt; 2</c>).</summary>
    public const int HeirRoll = 8, HeirWin = 2;

    /// <summary>
    /// 후손을 남길 수 있는지 — <b>아내가 있어야 한다</b>.
    /// </summary>
    /// <remarks>
    /// 게임은 줄의 켜짐을 <c>0x00460650</c> 하나로 정한다 — <c>[0x005B61B0] != -1</c>,
    /// 곧 아내가 있느냐다. 안 되면 줄이 흐릴 뿐 사라지지는 않는다.
    ///
    /// 게임은 그 뒤로 관문을 둘 더 둔다(아내 상태 <c>[아내+4] == 2</c> · 체력
    /// <c>[0x005B60D8] &gt;= 100</c>). 우리는 아내를 이름으로만 들고 체력 칸도 없어
    /// 그 둘은 안 옮겼다.
    /// </remarks>
    public static bool CanLeaveHeir(Player player) => player.Spouse.Length > 0;

    /// <summary>
    /// 이번에 후손을 얻었는지. <b>여덟에 둘</b>이라 네 번에 한 번 꼴이다.
    /// </summary>
    /// <remarks>
    /// 게임은 굴리기 전에 빈 아이 칸이 있고(<c>0x004AB9F0</c>) 막내가 이미 태어났는지(아내 <c>+0x38 == -1</c>)를 본다 —
    /// 둘 다 차 있거나 배 속에 아이가 있으면 굴림과 상관없이 안 된다.
    /// </remarks>
    public static bool HeirBorn(Player player, Random random) =>
        player.Children.Count < Player.MaxChildren
        && player.Children.All(c => c.IsBornBy(player.Date))
        && random.Next(HeirRoll) < HeirWin;

    /// <summary>
    /// 아이를 잉태한다(<c>0x00460C50</c>).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   성별      rand(2) == 0 이면 딸. 이미 아이가 있으면 그 반대 성별
    ///   태어나는 날  지금 달 + 10 (넘치면 이듬해), 날은 그 달 끝날(게임 굴림 고리가 늘 끝날 쪽으로 멎는다)
    ///   능력치    아버지 값 + rand(20) − 10 + 1, 1~100 (0x004610C0)
    ///   기능      아버지가 3 인 것은 3, 아닌 것 가운데 하나를 골라 2 (0x00460F1C)
    ///   언어      아버지가 3 인 것은 3 (0x00460EB8)
    /// </code>
    /// 게임은 능력치마다 폭을 조금씩 달리하고(칸에 따라 30 · −15), 딸에게 +5, 아내 직업 보정표(<c>0x0051ACA0</c> ·
    /// <c>0x0051B0A0</c>)를 얹으며, 아내 고향 말도 준다. 뜀표 칸마다의 짝을 다 짚지 못해 밑값 셈만 옮겼다.
    /// </remarks>
    public static Player.Child Conceive(Player father, Random random, string name)
    {
        bool daughter = father.Children.Count > 0 ? !father.Children[^1].Daughter : random.Next(2) == 0;

        var due = father.Date.AddMonths(10);
        due = new DateTime(due.Year, due.Month, DateTime.DaysInMonth(due.Year, due.Month));

        var abilities = new int[6];
        for (int i = 0; i < abilities.Length; i++)
            abilities[i] = Math.Clamp(father.AbilityOf(i) + random.Next(20) - 10 + 1, 1, 100);

        return Bless(father, random, new Player.Child(name, daughter, due, abilities,
                                                      new int[Skill.Names.Length], new int[Skill.Languages.Length]));
    }

    /// <summary>기능·언어를 아버지에게서 받는다 — 잉태할 때와, 이름만 있는 옛 세이브 아이를 채울 때 쓴다.</summary>
    public static Player.Child Bless(Player father, Random random, Player.Child child)
    {
        var skills = Skill.Names.Select(n => father.LevelOf(n) >= Skill.MaxLevel ? Skill.MaxLevel : 0).ToArray();
        var empty = Enumerable.Range(0, skills.Length).Where(i => skills[i] == 0).ToList();
        if (empty.Count > 0) skills[empty[random.Next(empty.Count)]] = 2;

        var tongues = Skill.Languages.Select(n => father.TongueOf(n) >= Skill.MaxLevel ? Skill.MaxLevel : 0).ToArray();

        var abilities = child.Abilities.All(a => a == 0)
            ? Enumerable.Range(0, 6).Select(i => Math.Clamp(father.AbilityOf(i) + random.Next(20) - 10 + 1, 1, 100)).ToArray()
            : child.Abilities;
        return child with { Abilities = abilities, Skills = skills, Tongues = tongues };
    }

    // ── 자택에 가면 — 아이 소개 ──────────────────────────────────────────────

    /// <summary>이름을 지을 때 글자 수(원본 인물 이름 칸을 따라 넉넉히 잡았다).</summary>
    public const int ChildNameMaxLength = 8;

    /// <summary>
    /// 아직 소개 안 한, 이미 태어난 아이들 — 태어난 차례대로(<c>0x0045FFC0</c>).
    /// </summary>
    public static List<Player.Child> NotIntroduced(Player player) =>
        [.. player.Children.Where(c => !c.Introduced && c.IsBornBy(player.Date)).OrderBy(c => c.Born)];

    /// <summary>
    /// 아이를 소개하는 말(<c>0x00460070</c>, <c>0x00539398</c>·<c>0x005393E8</c>).
    /// </summary>
    public static string IntroductionOf(Player.Child child, DateTime today)
    {
        int age = child.AgeOn(today);
        return age > 0
            ? $"보세요, 당신의 아이에요. 올해 {age}세가 되지요. 이름은 {child.Name}. 신부님이 지어 주셨어요."
            : $"보세요, 당신의 아이에요. 이름은 {child.Name}…";
    }

    // ── 딸의 결혼 ────────────────────────────────────────────────────────────

    /// <summary>이야기가 열리는 주사위(<c>0x00460180</c> 의 <c>rand(5) == 0</c>) — 다섯에 하나.</summary>
    public const int MarriageRoll = 5;

    /// <summary>딸이 결혼 이야기를 꺼낼 수 있는 나이(열다섯).</summary>
    public const int MarriageAge = 15;

    /// <summary>이야기가 열리는 데 있어야 하는 저금(만 닢).</summary>
    public const int MarriageSavings = 10000;

    /// <summary>결혼 준비금(<c>0x0047CC00(-10000)</c>).</summary>
    public const int MarriageDowry = 10000;

    /// <summary>
    /// 지금 결혼 이야기를 꺼낼 수 있는 맏딸 — 아내가 있고 저금이 <see cref="MarriageSavings"/>
    /// 이상이며, 그 딸이 <see cref="MarriageAge"/> 이상이라야 한다. 없으면 null.
    /// </summary>
    public static Player.Child? MarriageableDaughter(Player player) =>
        player.Spouse.Length == 0 || player.Savings < MarriageSavings ? null
        : player.Children.Where(c => c.Daughter && c.AgeOn(player.Date) >= MarriageAge)
                         .OrderBy(c => c.Born).FirstOrDefault();

    /// <summary>교육 나이 — 열 살부터(<c>0x0046181E</c>).</summary>
    public const int EducateAge = 10;

    /// <summary>
    /// 아버지가 아이보다 높은 것 — 가르칠 수 있는 기능(<c>0x004AC040</c>, 열셋)과 언어(<c>0x004AC090</c>, 열넷).
    /// </summary>
    /// <returns>(기능인가, 칸 번호) 차례 — 기능이 먼저다.</returns>
    public static List<(bool Skill, int Index)> Teachable(Player father, Player.Child child)
    {
        var list = new List<(bool, int)>();
        for (int i = 0; i < Skill.Names.Length && i < child.Skills.Length; i++)
            if (father.LevelOf(Skill.Names[i]) > child.Skills[i]) list.Add((true, i));
        for (int i = 0; i < Skill.Languages.Length && i < child.Tongues.Length; i++)
            if (father.TongueOf(Skill.Languages[i]) > child.Tongues[i]) list.Add((false, i));
        return list;
    }

    /// <summary>
    /// 더 배울 수 있는지(<c>0x004696E0</c> 기능 · <c>0x00469750</c> 언어).
    /// </summary>
    /// <remarks>
    /// 단계마다 무게를 매겨 더한 값이 지력으로 정한 한도 밑이라야 한다.
    /// <code>
    ///   점수 = (3단계 수 × 2 + 2단계 수) × 3 + 1단계 수
    ///   한도 = (지력 × 3 + 3) / 5            ; 지력은 아이 칸 +0x24
    ///   점수 &lt; 한도 라야 배운다
    /// </code>
    /// 언어 쪽(<c>0x00469750</c>)은 같은 꼴로 보고 옮겼다 — 따로 확인하지는 않았다.
    /// </remarks>
    public static bool CanLearnMore(Player.Child child, bool skill)
    {
        var levels = skill ? child.Skills : child.Tongues;
        int ones = levels.Count(l => l == 1), twos = levels.Count(l => l == 2), threes = levels.Count(l => l == 3);
        int score = (threes * 2 + twos) * 3 + ones;
        int mind = child.Abilities.Length > 1 ? child.Abilities[1] : 0;
        return score < (mind * 3 + 3) / 5;
    }

    /// <summary>
    /// 한 단계 가르치는 데 드는 날(<c>0x00461440</c>) — (120 − (지력 + 1)) / 10 × 다음 단계 × 30.
    /// </summary>
    public static int EducateDays(Player.Child child, int nextLevel)
    {
        int mind = child.Abilities.Length > 1 ? child.Abilities[1] : 0;
        return (120 - (mind + 1)) / 10 * nextLevel * 30;
    }

    /// <summary>
    /// 기능을 익힌 아이가 하는 말(<c>0x00461640</c> 의 뜀표) — 기능마다 (2단계, 3단계) 한 쌍이다.
    /// </summary>
    public static readonly (string Two, string Three)[] SkillRemarks =
    [
        ("이제 항해술은 완벽해! 빨리 바다로 나가고 싶군.", "항해술은 이제 됐으니 빨리 아버지 배에 태워줘요."),
        ("탐험 수칙인건 알겠지만, 걷는건 싫군.", "괜찮아! 숲도 사막도 위험하니까, 주위를 주의하면 되는 거죠?"),
        ("상대방이 상단공격을 하면 웅크리면 되죠?", "솜씨가 많이 늘었다! 워낙 칼싸움을 좋아하거든."),
        ("흠-, 대포도 여러 종류가 있군요.", "대포는 화약을 조심해야 하죠? 괜찮아요."),
        ("잘 보세요. 저 돌을 맞출테니…! 아, 빗나갔다.", "이것이 화승총이고, 이쪽이 머스켓총. 다 알았어요."),
        ("항해중엔 영양부족이 되니, 보리를 먹어야 되는거군···", "흠, 응급처지는 이렇게 하는 거구나. 이러면 다쳐도 걱정 없네요."),
        ("과연 상대방의 마음을 꿰뚫어 보는군. 그럼 이것으로 교섭이라면 걱정없어요.", "조리있게 말하는 건 어렵구나···"),
        ("멀리 있는 것과의 거리를 잴 때는···인지를 세워서···", "육분의나 나침반을 쓰는 방법은 다 이해했어요. 다음은 해보는 일만 남았군요."),
        ("역사란 재미있군. 옛날 세계를 한번 보고 싶네.", "나도 역사에 이름을 남길 수 있는 위대한 인물이 되고 싶어."),
        ("돈을 많이 모아서 어머니에게 큰 집을 지어 드릴께요.", "무역의 비결은···시세와 특산품에 있지요!"),
        ("아무때나 배가 고장나도 걱정없어요!", "완벽하게 수리했어요. 조선소 아저씨 못지 않아요."),
        ("성경책을 다 외웠어요. 옛? 암송해 보라고요? 내, 내일 할께요.", "신학은 심오하군요. 터득하려면 열심히 공부해야겠어요."),
        ("실험은 재미있군요! 더 가르쳐 줘요.", "좀 알것 같아요. 앞으로는 과학의 시대가 되겠지요!"),
    ];

    /// <summary>세대교체 나이 — 열여덟부터(<c>0x00461AF4</c>).</summary>
    public const int SucceedAge = 18;

    /// <summary>뒤를 이을 아들 — 성별 0 가운데 가장 나이 많은 것(<c>0x004AB790(0, 0)</c>). 없으면 null.</summary>
    public static Player.Child? EldestSon(Player player) =>
        player.Children.Where(c => !c.Daughter).OrderBy(c => c.Born).FirstOrDefault();

    /// <summary>물려받는 명성(<c>0x00461B66</c>) — 3000 밑 0 · 6000 밑 1/5 · 그 위 1/5 + 1000.</summary>
    public static int InheritedFame(int fame) => fame < 3000 ? 0 : fame < 6000 ? fame / 5 : fame / 5 + 1000;

    /// <summary>물려받는 악명(<c>0x00461B9E</c>) — 2000 밑 0 · 5000 밑 1/8 · 그 위 1/8 + 1000.</summary>
    public static int InheritedInfamy(int infamy) => infamy < 2000 ? 0 : infamy < 5000 ? infamy / 8 : infamy / 8 + 1000;
}
