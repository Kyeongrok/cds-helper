using System.IO;

namespace CdsHelper.Support.Local.Helpers;

/// <summary>
/// CDS_95.EXE 안의 후원자 표. 이름과 얼굴 번호, 성별, 직업이 들어 있다.
/// </summary>
/// <remarks>
/// <code>
///   표     VA 0x005228B8, 81행 x 60바이트
///   +0x00  이름 ptr("조안 2세", "아르발로·데·브라간사")
///   +0x04  얼굴 번호      +0x08  성별(0 남 · 1 여)
///   +0x10  직업 코드      14 국왕 · 15 교황 · 16 총독 · 17 귀족
///                        18 신부 · 19 상인 · 20 관리 · 21 학자
/// </code>
/// 얼굴 번호는 <see cref="Portraits"/> 의 파트 번호다 — 성별에 따라 MALE.CDS 나 FEMALE.CDS
/// 에서 꺼낸다. 여자 후원자는 둘뿐이다(이자벨 1세, 루크레치아·보르지아).
///
/// 후원자가 81명인 것은 게임 코드와도 맞는다 — 스폰서를 훑는 자리가 <c>cmp ebx, 0x51</c> 이다.
/// 이름에 가운뎃점(·)이 들어가고 차례도 <c>patrons.json</c> 과 달라서, 짝은 이름에서
/// 가운뎃점과 빈칸을 떼고 맞춘다(<see cref="FindByName"/>). 81명 모두 맞는다.
///
/// 안목·재력·명성은 이 표에 없다 — 그쪽은 <c>patrons.json</c> 을 쓴다.
/// </remarks>
public sealed class SponsorTable
{
    private const int TableVa = 0x005228B8;
    private const int RowCount = 81;
    private const int RowSize = 60;

    /// <summary>
    /// 취차(집사)의 얼굴 번호. MALE.CDS 의 것이다.
    /// </summary>
    /// <remarks>
    /// 후원자를 만나러 가면 먼저 이 사람이 나와 안내한다. <b>어느 후원자에게 가든 같은 얼굴이고
    /// 이름이 없다</b> — 후원자 표(81명)에도 없다.
    ///
    /// 게임에서도 후원자와 따로 논다. 시설이 후원자를 물릴 때(<c>0x0044E5D5</c> 의 루프)
    /// 끝에서 <c>this[+0xB8] = this[+0x80]</c> 로 화자를 넣는데, 이 값은 후원자를 못 찾아도
    /// 그대로 들어간다. 그래서 대사 창의 얼굴은 늘 이 사람이고, 후원자 이름만 문구에 끼워진다.
    ///
    /// 번호는 표에 적힌 것이 아니라 얼굴을 눈으로 맞춰 찾은 것이다.
    /// </remarks>
    public const int StewardFace = 229;

    /// <summary>후원자 한 줄.</summary>
    /// <param name="Index">표에서의 차례(0~80). 게임이 쓰는 번호다.</param>
    /// <param name="Name">이름. 가운뎃점이 들어 있다("아르발로·데·브라간사").</param>
    /// <param name="Face">얼굴 번호. <see cref="Portraits"/> 의 파트 번호다.</param>
    /// <param name="IsFemale">여자면 true — 얼굴을 FEMALE.CDS 에서 꺼낸다.</param>
    /// <param name="JobCode">직업 코드 14~21.</param>
    public readonly record struct Sponsor(int Index, string Name, int Face, bool IsFemale, int JobCode)
    {
        /// <summary>직업 이름. 모르는 코드면 빈 문자열.</summary>
        public string Job => JobCode switch
        {
            14 => "국왕", 15 => "교황", 16 => "총독", 17 => "귀족",
            18 => "신부", 19 => "상인", 20 => "관리", 21 => "학자",
            _ => "",
        };
    }

    private readonly List<Sponsor> _sponsors;
    private readonly Dictionary<string, Sponsor> _byName;

    private SponsorTable(List<Sponsor> sponsors)
    {
        _sponsors = sponsors;
        _byName = [];
        foreach (var s in sponsors) _byName[Key(s.Name)] = s;
    }

    /// <summary>왜 못 읽었는지. 잘 열렸으면 빈 문자열.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>표에 있는 후원자 전부.</summary>
    public IReadOnlyList<Sponsor> Sponsors => _sponsors;

    /// <summary>이름으로 찾는다. 가운뎃점과 빈칸은 무시한다. 없으면 null.</summary>
    public Sponsor? FindByName(string name) =>
        _byName.TryGetValue(Key(name), out var s) ? s : null;

    /// <summary>이름 맞추기용 열쇠 — 가운뎃점과 빈칸을 뗀다.</summary>
    private static string Key(string name) =>
        name.Replace("·", "").Replace(" ", "");

    /// <summary>게임 폴더의 CDS_95.EXE 에서 표를 읽는다. 못 읽으면 null.</summary>
    public static SponsorTable? Open(string gameDirectory)
    {
        LastError = "";
        var exe = PeImage.Read(Path.Combine(gameDirectory, "CDS_95.EXE"), out string error);
        if (exe == null) { LastError = error; return null; }

        var sponsors = new List<Sponsor>(RowCount);
        for (int k = 0; k < RowCount; k++)
        {
            int row = TableVa + k * RowSize;
            var name = exe.Text(exe.Word(row + 0x00));
            if (string.IsNullOrEmpty(name)) continue;

            sponsors.Add(new Sponsor(
                Index: k,
                Name: name,
                Face: exe.Int(row + 0x04),
                IsFemale: exe.Int(row + 0x08) == 1,
                JobCode: exe.Int(row + 0x10)));
        }

        // 판이 다른 EXE 를 잘못 읽지 않도록 첫 줄을 확인한다.
        if (sponsors.Count == 0 || sponsors[0].Name != "조안 2세")
        {
            LastError = "후원자 표가 기대한 모양이 아닙니다(다른 판의 EXE 일 수 있습니다)";
            return null;
        }
        return new SponsorTable(sponsors);
    }
}
