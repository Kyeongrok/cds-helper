using System.Text.Json.Serialization;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 나라 표 40개 — 이름·언어·수도. CDS_95.EXE 에 박혀 있다.
/// </summary>
/// <remarks>
/// <code>
///   표 VA 0x004CA370, 24바이트 x 78 (색인 = 나라 번호)
///   +0x00  이름 ptr("잉글랜드 왕국")   +0x04  언어(0~13)   +0x08  수도 도시 번호
///   +0x0C  갈래(0x004A1800 이 읽는다)
/// </code>
/// <b>나라는 마흔이 아니라 일흔여덟이다.</b> 예전에는 마흔에서 끊어 명·조선·일본과
/// 아메리카 나라들이 통째로 빠졌다 — 세이브가 이 표를 <c>0x005859C0</c> 부터
/// <c>0x00585EA0</c> 까지 적는데(<c>0x0047858E</c>) 한 칸이 열여섯이라 <b>일흔여덟</b>
/// 칸이다.</code>
/// <b>도시의 언어는 여기서 온다.</b> 도시 줄에는 언어가 없고 나라 번호만 있다
/// (<see cref="CityExeTable.NationOf"/>) — 도시를 정복하면 정복한 나라의 말을 쓰게 되므로
/// 도시에 박아 둘 수가 없다.
///
/// 잉글랜드 왕국은 언어 3(게르만어)에 수도 38(런던)이다 — 게임 화면과 맞다.
/// 나라 40개 중 37개는 제 수도의 나라 번호와 맞물린다. 어긋나는 셋(스웨덴·프로이센·
/// 모노모타파)은 <b>수도를 다른 나라와 함께 쓰는</b> 나라들이라, 그 도시가 한쪽만
/// 가리킬 수밖에 없는 것이다.
/// </remarks>
public sealed class NationTable
{
    /// <summary>적어 둘 파일 이름(<c>%APPDATA%\CdsHelper\exe-tables\나라표.json</c>).</summary>
    private const string CacheName = "나라표";

    private const int TableVa = 0x004CA370;
    private const int RowSize = 24;

    /// <summary>나라 수.</summary>
    public const int Count = 78;

    /// <summary>알맹이 모양 판 — 갈래를 더하고 나라 수를 고치면서 올렸다.</summary>
    private const int Shape = 2;

    private const int ProbeId = 11;
    private const string ProbeName = "잉글랜드 왕국";

    /// <summary>나라 하나.</summary>
    /// <param name="Language">언어 번호. 이름은 <see cref="CityBuildingTable.LanguageNames"/>.</param>
    /// <param name="Capital">수도 도시 번호.</param>
    /// <param name="Sect">
    /// 나라 갈래(<c>+0x0C</c>). <b>도시의 문화권과는 다른 물건</b>이다
    /// (<see cref="CityExeTable.CultureOf"/>).
    /// </param>
    /// <remarks>
    /// 갈래는 유럽이 0, 동유럽·정교권이 2, <b>이슬람권이 3</b>(사파비만 4), 인도가 3·5·6,
    /// 명·조선이 7, 일본이 6, 아메리카가 8 이다. <b>3 이나 4 일 때만 적대 도시에
    /// 「잠입한다」가 켜진다</b>(<c>0x004A1800</c>) — 명이나 조선에 못 숨어드는 까닭이다.
    /// </remarks>
    [method: JsonConstructor]
    public readonly record struct Nation(int Id, string Name, int Language, int Capital,
                                         int Sect = 0);

    /// <summary>JSON 으로 적어 두는 알맹이.</summary>
    internal sealed record Snapshot(Nation[] Nations);

    private readonly Nation[] _nations;

    private NationTable(Snapshot snapshot) => _nations = snapshot.Nations;

    /// <summary>왜 못 읽었는지. 잘 열렸으면 빈 문자열.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>
    /// 나라 전부. 색인이 곧 나라 번호다.
    /// </summary>
    /// <remarks>사람이 고쳐 둔 것이 있으면 그것이 이긴다(<see cref="NationEdits"/>).</remarks>
    public IReadOnlyList<Nation> Nations => [.. _nations.Select(NationEdits.Apply)];

    /// <summary>그 나라. 범위 밖이면 null.</summary>
    public Nation? Find(int id) =>
        id >= 0 && id < _nations.Length ? NationEdits.Apply(_nations[id]) : null;

    /// <summary>고치기 전, EXE 에 적힌 그대로의 나라. 범위 밖이면 null.</summary>
    public Nation? Original(int id) => id >= 0 && id < _nations.Length ? _nations[id] : null;

    /// <summary>표를 연다. 적어 둔 JSON 이 있으면 그것을 읽는다.</summary>
    public static NationTable? Open(string gameDirectory)
    {
        var snapshot = ExeTable.Open<Snapshot>(CacheName, gameDirectory, ReadFromExe, out string error,
                                              Shape);
        LastError = error;
        return snapshot == null ? null : new NationTable(snapshot);
    }

    private static Snapshot? ReadFromExe(PeImage exe, out string error)
    {
        error = "";

        var nations = new Nation[Count];
        for (int id = 0; id < Count; id++)
        {
            int row = TableVa + id * RowSize;
            nations[id] = new Nation(
                id,
                exe.Text(exe.Word(row + 0x00)) ?? "",
                exe.Int(row + 0x04),
                exe.Int(row + 0x08),
                exe.Int(row + 0x0C));
        }

        if (nations[ProbeId].Name != ProbeName)
        {
            error = "나라 표가 기대한 모양이 아닙니다(다른 판의 EXE 일 수 있습니다)";
            return null;
        }

        return new Snapshot(nations);
    }
}
