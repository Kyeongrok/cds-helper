using System.IO;
using System.Text.Json;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 게임 EXE 에 박혀 있는 표를 한 번 읽어 JSON 으로 적어 두고, 다음부터는 그것을 읽는다.
/// </summary>
/// <remarks>
/// 표를 그때그때 EXE 에서 읽으면 게임이 깔려 있어야만 앱이 돈다. 표는 판이 같으면 늘 같은
/// 값이므로 한 번 읽어 적어 두면 그 뒤로는 EXE 가 없어도 된다 — 게임을 지웠거나, 다른
/// 자리에 옮겼거나, 판이 달라 못 읽는 자리에서도 앱은 그대로 돈다.
///
/// 어느 것을 쓸지는 <b>도장</b>으로 가른다. 도장은 EXE 의 크기와 고친 때다.
/// <code>
///   적어 둔 것 있음 · EXE 도장 같음   적어 둔 것을 쓴다 (EXE 를 안 연다)
///   적어 둔 것 있음 · EXE 없음        적어 둔 것을 쓴다 (게임 없이 도는 자리)
///   적어 둔 것 있음 · EXE 도장 다름   EXE 를 다시 읽어 적어 둔다 (판이 갈렸다)
///   적어 둔 것 없음                   EXE 를 읽어 적어 둔다
///   적어 둔 것 없음 · EXE 도 없음     못 연다
/// </code>
/// EXE 를 새로 읽다 실패하면 적어 둔 것으로 버틴다. 낡았을지언정 아무것도 없는 것보다 낫다.
/// </remarks>
internal static class ExeTable
{
    private const string ExeName = "CDS_95.EXE";

    /// <summary>EXE 에서 표 하나를 읽어 내는 일. 못 읽으면 null 과 까닭을 낸다.</summary>
    public delegate T? Reader<T>(PeImage exe, out string error) where T : class;

    // 한글이 \uXXXX 로 깨져 보이지 않게 그대로 적는다(사람이 열어 볼 파일이다).
    private static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>적어 두는 자리. 세이브·설정과 같은 %APPDATA%\CdsHelper 밑이다.</summary>
    public static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CdsHelper", "exe-tables");

    public static string PathFor(string name) => Path.Combine(Folder, name + ".json");

    /// <summary>지금까지 적어 둔 파일 전부. 하나도 없으면 빈 목록.</summary>
    public static IReadOnlyList<string> Saved()
    {
        try
        {
            return Directory.Exists(Folder)
                ? Directory.GetFiles(Folder, "*.json").OrderBy(p => p).ToList()
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>적어 둔 한 벌. 도장을 같이 적어야 판이 갈린 것을 알아본다.</summary>
    private sealed record Cached<T>(string Stamp, T Data) where T : class;

    /// <summary>
    /// 표를 연다. 적어 둔 것이 쓸 만하면 그것을 쓰고, 아니면 EXE 에서 읽어 적어 둔다.
    /// </summary>
    /// <param name="name">적어 둘 파일 이름(확장자 뺀 것).</param>
    /// <param name="gameDirectory">게임 폴더. 비어 있어도 적어 둔 것이 있으면 열린다.</param>
    /// <param name="read">EXE 에서 표를 읽어 내는 일.</param>
    /// <param name="error">못 열었을 때의 까닭. 열렸으면 빈 문자열.</param>
    public static T? Open<T>(string name, string gameDirectory, Reader<T> read, out string error)
        where T : class
    {
        error = "";
        string cachePath = PathFor(name);
        string exePath = string.IsNullOrEmpty(gameDirectory)
            ? "" : Path.Combine(gameDirectory, ExeName);
        string stamp = StampOf(exePath);
        var cached = ReadCache<T>(cachePath);

        // 적어 둔 것이 지금 EXE 와 같거나, EXE 가 아예 없으면 그대로 쓴다.
        if (cached != null && (stamp.Length == 0 || cached.Stamp == stamp)) return cached.Data;

        if (stamp.Length == 0)
        {
            error = $"{ExeName} 을 찾지 못했고 적어 둔 {name}.json 도 없습니다";
            return null;
        }

        var exe = PeImage.Read(exePath, out string exeError);
        T? fresh = null;
        string readError = exeError;
        if (exe != null) fresh = read(exe, out readError);

        if (fresh == null)
        {
            // 새로 못 읽었으면 적어 둔 것으로 버틴다 — 낡았어도 없는 것보다 낫다.
            if (cached != null) return cached.Data;
            error = readError;
            return null;
        }

        WriteCache(cachePath, new Cached<T>(stamp, fresh));
        return fresh;
    }

    /// <summary>
    /// EXE 의 도장. 크기와 고친 때를 붙여 쓴다 — 판이 갈리면 둘 중 하나는 반드시 달라진다.
    /// 통째로 해시를 뜨는 길도 있지만 3MB 를 켤 때마다 읽는 값은 아니다.
    /// </summary>
    private static string StampOf(string exePath)
    {
        if (exePath.Length == 0) return "";
        try
        {
            var info = new FileInfo(exePath);
            return info.Exists ? $"{info.Length}-{info.LastWriteTimeUtc.Ticks}" : "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }

    private static Cached<T>? ReadCache<T>(string path) where T : class
    {
        try
        {
            if (!File.Exists(path)) return null;
            var cached = JsonSerializer.Deserialize<Cached<T>>(File.ReadAllText(path));
            return cached?.Data == null ? null : cached;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;   // 깨졌으면 없는 셈 치고 EXE 에서 다시 읽는다
        }
    }

    private static void WriteCache<T>(string path, Cached<T> cached) where T : class
    {
        try
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonSerializer.Serialize(cached, Pretty));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 적어 두지 못해도 이번 판은 EXE 에서 읽은 값으로 그대로 돈다.
            System.Diagnostics.Debug.WriteLine($"[ExeTable] {path} 를 적지 못했습니다: {ex.Message}");
        }
    }
}
