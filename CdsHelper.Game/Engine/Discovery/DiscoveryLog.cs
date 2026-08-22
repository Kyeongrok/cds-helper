using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Engine.Discovery;

/// <summary>
/// 발견물을 맡는다 — 지금 선 칸에서 발견될 것을 찾아 주고, 발견한 것을 주인공에게 적는다.
/// </summary>
/// <remarks>
/// 게임은 항해 루프를 한 번 돌 때마다 <c>0x0048D3F0</c> 에서 이 판정을 한다. 그 차례를
/// 그대로 옮겼다.
/// <code>
///   칸 = (0x5B63B0 / 16, 0x5B63B4 / 16)      배든 말이든 같은 값을 쓴다
///   i  = 0x00425640(칸)                       사각형 안에 드는 것 중 가장 좁은 것
///   0x004AAD20(obj[i])                        깃발 0x08 이 서 있고 아직 안 발견한 것인가
///   표[i].+0x28 == *0x5B61B4                  바다에서 찾을 것을 바다에서 만났는가
///   → DISEV.CDS 의 i 번 사건을 튼다
/// </code>
/// 깃발 <c>0x08</c> 은 새 판을 열 때 표의 <c>+0x24</c> 로 세우고(<c>0x004AA9A0</c>), 나중에
/// 힌트를 얻으면 힌트 쪽에서 세워 준다(<c>0x004AE030</c>). 여기서는 깃발을 들고 있지 않고
/// <see cref="IsOpen"/> 이 그때그때 따진다 — 결과가 같고, 힌트를 잃는 길이 없어 어긋날 수도
/// 없다.
///
/// 사건 연출(DISEV.CDS)은 아직 흉내내지 않는다. 발견을 적고 알리는 것까지만 한다.
/// </remarks>
public sealed class DiscoveryLog
{
    private readonly DiscoveryTable _table;
    private readonly HintTable? _hints;

    /// <param name="table">EXE 의 발견물 표.</param>
    /// <param name="hints">
    /// EXE 의 힌트 표. 없으면 힌트로 열리는 발견물(12 대발견 등)은 끝내 안 열린다 —
    /// 표를 못 읽었다고 아무 데서나 발견되게 하는 것보다 낫다.
    /// </param>
    public DiscoveryLog(DiscoveryTable table, HintTable? hints)
    {
        _table = table;
        _hints = hints;
    }

    /// <summary>발견물 표.</summary>
    public DiscoveryTable Table => _table;

    /// <summary>
    /// 그 발견물이 <b>열려 있는지</b>. 표의 <c>+0x24</c> 가 서 있으면 처음부터 열려 있고,
    /// 아니면 그것을 가리키는 힌트를 얻어야 열린다.
    /// </summary>
    /// <remarks>
    /// 힌트와 발견물은 번호로 짝을 맺는다 — 힌트의 <see cref="HintTable.Hint.Discovery"/> 와
    /// 발견물의 <see cref="DiscoveryTable.Record.Hint"/> 가 같으면 그 짝이다.
    /// </remarks>
    public bool IsOpen(Player player, in DiscoveryTable.Record row)
    {
        if (row.OpenAtStart) return true;
        if (_hints == null) return false;

        foreach (int id in player.Hints)
            if (_hints.Find(id) is { } hint && hint.Discovery == row.Hint) return true;
        return false;
    }

    /// <summary>
    /// 지금 칸에서 발견될 것. 없으면 -1.
    /// </summary>
    /// <param name="player">주인공. 이미 발견한 것과 가진 힌트를 본다.</param>
    /// <param name="cellX">지금 선 칸(가로). 0~2499.</param>
    /// <param name="cellY">지금 선 칸(세로). 0~1249.</param>
    /// <param name="onLand">뭍에 올라 있는지. 게임의 <c>0x5B61B4</c> 자리다.</param>
    /// <remarks>
    /// 사각형이 겹치면 <b>가장 좁은 것</b>이 이긴다 — 신대륙(480칸 폭) 안에 있는 유적이
    /// 신대륙에 가려지지 않게 하려는 것이다. 넓이가 같으면 번호가 작은 쪽이 이긴다
    /// (게임도 <c>0x004256DE</c> 에서 &gt; 로만 갈아 끼운다).
    /// </remarks>
    public int At(Player player, int cellX, int cellY, bool onLand)
    {
        int found = -1;
        int best = int.MaxValue;

        foreach (var row in _table.Discoveries)
        {
            if (!row.Covers(cellX, cellY)) continue;
            if (row.OnLand != onLand) continue;      // 표 +0x28 과 0x5B61B4 를 견주는 자리
            if (row.Indirect) continue;              // 깃발 0x04 가 없어 자리로는 안 잡힌다
            if (player.HasFound(row.Id)) continue;
            if (!IsOpen(player, row)) continue;

            if (row.Span >= best) continue;
            best = row.Span;
            found = row.Id;
        }

        return found;
    }

    /// <summary>
    /// 발견한 것으로 적는다. 주는 아이템이 있으면 소지품에 넣는다.
    /// </summary>
    /// <returns>
    /// 넣은 아이템 번호. 주는 것이 없거나, 이미 발견한 것이거나, <b>소지품이 꽉 차서</b>
    /// 못 들었으면 -1. (발견 자체는 그대로 적힌다 — 물건만 못 드는 것이다.)
    /// </returns>
    public int Discover(Player player, int id)
    {
        if (_table.Find(id) is not { } row) return -1;
        if (!player.Discover(id)) return -1;

        if (!row.GivesItem) return -1;
        return player.Take(row.ItemId) ? row.ItemId : -1;
    }
}
