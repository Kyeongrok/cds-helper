using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CdsHelper.Support.Local.Helpers;

/// <summary>
/// 함대 그림 8방향. 게임이 안 켜져 있어도 배를 그릴 수 있게 <c>asset/ship</c> 에 넣어 둔 것이다.
/// </summary>
/// <remarks>
/// 원본은 실행 중인 CDS_95 의 배 아틀라스(VA 0x5D68C8)에서 클래스 0 의 8방향을 뜬 것이다.
/// EXE 파일에는 없다 — 그 자리가 .data 의 초기화되지 않은 뒷부분(rawsize 0x51C00 밖)이라
/// 실행 중에만 찬다. 그래서 한 번 떠서 PNG 로 남겨 두고 여기서 읽는다.
///
/// 파일은 <c>asset/ship/ship_0.png</c> ~ <c>ship_7.png</c>, 한 장 48x48 이고 비침은 알파 0 이다.
/// 색인이 아니라 색이 그대로 들어 있으므로 그림판으로 열어 고쳐도 그대로 나온다.
///
/// 번호는 게임 방향(반시계)을 둘로 접은 것이다 — 0 북, 2 서, 4 남, 6 동.
/// 게임이 떠 있으면 <see cref="GameShipReader"/> 가 읽은 것을 쓰고, 없을 때 이 표로 물러선다.
/// </remarks>
public static class ShipSprites
{
    public const int Width = 48;
    public const int Size = Width * Width;
    public const int Directions = 8;

    /// <summary>실행 파일 옆의 이 폴더에서 찾는다.</summary>
    public const string AssetDirectory = "asset/ship";

    private static readonly uint[]?[] Frames = new uint[Directions][];
    private static readonly object Gate = new();

    /// <summary>못 읽은 까닭 한 줄. 다 읽었으면 빈 문자열.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>
    /// 16방향 값으로 48x48 그림 한 장(BGRA, 비침은 알파 0). 파일이 없으면 빈 span.
    /// 한 번 읽은 것은 들고 있는다.
    /// </summary>
    public static ReadOnlySpan<uint> Frame(int heading16)
    {
        int i = (heading16 & 0xF) >> 1;
        var cached = Frames[i];
        if (cached != null) return cached;

        lock (Gate)
        {
            Frames[i] ??= Load(i) ?? [];
            return Frames[i];
        }
    }

    private static uint[]? Load(int index)
    {
        var path = Path.Combine(AppContext.BaseDirectory, AssetDirectory, $"ship_{index}.png");
        if (!File.Exists(path))
        {
            LastError = $"{path} 없음";
            return null;
        }
        try
        {
            using var fs = File.OpenRead(path);
            var decoder = new PngBitmapDecoder(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var src = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
            if (src.PixelWidth != Width || src.PixelHeight != Width)
            {
                LastError = $"{path} 크기가 {src.PixelWidth}x{src.PixelHeight} — {Width}x{Width} 이어야 합니다";
                return null;
            }
            var px = new uint[Size];
            src.CopyPixels(px, Width * 4, 0);
            LastError = "";
            return px;
        }
        catch (Exception ex)
        {
            LastError = $"{path} 를 읽지 못했습니다 — {ex.Message}";
            return null;
        }
    }
}
