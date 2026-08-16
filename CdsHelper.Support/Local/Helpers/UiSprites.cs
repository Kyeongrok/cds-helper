using System.IO;

namespace CdsHelper.Support.Local.Helpers;

/// <summary>
/// 게임 폴더의 MISC.CDS — 화면 장식 조각들. 제목 상자의 덩굴 무늬가 여기서 온다.
/// </summary>
/// <remarks>
/// 도시 그림·초상화와 같은 LS12 아카이브다. 파트마다 딴 그림이고 폭이 적혀 있지 않아
/// <b>행 간 상관</b>으로 찾았다 — 후보 폭으로 잘라 위아래 줄이 얼마나 닮는지 재면 진짜 폭에서
/// 확 낮아진다.
/// <code>
///   파트 0   16 x  96   테두리 상자 조각
///   파트 3   32 x  48   스크롤 화살표
///   파트 4   16 x 180   제목 상자 조각 (덩굴 무늬)   ← 이것을 쓴다
///   파트 7   24 x 240   숫자 글꼴 0~9
///   파트 8  592 x 448   양피지 바탕
///   파트 11  48 x  48   닻
/// </code>
/// 파트 4 는 16x20 짜리 조각 아홉 장이 세로로 붙어 있다. 앞의 셋이 제목 상자다 —
/// 왼쪽 마구리, 가운데(옆으로 이어 깐다), 오른쪽 마구리.
///
/// 쓰는 색인이 0~73 뿐이라 <see cref="GamePalette"/> 만으로 다 그려진다(초상화와 같다).
/// </remarks>
public sealed class UiSprites
{
    /// <summary>제목 상자 조각이 든 파트와 그 폭.</summary>
    private const int TitlePart = 4, TitleWidth = 16, TileHeight = 20;

    /// <summary>제목 상자 조각의 차례.</summary>
    private const int CapLeftRow = 0, MiddleRow = 1, CapRightRow = 2;

    private readonly byte[] _title;

    private UiSprites(byte[] title) => _title = title;

    /// <summary>조각 한 장의 크기.</summary>
    public static int TileWidth => TitleWidth;
    public static int TileRows => TileHeight;

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
        if (archive.PartCount <= TitlePart) { LastError = "MISC.CDS 에 제목 조각이 없습니다"; return null; }

        var part = archive.Decode(TitlePart);
        if (part == null || part.Length < TitleWidth * TileHeight * 3)
        {
            LastError = "제목 조각이 기대한 크기가 아닙니다";
            return null;
        }
        return new UiSprites(part);
    }

    /// <summary>제목 상자 왼쪽 마구리(덩굴 무늬).</summary>
    public uint[] TitleCapLeft => Tile(CapLeftRow);

    /// <summary>제목 상자 가운데. 옆으로 이어 깔면 어떤 길이에도 맞는다.</summary>
    public uint[] TitleMiddle => Tile(MiddleRow);

    /// <summary>제목 상자 오른쪽 마구리(왼쪽과 좌우 뒤집힌 짝).</summary>
    public uint[] TitleCapRight => Tile(CapRightRow);

    /// <summary>조각 한 장을 BGRA 로 푼다.</summary>
    private uint[] Tile(int row)
    {
        int start = row * TitleWidth * TileHeight;
        var bgra = new uint[TitleWidth * TileHeight];
        for (int i = 0; i < bgra.Length; i++)
        {
            int c = _title[start + i] * 3;
            bgra[i] = (uint)(0xFF << 24 | GamePalette.Rgb[c] << 16
                             | GamePalette.Rgb[c + 1] << 8 | GamePalette.Rgb[c + 2]);
        }
        return bgra;
    }
}
