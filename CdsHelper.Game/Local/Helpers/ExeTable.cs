using System.IO;

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
///
/// 앱 DB 처럼 <b>바뀌는</b> 원본은 이 규칙을 쓰면 안 된다 — 고친 값이 안 비친다.
/// 그런 것은 <see cref="CityTable"/> 처럼 원본을 먼저 보는 규칙을 쓴다.
/// </remarks>
internal static class ExeTable
{
    private const string ExeName = "CDS_95.EXE";

    /// <summary>EXE 에서 표 하나를 읽어 내는 일. 못 읽으면 null 과 까닭을 낸다.</summary>
    public delegate T? Reader<T>(PeImage exe, out string error) where T : class;

    /// <summary>적어 두는 자리(게임데이터 창이 여기를 훑는다).</summary>
    public static string Folder => TableCache.Folder;

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
        string exePath = string.IsNullOrEmpty(gameDirectory)
            ? "" : Path.Combine(gameDirectory, ExeName);
        string stamp = StampOf(exePath);
        var cached = TableCache.Read<T>(name);

        // 적어 둔 것이 지금 EXE 와 같거나, EXE 가 아예 없으면 그대로 쓴다.
        //
        // Source 가 비었으면 한 번 다시 굽는다. 그 항목을 뒤에 붙였기 때문에 그 전에 적어 둔
        // 파일에는 없고, 도장이 같으면 다시 구울 일이 없어 영영 안 채워진다 — 그러면
        // 게임데이터 창이 어디서 온 표인지 끝내 못 보여 준다. EXE 가 없을 때는 채울 길이
        // 없으므로 그냥 쓴다.
        bool usable = cached != null
                      && (stamp.Length == 0 || (cached.Stamp == stamp && cached.Source.Length > 0));
        if (usable) return cached!.Data;

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

        TableCache.Write(name, new TableCache.Cached<T>(stamp, fresh, ExeName));
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
}
