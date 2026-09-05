using System.IO;
using CdsHelper.Support.Local.Helpers;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 게임 폴더의 <c>LANDDATA.CDS</c> — 육상전의 싸움터와 부대 그림.
/// </summary>
/// <remarks>
/// 자세한 것은 볼트 <c>65.분석-육상전</c> 의 「부대 그림 고르기」 절에 있다.
/// <code>
///   파트 0 · 5 · 7    팔레트 셋 (258 · 258 · 261바이트, 차례는 파랑·빨강·초록)
///   파트 1~4          싸움터 넷 640x480 (도시 · 초지 · 숲 · 황무지)
///   파트 6            배치판으로 보이는데 짜임을 아직 못 짚었다
///   파트 8~50         부대 그림 — 한 장에 몸짓 여덟(2x4)
///   파트 51 · 52      조각 둘
/// </code>
/// 부대 그림은 <b>192x192(칸 96x48)</b> 이고, 낙타병·코끼리병만 <b>256x256(칸 128x64)</b> 다.
/// </remarks>
public sealed class LandArt
{
    /// <summary>파일 이름. 게임 폴더는 대소문자를 안 가리지만 이름을 그대로 적어 둔다.</summary>
    private const string FileName = "LANDDATA.CDS";

    /// <summary>부대 그림 한 장에 든 몸짓 수 — 가로 둘 세로 넷이다.</summary>
    public const int FrameCols = 2, FrameRows = 4;

    /// <summary>그림 파트가 쓰는 팔레트.</summary>
    private const int UnitPalette = 7;

    /// <summary>싸움터 파트가 쓰는 팔레트.</summary>
    private const int FieldPalette = 0;

    /// <summary>싸움터 한 장의 크기.</summary>
    public const int FieldWidth = 640, FieldHeight = 480;

    /// <summary>싸움터 파트 — 지형 번호(0 도시 · 1 초지 · 2 숲 · 3 황무지) 차례다.</summary>
    private const int FirstField = 1;

    /// <summary>비침 색인. 다른 벌들처럼 마젠타 자리다.</summary>
    private const byte Transparent = 64;

    private readonly Ls12Reader _archive;
    private readonly List<(byte R, byte G, byte B)> _unitColors;
    private readonly List<(byte R, byte G, byte B)> _fieldColors;

    private LandArt(Ls12Reader archive)
    {
        _archive = archive;
        _unitColors = Colors(archive, UnitPalette);
        _fieldColors = Colors(archive, FieldPalette);
    }

    /// <summary>왜 못 열었는지. 잘 열렸으면 빈 글.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>게임 폴더의 LANDDATA.CDS 를 연다. 없거나 모양이 다르면 null.</summary>
    public static LandArt? Open(string gameDirectory)
    {
        LastError = "";

        string path = Path.Combine(gameDirectory, FileName);
        if (!File.Exists(path)) { LastError = $"{path} 가 없습니다"; return null; }

        var archive = Ls12Reader.Open(path);
        if (archive == null) { LastError = $"{path} 를 읽지 못했습니다"; return null; }
        if (archive.PartCount <= LandUnitArt.LastPart)
        {
            LastError = "LANDDATA.CDS 에 부대 그림이 모자랍니다";
            return null;
        }
        return new LandArt(archive);
    }

    /// <summary>
    /// 그 병종의 몸짓 한 장을 BGRA 로 푼다. 못 풀면 null.
    /// </summary>
    /// <param name="kind">병종 0~23.</param>
    /// <param name="friend">아군 자리인지 — 게임은 슬롯 0~5 를 아군으로 본다.</param>
    /// <param name="culture">상대 문화권 0~10. 몇몇 병종의 그림이 이것으로 갈린다.</param>
    /// <param name="frame">몸짓 0~7.</param>
    public uint[]? TryGetUnit(int kind, bool friend, int culture, int frame,
                              out int width, out int height)
    {
        width = height = 0;
        if (frame < 0 || frame >= FrameCols * FrameRows) return null;

        int part = LandUnitArt.PartOf(kind, friend, culture);
        var pixels = _archive.Decode(part);
        if (pixels == null) return null;

        int side = Side(pixels.Length);
        if (side == 0) return null;

        width = side / FrameCols;
        height = side / FrameRows;

        int left = frame % FrameCols * width;
        int top = frame / FrameCols * height;

        var bgra = new uint[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                byte v = pixels[(top + y) * side + left + x];
                if (v == Transparent) continue;
                bgra[y * width + x] = Argb(_unitColors, v);
            }
        return bgra;
    }

    /// <summary>싸움터 한 장(640x480)을 BGRA 로 푼다. 못 풀면 null.</summary>
    /// <param name="terrain">0 도시 · 1 초지 · 2 숲 · 3 황무지.</param>
    public uint[]? TryGetField(int terrain)
    {
        if (terrain < 0 || terrain > 3) return null;

        var pixels = _archive.Decode(FirstField + terrain);
        if (pixels == null || pixels.Length < FieldWidth * FieldHeight) return null;

        var bgra = new uint[FieldWidth * FieldHeight];
        for (int i = 0; i < bgra.Length; i++) bgra[i] = Argb(_fieldColors, pixels[i]);
        return bgra;
    }

    /// <summary>한 변의 길이. 그림은 정사각이라 넓이에서 되짚는다.</summary>
    private static int Side(int pixels) => pixels switch
    {
        36864 => 192,
        65536 => 256,
        _ => 0,
    };

    private static uint Argb(List<(byte R, byte G, byte B)> colors, byte index) =>
        index < colors.Count
            ? (uint)(0xFF << 24 | colors[index].R << 16 | colors[index].G << 8 | colors[index].B)
            : 0xFFFF00FFu;

    /// <summary>
    /// 팔레트 한 벌. 세 바이트가 한 색이고 차례는 <b>파랑 · 빨강 · 초록</b> 이다.
    /// </summary>
    /// <remarks>여섯 비트(0~63)로 적혀 있어 넷을 곱해야 눈에 보이는 밝기가 된다.</remarks>
    private static List<(byte R, byte G, byte B)> Colors(Ls12Reader archive, int part)
    {
        var raw = archive.Decode(part);
        var colors = new List<(byte, byte, byte)>();
        if (raw == null) return colors;

        bool dim = true;
        foreach (byte b in raw) if (b >= 64) { dim = false; break; }

        for (int i = 0; i + 2 < raw.Length; i += 3)
        {
            byte blue = raw[i], red = raw[i + 1], green = raw[i + 2];
            colors.Add(dim
                ? ((byte)(red * 4), (byte)(green * 4), (byte)(blue * 4))
                : (red, green, blue));
        }
        return colors;
    }
}

/// <summary>
/// 병종에서 <c>LANDDATA.CDS</c> 파트를 고르는 표 — 게임의 <c>0x0044A1F0</c> 이다.
/// </summary>
/// <remarks>
/// 아군(슬롯 0~5)과 적(6~11)이 다른 그림을 쓰는 병종이 여섯이고, 문화권으로 갈리는
/// 병종이 다섯이다. 문화권은 마을 공략이면 <b>그 도시의 것</b>이다
/// (<c>0x00447070</c> 이 전투 갈래 2·4 일 때 <c>[[+0xA0]+0x58]</c> 을 낸다).
/// </remarks>
public static class LandUnitArt
{
    /// <summary>부대 그림이 든 마지막 파트.</summary>
    public const int LastPart = 45;

    /// <summary>병종 수.</summary>
    public const int KindCount = 24;

    /// <summary>그 병종의 파트 번호.</summary>
    public static int PartOf(int kind, bool friend, int culture) => kind switch
    {
        0 => friend ? 8 : culture is 0 or 1 or 2 ? 16 : 17,      // 기병
        1 => friend ? 9 : culture is 0 or 1 or 2 ? 18 : 19,      // 중장기병
        2 => friend ? 14 : 24,                                    // 제독
        3 => friend ? 15 : 25,                                    // 무적제독
        4 => 29,                                                  // 창병
        5 => culture is 3 or 8 ? 30 : 31,                         // 경보병
        6 => 32,                                                  // 낙타병 (256x256)
        7 => 33,                                                  // 코끼리병 (256x256)
        8 => 36,                                                  // 사무라이
        9 => 37,                                                  // 하타모토
        10 => 38,                                                 // 닌자
        11 => 40,                                                 // 인디오
        12 => 43,                                                 // 족장
        13 => 45,                                                 // 영주
        14 => culture is 4 or 5 ? 41 : 42,                        // 장군
        15 => friend ? 10 : 20,                                   // 화승총대
        16 => friend ? 11 : 21,                                   // 머스켓총대
        17 => culture is 6 or 7 ? 27 : 26,                        // 궁병
        18 => friend ? 12 : 22,                                   // 포병
        19 => friend ? 13 : 23,                                   // 캐논포병
        20 => 35,                                                 // 화포병
        21 => 28,                                                 // 주술사
        22 => 34,                                                 // 고승
        23 => 39,                                                 // 표범
        _ => friend ? 8 : 16,                                     // 표 밖 — 게임도 이렇게 물러선다
    };
}
