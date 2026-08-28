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
    public static int FameFor(DiscoveryTable.Record row) =>
        Math.Max(FameFloor, row.Reward / FamePerReward);

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
