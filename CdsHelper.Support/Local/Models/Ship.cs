namespace CdsHelper.Support.Local.Models;

/// <summary>
/// 가지고 있는 배 한 척 — 선체와, 그 배만의 상태(지금 내구).
/// </summary>
/// <remarks>
/// <see cref="Models.Hull"/> 은 선체 <b>종류</b>라 배마다 다른 것을 담을 수 없다. 조선소
/// 수리가 배마다의 손상을 보므로 한 겹을 두었다.
///
/// 게임은 배 레코드(<c>0x005A4E18</c>, 108바이트)에 이렇게 담는다.
/// <code>
///   +0x38  지금 내구    +0x3C  최대 내구   (0x0044C820 / 0x0044C840)
///   +0x48  지금 돛      +0x4C  최대 돛     (0x0044C860 / 0x0044C880)
/// </code>
/// 수리비는 <b>두 손상을 더한 값</b>이다(<c>0x0044BBF0</c>). 여기서는 <b>내구만</b> 든다 —
/// 우리 선체 표에 돛 값이 없다.
/// </remarks>
public sealed class Ship
{
    /// <param name="hull">선체 종류.</param>
    /// <param name="hp">지금 내구. 안 주면 성한 채로 시작한다.</param>
    public Ship(Hull hull, int? hp = null)
    {
        Hull = hull;
        Hp = Math.Clamp(hp ?? hull.Hp, 0, hull.Hp);
    }

    /// <summary>선체 종류. 이름·적재량 같은 것은 다 여기에 있다.</summary>
    public Hull Hull { get; }

    /// <summary>지금 내구.</summary>
    public int Hp { get; private set; }

    /// <summary>성할 때의 내구.</summary>
    public int MaxHp => Hull.Hp;

    /// <summary>상한 만큼. 성하면 0 이다.</summary>
    public int Damage => Math.Max(0, MaxHp - Hp);

    /// <summary>손볼 데가 있는지.</summary>
    public bool NeedsRepair => Damage > 0;

    /// <summary>그만큼 상한다. 0 밑으로는 안 내려간다.</summary>
    public void Hurt(int amount) => Hp = Math.Clamp(Hp - Math.Max(0, amount), 0, MaxHp);

    /// <summary>말끔히 고친다.</summary>
    public void Repair() => Hp = MaxHp;

    /// <summary>이름은 선체 이름을 그대로 쓴다 — 배마다의 이름("%s호")은 아직 없다.</summary>
    public string Name => Hull.Name;
}
