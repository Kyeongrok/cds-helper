namespace CdsHelper.Support.Local.Models;

/// <summary>
/// 주인공의 직업 — 능력치가 어느 쪽으로 기우는지와, 처음에 든 기술을 정한다.
/// </summary>
/// <param name="Name">이름.</param>
/// <param name="Bias">능력치 보정(체력·지력·무력·매력·운·신앙심 차례).</param>
/// <param name="Skills">처음부터 든 기술 — (기술 번호, 자리).</param>
/// <remarks>
/// 이름은 게임 표(<c>0x00560AA8</c>) 여덟 그대로다. 능력치 보정은
/// <c>0x0051ACA0</c>(직업마다 32바이트 = int 여덟)에서 읽은 값이다 — 탐험가가 온통 0 인
/// <b>밑자리</b>고 나머지가 거기서 기운다.
/// <code>
///   탐험가  0  0  0  0  0  0  0  0
///   발굴자  2 -3 -2  5 -2  0  1  1
///   사냥꾼  2 -3  2 -3  2  0  7 -1
///   정복자 -2  3 -2  3 -2  0  3 -1
///   해적   -2 -4  5 -1  2  0  0  1
///   전도사 -2  4 -3 -1  2  0  1 -1
///   상인   -3  2  4 -2 -1  0  3  1
///   군인    2  2  2 -5  2  0  2 -1
/// </code>
/// <b>처음 든 기술은 게임 표에서 읽은 것이 아니다</b> — 화면(탐험가 · 항해술2 · 측량2 ·
/// 회계2)에서 본 것을 밑삼아 직업 결에 맞춰 지었다. 게임이 어느 표에서 꺼내는지는 아직
/// 못 짚었다.
/// </remarks>
public sealed record Job(string Name, int[] Bias, (int Skill, int Level)[] Skills)
{
    /// <summary>새 놀이에서 고를 수 있는 직업 넷. 게임 화면에도 이 넷만 뜬다.</summary>
    public const int Choosable = 4;

    /// <summary>여덟. 앞의 넷만 새 놀이에서 고른다.</summary>
    public static readonly Job[] All =
    [
        new("탐험가", [0, 0, 0, 0, 0, 0],
            [(Skill.Sailing, 2), (Skill.Survey, 2), (Skill.Accounting, 2)]),
        new("발굴자", [2, -3, -2, 5, -2, 0],
            [(Skill.Survey, 3), (Skill.History, 2), (Skill.Sailing, 1)]),
        new("사냥꾼", [2, -3, 2, -3, 2, 0],
            [(Skill.Shooting, 3), (Skill.Sword, 2), (Skill.Sailing, 1)]),
        new("정복자", [-2, 3, -2, 3, -2, 0],
            [(Skill.Sword, 2), (Skill.Gunnery, 2), (Skill.Sailing, 2)]),
        new("해적", [-2, -4, 5, -1, 2, 0],
            [(Skill.Sword, 3), (Skill.Gunnery, 2), (Skill.Handling, 1)]),
        new("전도사", [-2, 4, -3, -1, 2, 0],
            [(Skill.Theology, 3), (Skill.Medicine, 2), (Skill.Rhetoric, 1)]),
        new("상인", [-3, 2, 4, -2, -1, 0],
            [(Skill.Accounting, 3), (Skill.Rhetoric, 2), (Skill.Sailing, 1)]),
        new("군인", [2, 2, 2, -5, 2, 0],
            [(Skill.Gunnery, 3), (Skill.Sword, 2), (Skill.Handling, 1)]),
    ];

    /// <summary>번호로 찾는다. 표 밖이면 탐험가.</summary>
    public static Job Of(int index) =>
        index >= 0 && index < All.Length ? All[index] : All[0];
}

/// <summary>
/// 능력치 여섯 — 게임 표(<c>0x00560A88</c>) 차례 그대로다.
/// </summary>
/// <remarks>
/// 새 놀이 화면에는 앞의 다섯만 뜬다. <b>신앙심</b>은 안 보이는 자리에서 따로 굴린다
/// (<c>0x0045D590</c> 의 <c>rand(80) + 21</c>).
/// </remarks>
public static class Ability
{
    /// <summary>이름 여섯.</summary>
    public static readonly string[] Names = ["체력", "지력", "무력", "매력", "운", "신앙심"];

    /// <summary>번호.</summary>
    public const int Body = 0, Mind = 1, Might = 2, Charm = 3, Luck = 4, Faith = 5;

    /// <summary>화면에 뜨는 수(신앙심은 안 뜬다).</summary>
    public const int Shown = 5;

    /// <summary>능력치가 들 수 있는 폭(<c>0x0045D576</c> 의 <c>clamp(값, 20, 100)</c>).</summary>
    public const int Min = 20, Max = 100;

    /// <summary>능력치를 굴릴 때 얹는 밑값(<c>add $0x32</c>).</summary>
    public const int Base = 50;

    /// <summary>신앙심을 굴리는 폭(<c>rand(80) + 21</c>).</summary>
    public const int FaithRoll = 80, FaithBase = 21;

    /// <summary>
    /// 나이대 보정. 게임은 스택에 세 벌을 깔고 나이로 고른다(<c>0x0045D49A</c> 벌).
    /// </summary>
    /// <remarks>
    /// <b>짝이 정확한지는 못 확인했다.</b> 값이 20 · -20 · 0 인 것과 나이 26·36 에서
    /// 벌이 갈리는 것(<c>cmp $0x1A</c> · <c>cmp $0x24</c>)까지는 읽었는데, 스택 자리와
    /// 능력치의 짝을 확실히 못 짚었다. 폭(±20)만 맞춰 둔다.
    /// </remarks>
    public static readonly int[][] AgeBias =
    [
        [20, -20, 0, 0, 0, 0],      // 26살 밑
        [0, 0, -20, 20, -20, 0],    // 26~35
        [-20, 20, 0, 0, 0, 0],      // 36살 위
    ];

    /// <summary>나이가 어느 벌에 드는지.</summary>
    public static int TierOf(int age) => age >= 36 ? 2 : age >= 26 ? 1 : 0;

    /// <summary>
    /// 능력치 다섯과 신앙심을 굴린다.
    /// </summary>
    /// <remarks>
    /// <code>
    /// 45d548  값 = 밑값[i] + rand(나이) + 직업보정[i] + 나이보정[i] + 50
    /// 45d576  0x0049E540(값, 20, 100)                       ; 잘라 넣는다
    /// 45d590  신앙심 = rand(80) + 21
    /// </code>
    /// </remarks>
    public static int[] Roll(Job job, int age, Random rng)
    {
        var tier = AgeBias[TierOf(age)];
        var stats = new int[Names.Length];

        for (int i = 0; i < Shown; i++)
            stats[i] = Math.Clamp(Base + rng.Next(Math.Max(1, age)) + job.Bias[i] + tier[i],
                                  Min, Max);

        stats[Faith] = rng.Next(FaithRoll) + FaithBase;
        return stats;
    }

    /// <summary>
    /// 능력치를 굴린 뒤 남는 보너스 포인트.
    /// </summary>
    /// <remarks>
    /// 다섯을 더한 값으로 갈린다(<c>0x0045D5D5</c>). <b>잘 굴렸을수록 덜 준다.</b>
    /// <code>
    ///   합 &gt;= 451  rand(6)
    ///   합 &gt;= 351  rand(11) + 5
    ///   합 &gt;= 251  rand(11) + 10
    ///   그 밖      rand(11) + 20
    /// </code>
    /// </remarks>
    public static int BonusFor(int[] stats, Random rng)
    {
        int sum = 0;
        for (int i = 0; i < Shown; i++) sum += stats[i];

        return sum >= 451 ? rng.Next(6)
             : sum >= 351 ? rng.Next(11) + 5
             : sum >= 251 ? rng.Next(11) + 10
             : rng.Next(11) + 20;
    }
}
