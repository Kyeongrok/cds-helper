using System.Text.Json.Serialization;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 인물 281명의 밑표(CDS_95.EXE) — 세이브에 없는 <b>나라</b>와 <b>직업</b>이 여기서 온다.
/// </summary>
/// <remarks>
/// <code>
///   표 0x004DF3F0 · 204바이트(0xCC) x 281   꺼내기 0x00431A70 (번호 x 204)
///   +0x00 이름 ptr  +0x04 성 ptr  +0x08 얼굴  +0x10 나이
///   +0x14 나라  → 인물 +0x14 (vtbl+0x14 = 0x0041B210)
///   +0x20 직업  → 인물 +0x1C (vtbl+0x18 = 0x0041B220)
/// </code>
/// 판을 열 때 <c>0x00431A90</c> 이 이 줄을 인물 객체에 옮긴다. 세이브 쪽 직렬화는 두 칸을 안
/// 건드리므로 나라와 직업은 늘 이 표 그대로다.
///
/// 나라 0 은 포르투갈(디아스 · 다 가마), 1 은 에스파니아(베스풋치 · 피사로), 27 은 이슬람
/// (하산 · 아랍 해적)이다. 직업 4 가 해적이다(케말 레이스 · 사략 함대 · 콜세르).
/// 바다에서 붙은 배를 가를 때 쓴다(볼트 <c>59.분석-해적 조우</c> 6절).
/// </remarks>
public sealed class PersonTemplate
{
    /// <summary>적어 둘 파일 이름(<c>%APPDATA%\CdsHelper\exe-tables\인물밑표.json</c>).</summary>
    private const string CacheName = "인물밑표";

    private const int TableVa = 0x004DF3F0;
    private const int RowSize = 0xCC;

    /// <summary>줄 수 — 인물 수와 같다.</summary>
    public const int Count = 281;

    /// <summary>해적 직업 번호. <c>0x0048C2D9</c> 의 <c>cmp eax, 4</c> 다.</summary>
    public const int PirateJob = 4;

    /// <summary>한 사람의 밑줄.</summary>
    [method: JsonConstructor]
    public readonly record struct Template(int Id, int Nation, int Job);

    /// <summary>JSON 으로 적어 두는 알맹이.</summary>
    internal sealed record Snapshot(Template[] Rows);

    private readonly Template[] _rows;

    private PersonTemplate(Template[] rows) => _rows = rows;

    /// <summary>왜 못 열었는지. 잘 열렸으면 빈 문자열.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>표를 연다. 적어 둔 JSON 이 있으면 그것을 읽는다.</summary>
    public static PersonTemplate? Open(string gameDirectory)
    {
        var snapshot = ExeTable.Open<Snapshot>(CacheName, gameDirectory, ReadFromExe, out string error);
        LastError = error;
        return snapshot == null ? null : new PersonTemplate(snapshot.Rows);
    }

    /// <summary>그 사람의 밑줄. 범위 밖이면 null.</summary>
    public Template? Find(int id) => id >= 0 && id < _rows.Length ? _rows[id] : null;

    private static Snapshot? ReadFromExe(PeImage exe, out string error)
    {
        error = "";

        var rows = new Template[Count];
        for (int i = 0; i < Count; i++)
        {
            int row = TableVa + i * RowSize;
            rows[i] = new Template(i, exe.Int(row + 0x14), exe.Int(row + 0x20));
        }

        // 3번 바스코·다 가마는 포르투갈, 6번 아메리고·베스풋치는 에스파니아다 — 판이 다른
        // EXE 를 잘못 읽으면 여기서 어긋난다.
        if (rows[3].Nation != 0 || rows[6].Nation != 1)
        {
            error = "인물 밑표가 기대한 모양이 아닙니다(다른 판의 EXE 일 수 있습니다)";
            return null;
        }

        return new Snapshot(rows);
    }
}
