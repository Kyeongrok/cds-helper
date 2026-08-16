using System.IO;

namespace CdsHelper.Support.Local.Helpers;

/// <summary>게임 메뉴 띠의 세 가지 무늬.</summary>
public enum BandStyle
{
    /// <summary>진홍 장식 — 메뉴 <b>타이틀</b>에 쓴다.</summary>
    Title = 0,

    /// <summary>베이지 — 보통 버튼.</summary>
    Button = 1,

    /// <summary>회녹색 — 다른 상태 버튼.</summary>
    Alt = 2,
}

/// <summary>
/// 게임 폴더의 MISC.CDS — 화면 장식 조각들. 메뉴 타이틀·버튼 띠가 여기서 온다.
/// </summary>
/// <remarks>
/// 도시 그림·초상화와 같은 LS12 아카이브다.
/// <code>
///   파트 0   16 x  96   테두리 상자 조각
///   파트 3   32 x  48   스크롤 화살표
///   파트 4   2,880바이트  메뉴 띠 껍데기          ← 이것을 쓴다
///   파트 7   24 x 240   숫자 글꼴 0~9
///   파트 8  592 x 448   양피지 바탕
///   파트 11  48 x  48   닻
/// </code>
///
/// <b>파트 4 는 한 장짜리 그림이 아니다.</b> 한 벌 960바이트씩 <b>세 벌</b>이고,
/// 한 벌 안은 조각 셋이다. 조각마다 <b>제 폭으로</b> 위에서 아래로 담긴 8bpp 색인이다.
/// <code>
///   +0     16폭 x 24행 (384바이트)   왼끝
///   +384    8폭 x 24행 (192바이트)   가운데  ← 폭만큼 옆으로 되풀이한다
///   +576   16폭 x 24행 (384바이트)   오른끝
/// </code>
/// 그래서 띠는 늘 <b>24행</b>이고 폭은 <c>16 + 8*n + 16</c> 이다.
///
/// 전체를 한 폭으로 보고 가로로 자르면 안 된다 — 조각마다 폭이 달라서 엉뚱한 경계가
/// 잡힌다(16x180 으로 보고 y36·60·96 을 경계로 삼았던 적이 있는데 전부 헛것이었다).
///
/// 게임도 똑같이 짓는다 — 조각 꺼내기 <c>0x00463710(벌, 조각)</c>, 조각 시작 열 표
/// <c>0x00552898</c> = <c>{0, 16, 24}</c>, 띠 짓는 자리 <c>0x0041F606</c>.
/// 파트 4 는 <c>0x00463590</c> 이 게임 시작 때 객체 <c>0x005AA3B8+0x14</c> 로 읽어 둔다.
///
/// 쓰는 색인이 0~73 뿐이라 <see cref="GamePalette"/> 만으로 다 그려진다.
/// </remarks>
public sealed class UiSprites
{
    private const int BandPart = 4;

    /// <summary>띠 한 벌의 크기와 조각 배치.</summary>
    private const int StyleBytes = 960, StyleCount = 3;

    /// <summary>띠 높이. 게임이 늘 이 높이로 그린다.</summary>
    public const int BandHeight = 24;

    /// <summary>양 끝 조각의 폭.</summary>
    public const int CapWidth = 16;

    /// <summary>가운데 조각의 폭. 이만큼씩 되풀이해 띠를 늘린다.</summary>
    public const int MidWidth = 8;

    // 벌 안에서 조각이 앉은 자리. 게임 표 0x552898 의 열 {0,16,24} 을 바이트로 옮긴 것이다.
    private static readonly int[] PieceOffset = [0, 384, 576];
    private static readonly int[] PieceWidth = [CapWidth, MidWidth, CapWidth];

    private readonly byte[] _band;

    private UiSprites(byte[] band) => _band = band;

    /// <summary>왜 못 열었는지. 잘 열렸으면 빈 문자열.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>게임 폴더의 MISC.CDS 를 연다. 없거나 모양이 다르면 null.</summary>
    public static UiSprites? Open(string gameDirectory)
    {
        LastError = "";
        var path = Path.Combine(gameDirectory, "MISC.CDS");
        if (!File.Exists(path)) { LastError = $"{path} 가 없습니다"; return null; }

        var archive = Ls12Reader.Open(path);
        if (archive == null) { LastError = $"{path} 를 읽지 못했습니다"; return null; }
        if (archive.PartCount <= BandPart) { LastError = "MISC.CDS 에 띠 조각이 없습니다"; return null; }

        var part = archive.Decode(BandPart);
        if (part == null || part.Length < StyleBytes * StyleCount)
        {
            LastError = "띠 조각이 기대한 크기가 아닙니다";
            return null;
        }
        return new UiSprites(part);
    }

    /// <summary>가운데를 <paramref name="cells"/> 번 되풀이했을 때의 띠 폭.</summary>
    public static int WidthFor(int cells) => CapWidth * 2 + MidWidth * Math.Max(1, cells);

    /// <summary>
    /// 담고 싶은 폭을 넣으면 그만큼을 덮는 가장 작은 칸 수를 준다.
    /// 띠 폭은 8픽셀씩만 늘어나므로 딱 맞는 폭이 안 나올 수 있다.
    /// </summary>
    public static int CellsFor(double contentWidth)
    {
        double need = contentWidth - CapWidth * 2;
        if (need < 0) need = 0;
        return Math.Max(1, (int)Math.Ceiling(need / MidWidth));
    }

    /// <summary>
    /// 띠 하나를 BGRA 로 짓는다. 높이는 늘 <see cref="BandHeight"/> 다.
    /// 왼끝을 깔고 가운데를 <paramref name="cells"/> 번 되풀이한 뒤 오른끝을 덮는다 —
    /// 게임이 하는 그대로다.
    /// </summary>
    public uint[] Band(BandStyle style, int cells, out int width)
    {
        cells = Math.Max(1, cells);
        width = WidthFor(cells);

        var bgra = new uint[width * BandHeight];
        Blit(bgra, width, style, 0, 0);
        for (int i = 0; i < cells; i++)
            Blit(bgra, width, style, 1, CapWidth + i * MidWidth);
        Blit(bgra, width, style, 2, width - CapWidth);
        return bgra;
    }

    /// <summary>
    /// 조각 하나를 BGRA 로 꺼낸다. <paramref name="k"/> 는 0 왼끝 / 1 가운데 / 2 오른끝.
    /// 높이는 늘 <see cref="BandHeight"/> 다.
    /// </summary>
    /// <remarks>
    /// 화면에 붙일 때는 이 셋을 (왼끝 고정 · 가운데 이어 깔기 · 오른끝 고정)으로 놓으면
    /// 어떤 폭에도 맞고 도트도 안 뭉개진다. 게임이 하는 그대로다.
    /// </remarks>
    public uint[] Piece(BandStyle style, int k, out int width)
    {
        k = Math.Clamp(k, 0, 2);
        width = PieceWidth[k];
        var bgra = new uint[width * BandHeight];
        Blit(bgra, width, style, k, 0);
        return bgra;
    }

    /// <summary>조각 하나(<paramref name="k"/> = 0 왼끝 / 1 가운데 / 2 오른끝)를 x 자리에 옮긴다.</summary>
    private void Blit(uint[] dst, int stride, BandStyle style, int k, int x)
    {
        int src = (int)style * StyleBytes + PieceOffset[k];
        int w = PieceWidth[k];
        for (int r = 0; r < BandHeight; r++)
            for (int c = 0; c < w; c++)
            {
                int i = _band[src + r * w + c] * 3;
                dst[r * stride + x + c] = (uint)(0xFF << 24 | GamePalette.Rgb[i] << 16
                                                | GamePalette.Rgb[i + 1] << 8 | GamePalette.Rgb[i + 2]);
            }
    }
}
