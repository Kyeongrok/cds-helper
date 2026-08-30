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
    public static bool HeirBorn(Random random) => random.Next(HeirRoll) < HeirWin;
}
