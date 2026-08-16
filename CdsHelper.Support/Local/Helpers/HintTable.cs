using System.IO;

namespace CdsHelper.Support.Local.Helpers;

/// <summary>
/// CDS_95.EXE 안의 힌트 표. 힌트 하나마다 한 줄이고, 이름·갈래·등급과 후원자에게 손 벌릴 때
/// 쓰는 자금·기한이 다 들어 있다.
/// </summary>
/// <remarks>
/// <code>
///   표     VA 0x004D8E84, 186행 x 80바이트
///   +0x00  등급(1~5)          +0x04  발견물 일련번호(0~527)
///   +0x08  갈래(0~7)          +0x10  자금(닢, 5000~300000)
///   +0x14  기한 기준(0~7)     +0x18  제 번호
///   +0x4C  이름 ptr("인도항로")
///   갈래 이름표 0x00560C60[8]
/// </code>
/// 왕궁에서 후원자를 설득할 때 게임이 이 표를 그대로 쓴다(<c>0x004AEF50</c>) —
/// 자금은 <c>+0x10</c> 에 후원율을 곱해 10닢 단위로 내리고, 계약 기한은 <c>+0x14</c> 에 하나를
/// 더한 햇수다(<c>0x004AF19D</c> 가 <c>0x004D8E98 = 표 + 0x14</c> 를 읽는다).
///
/// 힌트를 <b>얻었는지</b>는 이 표가 아니라 실행 중 배열(<c>0x0058B4E0</c>, 8바이트 x 186)에
/// 있다. 그쪽은 EXE 파일에 없다(BSS) — 세이브에서 읽어야 한다.
///
/// 표는 EXE 에서 그때그때 읽는다. 판이 다르면 열리지 않을 뿐 엉뚱한 값을 내지 않도록
/// 첫 줄이 "서회항로"인지 보고 아니면 물러난다.
/// </remarks>
public sealed class HintTable
{
    private const int TableVa = 0x004D8E84;
    private const int RowCount = 186;
    private const int RowSize = 80;
    private const int CategoryNamesVa = 0x00560C60;

    /// <summary>
    /// 이름이 놓일 수 있는 가장 낮은 자리(.rdata 시작). 이보다 낮으면 코드 구역이라 이름이 아니다.
    /// </summary>
    /// <remarks>
    /// 표의 마지막 줄(186번째)은 자리만 채워 둔 것이라 이름 자리에 엉뚱한 값(0x00402F00)이
    /// 들어 있다. 그대로 읽으면 깨진 글자가 힌트 이름인 척한다 — 그래서 자리부터 본다.
    /// </remarks>
    private const uint LowestNameVa = 0x004C3000;

    /// <summary>갈래 수 — 지리·역사·보물·종교·교역품·미신·생물·민족.</summary>
    public const int CategoryCount = 8;

    /// <summary>힌트 한 줄.</summary>
    /// <param name="Id">힌트 번호(0~185).</param>
    /// <param name="Name">이름("인도항로", "아가멤논의 마스크").</param>
    /// <param name="Grade">등급 1~5. 후원자 안목이 모자라면 "이야기가 막연하다" 고 물린다.</param>
    /// <param name="Category">갈래 0~7. 후원자마다 좋아하는 갈래가 다르다.</param>
    /// <param name="Funds">이 힌트를 좇는 데 드는 자금(닢). 후원율을 곱하기 전 값이다.</param>
    /// <param name="Deadline">계약 기한(년).</param>
    /// <param name="Discovery">발견물 일련번호.</param>
    public readonly record struct Hint(
        int Id, string Name, int Grade, int Category, int Funds, int Deadline, int Discovery);

    private readonly List<Hint> _hints;

    private HintTable(List<Hint> hints, string[] categories)
    {
        _hints = hints;
        CategoryNames = categories;
    }

    /// <summary>왜 못 읽었는지. 잘 열렸으면 빈 문자열.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>갈래 이름 8개(번호 차례).</summary>
    public IReadOnlyList<string> CategoryNames { get; }

    /// <summary>표에 있는 힌트 전부.</summary>
    public IReadOnlyList<Hint> Hints => _hints;

    /// <summary>그 번호의 힌트. 표 밖이면 null.</summary>
    /// <remarks>
    /// 자리 채움 줄을 건너뛰므로 자리와 번호가 어긋날 수 있다 — 자리로 먼저 짚어 보고
    /// 번호가 다르면 훑는다.
    /// </remarks>
    public Hint? Find(int id)
    {
        if (id < 0) return null;
        if (id < _hints.Count && _hints[id].Id == id) return _hints[id];
        foreach (var h in _hints)
            if (h.Id == id) return h;
        return null;
    }

    /// <summary>그 힌트의 이름. 표 밖이면 번호로 물러선다.</summary>
    public string NameOf(int id) => Find(id)?.Name ?? $"힌트 {id}";

    /// <summary>그 갈래의 이름. 번호가 이상하면 빈 문자열.</summary>
    public string CategoryOf(int category) =>
        category >= 0 && category < CategoryNames.Count ? CategoryNames[category] : "";

    /// <summary>
    /// 후원자가 낼 자금. 게임처럼 후원율을 곱해 10닢 단위로 내리고, 20닢 밑으로는 안 내려간다.
    /// </summary>
    /// <param name="supportRate">후원율(%). patrons.json 의 supportRate 다.</param>
    public static int FundsFor(Hint hint, int supportRate)
    {
        long paid = (long)hint.Funds * supportRate / 100;
        if (paid < 20) paid = 20;
        return (int)(paid / 10 * 10);
    }

    /// <summary>게임 폴더의 CDS_95.EXE 에서 표를 읽는다. 못 읽으면 null.</summary>
    public static HintTable? Open(string gameDirectory)
    {
        LastError = "";
        var exe = PeImage.Read(Path.Combine(gameDirectory, "CDS_95.EXE"), out string error);
        if (exe == null) { LastError = error; return null; }

        var hints = new List<Hint>(RowCount);
        for (int k = 0; k < RowCount; k++)
        {
            int row = TableVa + k * RowSize;
            uint namePtr = exe.Word(row + 0x4C);
            if (namePtr < LowestNameVa) continue;        // 자리 채움 줄

            var name = exe.Text(namePtr);
            if (string.IsNullOrEmpty(name)) continue;

            hints.Add(new Hint(
                Id: k,
                Name: name,
                Grade: exe.Int(row + 0x00),
                Category: exe.Int(row + 0x08),
                Funds: exe.Int(row + 0x10),
                Deadline: exe.Int(row + 0x14) + 1,
                Discovery: exe.Int(row + 0x04)));
        }

        // 판이 다른 EXE 를 잘못 읽지 않도록 첫 줄을 확인한다.
        if (hints.Count == 0 || hints[0].Name != "서회항로")
        {
            LastError = "힌트 표가 기대한 모양이 아닙니다(다른 판의 EXE 일 수 있습니다)";
            return null;
        }

        var categories = new string[CategoryCount];
        for (int i = 0; i < CategoryCount; i++)
            categories[i] = exe.Text(exe.Word(CategoryNamesVa + i * 4)) ?? "";

        return new HintTable(hints, categories);
    }
}
