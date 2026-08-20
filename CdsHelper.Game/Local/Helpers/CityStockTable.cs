using System.Text.Json.Serialization;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 도시마다 시장에 내놓는 물건. CDS_95.EXE 의 도시 표에 박혀 있다.
/// </summary>
/// <remarks>
/// <code>
///   도시 표 VA 0x004D14B0 (파일오프셋 0x0CFAB0), 226행 x 136바이트, .rdata
///   +0x00  이름 ptr("리스본")     +0x20  문화권
///   +0x3C  시장 물건 8칸 (i32)    빈 칸은 -1
/// </code>
/// 값은 아이템 번호(<see cref="ItemTable"/> 의 색인)다. 리스본은
/// <c>[33, 34, 37, 66]</c> — 나침반·육분의·레이피아·66번이다.
///
/// <b>메모리를 읽을 까닭이 없다.</b> 게임이 돌 때는 도시 레코드(<c>0x005863B4</c>, 92바이트)
/// 의 <c>+20~+48</c> 에 같은 값이 올라와 있지만, 그것은 이 표를 그대로 옮겨 놓은 것이다 —
/// 켜 놓은 게임에서 226곳을 읽어 이 표와 대 보니 <b>한 칸도 다르지 않았다</b>. 게다가
/// 여기는 <c>.rdata</c> 라 놀이 중에 바뀌지도 않는다(시세는 다르다 — 그쪽은 돌아다닌다).
///
/// 도시 이름과 문화권도 같은 줄에 있지만 그것은 안 읽는다. 앱은 그것을 DB 에서 내고
/// (<see cref="CityTable"/>), 거기서는 사람이 고칠 수도 있기 때문이다.
/// </remarks>
public sealed class CityStockTable
{
    /// <summary>적어 둘 파일 이름(<c>%APPDATA%\CdsHelper\exe-tables\시장물건.json</c>).</summary>
    private const string CacheName = "시장물건";

    private const int TableVa = 0x004D14B0;
    private const int RowSize = 136;

    /// <summary>줄 안에서 시장 칸이 시작되는 자리.</summary>
    private const int StockOffset = 0x3C;

    /// <summary>도시 수.</summary>
    public const int Count = 226;

    /// <summary>한 도시가 내놓는 칸 수.</summary>
    public const int Slots = 8;

    /// <summary>빈 칸.</summary>
    private const int Empty = -1;

    /// <summary>판이 다른 EXE 를 잘못 읽지 않으려고 대 보는 줄 — 리스본.</summary>
    private static readonly int[] Probe = [33, 34, 37, 66];

    /// <summary>JSON 으로 적어 두는 알맹이. 바깥 색인이 도시 번호다.</summary>
    internal sealed record Snapshot(int[][] Stock);

    private readonly int[][] _stock;

    private CityStockTable(Snapshot snapshot) => _stock = snapshot.Stock;

    /// <summary>왜 못 읽었는지. 잘 열렸으면 빈 문자열.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>
    /// 그 도시가 내놓는 아이템 번호들. 빈 칸(-1)은 빼고 낸다 — 없으면 빈 목록.
    /// </summary>
    public IReadOnlyList<int> Of(int cityId) =>
        cityId >= 0 && cityId < _stock.Length ? _stock[cityId] : [];

    /// <summary>물건을 하나라도 내놓는 도시 수.</summary>
    public int CitiesWithStock => _stock.Count(s => s.Length > 0);

    /// <summary>
    /// 표를 연다. 적어 둔 JSON 이 있으면 그것을 읽고, 없거나 판이 갈렸으면 EXE 에서 읽어
    /// 적어 둔다. 둘 다 없을 때만 null 이다.
    /// </summary>
    public static CityStockTable? Open(string gameDirectory)
    {
        var snapshot = ExeTable.Open<Snapshot>(CacheName, gameDirectory, ReadFromExe, out string error);
        LastError = error;
        return snapshot == null ? null : new CityStockTable(snapshot);
    }

    private static Snapshot? ReadFromExe(PeImage exe, out string error)
    {
        error = "";

        var stock = new int[Count][];
        for (int city = 0; city < Count; city++)
        {
            var slots = new List<int>(Slots);
            for (int s = 0; s < Slots; s++)
            {
                int id = exe.Int(TableVa + city * RowSize + StockOffset + s * 4);
                // 0 은 잠수폭탄이라 시장에 안 나온다. 게임도 0 이하는 빈 칸으로 본다.
                if (id > 0 && id < ItemTable.Count) slots.Add(id);
                else if (id != Empty && id != 0) { /* 표 밖의 값은 조용히 버린다 */ }
            }
            stock[city] = [.. slots];
        }

        if (!stock[0].SequenceEqual(Probe))
        {
            error = "시장 물건 표가 기대한 모양이 아닙니다(다른 판의 EXE 일 수 있습니다)";
            return null;
        }

        return new Snapshot(stock);
    }
}
