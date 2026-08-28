namespace CdsHelper.Game.Engine.Town;

/// <summary>술집에서 치르는 값.</summary>
public static class Tavern
{
    /// <summary>
    /// 한잔 사는 값. 게임은 도시마다 파는 술이 달라 값도 다른데(술 표 <c>0x4FF978</c>)
    /// 아직 그 표를 안 읽어 한 값으로 둔다.
    /// </summary>
    public const int DrinkPrice = 10;
}
