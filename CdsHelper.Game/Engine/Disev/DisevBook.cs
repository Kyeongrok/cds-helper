using System.IO;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.Engine.Disev;

/// <summary>
/// 발견 이벤트 대본을 통째로 적어 둔 것 — <c>발견이벤트.json</c>.
/// </summary>
/// <remarks>
/// <b>차례가 이렇다.</b>
/// <list type="number">
///   <item>적어 둔 <c>발견이벤트.json</c> 이 있으면 <b>그것을 읽는다</b>.</item>
///   <item>없으면 게임 폴더의 <c>DISEV.CDS</c> 를 떠서 <b>먼저 적어 두고</b>, 그 다음에 읽는다.</item>
///   <item>편집기가 고치는 것도 이 JSON 이다. 원본 CDS 는 안 건드린다.</item>
///   <item>원본 게임에 먹일 때만 <b>한 번 굽는다</b> — 편집기의 「게임에 굽기」.</item>
/// </list>
/// 이 집이 EXE 표를 다루는 결과 같다(<see cref="ExeTable"/> · <c>발견물표.json</c> ·
/// <c>건물표.json</c>). 원본은 읽기만 하고, 사람이 보고 고치는 것은 늘 JSON 쪽이다.
///
/// 파트 하나를 통째로 <b>16진 글</b>로 적는다. 대본은 길이가 자유라 칸으로 나누면 되레
/// 어긋나고, 어느 파트인지는 번호로 알 수 있다.
///
/// <b>원본이 갈려도 저절로 다시 뜨지는 않는다.</b> 도장은 적어 두되 견주어 버리지는
/// 않는다 — 사람이 고쳐 둔 대본을 게임 파일이 갈렸다고 말없이 지울 수는 없다.
/// 다시 뜨고 싶으면 편집기의 「원본에서 다시 뜨기」를 누른다.
/// </remarks>
public sealed class DisevBook
{
    /// <summary>적어 둘 파일 이름(<c>발견이벤트.json</c>).</summary>
    public const string CacheName = "발견이벤트";

    /// <summary>알맹이 모양 판.</summary>
    private const int SnapshotVersion = 1;

    /// <summary>대본 한 파트.</summary>
    /// <param name="Index">발견물 번호이자 파트 번호(0~273).</param>
    /// <param name="Hex">그 파트의 날바이트를 16진으로 적은 것.</param>
    public readonly record struct Entry(int Index, string Hex);

    /// <summary>JSON 으로 적어 두는 알맹이.</summary>
    internal sealed record Snapshot(List<Entry> Parts);

    private readonly List<byte[]> _parts;
    private readonly bool[] _edited;
    private string _stamp;

    private DisevBook(List<byte[]> parts, string stamp)
    {
        _parts = parts;
        _edited = new bool[parts.Count];
        _stamp = stamp;
    }

    /// <summary>왜 못 열었는지. 잘 열렸으면 빈 문자열.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>적어 둔 파일 자리.</summary>
    public static string Path_ => TableCache.PathFor(CacheName);

    /// <summary>파트 수. 274 다.</summary>
    public int Count => _parts.Count;

    /// <summary>고친 파트가 하나라도 있는지.</summary>
    public bool HasChanges => Array.IndexOf(_edited, true) >= 0;

    /// <summary>그 파트를 고쳤는지.</summary>
    public bool IsEdited(int index) => index >= 0 && index < _edited.Length && _edited[index];

    /// <summary>그 파트의 알맹이. 밖에서 고치지 못하게 베껴 준다.</summary>
    public byte[] Part(int index) =>
        index >= 0 && index < _parts.Count ? (byte[])_parts[index].Clone() : [];

    /// <summary>그 파트를 갈아 끼운다. 적어 두는 것은 <see cref="Save"/> 가 한다.</summary>
    public void Replace(int index, byte[] data)
    {
        if (index < 0 || index >= _parts.Count || data.Length == 0) return;
        if (_parts[index].AsSpan().SequenceEqual(data)) return;

        _parts[index] = (byte[])data.Clone();
        _edited[index] = true;
    }

    /// <summary>
    /// 대본을 연다. 적어 둔 것이 있으면 그것을, 없으면 <c>DISEV.CDS</c> 를 떠서 적고 그것을.
    /// </summary>
    /// <param name="gameDirectory">게임 폴더. 적어 둔 것이 있으면 비어 있어도 열린다.</param>
    public static DisevBook? Open(string gameDirectory)
    {
        LastError = "";

        var cached = TableCache.Read<Snapshot>(CacheName);
        if (cached is { Version: SnapshotVersion } && cached.Data.Parts.Count > 0)
            return FromEntries(cached.Data.Parts, cached.Stamp);

        return Dump(gameDirectory);
    }

    /// <summary>
    /// <c>DISEV.CDS</c> 를 통째로 떠서 <c>발견이벤트.json</c> 에 적고 그것을 연다.
    /// </summary>
    /// <remarks>적어 둔 것이 있어도 <b>덮어쓴다</b> — 「원본에서 다시 뜨기」가 이 길이다.</remarks>
    public static DisevBook? Dump(string gameDirectory)
    {
        LastError = "";

        if (SourcePath(gameDirectory) is not { } path)
        {
            LastError = "게임 폴더에서 DISEV.CDS 를 찾지 못했습니다";
            return null;
        }

        var archive = DisevArchive.Open(path);
        if (archive == null)
        {
            LastError = $"DISEV.CDS 를 읽지 못했습니다 — {DisevArchive.LastError}";
            return null;
        }

        var parts = new List<byte[]>(archive.PartCount);
        for (int i = 0; i < archive.PartCount; i++) parts.Add(archive.Part(i));

        var book = new DisevBook(parts, StampOf(path));
        book.Write();
        return book;
    }

    /// <summary>고친 것을 적어 둔다.</summary>
    public void Save()
    {
        Write();
        Array.Clear(_edited);
    }

    /// <summary>
    /// 적어 둔 대본을 <c>DISEV.CDS</c> 에 굽는다 — 원본 게임에 먹일 때만 쓴다.
    /// </summary>
    /// <remarks>
    /// <c>CDS_95.EXE</c> 는 우리 JSON 을 모른다. 굽기 전에 파트를 죄다 되읽어 대 보고
    /// 날짜 붙인 <c>.bak</c> 을 남긴 뒤에 덮는다(<see cref="DisevArchive.Save"/>).
    /// </remarks>
    /// <returns>남긴 백업 파일 자리. 못 구웠으면 null 이고 까닭은 <see cref="LastError"/> 다.</returns>
    public string? Bake(string gameDirectory)
    {
        LastError = "";

        if (SourcePath(gameDirectory) is not { } path)
        {
            LastError = "게임 폴더에서 DISEV.CDS 를 찾지 못했습니다";
            return null;
        }

        var archive = DisevArchive.Open(path);
        if (archive == null)
        {
            LastError = $"DISEV.CDS 를 읽지 못했습니다 — {DisevArchive.LastError}";
            return null;
        }

        for (int i = 0; i < _parts.Count && i < archive.PartCount; i++)
            archive.ReplacePart(i, _parts[i]);

        if (!archive.HasChanges)
        {
            LastError = "원본과 다른 파트가 없습니다";
            return null;
        }

        string? backup = archive.Save();
        if (backup == null) LastError = DisevArchive.LastError;
        else _stamp = StampOf(path);
        return backup;
    }

    /// <summary>
    /// 그 파트를 <b>원본에서</b> 도로 가져온다. 원본을 못 읽으면 아무 일도 없다.
    /// </summary>
    public bool Restore(int index, string gameDirectory)
    {
        LastError = "";

        if (index < 0 || index >= _parts.Count) return false;
        if (SourcePath(gameDirectory) is not { } path)
        {
            LastError = "게임 폴더에서 DISEV.CDS 를 찾지 못했습니다";
            return false;
        }

        var archive = DisevArchive.Open(path);
        if (archive == null || index >= archive.PartCount)
        {
            LastError = $"DISEV.CDS 를 읽지 못했습니다 — {DisevArchive.LastError}";
            return false;
        }

        _parts[index] = archive.Part(index);
        _edited[index] = true;      // 적어 둔 책과 달라졌으니 저장할 거리가 있다
        return true;
    }

    /// <summary>게임 폴더의 <c>DISEV.CDS</c>. 없으면 null.</summary>
    private static string? SourcePath(string gameDirectory)
    {
        if (string.IsNullOrEmpty(gameDirectory)) return null;

        string path = System.IO.Path.Combine(gameDirectory, "DISEV.CDS");
        return File.Exists(path) ? path : null;
    }

    /// <summary>파일이 갈렸는지 알아보는 도장 — 크기와 쓴 시각이다.</summary>
    private static string StampOf(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return $"{info.Length}:{info.LastWriteTimeUtc.Ticks}";
        }
        catch (IOException)
        {
            return "";
        }
    }

    private static DisevBook? FromEntries(List<Entry> rows, string stamp)
    {
        var parts = new List<byte[]>(rows.Count);
        foreach (var row in rows.OrderBy(r => r.Index))
        {
            if (DisevScript.ParseHex(row.Hex) is not { Length: > 0 } data)
            {
                LastError = $"적어 둔 {CacheName}.json 의 파트 {row.Index} 가 깨졌습니다";
                return null;
            }
            parts.Add(data);
        }
        return new DisevBook(parts, stamp);
    }

    private void Write()
    {
        var rows = new List<Entry>(_parts.Count);
        for (int i = 0; i < _parts.Count; i++) rows.Add(new Entry(i, DisevScript.Hex(_parts[i])));

        TableCache.Write(CacheName, new TableCache.Cached<Snapshot>(
            _stamp, new Snapshot(rows), "DISEV.CDS", SnapshotVersion));
    }
}
