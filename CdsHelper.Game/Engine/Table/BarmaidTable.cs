using System.Text.Json.Serialization;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// CDS_95.EXE 안의 <b>여급 표</b> — 마을 술집에 서는 127명.
/// </summary>
/// <remarks>
/// <code>
///   표 VA 0x00517AF8, 127행 x 40바이트 (.rdata)
///   +0x00  이름 ptr("카를로타")      +0x04  얼굴 번호(FEMALE.CDS)
///   +0x08  ?                        +0x0C  별자리(0~11)
///   +0x10  혈액형(0 A · 1 B · 2 O · 3 AB)
///   +0x14  운명 얼굴 코드(0~30)      ← 궁합이 이 값 하나로 갈린다
///   +0x18  성격(0~7)                +0x1C  늘 4
///   +0x20  전수 언어 비트           +0x24  도시 번호
/// </code>
///
/// <b>궁합은 얼굴 코드다.</b> 게임은 <c>0x00465E70</c> 에서 이렇게 본다.
/// <code>
///   내 코드   = 0x0047CB10() = [0x005B60A0 + 8]     ; 주인공의 표시 얼굴 코드
///   그녀 코드 = [0x00517B0C + 번호 * 40]             ; 곧 이 표의 +0x14
///   같거나 ±1 이면 맞는다
/// </code>
/// 주인공 코드는 <b>서른여섯 살부터 16 을 더한다</b> — 초상화가 젊은 벌과 나이 든 벌로
/// 갈리고 그 사이가 16 이다. (이 두 가지는 <c>cds_save_editor</c> 의 여급 도감이 밝혀
/// 둔 것이고, 표 자리와 ±1 은 EXE 에서 되짚었다.)
///
/// 궁합이 맞으면 <b>첫 대화부터 말투가 다르고 친밀도가 50 오른다</b>(<c>0x00466730</c>).
/// 안 맞으면 3 이다 — 열일곱 배다.
/// </remarks>
public sealed class BarmaidTable
{
    /// <summary>적어 둘 파일 이름(<c>%APPDATA%\CdsHelper\exe-tables\여급표.json</c>).</summary>
    private const string CacheName = "여급표";

    private const int TableVa = 0x00517AF8;
    private const int RowSize = 40;

    /// <summary>여급 수.</summary>
    public const int Count = 127;

    /// <summary>서른여섯 살부터 주인공 얼굴 코드에 더하는 값.</summary>
    public const int AgedFaceStep = 16;

    /// <summary>그 나이부터 나이 든 얼굴로 친다.</summary>
    public const int AgedFrom = 36;

    /// <summary>궁합으로 쳐 주는 코드 차이.</summary>
    public const int DestinedGap = 1;

    /// <summary>궁합이 맞을 때와 아닐 때 첫 대화가 올리는 친밀도(<c>0x00466768</c> · <c>0x0046679A</c>).</summary>
    public const int DestinedLike = 50, PlainLike = 3;

    /// <summary>친밀도의 위아래(<c>0x00478530</c> 이 0~100 으로 자른다).</summary>
    public const int MaxLiking = 100;

    /// <summary>판이 다른 EXE 를 잘못 읽지 않으려고 대 보는 줄 — 첫 사람.</summary>
    private const string Probe = "카를로타";

    /// <summary>성격 이름(<c>+0x18</c> 의 색인).</summary>
    public static readonly string[] Personalities =
        ["냉냉한", "강인한", "의지가 강한", "용감한", "친절한", "로맨틱한", "섬세한", "견실한"];

    /// <summary>혈액형 이름(<c>+0x10</c> 의 색인).</summary>
    public static readonly string[] Bloods = ["A형", "B형", "O형", "AB형"];

    /// <summary>여급 한 사람.</summary>
    /// <param name="Face">FEMALE.CDS 의 얼굴 번호.</param>
    /// <param name="Fortune">운명 얼굴 코드 — 궁합이 이 값으로 갈린다.</param>
    /// <param name="Tongues">가르쳐 주는 언어 비트.</param>
    [method: JsonConstructor]
    public readonly record struct Barmaid(
        int Id, string Name, int City, int Face, int Zodiac, int Blood,
        int Fortune, int Personality, int Tongues)
    {
        /// <summary>성격 이름. 표 밖이면 빈 문자열.</summary>
        [JsonIgnore]
        public string PersonalityName =>
            Personality >= 0 && Personality < Personalities.Length ? Personalities[Personality] : "";

        /// <summary>혈액형 이름. 표 밖이면 빈 문자열.</summary>
        [JsonIgnore]
        public string BloodName => Blood >= 0 && Blood < Bloods.Length ? Bloods[Blood] : "";
    }

    /// <summary>JSON 으로 적어 두는 알맹이.</summary>
    internal sealed record Snapshot(List<Barmaid> Barmaids);

    private readonly List<Barmaid> _rows;

    private BarmaidTable(Snapshot snapshot) => _rows = snapshot.Barmaids;

    /// <summary>여급 전부.</summary>
    public IReadOnlyList<Barmaid> Barmaids => _rows;

    /// <summary>왜 못 읽었는지. 잘 열렸으면 빈 문자열.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>그 사람. 없으면 null.</summary>
    public Barmaid? Find(int id) => id >= 0 && id < _rows.Count ? _rows[id] : null;

    /// <summary>그 마을 술집에 서는 사람들.</summary>
    public List<Barmaid> InCity(int cityId) => [.. _rows.Where(b => b.City == cityId)];

    /// <summary>
    /// 그 나이의 주인공이 내는 <b>표시 얼굴 코드</b>. 서른여섯부터 열여섯이 더 붙는다.
    /// </summary>
    public static int FortuneOf(int face, int age) => face + (age >= AgedFrom ? AgedFaceStep : 0);

    /// <summary>
    /// 궁합이 맞는지 — 두 코드가 같거나 하나 차이일 때다(<c>0x00465E90</c>).
    /// </summary>
    /// <remarks>
    /// 여급 도감은 "일치" 만 운명의 반려자로 적어 두었는데, 코드를 보면 <b>±1 도 맞는 것</b>으로
    /// 친다. 그 자리가 <c>je</c> 셋(같음 · -1 · 1)이라 에누리가 없다.
    /// </remarks>
    public static bool Destined(int playerFortune, int barmaidFortune) =>
        Math.Abs(playerFortune - barmaidFortune) <= DestinedGap;

    /// <summary>첫 대화가 올리는 친밀도.</summary>
    public static int LikingGain(bool destined) => destined ? DestinedLike : PlainLike;

    /// <summary>표를 연다. 적어 둔 JSON 이 있으면 그것을 읽는다.</summary>
    public static BarmaidTable? Open(string gameDirectory)
    {
        var snapshot = ExeTable.Open<Snapshot>(CacheName, gameDirectory, ReadFromExe, out string error);
        LastError = error;
        return snapshot == null ? null : new BarmaidTable(snapshot);
    }

    private static Snapshot? ReadFromExe(PeImage exe, out string error)
    {
        error = "";

        var rows = new List<Barmaid>(Count);
        for (int i = 0; i < Count; i++)
        {
            int at = TableVa + i * RowSize;
            string? name = exe.Text(exe.Word(at + 0x00));
            if (name == null) { error = "여급 표에서 이름을 못 읽었습니다"; return null; }

            rows.Add(new Barmaid(i, name,
                                 City: exe.Int(at + 0x24),
                                 Face: exe.Int(at + 0x04),
                                 Zodiac: exe.Int(at + 0x0C),
                                 Blood: exe.Int(at + 0x10),
                                 Fortune: exe.Int(at + 0x14),
                                 Personality: exe.Int(at + 0x18),
                                 Tongues: exe.Int(at + 0x20)));
        }

        if (rows[0].Name != Probe)
        {
            error = "여급 표가 기대한 모양이 아닙니다(다른 판의 EXE 일 수 있습니다)";
            return null;
        }
        return new Snapshot(rows);
    }
}
