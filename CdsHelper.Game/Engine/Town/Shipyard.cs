using CdsHelper.Game.Engine.Models;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Engine.Town;

/// <summary>
/// 조선소에서 치르는 값 — 매각 · 수리 · 개조.
/// </summary>
/// <remarks>
/// 값은 다 <b>선체값에 견준 비율</b>이다. 게임 선체값은 만~이십오만 닢인데 우리
/// <see cref="Hull.Price"/> 는 조선소 화면에서 옮긴 100~500 짜리 사다리라 자릿수가
/// 다르다 — 그래서 액수가 아니라 나누는 수만 게임 것을 쓴다.
///
/// 묻고 알리는 것은 화면(<see cref="UI.Views.CityPicView"/>)이 맡는다. 여기 있는 것은
/// 얼마인지와 무엇을 잃는지뿐이다.
/// </remarks>
public static class Shipyard
{
    /// <summary>배를 팔 때 받는 값 — 선체 매각값에 도시 시세를 먹인다(<c>0x0044C0A0</c>).</summary>
    public static int SellPrice(Ship ship, int cityRate) =>
        Math.Max(1, ship.Hull.SellPrice * cityRate / 100);

    /// <summary>손상 한 점을 고치는 값의 밑수. 게임은 여기에 <c>rand(4)</c> 를 더한다.</summary>
    public const int RepairRate = 26;

    /// <summary>
    /// 배 한 척을 고치는 값.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   0x0044BBF0  손상 = (최대내구 - 지금내구) + (최대돛 - 지금돛)   ; 음수는 0
    ///   0x0044BAA1  값 = (rand(4) + 26) * 손상                        ; 26~29 곱
    ///   0x0044BABD  값 = 값 x 도시 시세 / 100                          ; 적어도 1
    /// </code>
    /// 우리 선체 표에는 돛 값이 없어 <b>내구만</b> 센다.
    /// </remarks>
    public static int RepairCost(Ship ship, int cityRate, Random random) =>
        Math.Max(1, (RepairRate + random.Next(4)) * ship.Damage * cityRate / 100);

    /// <summary>
    /// 이 마을에서 고칠 수 있는 배 — 함대 먼저, 그 뒤가 이 마을이 맡은 배다.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x0044BC50(도시, 0)</c> 이다. 이 목록이 비면 조선소 차림표의 <b>"수리" 줄이
    /// 꺼진다</b>(<c>0x0044BD40</c> 이 <c>0x0044BC50 &gt; 0</c> 을 본다) — 그래서 평소에는
    /// "수리가 필요한 배는 없네!" 를 볼 일이 없다.
    /// </remarks>
    public static List<(Ship Ship, bool Docked)> RepairTargets(Player player, int cityId)
    {
        var hurt = new List<(Ship Ship, bool Docked)>();
        foreach (var ship in player.Ships) if (ship.NeedsRepair) hurt.Add((ship, false));
        foreach (var ship in player.DockedAt(cityId)) if (ship.NeedsRepair) hurt.Add((ship, true));
        return hurt;
    }

    /// <summary>개조 값을 나누는 수(<c>0x004955F9</c> 의 <c>mov $0xf,%ecx ; idiv</c>).</summary>
    public const int RefitDivisor = 15;

    /// <summary>마스트 값을 나누는 수(<c>0x00494C32</c> 의 <c>mov $5,%ecx</c>).</summary>
    public const int MastDivisor = 5;

    /// <summary>돛 값을 나누는 수(<c>mov $0x14,%ecx</c>) — 돛종류 변경도 같다.</summary>
    public const int SailDivisor = 20;

    /// <summary>개조 한 번 값 — 선체 구입값의 열다섯 분의 일.</summary>
    public static int RefitCost(Ship ship) => Math.Max(1, ship.Hull.Price / RefitDivisor);

    /// <summary>마스트 하나를 세우는 값 — 선체 구입값의 다섯 분의 일.</summary>
    public static int MastCost(Ship ship) => Math.Max(1, ship.Hull.Price / MastDivisor);

    /// <summary>돛 하나를 달거나 갈아 다는 값 — 선체 구입값의 스무 분의 일.</summary>
    public static int SailCost(Ship ship) => Math.Max(1, ship.Hull.Price / SailDivisor);

    /// <summary>
    /// 그 줄이 무엇을 얻고 무엇을 잃는지 알려 주는 물음. 게임 문구 그대로다
    /// (<c>0x00531938</c> 벌).
    /// </summary>
    public static string RefitWarning(string item) => item switch
    {
        Facility.RefitTonnage =>
            "적재용량과 함께 중량도 조금 올라가지만, 스피드와 내구력이 조금 떨어지네. 괜찮겠나?",
        Facility.RefitReinforce =>
            "내구력이 올라가지만, 스피드와 적재중량이 조금 떨어지네. 괜찮겠나?",
        _ => "용량과 함께 적재용량도 조금 올라가지만, 스피드와 내구력이 조금 떨어지네. 괜찮겠나?",
    };
}
