using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Engine;

/// <summary>
/// 정보 판에 채울 것들 — 발견물 이름 · 힌트 이름 · 계약 판.
/// </summary>
/// <remarks>
/// 같은 판을 <b>바다에서도 도시에서도</b> 낸다(지도 창의 "정보", 도시 커맨드 창의
/// "○○ 정보"). 게임도 한 창이다. 그래서 채울 것을 여기 한 벌만 두고 두 화면이 갖다 쓴다 —
/// 예전에는 발견물 이름 짓는 코드가 두 곳, 힌트 이름이 두 곳에 따로 있었다.
///
/// 창을 <b>어떻게 띄우는지</b>는 화면이 정한다. 계약이 없을 때 지도 창은 한 줄로 물리고
/// 도시 창은 빈 판을 내는데, 그 차이는 그대로 두었다.
/// </remarks>
public static class GameInfo
{
    /// <summary>지금까지 발견한 것의 이름. 소지품 판의 발견물 칸에 쓴다.</summary>
    public static List<string> DiscoveryNames(Game game)
    {
        var table = game.Discoveries?.Table;
        return [.. game.Player.Discoveries.Order()
                   .Select(id => table?.Find(id)?.Name ?? $"발견물 {id}")];
    }

    /// <summary>
    /// 가지고 있는 힌트의 이름. 표 → DB → 번호 차례로 물러선다.
    /// </summary>
    /// <remarks>
    /// <b>찾아낸 것의 힌트는 뺀다.</b> 힌트는 「아직 못 찾은 것을 어디서 찾나」를 적어 둔
    /// 쪽지라, 그 발견물을 이미 찾았으면 목록에 남을 까닭이 없다. 힌트와 발견물은
    /// 번호로 짝을 맺는다(힌트의 <c>Discovery</c> 와 발견물의 <c>Hint</c> 가 같은 값).
    /// </remarks>
    public static List<string> HintNames(Game game)
    {
        var table = game.Discoveries?.Table;
        var hints = game.Hints;

        return [.. game.Player.Hints.Order()
                   .Where(id => !Found(game, table, hints, id))
                   .Select(game.HintName)];
    }

    /// <summary>그 힌트가 가리키는 발견물을 이미 찾았는지.</summary>
    private static bool Found(Game game, DiscoveryTable? table,
                              HintTable? hints, int hint)
    {
        if (table == null || hints?.Find(hint) is not { } row) return false;

        foreach (var found in table.Discoveries)
            if (found.Hint == row.Discovery && game.Player.HasFound(found.Id)) return true;

        return false;
    }

    /// <summary>
    /// 계약 정보 판에 채울 것.
    /// </summary>
    /// <param name="Contract">맺고 있는 계약. 없으면 null 이다.</param>
    /// <param name="HintName">맡은 힌트의 이름.</param>
    /// <param name="Found">계약 중에 찾아낸 것들.</param>
    /// <param name="Evidence">
    /// 그 가운데 <b>아직 지니고 있는</b> 물건 — 팔아 버렸으면 내밀 증거가 없다.
    /// </param>
    public readonly record struct ContractSheet(Contract? Contract, string HintName,
                                                List<string> Found, List<string> Evidence);

    /// <summary>계약 정보 판을 채운다.</summary>
    public static ContractSheet ContractSheetOf(Game game)
    {
        if (game.Player.Contract is not { } contract) return new(null, "", [], []);

        var table = game.Discoveries?.Table;
        var items = game.Items;

        var found = new List<string>();
        var evidence = new List<string>();
        foreach (int id in contract.Found)
        {
            var row = table?.Find(id);
            found.Add(row?.Name ?? $"발견물 {id}");

            if (row is not { GivesItem: true } got || !game.Player.HasItem(got.ItemId)) continue;
            evidence.Add(items?.Find(got.ItemId)?.Name ?? $"아이템 {got.ItemId}");
        }

        return new(contract, game.HintName(contract.Hint), found, evidence);
    }
}
