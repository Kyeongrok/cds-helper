using System.Text.Json.Serialization;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 칸을 지날 수 있는지 가르는 표. CDS_95.EXE 에 박혀 있다.
/// </summary>
/// <remarks>
/// WORLD.CDS 의 한 칸은 16비트다. 게임은 그것을 이렇게 읽는다(칸 읽개 <c>0x00426D70</c>,
/// 부류 판정 <c>0x00426710</c>).
/// <code>
///   타일  = 낱말 &amp; 0x3FFF        아래 14비트
///   색인  = 타일 &amp; 0xFF          그 아래 한 바이트 — <b>비트 7 도 쓴다</b>
///   부류  = 표[색인]                표 VA 0x004CD048, 256바이트
/// </code>
/// 부르는 쪽이 부류를 어떻게 쓰는지는 아홉 군데를 다 봤다.
/// <code>
///   0x0049485A  test eax,eax / jz 참 / cmp eax,1 / je 참       -> 0·1 이 참
///   0x0048A794  cmp eax,2 / jge                                -> 2 이상이 뭍
///   0x0048B255  cmp eax,2 / je                                 -> 2 가 뭍
/// </code>
/// 그래서 <b>부류 0·1 은 물, 2 이상은 뭍</b>이다. 지도에 실제로 나오는 칸은
/// 물 70.8% · 뭍 29.2% 로 갈린다(255 도 8.7% 나오는데 뭍 쪽이다).
///
/// <b>우리가 오래 틀렸던 자리다.</b> 그동안은 그림을 그리려고 재어 둔 육지 비율
/// (<c>WorldMapRenderer.GetCoastLandRatio</c>)이 반을 넘으면 막았는데, 그것은 색을 섞는
/// 비율이지 통행 규칙이 아니다. 그래서 런던 앞 하구처럼 육지가 50~55% 인 칸이
/// 막혀 게임에서는 들어가지는 데를 못 들어갔다. 이 표로는 그 칸들이 다 물이다.
/// </remarks>
public sealed class TerrainTable
{
    /// <summary>적어 둘 파일 이름(<c>%APPDATA%\CdsHelper\exe-tables\지형표.json</c>).</summary>
    private const string CacheName = "지형표";

    private const int TableVa = 0x004CD048;

    /// <summary>표 칸 수. 타일의 아래 한 바이트로 찾는다.</summary>
    public const int Count = 256;

    /// <summary>이 부류까지가 물이다. 배는 여기만 지난다.</summary>
    public const int WaterMax = 1;

    /// <summary>이 부류부터가 뭍이다. 말은 여기만 지난다.</summary>
    public const int LandMin = 2;

    /// <summary>판이 다른 EXE 를 잘못 읽지 않으려고 대 보는 값.</summary>
    private const int ProbeSea = 0, ProbeLand = 1;

    /// <summary>JSON 으로 적어 두는 알맹이.</summary>
    internal sealed record Snapshot(byte[] Classes);

    private readonly byte[] _classes;

    private TerrainTable(Snapshot snapshot) => _classes = snapshot.Classes;

    /// <summary>왜 못 읽었는지. 잘 열렸으면 빈 문자열.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>
    /// 그 칸의 부류. <paramref name="low"/> 는 WORLD.CDS 칸의 <b>첫 바이트 그대로</b>다
    /// (비트 7 을 지우면 안 된다).
    /// </summary>
    public int ClassOf(byte low) => _classes[low];

    /// <summary>배가 지날 수 있는 칸인지.</summary>
    public bool CanSail(byte low) => _classes[low] <= WaterMax;

    /// <summary>말이 지날 수 있는 칸인지.</summary>
    public bool CanWalk(byte low) => _classes[low] >= LandMin;

    /// <summary>
    /// 표를 연다. 적어 둔 JSON 이 있으면 그것을 읽고, 없거나 판이 갈렸으면 EXE 에서 읽어
    /// 적어 둔다. 둘 다 없을 때만 null 이다.
    /// </summary>
    public static TerrainTable? Open(string gameDirectory)
    {
        var snapshot = ExeTable.Open<Snapshot>(CacheName, gameDirectory, ReadFromExe, out string error);
        LastError = error;
        return snapshot == null ? null : new TerrainTable(snapshot);
    }

    private static Snapshot? ReadFromExe(PeImage exe, out string error)
    {
        error = "";

        var classes = new byte[Count];
        for (int i = 0; i < Count; i++)
            classes[i] = (byte)exe.Int(TableVa + i);   // 한 바이트씩이라 아래 8비트만 쓴다

        // 순 바다(0)는 물, 순 육지(1)는 뭍이어야 한다.
        if (classes[ProbeSea] > WaterMax || classes[ProbeLand] < LandMin)
        {
            error = "지형표가 기대한 모양이 아닙니다(다른 판의 EXE 일 수 있습니다)";
            return null;
        }

        return new Snapshot(classes);
    }
}
