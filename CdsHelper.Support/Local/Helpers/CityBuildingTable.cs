using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace CdsHelper.Support.Local.Helpers;

/// <summary>
/// CDS_95.EXE 안의 건물 표. (도시 × 건물) 한 쌍마다 한 줄이고, 그 줄에 이름·종류·도시 그림
/// 좌표·가르치는 기능까지 다 들어 있다.
/// </summary>
/// <remarks>
/// <code>
///   표     VA 0x00500918, 1504행 x 0x38 (.rdata)
///   +0x00  이름 ptr("베렌의 탑")     +0x04  종류이름 ptr("항구")
///   +0x08  도시 번호                 +0x0C  건물 코드
///   +0x20,+0x24  도시 그림 좌표      +0x28,+0x2C  96 x 80 (고정)
///   +0x30  기능·언어 비트마스크      +0x34  0
///   기능 이름표 0x00560A10[13] · 언어 이름표 0x00560A48[14]
/// </code>
/// 좌표는 도시 그림(400x320) 기준이고, 96x80 상자의 <b>가운데</b>가 건물이다. 리스본에서
/// 건물 여덟 개를 그림에서 따로 찾아 대 보니 가운데가 ±9점 안에 들었다.
///
/// 예전에는 건물 그림을 도시마다 맞춰서 자리를 찾았는데, 그 방식은 리스본 말고는 믿을 수
/// 없었다 — 같은 그림이 도시마다 다른 시설로 쓰이기 때문이다(베니스의 붉은 지붕 집은 술집이
/// 아니다). 이 표가 게임이 실제로 쓰는 자리다.
///
/// 표는 EXE 에서 그때그때 읽는다. 판이 다르면 열리지 않을 뿐 엉뚱한 값을 내지 않도록
/// 첫 줄이 "항구"인지 보고 아니면 물러난다.
/// </remarks>
public sealed class CityBuildingTable
{
    private const int TableVa = 0x00500918;
    private const int RowCount = 1504;
    private const int RowSize = 0x38;
    private const int SkillNamesVa = 0x00560A10;
    private const int LanguageNamesVa = 0x00560A48;

    /// <summary>기능 이름 수(비트 0~12).</summary>
    public const int SkillCount = 13;

    /// <summary>언어 이름 수(비트 13~26).</summary>
    public const int LanguageCount = 14;

    /// <summary>건물 상자 크기. 표에 96x80 으로 박혀 있다.</summary>
    public const int BoxWidth = 96, BoxHeight = 80;

    /// <summary>건물 한 채.</summary>
    /// <param name="City">도시 번호.</param>
    /// <param name="Code">건물 코드(항구 0 · 교역소 1 · 왕궁 2 · 교회 3 · 술집 4 …).</param>
    /// <param name="Kind">종류 이름("항구", "조합" …). 지도 이름표에 이것이 뜬다.</param>
    /// <param name="Name">건물 이름("베렌의 탑"). 명령 창 제목에 이것이 뜬다.</param>
    /// <param name="X">도시 그림에서 상자의 왼쪽 위(400x320 기준).</param>
    /// <param name="TeachMask">가르치는 기능·언어 비트. 0 이면 안 가르친다.</param>
    public readonly record struct Building(
        int City, int Code, string Kind, string Name, int X, int Y, uint TeachMask)
    {
        /// <summary>건물이 그려진 자리(상자 가운데).</summary>
        public int CenterX => X + BoxWidth / 2;
        public int CenterY => Y + BoxHeight / 2;

        /// <summary>무언가 가르치는 건물인지(조합·교회·학자 저택).</summary>
        public bool Teaches => TeachMask != 0;
    }

    private readonly List<Building> _buildings;
    private readonly Dictionary<int, List<Building>> _byCity = [];

    private CityBuildingTable(List<Building> buildings, string[] skills, string[] languages)
    {
        _buildings = buildings;
        SkillNames = skills;
        LanguageNames = languages;
        foreach (var b in buildings)
        {
            if (!_byCity.TryGetValue(b.City, out var list)) _byCity[b.City] = list = [];
            list.Add(b);
        }
    }

    /// <summary>왜 못 읽었는지. 잘 열렸으면 빈 문자열.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>기능 이름 13개(비트 0~12 차례).</summary>
    public IReadOnlyList<string> SkillNames { get; }

    /// <summary>언어 이름 14개(비트 13~26 차례).</summary>
    public IReadOnlyList<string> LanguageNames { get; }

    /// <summary>표에 있는 건물 전부.</summary>
    public IReadOnlyList<Building> Buildings => _buildings;

    /// <summary>그 도시의 건물들. 없으면 빈 목록.</summary>
    public IReadOnlyList<Building> InCity(int cityId) =>
        _byCity.TryGetValue(cityId, out var list) ? list : [];

    /// <summary>그 도시의 그 종류 건물. 없으면 null.</summary>
    public Building? Find(int cityId, string kind)
    {
        foreach (var b in InCity(cityId))
            if (b.Kind == kind) return b;
        return null;
    }

    /// <summary>비트마스크를 이름으로 푼다 — 기능 먼저, 언어 나중(게임 차례 그대로).</summary>
    public List<string> Teaches(uint mask)
    {
        var got = new List<string>();
        for (int i = 0; i < SkillCount; i++)
            if ((mask >> i & 1) != 0) got.Add(SkillNames[i]);
        for (int i = 0; i < LanguageCount; i++)
            if ((mask >> (SkillCount + i) & 1) != 0) got.Add(LanguageNames[i]);
        return got;
    }

    /// <summary>게임 폴더의 CDS_95.EXE 에서 표를 읽는다. 못 읽으면 null.</summary>
    public static CityBuildingTable? Open(string gameDirectory)
    {
        LastError = "";
        var path = Path.Combine(gameDirectory, "CDS_95.EXE");
        if (!File.Exists(path)) { LastError = $"{path} 가 없습니다"; return null; }

        byte[] exe;
        try { exe = File.ReadAllBytes(path); }
        catch (IOException ex) { LastError = ex.Message; return null; }
        catch (UnauthorizedAccessException ex) { LastError = ex.Message; return null; }

        var pe = PeImage.From(exe);
        if (pe == null) { LastError = "CDS_95.EXE 를 PE 로 읽지 못했습니다"; return null; }

        // 이름은 EXE 안에 CP949 로 들어 있다. 앱이 이미 등록해 두지만(App.cs) 시험용으로 이
        // 클래스만 쓸 때도 되도록 여기서도 한 번 등록한다 — 두 번 불러도 탈이 없다.
        Encoding cp949;
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            cp949 = Encoding.GetEncoding(949);
        }
        catch (ArgumentException)
        {
            LastError = "CP949 를 쓸 수 없습니다";
            return null;
        }

        string? Text(uint va)
        {
            int o = pe.Offset(va);
            if (o < 0) return null;
            int end = Array.IndexOf(exe, (byte)0, o, Math.Min(64, exe.Length - o));
            return end < 0 ? null : cp949.GetString(exe, o, end - o);
        }
        uint Word(int va)
        {
            int o = pe.Offset((uint)va);
            return o < 0 ? 0 : BinaryPrimitives.ReadUInt32LittleEndian(exe.AsSpan(o));
        }

        var buildings = new List<Building>(RowCount);
        for (int k = 0; k < RowCount; k++)
        {
            int row = TableVa + k * RowSize;
            if (pe.Offset((uint)row) < 0) { LastError = "건물 표가 파일 밖입니다"; return null; }

            var name = Text(Word(row + 0x00));
            var kind = Text(Word(row + 0x04));
            if (name == null || kind == null) continue;

            buildings.Add(new Building(
                (int)Word(row + 0x08), (int)Word(row + 0x0C), kind, name,
                (int)Word(row + 0x20), (int)Word(row + 0x24), Word(row + 0x30)));
        }

        // 판이 다른 EXE 를 잘못 읽지 않도록 첫 줄을 확인한다.
        if (buildings.Count == 0 || buildings[0].Kind != "항구")
        {
            LastError = "건물 표가 기대한 모양이 아닙니다(다른 판의 EXE 일 수 있습니다)";
            return null;
        }

        var skills = new string[SkillCount];
        for (int i = 0; i < SkillCount; i++) skills[i] = Text(Word(SkillNamesVa + i * 4)) ?? "";
        var languages = new string[LanguageCount];
        for (int i = 0; i < LanguageCount; i++) languages[i] = Text(Word(LanguageNamesVa + i * 4)) ?? "";

        return new CityBuildingTable(buildings, skills, languages);
    }

    /// <summary>VA 를 파일 오프셋으로 옮기는 데만 쓰는 아주 작은 PE 리더.</summary>
    private sealed class PeImage
    {
        private readonly (uint Va, uint VSize, uint Raw, uint RawSize)[] _sections;
        private readonly uint _imageBase;

        private PeImage(uint imageBase, (uint, uint, uint, uint)[] sections)
        {
            _imageBase = imageBase;
            _sections = sections;
        }

        public static PeImage? From(byte[] exe)
        {
            if (exe.Length < 0x40 || exe[0] != 'M' || exe[1] != 'Z') return null;
            int pe = BinaryPrimitives.ReadInt32LittleEndian(exe.AsSpan(0x3C));
            if (pe < 0 || pe + 0x78 > exe.Length) return null;
            if (BinaryPrimitives.ReadUInt32LittleEndian(exe.AsSpan(pe)) != 0x00004550) return null;

            int sectionCount = BinaryPrimitives.ReadUInt16LittleEndian(exe.AsSpan(pe + 6));
            int optSize = BinaryPrimitives.ReadUInt16LittleEndian(exe.AsSpan(pe + 20));
            uint imageBase = BinaryPrimitives.ReadUInt32LittleEndian(exe.AsSpan(pe + 24 + 28));
            int table = pe + 24 + optSize;

            var sections = new (uint, uint, uint, uint)[sectionCount];
            for (int i = 0; i < sectionCount; i++)
            {
                int s = table + i * 40;
                if (s + 40 > exe.Length) return null;
                sections[i] = (
                    BinaryPrimitives.ReadUInt32LittleEndian(exe.AsSpan(s + 12)),   // VirtualAddress
                    BinaryPrimitives.ReadUInt32LittleEndian(exe.AsSpan(s + 8)),    // VirtualSize
                    BinaryPrimitives.ReadUInt32LittleEndian(exe.AsSpan(s + 20)),   // PointerToRawData
                    BinaryPrimitives.ReadUInt32LittleEndian(exe.AsSpan(s + 16)));  // SizeOfRawData
            }
            return new PeImage(imageBase, sections);
        }

        /// <summary>VA 의 파일 오프셋. 파일에 값이 없는 자리면 -1.</summary>
        public int Offset(uint va)
        {
            uint rva = va - _imageBase;
            foreach (var (secRva, vsize, raw, rawSize) in _sections)
            {
                if (rva < secRva || rva >= secRva + Math.Max(vsize, rawSize)) continue;
                uint o = rva - secRva;
                return o < rawSize ? (int)(raw + o) : -1;
            }
            return -1;
        }
    }
}
