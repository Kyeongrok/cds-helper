using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Engine.Sea;

/// <summary>바다에서 하루를 넘길 때 일어날 수 있는 일.</summary>
/// <remarks>
/// 게임은 일곱 갈래를 굴리고(<c>0x004746CD</c> 의 <c>rand(7)</c>) 갈래마다 딴 처리로
/// 뛴다(점프표 <c>0x00474D7C</c>). 여기서 흉내내는 것은 넷째·다섯째 — 폭풍과 눈보라뿐이다.
/// 나머지는 부하·역병 같은 아직 없는 것을 건드린다.
/// <code>
///   0  0x004746F9   암초        3  0x00474B4A  반란
///   1  0x00474812   무풍        4  0x00474BD5  폭풍     ← 옮겼다
///   2  0x004749FA   병          5  0x00474BD5  눈보라   ← 옮겼다
///                               6  0x004746F9  암초(0 과 같은 자리)
/// </code>
/// </remarks>
public enum SeaEventKind
{
    /// <summary>폭풍. 갈래 넷.</summary>
    Storm,

    /// <summary>눈보라. 갈래 다섯.</summary>
    Blizzard,
}

/// <summary>폭풍이 지나간 뒤에 남은 것.</summary>
/// <param name="Kind">폭풍인지 눈보라인지.</param>
/// <param name="Hurt">배마다 깎인 내구. 함대 차례대로다.</param>
/// <param name="Lost">놓친 배의 이름.</param>
public sealed record SeaEventResult(SeaEventKind Kind, IReadOnlyList<int> Hurt,
                                    IReadOnlyList<string> Lost)
{
    /// <summary>어느 배든 상했는지.</summary>
    public bool AnyHurt => Hurt.Any(h => h > 0);

    /// <summary>폭풍이면 "폭풍", 눈보라면 "눈보라". 게임 문구에 그대로 끼운다.</summary>
    public string Word => Kind == SeaEventKind.Storm ? "폭풍" : "눈보라";
}

/// <summary>
/// 바다 사건 판정. 게임의 <c>0x00474680</c>(일어나는가)과 <c>0x00474DA0</c>(뒷정리)을
/// 옮긴 것이다.
/// </summary>
/// <remarks>
/// <code>
/// ; 일어나는가  0x00474680
/// 474680  if ([0x5A4D40] &lt;= 9) return           ; 열흘 넘게 항해했을 때만
/// 4746a6  edi = [항해사+0x40] * 25
/// 4746b4  edi += [0x5B60D4] + 0x1A              ; 항해 능력 + 26
/// 4746c5  if (edi &gt;= rand(200)) return          ; 안 일어난다
/// 4746cd  edi = rand(7)                          ; 갈래
/// 4746db  if (그 갈래 비트가 이미 서 있으면) return
/// 4746f2  jmp *0x474D7C[edi*4]
/// </code>
/// 사건 갈래는 함대 객체 <c>+0xD4</c> 의 비트 하나씩으로 든다
/// (<c>0x00474630</c> 세우기 · <c>0x00474660</c> 보기). 뒷정리가 그 비트를 도로 끈다.
/// </remarks>
public static class SeaEvents
{
    /// <summary>이 날수를 넘겨야 사건이 일어난다(<c>0x00474680</c> 의 <c>cmpl $9</c>).</summary>
    public const int MinDaysAtSea = 10;

    /// <summary>사건 갈래 수(<c>rand(7)</c>).</summary>
    public const int KindCount = 7;

    /// <summary>폭풍과 눈보라의 갈래 번호. 점프표에서 둘 다 <c>0x00474BD5</c> 로 간다.</summary>
    public const int StormKind = 4, BlizzardKind = 5;

    /// <summary>안 일어나게 하는 밑값(<c>add edi, 0x1A</c>).</summary>
    public const int SafeBase = 26;

    /// <summary>항해술 한 자리가 더해 주는 안전(<c>edi * 25</c>).</summary>
    public const int SafePerLevel = 25;

    /// <summary>판정에 굴리는 주사위 폭(<c>push 0xC8</c>).</summary>
    public const int SafeRoll = 200;

    /// <summary>사건이 걸리는 항해 기술 이름.</summary>
    public const string SkillName = "항해술";

    /// <summary>폭풍이 부는 위도 띠(도). 무역풍 자리다.</summary>
    public const double StormLatMin = 10, StormLatMax = 25;

    /// <summary>눈보라가 치는 위도 띠(도).</summary>
    public const double BlizzardLatMin = 60, BlizzardLatMax = 75;

    /// <summary>
    /// 오늘 무슨 일이 있는지. 없으면 <c>null</c>.
    /// </summary>
    /// <param name="player">함대.</param>
    /// <param name="lat">지금 위도(북이 양수).</param>
    /// <param name="rng">주사위.</param>
    public static SeaEventKind? Roll(Player player, double lat, Random rng)
    {
        if (player.DaysAtSea <= MinDaysAtSea) return null;

        int safe = player.LevelOf(SkillName) * SafePerLevel + SafeBase;
        if (safe >= rng.Next(SafeRoll)) return null;

        int kind = rng.Next(KindCount);
        if (kind != StormKind && kind != BlizzardKind) return null;

        return BandOf(lat);
    }

    /// <summary>
    /// 그 위도에서 부는 것. 띠 밖이면 <c>null</c>.
    /// </summary>
    /// <remarks>
    /// 게임은 위도를 <c>0x005B63B4</c> 에 0~20000 으로 들고(10000 이 적도) 띠를 이렇게 나눈다.
    /// <code>
    ///   폭풍     0x1C37~0x22B9 · 0x2B67~0x31E9   = 적도에서 10~25도
    ///   눈보라   0x0683~0x0D06 · 0x411A~0x479D   = 적도에서 60~75도
    /// </code>
    /// 여덟 값이 10000 을 가운데 두고 짝을 이룬다 — 남북이 같다.
    /// </remarks>
    public static SeaEventKind? BandOf(double lat)
    {
        double a = Math.Abs(lat);
        if (a is >= StormLatMin and <= StormLatMax) return SeaEventKind.Storm;
        if (a is >= BlizzardLatMin and <= BlizzardLatMax) return SeaEventKind.Blizzard;
        return null;
    }

    /// <summary>폭풍이 올리는 피로도(<c>0x00474D18</c> 의 <c>rand(11) + 0x14</c>).</summary>
    public static int TireOf(Random rng) => rng.Next(11) + 20;

    /// <summary>
    /// 폭풍을 맞는다. 배마다 내구를 깎고, 0 이 된 배는 놓친다.
    /// </summary>
    /// <remarks>
    /// <code>
    /// ; 뒷정리  0x00474DA0 — 폭풍/눈보라 자리(0x00474EB1~)
    /// 474ef9  esi = 100 - 돛(0x44C860)
    /// 474f0c  esi = esi / 10 + rand(3)                 ; 손상
    /// 474f28  내구 = clamp(내구 - 손상, 0, 150)          ; 0x44C810 세터
    /// 474f5a  돛   = clamp(돛   - 손상, 기함?1:0, 250)   ; 0x44C850 세터
    /// 474f74  if (돛 == 0) 그 배를 함대에서 뺀다(0x473E60) — "눈에 띄지 않습니다"
    /// </code>
    /// <b>손상은 돛이 성할수록 작다</b> — 성한 배는 rand(3), 너덜너덜한 배는 열 남짓 깎인다.
    /// 우리 배는 돛을 안 들므로 그 자리에 <b>내구 백분율</b>을 넣었다. 뜻은 같다 —
    /// 한 번 상하기 시작하면 다음 폭풍이 더 아프다.
    ///
    /// 기함은 안 잃는다. 게임도 기함 자리의 돛을 1 밑으로 안 내린다(<c>ebp</c>).
    /// </remarks>
    public static SeaEventResult Resolve(Player player, SeaEventKind kind, Random rng)
    {
        player.Tire(TireOf(rng));

        var hurt = new int[player.Ships.Count];
        for (int i = 0; i < hurt.Length; i++)
        {
            var ship = player.Ships[i];
            int worn = ship.MaxHp <= 0 ? 0 : 100 - ship.Hp * 100 / ship.MaxHp;
            hurt[i] = worn / 10 + rng.Next(3);
        }

        // 뒤에서부터 깎아야 배를 잃어도 앞 칸의 짝이 안 어긋난다.
        var lost = new List<string>();
        for (int i = hurt.Length - 1; i >= 0; i--)
        {
            var ship = player.Ships[i];
            bool flag = i == player.Flagship;
            ship.Hurt(hurt[i], floor: flag ? 1 : 0);
            if (ship.Hp == 0 && !flag && player.Ships.Count > 1)
            {
                lost.Add(ship.Name);
                player.LoseShip(i);
            }
        }
        lost.Reverse();

        return new SeaEventResult(kind, hurt, lost);
    }
}
