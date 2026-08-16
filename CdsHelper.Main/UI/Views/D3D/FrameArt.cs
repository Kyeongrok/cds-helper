using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CdsHelper.Main.UI.Views.D3D;

/// <summary>
/// 게임 액자를 코드로 그린다. 어떤 크기로든 이음매 없이 나온다.
/// </summary>
/// <remarks>
/// 게임 그림(<c>MISC.CDS</c> 파트 0)을 픽셀 단위로 읽어 보니 규칙이 딱 떨어졌다 —
/// 바깥에서 안으로 겹이 이렇게 쌓인다.
/// <code>
///   깊이 0        짙은 밤색 한 줄
///   깊이 1~2      밝은 테 두 줄
///   깊이 3~5      구슬 무늬 띠 세 줄   (다섯 칸마다 되풀이)
///   깊이 6        밝은 테 한 줄
///   깊이 7        짙은 밤색 한 줄
///   깊이 8        그늘 한 줄
///   깊이 9~       속(한 색)
/// </code>
/// 그래서 그림을 잘라 늘리는 대신 그때그때 그린다. 잘라 쓰면 늘릴 때 이음매가 보이고 크기마다
/// 조각을 따로 떠야 하는데, 그릴 줄 알면 그 수고가 다 없어진다.
///
/// 색은 원본에서 그대로 뽑았다.
/// </remarks>
internal static class FrameArt
{
    private const uint Outer = 0xFF705C48;   // 짙은 밤색 테
    private const uint Light = 0xFFC4B494;   // 밝은 테
    private const uint Dark = 0xFF503824;    // 구슬 띠 바탕
    private const uint Mid = 0xFF584838;     // 구슬
    private const uint Shade = 0xFFBCA880;   // 속 가장자리 그늘
    private const uint Fill = 0xFFD4C8B0;    // 속

    /// <summary>테 두께. 이만큼 안쪽부터 속이다.</summary>
    public const int Border = 9;

    /// <summary>
    /// 얇은 테 두께. 상단·하단 띠처럼 낮아야 하는 자리에 쓴다.
    /// </summary>
    /// <remarks>
    /// 게임은 창 크기에 맞춰 화면을 줄인다. 큰 창에서 찍은 그림을 그대로 1배로 쓰면 우리
    /// 창에서는 테가 두꺼워 띠가 뚱뚱해진다.
    ///
    /// <b>줄일 때 밝은 테를 빼면 안 된다.</b> 구슬 띠는 두 어두운 색으로만 되어 있어서,
    /// 양쪽의 밝은 테가 없으면 무늬가 안 보이고 그냥 어두운 줄 하나로 뭉친다. 그래서 뺄 것은
    /// 밝은 테가 아니라 밝은 테 하나와 안쪽 짙은 선이다 —
    /// <c>짙은선(1) + 밝은테(1) + 구슬띠(3) + 밝은테(1) + 그늘(1)</c> 로 일곱 줄을 남긴다.
    ///
    /// 안쪽 그늘도 빼면 안 된다. 게임 띠를 확대해 보면 구슬 띠와 속 사이에 줄이 <b>둘</b>
    /// 보이는데, 밝은 테 한 줄과 이 그늘 한 줄이다. 그늘이 빠지면 속이 곧바로 붙어 밋밋해진다.
    /// </remarks>
    public const int ThinBorder = 7;

    /// <summary>구슬 무늬가 되풀이되는 참.</summary>
    private const int BeadPeriod = 5;

    /// <summary>
    /// 구슬 무늬 — 세로가 테 깊이(3~5), 가로가 변을 따라가는 자리(다섯 칸마다 되풀이).
    /// </summary>
    private static readonly uint[,] Bead =
    {
        { Dark,  Mid,  Mid,  Dark,  Dark },
        { Outer, Dark, Dark, Outer, Dark },
        { Dark,  Mid,  Mid,  Dark,  Dark },
    };

    private static uint Pixel(int depth, int along, bool thin)
    {
        int bead = ((along % BeadPeriod) + BeadPeriod) % BeadPeriod;
        if (thin)
            return depth switch
            {
                0 => Outer,
                1 => Light,
                2 or 3 or 4 => Bead[depth - 2, bead],
                5 => Light,
                6 => Shade,
                _ => Fill,
            };

        return depth switch
        {
            0 => Outer,
            1 or 2 => Light,
            3 or 4 or 5 => Bead[depth - 3, bead],
            6 => Light,
            7 => Outer,
            8 => Shade,
            _ => Fill,
        };
    }

    // 날짜 칸 같은 밝은 상자에 쓰는 색. box-light-24.png 에서 뽑았다.
    private const uint CellBase = 0xFFE0D4C0;   // 속 바탕
    private const uint CellDot = 0xFFF0E4CC;    // 속에 성기게 박힌 밝은 점
    private const uint CellEdge = 0xFFC4B494;   // 속을 두르는 밝은 테

    /// <summary>밝은 상자의 테 두께.</summary>
    public const int CellBorder = 5;

    /// <summary>
    /// 상단 띠 안에 놓는 밝은 상자(날짜 칸 따위). 테는 액자와 같은 구슬 무늬고, 속은
    /// 밝은 바탕에 점이 성기게 박혀 반짝인다.
    /// </summary>
    /// <remarks>
    /// 게임 그림(<c>box-light-24.png</c>)을 재 보면 속이 한 색이 아니라 밝은 색 몇 가지가
    /// 섞여 있다. 그 자잘한 결이 상자를 반짝이게 하므로 한 색으로 채우면 밋밋해진다 —
    /// 다섯 칸마다 점을 하나 박아 흉내낸다.
    /// </remarks>
    public static BitmapSource? DrawCell(int width, int height)
    {
        if (width < CellBorder * 2 || height < CellBorder * 2) return null;

        var px = new uint[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int dx = Math.Min(x, width - 1 - x);
                int dy = Math.Min(y, height - 1 - y);
                int depth = Math.Min(dx, dy);
                int along = dy <= dx ? x : y;
                int bead = ((along % BeadPeriod) + BeadPeriod) % BeadPeriod;

                px[y * width + x] = depth switch
                {
                    0 => Outer,
                    1 or 2 or 3 => Bead[depth - 1, bead],
                    4 => CellEdge,
                    // 점은 비스듬히 놓아야 눈에 결로 보인다. 가로세로로 줄 세우면 자국이 진다.
                    _ => (x + y * 2) % 5 == 0 ? CellDot : CellBase,
                };
            }

        var bmp = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null,
                                      px, width * 4);
        bmp.Freeze();
        return bmp;
    }

    /// <summary>액자 한 벌을 그린다. 크기가 테보다 작으면 null.</summary>
    /// <param name="thin">얇은 테(<see cref="ThinBorder"/>)로 그린다.</param>
    public static BitmapSource? Draw(int width, int height, bool thin = false)
    {
        int border = thin ? ThinBorder : Border;
        if (width < border * 2 || height < border * 2) return null;

        var px = new uint[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int dx = Math.Min(x, width - 1 - x);
                int dy = Math.Min(y, height - 1 - y);
                // 가까운 변이 위아래면 무늬가 가로로, 좌우면 세로로 흐른다.
                px[y * width + x] = Pixel(Math.Min(dx, dy), dy <= dx ? x : y, thin);
            }

        var bmp = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null,
                                      px, width * 4);
        bmp.Freeze();
        return bmp;
    }
}
