using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Engine.Town;

/// <summary>
/// 항구에서 발견물을 알리는 규칙 — 무엇을 알릴 수 있고 명성이 얼마나 오르는지.
/// </summary>
public static class Harbor
{
    /// <summary>알려서 오르는 명성 — 보수를 이만큼으로 나눈다(<c>0x0047E851</c>).</summary>
    public const int FamePerReward = 70;

    /// <summary>아무리 하찮아도 이만큼은 오른다(<c>0x0047E853</c>).</summary>
    public const int FameFloor = 10;

    /// <summary>그것을 알려서 오르는 명성.</summary>
    /// <remarks>
    /// 후원자에게 하는 <b>보고</b>는 셈이 다르다 — <see cref="Palace.FameFor"/> 는 보수/50
    /// 이고 늦으면 반이다. 같은 발견물이라도 계약으로 맡아 보고하는 편이 후하다.
    /// </remarks>
    public static int FameFor(DiscoveryTable.Record row) =>
        Math.Max(FameFloor, row.Reward / FamePerReward);

    /// <summary>
    /// 알리고 나면 <b>피로도가 풀리고 규율이 꽉 찬다</b>(<c>0x0047E885</c> · <c>0x0047E88A</c>).
    /// </summary>
    /// <remarks>
    /// 후원자에게 보고할 때도 똑같이 한다(<c>0x0041156A</c>) — 다만 그쪽은 <b>명성이 오른
    /// 때만</b>이다. 이미 알려진 것을 보고하면 명성 칸과 함께 이 줄도 건너뛴다.
    /// </remarks>
    public static void Celebrate(Player player)
    {
        player.SetFatigue(0);
        player.SetMorale(Player.MaxMorale);
    }

    /// <summary>
    /// 이미 세상에 알려진 것을 알리려 들었을 때 듣는 말(<c>0x0055A298</c>).
    /// </summary>
    /// <remarks>
    /// 판정은 <c>0x004AADB0</c> 이다 — 발견물 인스턴스의 사람 칸 2 에 남의 이름이 올라가
    /// 있으면 참이고, 그러면 <b>명성이 안 오른다</b>. 남이 먼저 보고해 버린 것이라
    /// 우리 쪽에는 아직 그 갈래가 없다(라이벌 항해자를 안 옮겼다).
    /// </remarks>
    public const string AlreadyKnown = "자네, 그런 건 벌써 모두 알고 있네.";

    /// <summary>
    /// 지금 항구에서 알릴 수 있는 발견물. 찾은 차례대로다.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x00476D20</c> · <c>0x00476DA0</c> 그대로다.
    /// <code>
    ///   발견했고(깃발 0x40) · 아직 발표 안 했고(0x80 없음)
    ///   계약이 있으면 그 계약의 유적 번호와 <b>다른</b> 것만
    /// </code>
    /// 계약으로 맡은 것은 항구에서 못 알린다 — 그쪽은 후원자에게 보고해야 한다.
    /// 그래서 <b>계약 없이 발견한 것</b>이 여기 뜬다.
    /// </remarks>
    public static List<DiscoveryTable.Record> Announceable(Player player,
                                                          DiscoveryTable? table,
                                                          HintTable? hints)
    {
        if (table == null) return [];

        int target = player.Contract is { } contract && hints?.Find(contract.Hint) is { } hint
                   ? hint.Discovery : -1;

        var rows = new List<DiscoveryTable.Record>();
        foreach (int id in player.Discoveries.Order())
        {
            if (player.HasAnnounced(id)) continue;
            if (table.Find(id) is not { } row) continue;
            if (target >= 0 && row.Hint == target) continue;   // 계약의 목표는 뺀다
            rows.Add(row);
        }
        return rows;
    }
}
