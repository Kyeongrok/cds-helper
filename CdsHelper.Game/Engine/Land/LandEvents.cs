using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Engine.Land;

/// <summary>
/// 뭍을 걷다 마주치는 일 — 독충과 들짐승.
/// </summary>
/// <remarks>
/// 게임의 <c>0x00427828</c>(독충)과 <c>0x00427A1F</c>(짐승)이다. 둘이 짜임이 같다 —
/// 지형을 보고, 확률을 굴리고, <b>「싸운다 · 도망친다」</b>를 묻고, 어느 쪽이든 굴림 하나로
/// 갈린다. 문구는 <c>0x005338E0</c> 부터 한 덩이로 모여 있다.
/// <code>
///   427A21  지형(0x00426740) 이 2 라야 짐승, 6 이라야 독충
///   427A2F  rand(200) == 0   짐승        427838  rand(250) == 0   독충
///   427A52  자리(경도 0x5B63B0 · 위도 0x5B63B4)로 어느 짐승인지 고른다
/// </code>
/// 회오리(<c>0x00427DB8</c>)는 짜임이 다르다 — <b>고를 것이 없고</b> 그냥 당한다.
///
/// 유성(<c>0x00427D05</c>)도 같은 덩이에 있다 — <see cref="Meteor"/>.
///
/// <b>일식은 안 옮겼다 — 원본에서 절대 안 난다.</b> 조건이 「경도 &lt; 5000 이고 경도 ≥ 35000」
/// (<c>0x00427EAE</c>·<c>0x00427EBE</c>)이라 서로 어긋난다. 원본 데이터의 흠이라 옮길 것이 없다.
/// </remarks>
public static class LandEvents
{
    /// <summary>
    /// 뭍을 걷다 <b>동굴</b>을 찾는다(<c>0x0048DC60</c>) — 백에 하나다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   0048dc6a  rand(100) == 0
    ///   0048dc7c  부관 「제독, 동굴을 발견했습니다!」   0x005708D0
    ///   0048dca1  물음 「제독, 동굴속을 탐색하겠습니까?」 0x005708F0
    ///   0048dcd7  문턱 = (신앙심 + 운 + 2) / 5
    ///   0048dce8  문턱 >= rand(100) 이면 보물, 아니면 짐승의 소굴
    /// </code>
    /// </remarks>
    public const int CaveOdds = 100;

    /// <summary>동굴을 뒤져 보물을 찾는지(<c>0x0048DCE8</c>).</summary>
    public static bool CaveTreasure(int faith, int luck, GameRandom dice) =>
        (faith + luck + 2) / 5 >= dice.Next(100);

    /// <summary>
    /// 찾은 보물의 닢수(<c>0x0048DD0C</c>) — <c>운 x 100 + 100 + rand(20)</c>.
    /// </summary>
    public static int CaveGold(int luck, GameRandom dice) =>
        (luck * 4 + 4) * 25 + dice.Next(20);

    /// <summary>
    /// 짐승의 소굴에서 당하는 사람 수의 <b>밑값</b>(<c>0x0048DD62</c> — <c>rand(10)+10</c>).
    /// </summary>
    /// <remarks>
    /// 게임은 이 값을 부하 값 하나(<c>0x0048DDB2</c> 의 <c>+0x34</c>)로 다시 깎는데 그 칸이
    /// 무엇인지 아직 못 짚어 <b>밑값 그대로</b> 쓴다.
    /// </remarks>
    public static int DenLoss(GameRandom dice) => dice.Next(10) + 10;

    // ── 하루가 갈 때 도는 뭍 사건들(0x00426E80 의 땅 갈래) ────────────────────
    //
    // 차례가 있다 — 더위(4) · 추위(2) · 온천(2) · 낙석(3) · 늪(6) · 유사(4) · 독충(6) ·
    // 짐승(2) 순으로 굴려 <b>하루에 하나만</b> 터진다. 도시 안에서는 아무것도 안 난다.

    /// <summary>뭍 사건 대부분이 쓰는 분모(<c>0x0042731F</c> 따위).</summary>
    public const int GroundOdds = 250;

    /// <summary>지형 부류 — 초원 2 · 산 3 · 사막 4 · 밀림(늪) 6.</summary>
    public const int Grass = 2, Mountain = 3, Desert = 4, Jungle = 6;

    /// <summary>쉬어 가는 사건(더위 · 추위 · 온천)이 풀어 주는 피로도 — <c>rand(10)+10</c>.</summary>
    public static int RestGain(GameRandom dice) => dice.Next(10) + 10;

    /// <summary>그 셋이 함께 올려 주는 규율(<c>0x004273BB</c>).</summary>
    public const int RestMorale = 10;

    /// <summary>다치는 사건(낙석 · 늪 · 유사)이 앗아 가는 사람 수 — <c>rand(10)+5</c>.</summary>
    public static int HurtCount(GameRandom dice) => dice.Next(10) + 5;

    /// <summary>
    /// 다친 사람 가운데 <b>돌아오는 수</b>(<c>0x00426DA0</c>) — 의학이 높을수록 많이 돌아온다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   X = 제독과 부관 가운데 높은 의학
    ///   돌아온수 = clamp((X + rand(2)) * n / 10 + 1, 1, n*2/5 + 1)
    ///   실제로 주는 수 = n − 돌아온수
    /// </code>
    /// 말은 <b>n</b> 으로 「대원 %d명이 사망했습니다.」를 내고, 그 뒤에 돌아온 수를 따로 이른다.
    /// </remarks>
    public static int Returned(int medicine, int hurt, GameRandom dice) =>
        Math.Clamp((medicine + dice.Next(2)) * hurt / 10 + 1, 1, hurt * 2 / 5 + 1);

    /// <summary>사막에서 목이 타다 물을 찾는다(<c>0x00427311</c>).</summary>
    public static bool Heat(GameRandom dice, int ground) =>
        ground == Desert && dice.Next(GroundOdds) == 0;

    /// <summary>
    /// 얼어붙을 만큼 춥다가 불빛을 보고 오두막에 든다(<c>0x00427424</c>).
    /// </summary>
    /// <remarks>초원이고 <b>북위 60~70도 · 서경 10~25도</b>(아이슬란드 언저리)라야 난다.</remarks>
    public static bool Cold(GameRandom dice, int ground, double lat, double lon) =>
        ground == Grass && lat >= 60 && lat <= 70 && lon >= -25 && lon <= -10
        && dice.Next(GroundOdds) == 0;

    /// <summary>온천을 만난다(<c>0x0042756D</c>) — 초원이고 북위 40~50도다.</summary>
    public static bool HotSpring(GameRandom dice, int ground, double lat) =>
        ground == Grass && lat >= 40 && lat <= 50 && dice.Next(GroundOdds) == 0;

    /// <summary>산에서 돌이 굴러떨어진다(<c>0x00427672</c>).</summary>
    public static bool Rockfall(GameRandom dice, int ground) =>
        ground == Mountain && dice.Next(GroundOdds) == 0;

    /// <summary>늪에 빠진다(<c>0x00427704</c>) — 독충보다 먼저 굴린다.</summary>
    public static bool Swamp(GameRandom dice, int ground) =>
        ground == Jungle && dice.Next(GroundOdds) == 0;

    /// <summary>모래수렁에 빠진다(<c>0x00427796</c>) — 더위 굴림에 진 뒤다.</summary>
    public static bool Quicksand(GameRandom dice, int ground) =>
        ground == Desert && dice.Next(GroundOdds) == 0;

    /// <summary>독충 — 이름 둘.</summary>    /// <summary>독충 — 이름 둘.</summary>
    public static readonly string[] Vermin = ["독거미", "독사"];

    /// <summary>짐승 — 이름 여섯. 자리로 갈린다.</summary>
    public static readonly string[] Beasts = ["코요테", "퓨마", "쟈가", "사자", "코끼리", "늑대"];

    /// <summary>독충이 나올 확률의 분모(<c>0x00427838</c>).</summary>
    public const int VerminOdds = 250;

    /// <summary>짐승이 나올 확률의 분모(<c>0x00427A2F</c>).</summary>
    public const int BeastOdds = 200;

    /// <summary>마주친 것.</summary>
    /// <param name="Name">이름 — 「늑대」 · 「독사」.</param>
    /// <param name="Venomous">독충인지. 지면 죽는 사람 수가 다르다.</param>
    public readonly record struct Meeting(string Name, bool Venomous);

    /// <summary>한 판의 끝.</summary>
    /// <param name="Won">이겼거나 잘 도망쳤으면 참.</param>
    /// <param name="Dead">죽은 대원 수.</param>
    /// <param name="Cornered">도망치려다 막혔는지 — 말이 갈린다.</param>
    public readonly record struct Outcome(bool Won, int Dead, bool Cornered = false);

    /// <summary>짐승과 회오리가 나는 지형 부류(<c>0x00427A26</c>).</summary>
    public const int BeastGround = 2;

    /// <summary>독충이 나는 지형 부류(<c>0x0042782F</c>).</summary>
    public const int VerminGround = 6;

    /// <summary>
    /// 오늘 무엇을 마주치는지. 아무 일도 없으면 null.
    /// </summary>
    /// <remarks>
    /// <b>지형이 가른다.</b> 짐승은 부류 2, 독충은 부류 6 에서만 난다 — 부류는 자리를
    /// 열여섯으로 나눠 잡은 칸의 그림 번호로 표(<c>0x004CD048</c>)를 찾은 값이다
    /// (<see cref="Table.TerrainTable.ClassOfCell"/>).
    /// </remarks>
    /// <param name="ground">지금 선 자리의 지형 부류. 모르면 -1 을 준다.</param>
    public static Meeting? Meet(GameRandom dice, int ground, IReadOnlyList<int> here)
    {
        if (ground == BeastGround && dice.Next(BeastOdds) == 0)
            return new Meeting(BeastAt(here, dice), Venomous: false);

        if (ground == VerminGround && dice.Next(VerminOdds) == 0)
            return new Meeting(Vermin[dice.Next(Vermin.Length)], true);

        return null;
    }

    /// <summary>
    /// 그 자리에 사는 짐승. 자리 표에 안 걸리면 늑대다.
    /// </summary>
    /// <remarks>
    /// 게임은 경도(<c>0x005B63B0</c>)와 위도(<c>0x005B63B4</c>)를 네모로 잘라 짝을 고른다
    /// (<c>0x00427A52</c> 부터). 네모마다 둘씩이라 그 안에서 <c>rand(2)</c> 로 다시 가른다.
    /// <code>
    ///   경도 0x0458~0x3416 · 위도 0x08AF~0x208E   코요테 · 퓨마
    ///   경도 0x2710~0x411B · 위도 0x208E~0x411A   쟈가   · 사자
    ///   그 밖                                     늑대
    /// </code>
    /// <b>우리는 아직 그 자리 값을 안 들고 다닌다</b> — 걸을 때 위·경도를 재는 자리가
    /// 따로 없어서, 지금은 늑대로만 낸다. 자리를 넘겨 주면 표대로 갈린다.
    /// </remarks>
    private static string BeastAt(IReadOnlyList<int> here, GameRandom dice)
    {
        if (here.Count < 2) return Beasts[^1];        // 늑대

        int lon = here[0], lat = here[1];

        if (lon is >= 0x0458 and <= 0x3416 && lat is >= 0x08AF and <= 0x208E)
            return Beasts[dice.Next(2)];
        if (lon is >= 0x2710 and <= 0x411B && lat is >= 0x208E and <= 0x411A)
            return Beasts[2 + dice.Next(2)];
        // 셋째 네모(코끼리 쪽)는 아직 못 짚었다 — 읽어 낸 두 끝이 서로 어긋난다
        // (0x00427AC2 의 0x4E20 과 0x4572). 다시 뜯을 때까지 늑대로 둔다.

        return Beasts[^1];
    }

    /// <summary>
    /// 싸운다 — 무력과 검·포·사격 세 기능으로 가린다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   4278E3  성공값 = 검술 + rand(7) + 포술 + 사격술
    ///   4278FE  성공값 = (무력 + 1) / 2 + 성공값 x 2
    ///   427918  성공값 &gt;= rand(100) 이면 퇴치
    ///   42796D  지면 대원 rand(독사면 5, 아니면 3) + 3 명이 죽는다
    /// </code>
    /// </remarks>
    public static Outcome Fight(Player player, in Meeting met, GameRandom dice)
    {
        int skill = player.LevelOf(Skill.Names[Skill.Sword])
                  + player.LevelOf(Skill.Names[Skill.Gunnery])
                  + player.LevelOf(Skill.Names[Skill.Shooting])
                  + dice.Next(7);

        int score = (player.AbilityOf(Ability.Might) + 1) / 2 + skill * 2;
        if (score >= dice.Next(100)) return new Outcome(true, 0);

        return new Outcome(false, Kill(player, met, dice));
    }

    /// <summary>
    /// 도망친다 — 운과 신앙심으로 가린다(<c>0x0042798A</c>).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   42798A  도망값 = (운 + 신앙심 + 2) / 3
    ///   4279AD  도망값 &gt;= rand(100) 이면 빠져나온다
    ///   4279D6  못 빠지면 "도망칠 수 없습니다!" 하고 대원이 죽는다
    /// </code>
    /// </remarks>
    public static Outcome Flee(Player player, in Meeting met, GameRandom dice)
    {
        int score = (player.AbilityOf(Ability.Luck) + player.AbilityOf(Ability.Faith) + 2) / 3;
        if (score >= dice.Next(100)) return new Outcome(true, 0);

        return new Outcome(false, Kill(player, met, dice), Cornered: true);
    }

    /// <summary>유성이 흐를 확률의 분모(<c>0x00427D1B</c>).</summary>
    public const int MeteorOdds = 200;

    /// <summary>유성이 흐르는 달 둘(<c>0x00427D05</c>) — 8월과 12월이다.</summary>
    public static readonly int[] MeteorMonths = [8, 12];

    /// <summary>
    /// 오늘 유성이 흐르는지 — <b>8월·12월에만</b>, 이백에 하나다. 지형은 안 본다.
    /// </summary>
    public static bool Meteor(GameRandom dice, int month) =>
        MeteorMonths.Contains(month) && dice.Next(MeteorOdds) == 0;

    /// <summary>
    /// 유성이 흐를 때 부관이 하는 말 셋(<c>0x00533B50</c> 부터). 잃는 것도 얻는 것도 없다.
    /// </summary>
    public static readonly string[] MeteorLines =
    [
        "제독, 하늘을 보십시오.",
        "무언가 빌었습니까? 유성이 없어지기 전에 소원을 빌면 이루어진다고 합니다.",
        "예? 저는 무얼 빌었냐구요? 창피하니까 비밀로 해 두지요.",
    ];

    /// <summary>회오리가 칠 확률의 분모(<c>0x00427DC8</c>).</summary>
    public const int TornadoOdds = 500;

    /// <summary>
    /// 오늘 회오리가 치는지.
    /// </summary>
    /// <remarks>
    /// 게임은 셋을 함께 본다(<c>0x00427DA3</c> 부터).
    /// <code>
    ///   427DA3  [0x005A4D20] % 3 != 0        ; 해를 셋으로 나눈 나머지
    ///   427DBA  지형(0x00426740) == 2
    ///   427DC8  rand(500) == 0
    /// </code>
    /// <b><c>0x005A4D20</c> 은 해(년)다.</b> <c>0x00469880</c> 이 그것을
    /// <c>0x5D6</c>(=1494)과 견주어 트루데시야스 조약을 켜는 데서 잡았다. 그러니 이 조건은
    /// 「사흘에 두 번」이 아니라 <b>셋에 두 해꼴로만 회오리가 부는 해</b>다 — 1480·1481
    /// 에는 불고 1482 에는 안 분다.
    /// </remarks>
    /// <param name="ground">지금 선 자리의 지형 부류.</param>
    /// <param name="year">지금 해.</param>
    public static bool Tornado(GameRandom dice, int ground, int year) =>
        year % 3 != 0 && ground == BeastGround && dice.Next(TornadoOdds) == 0;

    /// <summary>
    /// 회오리에 휩쓸린다 — <b>가릴 것도 고를 것도 없다</b>. 죽은 대원 수를 낸다.
    /// </summary>
    /// <remarks>
    /// <c>0x00427E8F</c> 가 <c>rand(30) + 30</c> 이다 — <b>서른에서 쉰아홉</b>이 한 번에
    /// 죽는다. 짐승에 물려 서넛 잃는 것과는 자릿수가 다르다. 술집 소문이 「회오리를 만난
    /// 탐험가를 만났다네. 그 동료가 말려들어 죽었다는군」(<c>0x00550888</c>) 인 까닭이다.
    /// </remarks>
    public static int Strike(Player player, GameRandom dice)
    {
        int dead = Math.Min(dice.Next(30) + 30, player.Crew);
        player.AddCrew(-dead);
        return dead;
    }

    /// <summary>회오리가 치는 동안 나오는 말 다섯(<c>0x00533C78</c> 부터).</summary>
    public static readonly string[] TornadoLines =
    [
        "제독, 굉장한 바람이군요.",
        "뭐, 뭐야! 저것은?",
        "우와아, 여기서도... 큰일이다!",
        "후우, 간신히 살아있는 듯 하군요.",
        "제독은 괜찮으십니까?",
    ];

    /// <summary>죽는 대원 수 — 독사가 더 사납다(<c>0x0042796D</c>).</summary>
    private static int Kill(Player player, in Meeting met, GameRandom dice)
    {
        int dead = dice.Next(met.Venomous ? 5 : 3) + 3;
        dead = Math.Min(dead, player.Crew);
        player.AddCrew(-dead);
        return dead;
    }
}
