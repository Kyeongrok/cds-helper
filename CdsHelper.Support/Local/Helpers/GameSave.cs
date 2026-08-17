using System.IO;
using System.Text.Json;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Support.Local.Helpers;

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

    /// <summary>세이브 파일 자리.</summary>
    public static string Path => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CdsHelper", "SAVEDATA.CDS");

    /// <summary>적어 두는 것.</summary>
    /// <param name="Version">형식 판. 나중에 늘릴 때 본다.</param>
    /// <param name="SavedAt">적은 때(현실 시각).</param>
    /// <param name="Mates">술집에서 부하로 삼은 사람. 판 2 부터 있어 옛 세이브에서는 null 이다.</param>
    /// <param name="Met">낯을 튼 사람. 이 사람들만 술집에서 이름이 보인다.</param>
    public sealed record Data(
        int Version, DateTime SavedAt, int Gold, DateTime Date,
        int CityId, string CityName, Dictionary<string, int> Skills, List<int> Hints,
        List<string>? Mates = null, List<string>? Met = null);

    /// <summary>지금 상태를 적는다. 실패하면 까닭을 돌려준다(성공이면 빈 문자열).</summary>
    public static string Save(Player player)
    {
        var data = new Data(2, DateTime.Now, player.Gold, player.Date,
                            player.CityId, player.CityName,
                            new Dictionary<string, int>(player.Skills), [.. player.Hints],
                            [.. player.Mates], [.. player.Met]);
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
