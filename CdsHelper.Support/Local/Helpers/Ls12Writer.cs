using System.Buffers.Binary;
using System.IO;

namespace CdsHelper.Support.Local.Helpers;

/// <summary>
/// LS11/Ls12 아카이브에 파트 하나를 <b>갈아 끼우거나 덧붙이는</b> 손.
/// </summary>
/// <remarks>
/// <b>압축은 안 한다.</b> 파트 표는 <c>압축크기 == 원본크기</c> 면 날것으로 보므로
/// (<see cref="Ls12Reader.Decode"/> 의 「무압축 저장」 갈래) 새로 넣는 파트만 그렇게 적는다.
/// 건드리지 않는 파트는 <b>원본 덩어리를 그대로 옮겨 붙인다</b> — 다시 압축할 일이 없고
/// 파일도 거의 안 커진다.
///
/// <code>
///   0x000  매직 + 패딩        16바이트   그대로 옮긴다
///   0x010  사전 dictionary    256바이트  그대로 옮긴다
///   0x110  파트 표 12바이트 x N + 끝을 알리는 4바이트 0
///          데이터 덩어리들
/// </code>
/// 자리는 파일 첫머리부터 잰 <b>절대 자리</b>라, 파트를 하나 덧붙이면 표가 12바이트
/// 길어지면서 모든 덩어리가 밀린다. 그래서 통째로 다시 쓴다.
/// </remarks>
public static class Ls12Writer
{
    private const int HeadSize = 0x110;      // 매직 16 + 사전 256
    private const int RowSize = 12;

    /// <summary>왜 못 썼는지. 잘 됐으면 빈 글.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>
    /// 그 파트를 <paramref name="raw"/> 로 갈아 끼운다. <paramref name="part"/> 가 파트 수와
    /// 같으면 <b>맨 뒤에 덧붙인다</b>. 잘 됐으면 참.
    /// </summary>
    /// <param name="path">고칠 아카이브. 없으면 거짓.</param>
    /// <param name="part">갈아 끼울 자리. 0 부터다.</param>
    /// <param name="raw">넣을 날것. 크기는 마음대로다.</param>
    public static bool Put(string path, int part, byte[] raw)
    {
        LastError = "";
        if (raw.Length == 0) { LastError = "넣을 것이 비었습니다"; return false; }

        byte[] data;
        try { data = File.ReadAllBytes(path); }
        catch (IOException e) { LastError = e.Message; return false; }
        catch (UnauthorizedAccessException e) { LastError = e.Message; return false; }

        if (data.Length < HeadSize + 4) { LastError = "아카이브가 너무 짧습니다"; return false; }

        var magic = System.Text.Encoding.ASCII.GetString(data, 0, 4);
        if (magic != "LS11" && magic != "Ls12") { LastError = "LS11/Ls12 가 아닙니다"; return false; }

        // 원본 파트 표를 그대로 읽는다.
        var parts = new List<(uint Comp, uint Uncomp, uint Off)>();
        for (int pos = HeadSize; pos + RowSize <= data.Length; pos += RowSize)
        {
            uint comp = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos));
            if (comp == 0) break;
            parts.Add((comp,
                       BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos + 4)),
                       BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos + 8))));
        }
        if (parts.Count == 0) { LastError = "파트가 없습니다"; return false; }
        if (part < 0 || part > parts.Count)
        {
            LastError = $"파트 자리가 범위 밖입니다(0~{parts.Count})";
            return false;
        }

        // 옮겨 붙일 덩어리들. 갈아 끼우는 자리만 날것으로 바꾼다.
        var blocks = new List<byte[]>(parts.Count + 1);
        for (int i = 0; i < parts.Count; i++)
        {
            var (comp, _, off) = parts[i];
            if (i == part) { blocks.Add(raw); continue; }

            if (off > (uint)data.Length || comp > (uint)data.Length - off)
            {
                LastError = $"파트 {i} 가 파일 밖을 가리킵니다";
                return false;
            }
            blocks.Add(data[(int)off..(int)(off + comp)]);
        }
        if (part == parts.Count) blocks.Add(raw);

        // 표가 길어지면 덩어리가 다 밀리므로 자리를 새로 매긴다.
        int table = HeadSize + blocks.Count * RowSize + 4;
        var made = new byte[table + blocks.Sum(b => b.Length)];
        Array.Copy(data, made, HeadSize);

        int at = table;
        for (int i = 0; i < blocks.Count; i++)
        {
            int row = HeadSize + i * RowSize;
            uint size = (uint)blocks[i].Length;
            // 갈아 끼운 자리만 날것이라 압축크기와 원본크기가 같다.
            uint plain = i == part ? size : parts[i].Uncomp;

            BinaryPrimitives.WriteUInt32BigEndian(made.AsSpan(row), size);
            BinaryPrimitives.WriteUInt32BigEndian(made.AsSpan(row + 4), plain);
            BinaryPrimitives.WriteUInt32BigEndian(made.AsSpan(row + 8), (uint)at);

            Array.Copy(blocks[i], 0, made, at, blocks[i].Length);
            at += blocks[i].Length;
        }
        // 표 끝을 알리는 4바이트 0 은 배열이 이미 0 이라 그대로 둔다.

        try { File.WriteAllBytes(path, made); }
        catch (IOException e) { LastError = e.Message; return false; }
        catch (UnauthorizedAccessException e) { LastError = e.Message; return false; }
        return true;
    }

    /// <summary>
    /// 처음 고칠 때 옆에 백업을 하나 남긴다. 이미 있으면 그대로 둔다.
    /// </summary>
    public static void Backup(string path)
    {
        string keep = path + ".bak";
        try
        {
            if (File.Exists(path) && !File.Exists(keep)) File.Copy(path, keep);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
