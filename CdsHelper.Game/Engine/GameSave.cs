using System.IO;
using System.Text.Json;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Engine;

/// <summary>
/// 함대 창 놀이의 세이브. 소지금·날짜·있는 도시·배운 기술을 적어 둔다.
/// </summary>
/// <remarks>
/// 파일 이름은 게임과 같은 <c>SAVEDATA.CDS</c> 지만 <b>게임 폴더에는 쓰지 않는다</b> —
/// 거기 있는 것은 진짜 게임 세이브라 덮어쓰면 그 판이 날아간다. 그래서 설정 파일과 같은
/// 자리(<c>%APPDATA%\CdsHelper</c>)에 둔다. 속은 게임 형식이 아니라 우리 것(JSON)이다.
/// </remarks>
public static class GameSave
{
    // 한글이 \uXXXX 로 깨져 보이지 않게 그대로 적는다(사람이 열어 볼 파일이다).
    private static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// 이 판부터 <c>Supplies</c> 의 식량·물이 <b>통이 아니라 단위</b>로 적힌다(한 통이 열).
    /// </summary>
    public const int SupplyUnitsFrom = 16;

    /// <summary>이 판부터 <c>ShipStats</c> 에 포탑수·대포가 함께 적힌다.</summary>
    public const int GunsInStatsFrom = 18;

    /// <summary>세이브 파일 자리.</summary>
    public static string Path => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CdsHelper", "SAVEDATA.CDS");

    /// <summary>적어 두는 것.</summary>
    /// <param name="Version">형식 판. 나중에 늘릴 때 본다.</param>
    /// <param name="SavedAt">적은 때(현실 시각).</param>
    /// <param name="Mates">술집에서 부하로 삼은 사람. 판 2 부터 있어 옛 세이브에서는 null 이다.</param>
    /// <param name="Met">낯을 튼 사람. 이 사람들만 술집에서 이름이 보인다.</param>
    /// <param name="Items">소지품(아이템 번호). 판 3 부터 있어 그 전 세이브에서는 null 이다.</param>
    /// <param name="Supplies">
    /// 실어 둔 보급품(식량·물·자재·탄약). 판 4 부터 있어 그 전 세이브에서는 null 이다.
    /// <b>판 16 부터 식량·물은 통이 아니라 단위</b>다 — 한 통이 열이다.
    /// </param>
    /// <param name="Discoveries">
    /// 발견한 발견물 번호(게임 표의 줄 번호). 판 5 부터 있어 그 전 세이브에서는 null 이다.
    /// </param>
    /// <param name="Contract">
    /// 맺고 있는 계약. 판 6 부터 있어 그 전 세이브에서는 null 이다(= 계약 없음).
    /// </param>
    /// <param name="Crew">
    /// 태우고 있는 선원 수. 판 7 부터 있어 그 전 세이브에서는 null 이다 — 그때는 최저 승원
    /// 수로 채운다(그 전까지 쓰던 값이 그것이다).
    /// </param>
    /// <param name="Announced">
    /// 발표한 발견물 번호. 판 8 부터 있어 그 전 세이브에서는 null 이다(= 아직 아무것도 안 알림).
    /// </param>
    /// <param name="Fame">명성. 판 8 부터 있다 — 발표로 오르기 시작해서 적어 둬야 한다.</param>
    /// <param name="Stored">자택에 맡겨 둔 것(아이템 번호). 판 9 부터 있다.</param>
    /// <param name="Savings">자택에 맡겨 둔 돈(닢). 판 10 부터 있다.</param>
    /// <param name="Ships">가진 배(선체 이름). 판 11 부터 있다 — 그 전에는 아예 안 적었다.</param>
    /// <param name="Flagship">기함이 <paramref name="Ships"/> 에서 몇째인지.</param>
    /// <param name="Docked">마을에 맡겨 둔 배 — 도시 번호마다 선체 이름들.</param>
    /// <param name="ShipHp">배마다의 지금 내구. 판 12 부터 있다 — 없으면 성한 채로 연다.</param>
    /// <param name="DockedHp">맡겨 둔 배의 지금 내구.</param>
    /// <param name="Fatigue">
    /// 선원들이 지친 만큼(0~100). 판 13 부터 있다 — 폭풍이 올리고 자택 휴양이 푼다.
    /// </param>
    /// <param name="DaysAtSea">바다에서 지낸 날수. 판 13 부터 있다.</param>
    /// <param name="ShipStats">
    /// 개조로 갈린 배마다의 값(내구·추진력·용량·중량·승원). 판 14 부터 있다 — 없으면
    /// 선체 기본값으로 연다.
    /// </param>
    /// <param name="DockedStats">맡겨 둔 배의 개조 값.</param>
    /// <param name="Morale">선원들의 사기(0~100). 판 15 부터 있다 — 없으면 꽉 찬 채로 연다.</param>
    /// <param name="ShipNames">
    /// 배마다의 이름. 판 17 부터 있다 — 없으면 선체 이름을 쓴다.
    /// </param>
    /// <param name="DockedNames">맡겨 둔 배의 이름.</param>
    /// <remarks>
    /// 판 18 부터 <c>ShipStats</c> 에 포탑수·대포갈래·대포수가 함께 적힌다. 그 앞 세이브는
    /// 그 칸이 비어(0·0·0) 들어오므로 <b>선체 기본값으로 되살린다</b> —
    /// <see cref="Support.Local.Models.Player.RestoreFleet"/> 의 <c>gunsInStats</c>.
    /// </remarks>
    public sealed record Data(
        int Version, DateTime SavedAt, int Gold, DateTime Date,
        int CityId, string CityName, Dictionary<string, int> Skills, List<int> Hints,
        List<string>? Mates = null, List<string>? Met = null,
        List<int>? Items = null, List<int>? Supplies = null,
        List<int>? Discoveries = null, Deal? Contract = null, int? Crew = null,
        List<int>? Announced = null, int? Fame = null, List<int>? Stored = null,
        int? Savings = null, List<string>? Ships = null, int Flagship = 0,
        Dictionary<int, List<string>>? Docked = null,
        List<int>? ShipHp = null, Dictionary<int, List<int>>? DockedHp = null,
        int? Fatigue = null, int? DaysAtSea = null,
        List<Ship.Stats>? ShipStats = null,
        Dictionary<int, List<Ship.Stats>>? DockedStats = null, int? Morale = null,
        List<string>? ShipNames = null, Dictionary<int, List<string>>? DockedNames = null);

    /// <summary>
    /// 세이브에 적는 계약. <see cref="Support.Local.Models.Contract"/> 를 그대로 적을 수도
    /// 있지만, 세이브 형식은 놀이 쪽 모델이 바뀌어도 그대로여야 하므로 따로 둔다.
    /// </summary>
    /// <param name="Found">이 계약을 맺은 뒤 발견한 것(발견물 번호).</param>
    public sealed record Deal(
        int Hint, string Sponsor, string City, int Amount, DateTime SignedOn, int Years,
        List<int>? Found = null);

    /// <summary>지금 상태를 적는다. 실패하면 까닭을 돌려준다(성공이면 빈 문자열).</summary>
    public static string Save(Player player)
    {
        var data = new Data(18, DateTime.Now, player.Gold, player.Date,
                            player.CityId, player.CityName,
                            new Dictionary<string, int>(player.Skills), [.. player.Hints],
                            [.. player.Mates], [.. player.Met], [.. player.Items],
                            [.. player.Supplies], [.. player.Discoveries], DealOf(player),
                            player.Crew, [.. player.Announced], player.Fame,
                            [.. player.Stored], player.Savings,
                            [.. player.Ships.Select(s => s.Name)], player.Flagship,
                            player.Docked.ToDictionary(
                                e => e.Key, e => e.Value.Select(s => s.Name).ToList()),
                            [.. player.Ships.Select(s => s.Hp)],
                            player.Docked.ToDictionary(
                                e => e.Key, e => e.Value.Select(s => s.Hp).ToList()),
                            player.Fatigue, player.DaysAtSea,
                            [.. player.Ships.Select(s => s.Snapshot())],
                            player.Docked.ToDictionary(
                                e => e.Key, e => e.Value.Select(s => s.Snapshot()).ToList()),
                            player.Morale,
                            [.. player.Ships.Select(s => s.Name)],
                            player.Docked.ToDictionary(
                                e => e.Key, e => e.Value.Select(s => s.Name).ToList()));
        try
        {
            var dir = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(Path, JsonSerializer.Serialize(data, Pretty));
            return "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ex.Message;
        }
    }

    /// <summary>맺고 있는 계약을 적을 꼴로. 계약이 없으면 null.</summary>
    private static Deal? DealOf(Player player) =>
        player.Contract is not { } c ? null
        : new Deal(c.Hint, c.Sponsor, c.City, c.Amount, c.SignedOn, c.Years, [.. c.Found]);

    /// <summary>적어 둔 계약을 놀이 쪽 모델로. 없으면 null.</summary>
    public static Contract? ContractOf(Data saved)
    {
        if (saved.Contract is not { } d) return null;

        var contract = new Contract(d.Hint, d.Sponsor, d.City, d.Amount, d.SignedOn, d.Years);
        contract.Restore(d.Found);
        return contract;
    }

    /// <summary>적어 둔 것을 읽는다. 없거나 깨졌으면 null.</summary>
    public static Data? Load()
    {
        try
        {
            if (!File.Exists(Path)) return null;
            return JsonSerializer.Deserialize<Data>(File.ReadAllText(Path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }
}
