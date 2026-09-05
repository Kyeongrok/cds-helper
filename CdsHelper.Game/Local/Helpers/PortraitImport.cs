using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Support.Local.Helpers;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 바깥 그림 한 장을 게임 초상화(<b>80x96 · 256색</b>)로 옮겨 넣는다.
/// </summary>
/// <remarks>
/// 두 가지를 한다.
/// <list type="number">
///   <item><b>네모에 맞춘다.</b> 비율이 다르면 <see cref="Fit.Cover"/> 로 <b>넘치게</b>
///   키운 뒤 넘어가는 만큼 잘라 낸다 — 얼굴이 찌그러지지 않는다. 안 자르고 통째로
///   넣고 싶으면 <see cref="Fit.Contain"/> 이다.</item>
///   <item><b>색을 게임 팔레트로 줄인다.</b> 게임 초상화는 색인 그림이라
///   <see cref="GamePalette"/> 의 256색 가운데 가장 가까운 것을 고른다.</item>
/// </list>
/// 넣는 데는 <c>asset\MALE.CDS</c> · <c>asset\FEMALE.CDS</c> 다 —
/// <see cref="Portraits.Open"/> 이 게임 폴더보다 이 벌을 먼저 보므로, 게임 파일은
/// 안 건드리고도 앱에 바로 먹는다.
/// </remarks>
public static class PortraitImport
{
    /// <summary>네모에 맞추는 두 가지 결.</summary>
    public enum Fit
    {
        /// <summary>넘치게 키워 넣고 넘어가는 만큼 <b>잘라 낸다</b>. 비율이 안 찌그러진다.</summary>
        Cover,

        /// <summary>통째로 들어가게 <b>줄이고</b> 남는 자리는 가장자리 색으로 메운다.</summary>
        Contain,
    }

    /// <summary>왜 못 했는지. 잘 됐으면 빈 글.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>
    /// 그림 파일 하나를 80x96 BGRA 로 맞춘다. 못 읽으면 null.
    /// </summary>
    /// <param name="path">그림 파일. PNG · JPG · BMP · GIF 를 읽는다.</param>
    /// <param name="fit">네모에 맞추는 결.</param>
    public static uint[]? Shape(string path, Fit fit)
    {
        LastError = "";
        if (!File.Exists(path)) { LastError = $"{path} 가 없습니다"; return null; }

        BitmapSource source;
        try
        {
            var made = new BitmapImage();
            made.BeginInit();
            made.UriSource = new Uri(path);
            made.CacheOption = BitmapCacheOption.OnLoad;
            made.EndInit();
            made.Freeze();
            source = made;
        }
        catch (NotSupportedException e) { LastError = $"못 읽는 그림입니다 — {e.Message}"; return null; }
        catch (IOException e) { LastError = e.Message; return null; }

        return Shape(source, fit);
    }

    /// <summary>이미 읽어 둔 그림을 80x96 BGRA 로 맞춘다.</summary>
    public static uint[] Shape(BitmapSource source, Fit fit)
    {
        int w = Portraits.Width, h = Portraits.Height;

        // 넘치게 키울지(Cover) 통째로 넣을지(Contain)에 따라 배수가 갈린다.
        double sx = (double)w / source.PixelWidth;
        double sy = (double)h / source.PixelHeight;
        double zoom = fit == Fit.Cover ? Math.Max(sx, sy) : Math.Min(sx, sy);

        double drawW = source.PixelWidth * zoom;
        double drawH = source.PixelHeight * zoom;

        // 가운데를 맞춘다 — Cover 면 넘어간 만큼이 네모 밖으로 잘려 나간다.
        var box = new Rect((w - drawW) / 2, (h - drawH) / 2, drawW, drawH);

        var canvas = new DrawingVisual();
        using (var draw = canvas.RenderOpen())
        {
            // 남는 자리는 검정으로 메운다 — Contain 일 때만 보인다.
            draw.DrawRectangle(Brushes.Black, null, new Rect(0, 0, w, h));
            draw.DrawImage(source, box);
        }

        var made2 = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        made2.Render(canvas);

        var bgra = new uint[w * h];
        made2.CopyPixels(bgra, w * 4, 0);
        return bgra;
    }

    /// <summary>
    /// 80x96 BGRA 를 게임 팔레트 색인 7,680바이트로 줄인다.
    /// </summary>
    /// <remarks>
    /// 가장 가까운 색은 <b>제곱 거리</b>로 고른다. 팔레트가 256색뿐이라 한 점에 256번을
    /// 재는데, 7,680점이라 다 해도 이백만 번이 안 된다 — 눈 깜짝할 새다.
    /// </remarks>
    public static byte[] Quantize(uint[] bgra)
    {
        var made = new byte[Portraits.Width * Portraits.Height];
        var rgb = GamePalette.Rgb;
        int colors = Math.Min(256, rgb.Length / 3);

        for (int i = 0; i < made.Length && i < bgra.Length; i++)
        {
            int r = (byte)(bgra[i] >> 16), g = (byte)(bgra[i] >> 8), b = (byte)bgra[i];

            int best = 0, near = int.MaxValue;
            for (int c = 0; c < colors; c++)
            {
                int dr = r - rgb[c * 3], dg = g - rgb[c * 3 + 1], db = b - rgb[c * 3 + 2];
                int far = dr * dr + dg * dg + db * db;
                if (far >= near) continue;
                near = far;
                best = c;
                if (far == 0) break;
            }
            made[i] = (byte)best;
        }
        return made;
    }

    /// <summary>줄여 둔 색인을 다시 BGRA 로 — 넣기 전에 눈으로 보라고 낸다.</summary>
    public static uint[] Preview(byte[] indexed)
    {
        var rgb = GamePalette.Rgb;
        var bgra = new uint[indexed.Length];
        for (int i = 0; i < indexed.Length; i++)
        {
            int c = indexed[i] * 3;
            if (c + 2 >= rgb.Length) continue;
            bgra[i] = (uint)(0xFF << 24 | rgb[c] << 16 | rgb[c + 1] << 8 | rgb[c + 2]);
        }
        return bgra;
    }

    /// <summary>초상화 벌이 놓인 자리. 없으면 빈 글.</summary>
    public static string PathOf(bool female)
    {
        string file = female ? "FEMALE.CDS" : "MALE.CDS";
        string path = Path.Combine(AppContext.BaseDirectory, "asset", file);
        return File.Exists(path) ? path : "";
    }

    /// <summary>
    /// 그 자리에 초상화를 넣는다. <paramref name="face"/> 가 지금 장수와 같으면 뒤에 붙인다.
    /// </summary>
    /// <returns>넣은 얼굴 번호. 못 넣었으면 −1 이고 까닭은 <see cref="LastError"/> 다.</returns>
    public static int Put(bool female, int face, byte[] indexed)
    {
        LastError = "";

        string path = PathOf(female);
        if (path.Length == 0)
        {
            LastError = $"asset 폴더에 {(female ? "FEMALE" : "MALE")}.CDS 가 없습니다";
            return -1;
        }

        // 처음 손댈 때만 옆에 원본을 하나 남긴다.
        Ls12Writer.Backup(path);

        if (!Ls12Writer.Put(path, face, indexed))
        {
            LastError = Ls12Writer.LastError;
            return -1;
        }
        return face;
    }
}
