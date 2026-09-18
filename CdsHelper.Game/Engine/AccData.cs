using System.IO;
using System.Text.Json;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Engine;

/// <summary>
/// <b>누적 캐릭터</b> — 은퇴한 제독을 다섯 자리에 올려 두고 다음 판에 남으로 내보내는 자리.
/// </summary>
/// <remarks>
/// 게임은 자리마다 파일 둘을 쓴다(<c>0x00425220</c> 이 EXE 옆을 가리킨다).
/// <code>
///   ACCDATA.CDS      지금 놀고 있는 제독의 <b>행적 기록</b>(0x00419F90 이 만들고 0x0041A070 이 덧붙인다)
///   ACCDATA%d.IDX    은퇴한 제독의 인물 레코드 — 직렬화한 CPerson 그대로(0x0041A270)
///   ACCDATA%d.ACC    그 행적을 DISEV 대본으로 옮긴 것 — 다음 판에서 되돌려 튼다(0x0040D1D0)
/// </code>
/// 등록은 <c>0x0041AB90</c> 이다 — <b>초심자용 캐릭터(<c>0x005A4D1A</c> 비트 8)는 못 올리고</b>,
/// 그 밖에는 명성·나이·돈 어떤 조건도 없다. 빈 자리를 <c>0x0041AC60</c> 이 찾아 주고,
/// 다섯이 다 차 있으면 <b>아무 말 없이</b> 세이브만 지운다(원본 그대로다 — 5명이 찼다고
/// 이르는 문구 <c>0x00571D88</c> 는 절대 안 걸리는 가지에 있다).
///
/// 여기서는 행적 대본까지는 안 옮기고 <b>인물 레코드</b>만 올린다 — 다음 판에 남으로 서는 자리
/// (인물 번호 276~280)와 그 대본 되돌리기는 아직이다.
/// </remarks>
public static class AccData
{
    /// <summary>자리 수 — 다섯이다(<c>0x0041AC60</c> 의 <c>cmp 5</c>).</summary>
    public const int Slots = 5;

    /// <summary>은퇴한 제독 하나.</summary>
    /// <param name="Face">얼굴 번호. MALE 그림의 자리다.</param>
    /// <param name="RetiredOn">은퇴한 놀이 날짜.</param>
    public sealed record Character(string Name, string Family, string Given, int Face,
                                   int Nation, int JobIndex, int Blood, int Fame,
                                   int[] Abilities, Dictionary<string, int> Skills,
                                   Dictionary<string, int> Tongues, DateTime RetiredOn);

    /// <summary>적어 두는 자리 — 세이브와 같은 폴더다.</summary>
    public static string Path => System.IO.Path.Combine(
        System.IO.Path.GetDirectoryName(GameSave.Path) ?? "", "누적캐릭터.json");

    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    /// <summary>올라 있는 사람들. 없으면 빈 목록이다.</summary>
    public static List<Character> Load()
    {
        try
        {
            if (!File.Exists(Path)) return [];
            return JsonSerializer.Deserialize<List<Character>>(File.ReadAllText(Path)) ?? [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>그 이름을 쓰는 사람이 이미 있는지(<c>0x0045CF4A</c>).</summary>
    /// <remarks>새 제독을 지을 때 걸린다 — 「같은 성명을 쓰는 누적 캐릭터가 있습니다」(<c>0x00571758</c>).</remarks>
    public static bool NameTaken(string name) =>
        Load().Any(c => string.Equals(c.Name, name, StringComparison.Ordinal));

    /// <summary>
    /// 은퇴한 제독을 빈 자리에 올린다(<c>0x0041AB90</c> → <c>0x0041A270</c>).
    /// </summary>
    /// <returns>올렸으면 true. 자리가 다 찼으면 false 인데, 게임도 그때는 <b>아무 말이 없다</b>.</returns>
    public static bool Register(Player player)
    {
        var all = Load();
        if (all.Count >= Slots) return false;

        all.Add(new Character(player.Name, player.Family, player.Given, player.Face,
                              player.Nation, player.JobIndex, player.Blood, player.Fame,
                              [.. player.Abilities],
                              new Dictionary<string, int>(player.Skills),
                              new Dictionary<string, int>(player.Tongues),
                              player.Date));
        return Save(all);
    }

    /// <summary>다섯 자리를 통째로 비운다 — 「누적 캐릭터를 등장시키지 않는다」를 고른 뒤다(<c>0x0041AD55</c>).</summary>
    public static bool Clear() => Save([]);

    private static bool Save(List<Character> all)
    {
        try
        {
            string dir = System.IO.Path.GetDirectoryName(Path) ?? "";
            if (dir.Length > 0) Directory.CreateDirectory(dir);
            File.WriteAllText(Path, JsonSerializer.Serialize(all, Pretty));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
