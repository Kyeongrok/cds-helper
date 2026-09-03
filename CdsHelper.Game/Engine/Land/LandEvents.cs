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
/// <b>아직 안 옮긴 것</b> — 같은 덩이에 회오리 · 유성 · 일식이 더 있다
/// (<c>0x00427E2C</c> 어름). 확률과 조건을 아직 못 짚었다.
/// </remarks>
public static class LandEvents
{
    /// <summary>독충 — 이름 둘.</summary>
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

    /// <summary>
    /// 오늘 무엇을 마주치는지. 아무 일도 없으면 null.
    /// </summary>
    /// <remarks>
    /// 게임은 지형 번호로 가른다 — 짐승은 2, 독충은 6 이다. 우리 쪽에는 그 번호가 없어
    /// <b>둘 다 굴린다</b>. 짐승이 먼저다.
    /// </remarks>
    public static Meeting? Meet(GameRandom dice, IReadOnlyList<int> here)
    {
        if (dice.Next(BeastOdds) == 0) return new Meeting(BeastAt(here, dice), Venomous: false);
        if (dice.Next(VerminOdds) == 0) return new Meeting(Vermin[dice.Next(Vermin.Length)], true);
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

    /// <summary>죽는 대원 수 — 독사가 더 사납다(<c>0x0042796D</c>).</summary>
    private static int Kill(Player player, in Meeting met, GameRandom dice)
    {
        int dead = dice.Next(met.Venomous ? 5 : 3) + 3;
        dead = Math.Min(dead, player.Crew);
        player.AddCrew(-dead);
        return dead;
    }
}
