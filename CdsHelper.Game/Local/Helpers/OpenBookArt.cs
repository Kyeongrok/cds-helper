using System.IO;
using CdsHelper.Support.Local.Helpers;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 게임 폴더의 <c>OPENBOOK.CDS</c> — 도서관에서 책을 읽으면 뜨는 <b>펼친 책</b>.
/// </summary>
/// <remarks>
/// LS12 아카이브다. 앞의 파트 셋이 팔레트고, 그림은 <b>파트 3부터</b> 스물넷이다 —
/// 게임도 그림 번호 <c>n</c> 을 파트 <c>n+3</c> 으로 바꿔 읽는다(<c>0x00463FE0</c>).
/// <code>
///   파트 0   204바이트  68색 팔레트 — 책 틀과 삽화가 쓴다(밑동 74)
///   파트 1    48바이트  16색 — 누런 종이(밑동 144)
///   파트 2    48바이트  16색 — 흰 종이(밑동 160)
///   파트 3~   그림 33장. 크기는 EXE 표 0x005528A8 (8바이트 x 33) 에 적혀 있다
/// </code>
///
/// 그림이 하는 일은 이렇다.
/// <code>
///   0        544x304  펼친 책 틀(붉은 가죽)
///   1~10     256x288  낱장 열 벌 — 왼쪽 면과 오른쪽 면
///   11·12     16x16   작은 조각 둘
///   13~32    160x240 또는 240x160  삽화 스무 장
/// </code>
///
/// 어느 낱장을 쓰는지는 <c>0x00464D5D</c> 가 고른다 — 삽화가 없으면 왼쪽이 2 또는 6,
/// 있으면 3·4·7·8 이고, 오른쪽은 9 또는 10 이다. 2·3·4·9 는 흰 종이 벌이고
/// 6·7·8·10 은 누런 종이 벌이라 <b>두 면의 색이 늘 맞는다</b>.
/// </remarks>
public sealed class OpenBookArt
{
    /// <summary>그림 번호가 파트 번호보다 이만큼 작다.</summary>
    private const int FirstPicturePart = 3;

    /// <summary>그림 장수(<c>0x00463EB9</c> 의 <c>cmp eax,0x21</c>).</summary>
    public const int PictureCount = 33;

    /// <summary>펼친 책 틀.</summary>
    public const int Frame = 0, FrameWidth = 544, FrameHeight = 304;

    /// <summary>낱장이 놓이는 자리와 크기.</summary>
    public const int PageWidth = 256, PageHeight = 288;
    public const int LeftPageX = 8, RightPageX = 280, PageY = 8;

    /// <summary>삽화가 없을 때 쓰는 낱장 — 누런 종이 벌이다.</summary>
    public const int LeftPage = 6, RightPage = 10;

    /// <summary>그림마다의 크기. EXE 표(<c>0x005528A8</c>)에서 그대로 옮겼다.</summary>
    private static readonly (int W, int H)[] Sizes =
    [
        (544, 304),
        (256, 288), (256, 288), (256, 288), (256, 288), (256, 288),
        (256, 288), (256, 288), (256, 288), (256, 288), (256, 288),
        (16, 16), (16, 16),
        (160, 240), (160, 240),
        (240, 160), (240, 160), (240, 160), (240, 160), (240, 160),
        (240, 160), (240, 160), (240, 160),
        (160, 240), (160, 240), (160, 240), (160, 240), (160, 240),
        (160, 240), (160, 240),
        (240, 160), (240, 160), (240, 160),
    ];

    /// <summary>제 팔레트가 얹히는 첫 색인 셋. 파트 0·1·2 차례다.</summary>
    private static readonly int[] Bases = [74, 144, 160];

    private readonly Ls12Reader _archive;
    private readonly byte[]?[] _palettes = new byte[]?[3];
    private readonly Dictionary<int, uint[]?> _made = [];

    private OpenBookArt(Ls12Reader archive) => _archive = archive;

    /// <summary>왜 못 열었는지. 잘 열렸으면 빈 문자열.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>게임 폴더에서 연다. 없거나 형식이 아니면 null.</summary>
    public static OpenBookArt? Open(string gameDirectory)
    {
        LastError = "";
        string path = Path.Combine(gameDirectory, "OPENBOOK.CDS");
        if (!File.Exists(path)) { LastError = $"{path} 가 없습니다"; return null; }

        var archive = Ls12Reader.Open(path);
        if (archive == null) { LastError = $"{path} 를 읽지 못했습니다"; return null; }
        if (archive.PartCount < FirstPicturePart + PictureCount)
        {
            LastError = "OPENBOOK.CDS 에 그림이 모자랍니다";
            return null;
        }
        return new OpenBookArt(archive);
    }

    /// <summary>그 그림의 크기.</summary>
    public static (int W, int H) SizeOf(int picture) =>
        picture >= 0 && picture < Sizes.Length ? Sizes[picture] : (0, 0);

    /// <summary>
    /// 그림 한 장을 BGRA 로 푼다. 못 풀면 null.
    /// </summary>
    /// <remarks>
    /// 팔레트는 <b>쓰는 색인을 보고</b> 고른다 — 책 틀과 삽화는 74 부터, 누런 종이는
    /// 144 부터, 흰 종이는 160 부터라 서로 겹치지 않는다.
    /// </remarks>
    public uint[]? TryGetBgra(int picture)
    {
        if (picture < 0 || picture >= PictureCount) return null;
        if (_made.TryGetValue(picture, out var kept)) return kept;

        uint[]? made = null;
        var (w, h) = SizeOf(picture);
        var idx = _archive.Decode(picture + FirstPicturePart);

        if (idx != null && w > 0 && idx.Length >= w * h)
        {
            byte lowest = 255;
            for (int i = 0; i < w * h; i++) lowest = Math.Min(lowest, idx[i]);
            int which = lowest < Bases[1] ? 0 : lowest < Bases[2] ? 1 : 2;
            int start = Bases[which];
            var pal = _palettes[which] ??= _archive.Decode(which);

            if (pal != null)
            {
                made = new uint[w * h];
                for (int i = 0; i < made.Length; i++)
                {
                    byte v = idx[i];
                    int k = (v - start) * 3;
                    byte r, g, b;
                    if (v >= start && k + 2 < pal.Length)
                    {
                        b = pal[k];                 // 도시 그림과 같은 (파랑, 빨강, 초록) 차례다
                        r = pal[k + 1];
                        g = pal[k + 2];
                    }
                    else
                    {
                        r = GamePalette.Rgb[v * 3];
                        g = GamePalette.Rgb[v * 3 + 1];
                        b = GamePalette.Rgb[v * 3 + 2];
                    }
                    made[i] = 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | b;
                }
            }
        }

        _made[picture] = made;
        return made;
    }
}
