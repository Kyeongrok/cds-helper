using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Engine.Sea;

/// <summary>
/// 상륙해서 <b>자재로 배를 고친다</b>(<c>0x0048E140</c>) — 상륙 차림표의 셋째 줄이다.
/// </summary>
/// <remarks>
/// 조선소와 달리 <b>돈이 안 들고 날도 안 간다</b> — 드는 것은 자재뿐이다. 선원·피로도·규율·
/// 명성 어느 것도 안 건드린다.
/// <code>
///   0048e15e  자재가 0 통이면  「수리하는데 필요한 자재가 없습니다!」            0x00570AB8
///   0048e1a9  조선기술이 0 이면「조선기술을 가진 사람이 없습니다!」              0x00570AE0
///   0048e36b  고칠 배가 없으면「어느 배도 다 완전합니다. 수리할 필요는 없습니다.」0x00570B30
///   0048e4f8  올림값 = (조선기술 + 1) x 자재 통수                               ; 주사위 없음
///   0048e541  내구 = min(내구 + 올림값, 최대내구)
/// </code>
/// <b>되돌이다</b> — 한 척을 고치고 나면 자재가 남는 한 배 고르기부터 다시 묻는다.
///
/// 조선기술은 <b>제독과 부관(자리 0) 가운데 높은 쪽</b>이고(<c>0x0047CCA0(0xA,0,-1,-1,-1)</c>),
/// 부관이 더 높으면 부관이 「제가 수리하겠습니다.」(<c>0x00570BB0</c>) 하고 나선다.
///
/// <b>추진력은 안 옮겼다.</b> 원본은 배 레코드에 지금 추진력(<c>+0x38</c>)과 최대 추진력
/// (<c>+0x3C</c>)을 따로 들고 둘 다 올리는데, 우리 배는 <see cref="Ship.Speed"/> 하나가
/// 곧 최대라(해전에서 깎이면 최대째 준다) 되돌릴 자리가 없다.
/// </remarks>
public static class ShoreRepair
{
    /// <summary>조선기술 자리(기능 열셋 가운데 열한째).</summary>
    public static readonly string Skill = Support.Local.Models.Skill.Names[Support.Local.Models.Skill.Shipwright];

    /// <summary>자재 한 통이 올리는 값 — <c>조선기술 + 1</c>(<c>0x0048E446</c>).</summary>
    public static int PerBarrel(int shipwright) => shipwright + 1;

    /// <summary>
    /// 그 배를 다 고치는 데 드는 통 수(<c>0x0048E447</c>) — 모자란 만큼을 올려 나눈다.
    /// </summary>
    public static int BarrelsFor(Ship ship, int shipwright)
    {
        int per = PerBarrel(shipwright);
        return per <= 0 ? 0 : (ship.MaxHp - ship.Hp + per - 1) / per;
    }

    /// <summary>고칠 데가 있는 배인지 — 성한 배는 목록에 안 오른다(<c>0x0048E284</c>).</summary>
    public static bool Damaged(Ship ship) => ship.Hp < ship.MaxHp;

    /// <summary>한 번에 고르게 하는 배 수(<c>0x0048E360</c> 의 <c>cmp 8</c>).</summary>
    public const int MaxListed = 8;
}
