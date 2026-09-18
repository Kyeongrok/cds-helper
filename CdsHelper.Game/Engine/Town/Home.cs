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

    /// <summary>후손을 보려면 컨디션이 이만큼은 있어야 한다(<c>0x0046139E</c> 의 <c>cmp 0x64</c>).</summary>
    public const int HeirCondition = 100;

    /// <summary>컨디션이 모자랄 때 아내가 하는 말(<c>0x00539A70</c>).</summary>
    public const string HeirTired = "안색이 안 좋은데요. 너무 무리하지 마세요.";

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
    /// 능력치 폭은 칸마다 다르고(운·신앙심만 <c>rand(30) − 15</c>), 딸이면 체력·무력이 −10, 나머지 넷이 +5 다.
    /// 거기에 <b>아내 운명 코드 줄</b>(<c>0x0051B0A0</c>, <see cref="Oracle.WifeSlope"/>)이 얹힌다.
    /// </remarks>
    /// <param name="wifeFortune">아내 운명 코드. 모르면 −1 이라 보정이 없다.</param>
    /// <param name="wifeBlood">아내 혈액형. 모르면 −1 이라 아버지 것만 본다.</param>
    /// <param name="daughter">딸인지. 안 주면 여기서 굴린다(이미 아이가 있으면 그 반대 성별이다).</param>
    public static Player.Child Conceive(Player father, Random random, string name,
                                        int wifeFortune = -1, int wifeBlood = -1, bool? daughter = null)
    {
        daughter ??= father.Children.Count > 0 ? !father.Children[^1].Daughter : random.Next(2) == 0;

        var due = DueDate(father.Date, random);

        var abilities = new int[6];
        for (int i = 0; i < abilities.Length; i++)
            abilities[i] = AbilityOfChild(father.AbilityOf(i), i, daughter.Value, wifeFortune, random);

        var child = new Player.Child(name, daughter.Value, due, abilities,
                                     new int[Skill.Names.Length], new int[Skill.Languages.Length],
                                     Blood: BloodOf(father.Blood, wifeBlood, random),
                                     Face: ChildFaces[random.Next(2) + (daughter.Value ? 2 : 0)][0]);
        return Bless(father, random, child);
    }

    /// <summary>
    /// 아이 얼굴 표(<c>0x00560D10</c>) — 넉 줄에 얼굴 셋씩이다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   아들  393 · 396 · 395        아들  394 · 397 · 395
    ///   딸    139 · 141 · 143        딸    140 · 142 · 143
    /// </code>
    /// 태어날 때 줄을 굴려 고르고(<c>0x00460CD8</c> — <c>rand(2) + 딸*2</c>), 그 뒤로는 나이로
    /// 칸이 바뀐다(<c>0x0047D710</c>). 아들은 <b>열다섯 살부터</b> 표를 떠나 제 얼굴을 쓰는데
    /// (<c>0x0047D726</c>), 그 얼굴은 후손으로 물려받을 때 정해지므로 여기서는 표만 든다.
    /// 딸은 끝까지 표를 쓴다 — 그래서 열 살 넘은 두 줄이 같은 얼굴(143)이다.
    /// </remarks>
    public static readonly int[][] ChildFaces =
    [
        [393, 396, 395], [394, 397, 395],
        [139, 141, 143], [140, 142, 143],
    ];

    /// <summary>나이 한 칸이 다섯 해고 칸은 셋뿐이다(<c>0x0047D73F</c> 의 <c>idiv 5</c> → <c>0x0049E540(0,2)</c>).</summary>
    public const int FaceYearsPerStep = 5, FaceSteps = 3;

    /// <summary>아들이 표를 떠나 제 얼굴을 쓰는 나이(<c>0x0047D726</c> 의 <c>cmp 0xF</c>).</summary>
    public const int GrownSonAge = 15;

    /// <summary>
    /// 그 나이의 아이 얼굴(<c>0x0047D710</c>). 얼굴을 안 적던 옛 세이브면 −1 이다.
    /// </summary>
    public static int FaceOf(Player.Child child, int age)
    {
        if (child.Face < 0) return -1;
        int row = Array.FindIndex(ChildFaces, r => r[0] == child.Face);
        if (row < 0) return child.Face;

        // 열다섯 넘은 아들은 표를 떠난다 — 우리는 아직 그 얼굴이 없어 마지막 칸을 그대로 쓴다.
        int step = Math.Clamp(age / FaceYearsPerStep, 0, FaceSteps - 1);
        return ChildFaces[row][step];
    }

    /// <summary>능력치 폭과 밑값(<c>0x004610C0</c> 의 칸별 값) — 운·신앙심만 넓다.</summary>
    private static readonly int[] Spread = [20, 20, 20, 20, 30, 30];
    private static readonly int[] Floor = [-10, -10, -10, -10, -15, -15];

    /// <summary>딸이면 얹히는 값(<c>0x00461123</c>) — 체력·무력은 오히려 깎인다.</summary>
    private static readonly int[] DaughterBonus = [-10, 5, -10, 5, 5, 5];

    /// <summary>아이 능력치 한 칸(<c>0x004610C0</c>). 1~100 으로 자른다.</summary>
    public static int AbilityOfChild(int fathers, int ability, bool daughter, int wifeFortune, Random random)
    {
        int value = fathers + random.Next(Spread[ability]) + Floor[ability]
                    + (daughter ? DaughterBonus[ability] : 0)
                    + Oracle.WifeSlope(wifeFortune, ability) + 1;
        return Math.Clamp(value, 1, 100);
    }

    /// <summary>한 해의 달마다의 날 수(<c>0x004FF940</c>) — 윤년을 안 본다.</summary>
    private static readonly int[] MonthDays = [0, 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];

    /// <summary>
    /// 태어나는 날(<c>0x00460CDE</c>) — 열 달 뒤, 날은 <c>rand(31)+1</c> 이 <b>달 길이 이상</b> 나올 때까지 굴린다.
    /// </summary>
    /// <remarks>
    /// 견줌이 뒤집혀 있어(원본의 흠) 실제로는 <b>달 끝 무렵</b>만 나온다 — 2월이면 28~31 이고, 서른 날짜리 달에도
    /// 31 이 나온다. 그래서 그 달에 없는 날이면 달 끝날로 앉힌다.
    /// </remarks>
    public static DateTime DueDate(DateTime today, Random random)
    {
        int year = today.Year, month = today.Month + 10;
        if (month > 12) { month -= 12; year++; }

        int day;
        do { day = random.Next(31) + 1; } while (day < MonthDays[month]);
        return new DateTime(year, month, Math.Min(day, DateTime.DaysInMonth(year, month)));
    }

    /// <summary>
    /// 아이 혈액형(<c>0x00460FA0</c>) — 0 A · 1 B · 2 O · 3 AB.
    /// </summary>
    /// <remarks>
    /// 부모가 AB 면 한쪽 인자는 A, 다른 쪽은 B 로 보고, 아니면 반반으로 O 를 섞는다. 어머니 쪽 인자를
    /// <b>반만 굴리는</b> 원본의 흠(굴림이 빗나가면 아버지 값이 그대로 남는다)까지 그대로 옮겼다.
    /// </remarks>
    public static int BloodOf(int fathers, int mothers, Random random)
    {
        if (fathers is < 0 or > 3) return 0;
        if (mothers is < 0 or > 3) mothers = fathers;

        int f = fathers == 3 ? 0 : fathers;
        int altF = fathers == 3 ? 1 : random.Next(2) == 0 ? 2 : fathers;
        int m = mothers == 3 ? 0 : mothers;
        int altM = mothers == 3 ? 1 : random.Next(2) == 0 ? 2 : altF;   // 어머니 쪽 굴림이 빗나가면 아버지 값이 남는다

        if (random.Next(2) == 0) f = altF;
        if (random.Next(2) == 0) m = altM;

        if ((f == 0 && m == 1) || (f == 1 && m == 0)) return 3;
        if (f == 0 || m == 0) return 0;
        return f == 1 || m == 1 ? 1 : 2;
    }

    /// <summary>
    /// 아이를 가지려 한 뒤 아내가 하는 말 스물하나(<c>0x00414B30</c>). 쉰 살 밑이면 앞 셋은 안 나온다.
    /// </summary>
    public static string WifeWord(int age, Random random)
    {
        int at = age >= 50 ? random.Next(WifeWords.Length) : random.Next(WifeWords.Length - 3) + 3;
        return WifeWords[at];
    }

    /// <summary>그 말들(<c>0x00414B30</c> 표 차례 그대로).</summary>
    public static readonly string[] WifeWords =
    [
        "당신 아직 젊군요.",
        "괜찮아요. 무리하지 않아도.",
        "오래간만이에요.",
        "사랑해요, 당신.",
        "당신이 없으면 외로우니까, 아이들이 많이 있었으면 해요.",
        "너무 안 돌아오면 생각 달리 할테니까···농담이에요.",
        "남자애나 여자애나 어느 쪽이라도 좋으니, 건강한 아이면 좋겠어요.",
        "언제까지나 함께 있고 싶어요.",
        "···(화끈)",
        "후후후.",
        "싫어요.",
        "응, 당신은.",
        "그렇게 쳐다보지 말아요.",
        "좀더 이쪽으로 와요.",
        "서두르지 말아요. 밤은 기니까.",
        "둘이 있을 때가 가장 행복해요.",
        "나는 여자 아이가 좋아.",
        "당신은 남자 아이가 좋죠?",
        "이대로 밤이 새지 않았으면···",
        "사랑해요.",
        "당신도 좋아하죠.",
    ];

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

    /// <summary>이 나이까지는 소개할 때 사건 그림을 함께 낸다(<c>0x0045FFE6</c>).</summary>
    public const int BabyAge = 5;

    /// <summary>그때 세우는 사건 그림(<c>0x00472FA0(8)</c>).</summary>
    public const int BabyStill = 8;

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
