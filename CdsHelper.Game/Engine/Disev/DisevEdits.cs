using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.Engine.Disev;

/// <summary>
/// 발견 이벤트 대본을 손으로 갈아 둔 것. <c>DISEV.CDS</c> 위에 덧씌운다.
/// </summary>
/// <remarks>
/// <b>원본 파일은 손대지 않는다</b> — 이 집의 규칙이다
/// (<see cref="CityCultureEdits"/> · <c>도시-왕국-고친것</c> 과 같은 결).
/// 게임 폴더의 <c>DISEV.CDS</c> 는 그대로 두고, 갈아 둔 파트만
/// <c>%APPDATA%\CdsHelper\exe-tables\발견이벤트-고친것.json</c> 에 적는다.
/// 앱이 대본을 열 때 그 위에 씌우므로 우리 놀이에는 곧장 든다
/// (<see cref="DisevRunner.Open"/>).
///
/// <b>원본 게임에 먹이려면 한 번 더 굽는다.</b> <c>CDS_95.EXE</c> 는 JSON 을 모르니
/// 편집기의 「게임에 굽기」가 <see cref="DisevArchive.Save"/> 로 CDS 를 다시 쓴다 —
/// EXE 패치 창이 <c>custom_patches.json</c> 을 두고 「적용」할 때만 EXE 를 건드리는 것과
/// 같은 차례다.
///
/// 파트 하나를 통째로 <b>16진 글</b>로 적어 둔다. 대본은 길이가 자유라 칸으로 나누면
/// 되레 어긋나고, 사람이 열어 봐도 어느 파트가 갈렸는지는 번호로 알 수 있다.
/// </remarks>
public static class DisevEdits
{
    /// <summary>적어 둘 파일 이름(<c>발견이벤트-고친것.json</c>).</summary>
    private const string CacheName = "발견이벤트-고친것";

    /// <summary>갈아 둔 파트 하나.</summary>
    /// <param name="Part">발견물 번호이자 파트 번호(0~273).</param>
    /// <param name="Hex">그 파트의 날바이트를 16진으로 적은 것.</param>
    public readonly record struct Entry(int Part, string Hex);

    /// <summary>JSON 으로 적어 두는 알맹이.</summary>
    internal sealed record Snapshot(List<Entry> Parts);

    private static Dictionary<int, byte[]>? _map;

    /// <summary>갈아 둔 것이 바뀌었을 때 알린다.</summary>
    public static event Action? Changed;

    /// <summary>갈아 둔 파트 전부. 하나도 없으면 빈 것.</summary>
    public static IReadOnlyDictionary<int, byte[]> All => Map;

    /// <summary>갈아 둔 파트 수.</summary>
    public static int Count => Map.Count;

    /// <summary>그 파트에 씌워 둔 알맹이. 안 갈았으면 null.</summary>
    public static byte[]? Of(int part) => Map.TryGetValue(part, out var data) ? data : null;

    /// <summary>그 파트를 갈아 씌운다.</summary>
    public static void Set(int part, byte[] data)
    {
        if (part < 0 || data.Length == 0) return;
        if (Of(part) is { } had && had.AsSpan().SequenceEqual(data)) return;

        Map[part] = (byte[])data.Clone();
        Save();
    }

    /// <summary>씌운 것을 걷어 원본 대본으로 되돌린다.</summary>
    public static void Reset(int part)
    {
        if (!Map.Remove(part)) return;
        Save();
    }

    /// <summary>씌운 것을 몽땅 걷는다.</summary>
    public static void ResetAll()
    {
        if (Map.Count == 0) return;
        Map.Clear();
        Save();
    }

    /// <summary>연 아카이브에 갈아 둔 것을 씌운다.</summary>
    public static void ApplyTo(DisevArchive archive)
    {
        foreach (var (part, data) in Map)
            if (part < archive.PartCount) archive.ReplacePart(part, data);
    }

    private static Dictionary<int, byte[]> Map => _map ??= Load();

    private static Dictionary<int, byte[]> Load()
    {
        var saved = TableCache.Read<Snapshot>(CacheName);
        var map = new Dictionary<int, byte[]>();

        foreach (var row in saved?.Data.Parts ?? [])
            if (row.Part >= 0 && DisevScript.ParseHex(row.Hex) is { Length: > 0 } data)
                map[row.Part] = data;

        return map;
    }

    private static void Save()
    {
        var rows = Map.OrderBy(p => p.Key)
                      .Select(p => new Entry(p.Key, DisevScript.Hex(p.Value)))
                      .ToList();

        TableCache.Write(CacheName, new TableCache.Cached<Snapshot>(
            $"{rows.Count}개", new Snapshot(rows), "사람이 고친 것"));
        Changed?.Invoke();
    }
}
