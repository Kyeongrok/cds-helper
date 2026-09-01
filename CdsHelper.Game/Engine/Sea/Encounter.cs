using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Engine.Sea;

/// <summary>붙는 무리의 갈래. 게임의 <c>0x004555B0(kind)</c> 인자 그대로다.</summary>
public enum EnemyKind
{
    /// <summary>갑자기 쳐들어온 무리 — 창 제목이 "적의 공격" 이다.</summary>
    Raider = 0,

    /// <summary>추격대·토벌대. <b>교섭이 통하지 않는다.</b></summary>
    Chaser = 1,

    /// <summary>해적.</summary>
    Pirate = 2,

    /// <summary>이슬람 함대.</summary>
    Islam = 3,
}

/// <summary>붙은 무리 하나.</summary>
/// <param name="Ships">적 함대 척수. 요구액과 교섭 확률이 여기 걸린다.</param>
/// <param name="Sum">적장 능력 넷의 합에 1 을 더한 값(<c>0x00455A36</c>).</param>
public readonly record struct Enemy(EnemyKind Kind, string Name, int Ships, int Sum);

/// <summary>
/// 바다에서 남의 함대를 만났을 때의 셈과 말 — <b>교섭 · 도망 · 응전</b>.
/// </summary>
/// <remarks>
/// 게임의 <c>0x004555B0(kind)</c> 이다. 돌려주는 값이 1 이면 도망 성공(부른 쪽이 그냥
/// 넘어간다), 5 면 교섭 성공, 0 이면 해전이다.
/// <code>
///   0x004878A0  고르기 — 교섭한다(0) · 도망간다(1) · 응전한다(2)
///   0x00455859  교섭   0x00455B5F  도망   0x00455C38  응전
/// </code>
///
/// <b>적 함대는 우리가 지어낸다.</b> 게임은 지도 위를 돌아다니는 함대 객체를 들고 있다가
/// 두 칸 안으로 붙으면 그것을 넘기는데([[59.분석-해적 조우]]), 우리 쪽에는 그 객체가
/// 없다. 그래서 척수와 적장 능력을 여기서 굴린다 — 셈식만은 게임 것 그대로다.
/// </remarks>
public static class Encounter
{
    // ── 말 ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// 갈래마다 다섯 줄씩. 게임도 <c>0x004B7C0F(5)</c> 로 하나를 뽑는다.
    /// </summary>
    private static readonly string[][] Greetings =
    [
        // 0 갑자기 쳐들어온 무리 — 이름을 아는 적이라 한 줄뿐이다(0x0055F6A8).
        ["네가 {0}(이)군. 찾고 있었다! 너를 토벌하라는 명령이다. 각오해라."],

        // 1 추격대·토벌대 (0x0055F6F0 벌)
        [
            "제독, 추격대가 왔습니다!",
            "추격대인 것 같습니다! 어떻게 할까요?",
            "큰일이군! 토벌대입니다!",
            "제독, 끈질긴 추격대의 함대입니다. 어떻게 할까요?",
            "귀찮은 녀석들이군요. 추격대의 함대입니다.",
        ],

        // 2 해적 (0x0055F7C0 벌)
        [
            "제독, 해적입니다. 어떻게 할까요?",
            "이 주변을 흐리고 있는 해적들인 것 같습니다. 어떻게 할까요?",
            "해적이다! 제독, 녀석들이 이쪽으로 오고 있습니다.",
            "해적입니다. 성가신 녀석들이 왔군요.",
            "제독, 해적입니다! 어떻게 처리할까요?",
        ],

        // 3 이슬람 함대 (0x0055F8B8 벌)
        [
            "이교도의 녀석들입니다. 제독, 지시를!",
            "이슬람 함대가 이쪽으로 오고 있습니다. 어떻게 할까요?",
            "제독, 저쪽에서 이슬람 녀석들이 오고 있습니다. 어떻게 할까요?",
            "이슬람교의 녀석들이 오고 있습니다. 우리들을 공격할 것 같습니다.",
            "큰일입니다. 이슬람 함대가 돌격해 오고 있습니다.",
        ],
    ];

    /// <summary>말이 안 통해 교섭이 엎어질 때(<c>0x0055FA00</c> 벌).</summary>
    private static readonly string[] NoWords =
    [
        "제독, 말이 통하지 않습니다! 교섭은 실패입니다.",
        "말이 통하지 않아서 교섭은 불가능합니다. 싸웁시다!",
        "녀석들, 습격해 왔습니다! 말이 통하지 않아서 무리였던 것 같습니다.",
        "말이 통하지 않아서 교섭할 수 없었습니다. 제독, 응전합시다!",
        "안되겠습니다. 대화를 할 수 없습니다. 제독, 응전합시다!",
    ];

    /// <summary>돈을 요구하는 말(<c>0x0055FB30</c> 벌). <c>{0}</c> 이 요구액이다.</summary>
    private static readonly string[] Demands =
    [
        "제독, 적은 금화 {0}닢을 요구하고 있습니다. 어떻게 할까요?",
        "녀석들, 금화 {0}닢을 요구하고 있습니다. 제독. 어떻게 할까요?",
        "금화 {0}닢을 달라고 하고 있습니다. 어떻게 대답할까요?",
        "{0}닢의 금화를 요구하고 있습니다만, 어떻게 할까요?",
        "{0}닢의 금화를 주면 봐 주겠다고 하고 있습니다만, 어떻게 할까요?",
    ];

    /// <summary>돈이 모자랄 때(<c>0x0055FCA0</c> 벌).</summary>
    private static readonly string[] TooPoor =
    [
        "제독, 그런 돈은 없습니다!",
        "제독, 금화가 모자랍니다!",
        "그렇게까지 지불할 수 없습니다.",
        "제독, 그렇게까지 돈이 없을 텐데요?",
        "제독, 그렇게 금화를 가지고 있지 않습니다!",
    ];

    /// <summary>돈을 내고 물러갈 때(<c>0x0055FD60</c> 벌).</summary>
    private static readonly string[] Paid =
    [
        "적은 만족해 하며 사라졌습니다.",
        "적이 납득한 것 같습니다. 전투는 피한 것 같습니다.",
        "적은 사라졌습니다. 이것으로 싸우지 않고 끝난 것이라면, 괜찮군요.",
        "적은 허락한 것 같습니다.",
        "후우, 싸움은 간신히 피한 것 같군요.",
        "잘 되었습니다. 쓸데없는 싸움은 피하는 것이 좋습니다.",
    ];

    /// <summary>교섭이 깨질 때(<c>0x0055FE88</c> 벌).</summary>
    private static readonly string[] TalkFailed =
    [
        "제독, 응해주지 않는군요. 싸웁시다!",
        "실패입니다. 적이 공격해 왔습니다!",
        "안되겠습니다. 제독, 싸움 준비를!",
        "제독, 안되겠습니다. 녀석들 화가 나 있습니다. 싸움은 피할 수 없습니다!",
        "교섭은 실패입니다. 제독, 싸울 수 밖에 없군요.",
        "제독, 최악입니다. 교섭에 실패했습니다.",
    ];

    /// <summary>도망에 성공할 때(<c>0x0055FFA8</c> 벌).</summary>
    private static readonly string[] Fled =
    [
        "간신히 추격을 따돌린 것 같습니다.",
        "잘 되었습니다. 아둔한 녀석들이어서 살았습니다.",
        "따라오지 않는군요. 습격할 생각이 없었나···",
        "도망쳐 나왔습니다. 추격하지 않는 듯하군요.",
        "잘 도망쳐 나온 것 같습니다. 도망치는 것도 전법의 하나이군요.",
    ];

    /// <summary>도망에 실패할 때(<c>0x005600B0</c> 벌).</summary>
    private static readonly string[] Caught =
    [
        "큰일이다! 둘러 싸였다.",
        "안됩니다! 도망칠 수 없습니다!",
        "제독, 실패입니다. 응전합시다.",
        "추격을 따돌릴 수 없었습니다. 제독, 싸웁시다.",
        "안되겠다. 제독, 포기하고 싸웁시다.",
    ];

    /// <summary>응전을 고를 때(<c>0x00560170</c> 벌).</summary>
    private static readonly string[] FightOn =
    [
        "맞받아 공격할 것이죠. 역시 제독은 이래야지.",
        "제독, 저희들의 실력을 보여 줍시다.",
        "때마침 몸이 근질거리던 차입니다. 해치워 버립시다.",
        "저런 녀석들, 적도 아닙니다. 해치워 버립시다.",
        "모두들! 준비되었나!",
    ];

    /// <summary>고르는 세 줄(<c>0x0055F9D0</c>·<c>0x0055F9E0</c>·<c>0x0055F9F0</c>).</summary>
    public static readonly string[] Choices = ["교섭한다", "도망간다", "응전한다"];

    /// <summary>창 제목 — 이름 있는 적만 "적의 공격" 이고 나머지는 "해전" 이다.</summary>
    public static string TitleOf(EnemyKind kind) =>
        kind == EnemyKind.Raider ? "적의 공격" : "해전";

    private static string One(string[] lines, Random rng) => lines[rng.Next(lines.Length)];

    /// <summary>들어설 때 건네는 말.</summary>
    public static string GreetOf(in Enemy foe, Random rng) =>
        foe.Kind == EnemyKind.Raider
            ? string.Format(Greetings[0][0], foe.Name)
            : One(Greetings[(int)foe.Kind], rng);

    public static string NoWordsWord(Random rng) => One(NoWords, rng);
    public static string DemandWord(int gold, Random rng) => string.Format(One(Demands, rng), gold);
    public static string TooPoorWord(Random rng) => One(TooPoor, rng);
    public static string PaidWord(Random rng) => One(Paid, rng);
    public static string TalkFailedWord(Random rng) => One(TalkFailed, rng);
    public static string FledWord(Random rng) => One(Fled, rng);
    public static string CaughtWord(Random rng) => One(Caught, rng);
    public static string FightOnWord(Random rng) => One(FightOn, rng);

    // ── 셈 ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// 적이 부르는 돈(<c>0x00455A36</c>).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   요구액 = ((적장 능력 넷의 합 + 1) * 적 함대수 * 20 / 100) * 100
    /// </code>
    /// 백 닢 단위로 내림한다.
    /// </remarks>
    public static int Demand(in Enemy foe) => foe.Sum * foe.Ships * 20 / 100 * 100;

    /// <summary>
    /// 교섭이 통할 확률(%). <c>0x004559AE</c> 그대로다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   확률 = 내 함대수 / 2 + 매력 * 10 + 밑값
    ///   밑값 = rand(10) + 30   (또는 딴 갈래에서 rand(5))
    /// </code>
    /// 갈래를 가르는 조건을 아직 못 짚어 <b>너그러운 쪽(rand(10) + 30)</b>만 쓴다.
    /// </remarks>
    public static int TalkOdds(Player player, Random rng) =>
        player.Ships.Count / 2 + player.AbilityOf(Ability.Charm) * 10 + rng.Next(10) + 30;

    /// <summary>
    /// 교섭이 통하는지. <b>추격대·토벌대에게는 통하지 않는다</b>(<c>0x0045585C</c>).
    /// </summary>
    public static bool CanTalk(EnemyKind kind) => kind != EnemyKind.Chaser;

    /// <summary>백 가운데 몇이면 되는지를 굴린다 — 게임의 <c>0x004B7C62</c> 다.</summary>
    public static bool Roll(int percent, Random rng) => rng.Next(100) < percent;

    /// <summary>
    /// 도망칠 수 있는지(<c>0x00455B5F</c>).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   0x00455B83  eax = (내 값 - 적 값) + 인자 + 100
    ///   0x00455B88  eax /= 나눔수
    ///   0x00455B8D  rand(eax) 가 0 이 아니면 성공
    /// </code>
    /// 두 전역(<c>0x005B3950</c>·<c>0x005B3954</c>)이 무엇인지 아직 못 짚었다 — 꼴로 보아
    /// 함대 속력이다. 그래서 <b>배의 추진력</b>으로 갈음한다. 셈의 얼개(차 + 100 을 나누어
    /// 굴린다)는 게임 것 그대로다.
    /// </remarks>
    public static bool Escapes(Player player, in Enemy foe, Random rng)
    {
        int mine = player.Ships.Count == 0 ? 0 : player.Ships.Max(s => s.Speed);
        int theirs = foe.Ships * 10;
        int odds = (mine - theirs + 100) / 2;
        return rng.Next(Math.Max(1, odds)) != 0;
    }

    // ── 누구를 만나는가 ─────────────────────────────────────────────────────

    /// <summary>
    /// 적 함대 이름표(<c>0x00548900</c>). 앞의 여섯이 바다 것이고 그 뒤는 뭍 도적이다.
    /// </summary>
    public static readonly string[] Names =
    [
        "터키 해군", "이슬람 함대", "아랍 해적", "콜세르", "해적", "사략 함대",
    ];

    /// <summary>
    /// 만난 무리 하나를 굴린다. <b>이쪽은 우리가 지어낸 것이다</b> — 게임은 지도 위를
    /// 돌아다니는 함대 객체를 넘긴다.
    /// </summary>
    public static Enemy Roll(Random rng)
    {
        // 해적이 흔하고 이슬람 함대가 그다음이다. 추격대는 악명이 붙어야 나오므로
        // 여기서는 안 낸다(아직 그 조건을 안 옮겼다).
        var kind = rng.Next(4) == 0 ? EnemyKind.Islam : EnemyKind.Pirate;
        string name = kind == EnemyKind.Islam ? Names[1] : Names[rng.Next(2, Names.Length)];

        int ships = rng.Next(1, 5);
        int sum = rng.Next(40, 200) + 1;      // 적장 능력 넷의 합
        return new Enemy(kind, name, ships, sum);
    }
}
