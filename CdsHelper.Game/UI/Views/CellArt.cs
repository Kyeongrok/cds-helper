using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 띠 위에 얹는 밝은 칸(날짜 칸 따위)을 코드로 그린다.
/// </summary>
/// <remarks>
/// 액자(<see cref="FrameArt"/>)와 <b>따로</b> 짰다. 얼핏 같은 무늬로 보이지만 게임 그림
/// (<c>box-light-24.png</c>, 16x24, 색 18가지)을 픽셀로 펼쳐 보면 겹 구성이 아주 다르다.
/// <code>
///          액자                     칸
///   바깥   짙은 선                  밝은 선   (위) / 짙은 그림자 (아래)
///   구슬   세 줄, 차례 0·1·2        두 줄, 차례 1·0 (뒤집힘)
///   안쪽   밝은 테 + 짙은 선 + 그늘  중간톤 이음줄 하나
///   속     한 색                    밝은 색 여럿이 섞인 결
/// </code>
/// 그래서 무늬를 빌려 쓰지 않고 여기서 제 것을 그린다. 액자를 손보다 칸이 같이 틀어지는 일도
/// 없다.
///
/// 원본은 위아래가 다르다 — 맨 위는 밝은 선으로 빛을 받고, 맨 아래는 아주 짙은 그림자
/// (<c>341C14</c>)로 눌린다. 그 덕에 칸이 띠 위로 도드라져 보이므로 그대로 살렸다.
/// </remarks>
internal static class CellArt
{
    // box-light-24.png 에서 그대로 뽑은 색.
    private const uint TopLine = 0xFFC4B494;   // 맨 위 밝은 선
    private const uint Shadow = 0xFF341C14;    // 맨 아래 짙은 그림자
    private const uint Edge = 0xFF503824;      // 좌우 바깥 선
    private const uint Outer = 0xFF705C48;
    private const uint Dark = 0xFF503824;
    private const uint Mid = 0xFF584838;
    private const uint Seam = 0xFF908874;      // 구슬과 속 사이 이음줄
    private const uint SeamLow = 0xFF80786C;   // 아래쪽 이음줄은 더 어둡다

    // 속을 이루는 밝은 색들. 한 색으로 채우면 밋밋해서 결이 안 산다.
    private const uint Base = 0xFFE0D4C0;
    private const uint Bright = 0xFFF4D8B0;
    private const uint Dim = 0xFFD4C8B0;
    private const uint Inset = 0xFFA09488;     // 안쪽 오른·아래에 지는 그늘

    /// <summary>구슬 무늬가 되풀이되는 참.</summary>
    private const int BeadPeriod = 5;

    /// <summary>
    /// 구슬 무늬 두 줄. 원본 1·2행을 그대로 옮겼다 — 액자 것과 줄 수도 차례도 다르다.
    /// </summary>
    private static readonly uint[,] Bead =
    {
        { Outer, Dark, Dark, Outer, Dark },
        { Dark,  Mid,  Mid,  Dark,  Dark },
    };

    /// <summary>
    /// 위아래 테 두께 — <c>밝은선(1) + 구슬(2) + 이음줄(1)</c>.
    /// </summary>
    public const int BorderY = 4;

    /// <summary>
    /// 좌우 테 두께 — <b>구슬 두 칸뿐</b>이다.
    /// </summary>
    /// <remarks>
    /// 원본을 재 보면 위아래와 좌우가 다르다. 위아래에는 밝은 선과 이음줄이 붙지만 좌우에는
    /// 없다. 네 변에 똑같이 두르면 좌우가 두꺼워져 칸이 답답해 보인다.
    /// </remarks>
    public const int BorderX = 2;

    /// <summary>안쪽 그늘이 지는 두께. 오른쪽과 아래에만 진다.</summary>
    private const int InsetDepth = 2;

    /// <summary>칸 하나를 그린다. 크기가 테보다 작으면 null.</summary>
    public static BitmapSource? Draw(int width, int height)
    {
        if (width < BorderX * 2 || height < BorderY * 2) return null;

        var px = new uint[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                px[y * width + x] = Pixel(x, y, width, height);

        var bmp = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null,
                                      px, width * 4);
        bmp.Freeze();
        return bmp;
    }

    private static uint Pixel(int x, int y, int width, int height)
    {
        int left = x, right = width - 1 - x, top = y, bottom = height - 1 - y;
        int nearX = Math.Min(left, right), nearY = Math.Min(top, bottom);

        uint At(int row, int along) => Bead[row, ((along % BeadPeriod) + BeadPeriod) % BeadPeriod];

        // 위아래 테가 먼저다 — 원본에서도 맨 윗줄 밝은 선이 좌우 끝까지 이어진다.
        if (nearY < BorderY)
            return nearY switch
            {
                0 => top <= bottom ? TopLine : Shadow,
                1 => At(0, x),
                2 => At(1, x),
                _ => bottom < top ? SeamLow : Seam,
            };

        // 좌우 테는 구슬 두 칸뿐이다.
        if (nearX < BorderX) return At(nearX, y);

        // 속. 오른쪽과 아래에 그늘을 둬 칸이 옴폭하게 보이도록 한다.
        if (right - BorderX < InsetDepth || bottom - BorderY < InsetDepth) return Inset;
        return Grain(x, y);
    }

    /// <summary>
    /// 속의 결. 밝은 색 셋을 성기게 섞는다 — 규칙이 눈에 띄지 않게 서로 소인 수로 흩는다.
    /// </summary>
    private static uint Grain(int x, int y) => (uint)(x * 7 + y * 13) % 11 switch
    {
        0 => Bright,
        1 or 2 => Dim,
        _ => Base,
    };
}
