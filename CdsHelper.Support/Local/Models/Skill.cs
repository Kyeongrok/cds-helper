namespace CdsHelper.Support.Local.Models;

/// <summary>
/// 조합에서 배울 수 있는 기술.
/// </summary>
/// <remarks>
/// 무엇을 가르치는지는 건물마다 다르다 — 건물 표(<c>CityBuildingTable</c>, CdsHelper.Game)의
/// 비트마스크에서 온다. 조합·교회·학자 저택이 가르친다.
/// </remarks>
public static class Skill
{
    /// <summary>배울 수 있는 가장 높은 자리.</summary>
    public const int MaxLevel = 3;

    /// <summary>한 자리 올리는 데 드는 값(닢). 자리와 상관없이 같다.</summary>
    public const int Price = 120;

    /// <summary>
    /// 그 자리까지 올리는 데 걸리는 달수. 0→1 은 석 달, 2 는 여섯 달, 3 은 열두 달이다.
    /// </summary>
    public static int MonthsFor(int nextLevel) => nextLevel switch
    {
        1 => 3,
        2 => 6,
        3 => 12,
        _ => 0,
    };
}
