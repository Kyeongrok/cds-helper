using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CdsHelper.Support.Local.Helpers;

/// <summary>
/// WORLD.CDS 파일을 읽어 단일 타일(2500x1250) 비트맵으로 렌더링하는 헬퍼.
/// 색상/좌표 변환 로직은 WorldMapContent와 일치하도록 유지.
/// </summary>
public static class WorldMapRenderer
{
    public const int RawStride = 2500;   // bytes per row in file
    public const int CellW = 1250;       // cells per row (2 bytes/cell)
    public const int CellH = 1250;       // unfolded rows (2500 raw / 2)
    public const int UnfoldedW = 2500;   // unfolded width (left half + right half)

    /// <summary>
    /// WORLD.CDS 파일을 로드한다. 유효하지 않으면 null 반환.
    /// </summary>
    public static byte[]? LoadWorldData(string path)
    {
        if (!File.Exists(path)) return null;
        var data = File.ReadAllBytes(path);
        if (data.Length != RawStride * CellH * 2) return null;
        return data;
    }

    /// <summary>
    /// WORLD.CDS 데이터로부터 2500x1250 단일 타일을 렌더링한 WriteableBitmap 생성.
    /// </summary>
    public static WriteableBitmap RenderSingleTile(byte[] worldData, bool showCoast = true, bool showWind = false)
    {
        var pixels = new int[UnfoldedW * CellH];

        for (int ry = 0; ry < CellH; ry++)
        {
            int evenRow = ry * 2;
            int oddRow = ry * 2 + 1;

            for (int cx = 0; cx < CellW; cx++)
            {
                int offE = evenRow * RawStride + cx * 2;
                byte tE = (byte)(worldData[offE] & 0x7F);
                byte aE = worldData[offE + 1];

                int offO = oddRow * RawStride + cx * 2;
                byte tO = (byte)(worldData[offO] & 0x7F);
                byte aO = worldData[offO + 1];

                pixels[ry * UnfoldedW + cx] = ColorToInt(GetCellColor(tE, aE, showWind, showCoast));
                pixels[ry * UnfoldedW + cx + CellW] = ColorToInt(GetCellColor(tO, aO, showWind, showCoast));
            }
        }

        var bmp = new WriteableBitmap(UnfoldedW, CellH, 96, 96, PixelFormats.Bgr32, null);
        bmp.WritePixels(new Int32Rect(0, 0, UnfoldedW, CellH), pixels, UnfoldedW * 4, 0);
        return bmp;
    }

    /// <summary>위도/경도를 단일 타일 기준 픽셀 좌표로 변환.</summary>
    public static (double px, double py) LatLonToPixel(double lat, double lon)
    {
        double cellX = (lon + 180.0) / 360.0 * UnfoldedW;
        double cellY = (90.0 - lat) / 180.0 * CellH;
        return (cellX, cellY);
    }

    /// <summary>단일 타일 픽셀 좌표를 위도/경도로 역변환.</summary>
    public static (double lat, double lon) PixelToLatLon(double px, double py)
    {
        double lon = px * 360.0 / UnfoldedW - 180;
        double lat = 90.0 - py * 180.0 / CellH;
        return (lat, lon);
    }

    #region 색상 변환 (WorldMapContent와 동일)

    private static readonly Color SeaBase = Color.FromRgb(57, 83, 103);

    private static Color GetCellColor(byte terrain, byte attr, bool showWind, bool showCoast)
    {
        if (terrain == 0)
            return showWind ? GetWindColor(attr) : SeaBase;
        if (terrain == 1)
            return GetLandColor(attr);
        float landRatio = GetCoastLandRatio(terrain);
        if (!showCoast)
            landRatio = Math.Clamp(landRatio, 0.2f, 0.8f);
        var sea = SeaBase;
        var land = GetLandAttrColor(attr);
        return BlendColor(sea, land, landRatio);
    }

    private static Color BlendColor(Color a, Color b, float t)
    {
        return Color.FromRgb(
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));
    }

    private static int ColorToInt(Color c) => (c.R << 16) | (c.G << 8) | c.B;

    private static Color GetWindColor(byte wind)
    {
        return wind switch
        {
            0 => Color.FromRgb(45, 70, 88),
            1 => Color.FromRgb(50, 75, 93),
            5 => Color.FromRgb(58, 84, 105),
            6 => Color.FromRgb(55, 82, 102),
            7 => Color.FromRgb(62, 88, 108),
            8 => Color.FromRgb(65, 90, 110),
            9 => Color.FromRgb(60, 86, 106),
            10 => Color.FromRgb(63, 89, 109),
            _ => Color.FromRgb(68, 96, 117),
        };
    }

    private static Color GetLandColor(byte attr)
    {
        return attr switch
        {
            64 => Color.FromRgb(161, 134, 102),
            66 => Color.FromRgb(148, 139, 108),
            67 => Color.FromRgb(95, 120, 72),
            68 => Color.FromRgb(85, 110, 65),
            _ when attr >= 86 && attr <= 116 => GetClimateColor(attr),
            _ => Color.FromRgb(137, 126, 94),
        };
    }

    private static Color GetLandAttrColor(byte attr)
    {
        if (attr <= 10) return Color.FromRgb(137, 126, 94);
        return GetLandColor(attr);
    }

    private static Color GetClimateColor(byte attr)
    {
        float t = (attr - 86f) / 30f;
        return Color.FromRgb(
            (byte)(140 + t * 30),
            (byte)(120 + t * 20),
            (byte)(85 + t * 25));
    }

    private static float GetCoastLandRatio(byte terrain)
    {
        return terrain switch
        {
            2 => 0.39f, 3 => 0.18f, 4 => 0.49f, 5 => 0.00f,
            6 => 0.50f, 7 => 0.43f, 8 => 0.52f, 9 => 0.50f,
            10 => 0.14f, 11 => 0.55f, 12 => 0.29f, 13 => 0.00f,
            14 => 0.11f, 15 => 0.55f, 16 => 0.42f, 17 => 0.35f,
            18 => 0.84f, 19 => 0.43f, 20 => 0.50f, 21 => 0.03f,
            22 => 0.00f, 23 => 0.29f, 24 => 0.43f, 25 => 0.31f,
            26 => 0.00f, 27 => 0.00f, 28 => 0.00f, 29 => 0.29f,
            30 => 0.12f, 31 => 0.11f, 32 => 0.27f, 33 => 0.27f,
            34 => 0.60f, 35 => 0.51f, 36 => 0.12f, 37 => 0.54f,
            38 => 0.29f, 39 => 0.66f, 40 => 0.00f, 41 => 0.25f,
            42 => 1.00f, 43 => 0.78f, 44 => 0.00f, 45 => 0.57f,
            46 => 0.50f, 48 => 0.92f, 49 => 0.54f, 50 => 0.28f,
            51 => 0.14f, 53 => 0.99f, 55 => 0.78f, 56 => 0.00f,
            57 => 0.83f, 58 => 0.34f, 59 => 0.40f, 60 => 1.00f,
            61 => 0.04f, 62 => 0.10f, 64 => 0.00f, 65 => 0.06f,
            66 => 0.97f, 67 => 0.58f, 68 => 0.17f, 70 => 0.49f,
            71 => 0.26f, 72 => 1.00f, 73 => 0.12f, 75 => 0.91f,
            76 => 0.96f, 77 => 0.62f, 78 => 1.00f, 79 => 0.49f,
            81 => 0.66f, 82 => 0.99f, 83 => 0.03f, 84 => 0.93f,
            85 => 0.23f, 92 => 0.44f, 96 => 0.96f, 103 => 1.00f,
            118 => 0.94f,
            _ => Math.Min(terrain / 127f, 1f),
        };
    }

    #endregion
}
