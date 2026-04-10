using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Support.Local.Settings;
using Microsoft.Win32;

namespace CdsHelper.Main.UI.Views;

public class WorldMapContent : ContentControl
{
    private Image? _mapImage;
    private ScrollViewer? _scrollViewer;
    private ScaleTransform? _scaleTransform;
    private TextBlock? _txtStatus;
    private TextBlock? _txtCellInfo;
    private Button? _btnOpen;
    private Button? _btnZoomIn;
    private Button? _btnZoomOut;
    private Button? _btnZoomReset;
    private CheckBox? _chkShowCoast;
    private CheckBox? _chkShowWind;

    private double _currentScale = 1.0;
    private const double ScaleStep = 0.25;
    private const double MinScale = 0.2;
    private const double MaxScale = 5.0;

    private byte[]? _mapData;
    private const int RawStride = 2500;   // bytes per row in file
    private const int CellW = 1250;       // cells per row (2 bytes/cell)
    private const int CellH = 1250;       // unfolded rows (2500 raw / 2)
    private const int UnfoldedW = 2500;   // unfolded width (left half + right half)
    private const int TileCount = 3;      // 3 copies for infinite horizontal scroll
    private const int RenderW = 2500 * TileCount; // 7500 total width
    private const int RenderH = 1250;     // display height

    private bool _isDragging;
    private Point _lastMousePos;

    static WorldMapContent()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(WorldMapContent),
            new FrameworkPropertyMetadata(typeof(WorldMapContent)));
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        _mapImage = GetTemplateChild("PART_MapImage") as Image;
        _scrollViewer = GetTemplateChild("PART_ScrollViewer") as ScrollViewer;
        _txtStatus = GetTemplateChild("PART_Status") as TextBlock;
        _txtCellInfo = GetTemplateChild("PART_CellInfo") as TextBlock;
        _btnOpen = GetTemplateChild("PART_BtnOpen") as Button;
        _btnZoomIn = GetTemplateChild("PART_BtnZoomIn") as Button;
        _btnZoomOut = GetTemplateChild("PART_BtnZoomOut") as Button;
        _btnZoomReset = GetTemplateChild("PART_BtnZoomReset") as Button;
        _chkShowCoast = GetTemplateChild("PART_ShowCoast") as CheckBox;
        _chkShowWind = GetTemplateChild("PART_ShowWind") as CheckBox;

        _scaleTransform = new ScaleTransform(1, 1);
        if (_mapImage != null)
        {
            _mapImage.LayoutTransform = _scaleTransform;
            _mapImage.MouseMove += MapImage_MouseMove;
        }

        if (_btnOpen != null) _btnOpen.Click += (_, _) => OpenFile();
        if (_btnZoomIn != null) _btnZoomIn.Click += (_, _) => Zoom(ScaleStep);
        if (_btnZoomOut != null) _btnZoomOut.Click += (_, _) => Zoom(-ScaleStep);
        if (_btnZoomReset != null) _btnZoomReset.Click += (_, _) => { _currentScale = 1.0; ApplyScale(); };

        if (_chkShowCoast != null) _chkShowCoast.Click += (_, _) => Rerender();
        if (_chkShowWind != null) _chkShowWind.Click += (_, _) => Rerender();

        if (_scrollViewer != null)
        {
            _scrollViewer.PreviewMouseWheel += (s, e) =>
            {
                ZoomAtCursor(e.Delta > 0 ? ScaleStep : -ScaleStep, e);
                e.Handled = true;
            };
            _scrollViewer.PreviewMouseLeftButtonDown += ScrollViewer_MouseDown;
            _scrollViewer.PreviewMouseLeftButtonUp += ScrollViewer_MouseUp;
            _scrollViewer.PreviewMouseMove += ScrollViewer_MouseMove;
            _scrollViewer.ScrollChanged += ScrollViewer_WrapHorizontal;
        }

        // 세이브 파일 경로 기준으로 WORLD.CDS 자동 로드
        var savePath = AppSettings.LastSaveFilePath;
        if (!string.IsNullOrEmpty(savePath))
        {
            var dir = Path.GetDirectoryName(savePath);
            if (dir != null)
            {
                var worldPath = Path.Combine(dir, "WORLD.CDS");
                if (File.Exists(worldPath))
                    LoadAndRender(worldPath);
            }
        }
    }

    private void ScrollViewer_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_scrollViewer == null) return;
        _isDragging = true;
        _lastMousePos = e.GetPosition(_scrollViewer);
        _scrollViewer.Cursor = Cursors.Hand;
        e.Handled = true;
    }

    private void ScrollViewer_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _isDragging = false;
        if (_scrollViewer != null) _scrollViewer.Cursor = Cursors.Arrow;
    }

    private void ScrollViewer_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging || _scrollViewer == null) return;
        var pos = e.GetPosition(_scrollViewer);
        var dx = pos.X - _lastMousePos.X;
        var dy = pos.Y - _lastMousePos.Y;
        _scrollViewer.ScrollToHorizontalOffset(_scrollViewer.HorizontalOffset - dx);
        _scrollViewer.ScrollToVerticalOffset(_scrollViewer.VerticalOffset - dy);
        _lastMousePos = pos;
    }

    private bool _isWrapping;

    private void ScrollViewer_WrapHorizontal(object sender, ScrollChangedEventArgs e)
    {
        if (_isWrapping || _scrollViewer == null || _mapData == null) return;

        double tileW = UnfoldedW * _currentScale;
        double offset = _scrollViewer.HorizontalOffset;

        // Keep scroll within the middle tile (tile index 1)
        if (offset < tileW * 0.5)
        {
            _isWrapping = true;
            _scrollViewer.ScrollToHorizontalOffset(offset + tileW);
            _isWrapping = false;
        }
        else if (offset > tileW * 1.5)
        {
            _isWrapping = true;
            _scrollViewer.ScrollToHorizontalOffset(offset - tileW);
            _isWrapping = false;
        }
    }

    private void CenterScrollToMiddleTile()
    {
        if (_scrollViewer == null) return;
        _scrollViewer.UpdateLayout();
        double tileW = UnfoldedW * _currentScale;
        _scrollViewer.ScrollToHorizontalOffset(tileW);
    }

    private void MapImage_MouseMove(object sender, MouseEventArgs e)
    {
        if (_mapData == null || _mapImage?.Source == null || _txtCellInfo == null) return;

        var pos = e.GetPosition(_mapImage);
        int rx = (int)pos.X;
        int ry = (int)pos.Y;
        if (rx < 0 || rx >= RenderW || ry < 0 || ry >= RenderH) return;

        // Map render pixel to unfolded coordinate (wrap within one tile)
        int tileX = rx % UnfoldedW;
        bool isRightHalf = tileX >= CellW;
        int cx = isRightHalf ? tileX - CellW : tileX;
        if (cx >= CellW) cx = CellW - 1;
        if (ry >= CellH) ry = CellH - 1;

        int rawRow = isRightHalf ? ry * 2 + 1 : ry * 2;
        int off = rawRow * RawStride + cx * 2;
        byte terrain = (byte)(_mapData[off] & 0x7F);
        byte attr = _mapData[off + 1];

        int unfoldedX = isRightHalf ? cx + CellW : cx;
        double lon = unfoldedX * 360.0 / UnfoldedW - 180;
        double lat = 90.0 - ry * 180.0 / CellH;

        string terrainName = terrain switch
        {
            0 => "바다",
            1 => "육지",
            _ => $"해안({terrain})"
        };

        string attrName = terrain switch
        {
            0 => $"해류={attr}",
            1 => attr switch
            {
                68 => "온대림",
                67 => "밀림",
                66 => "초원",
                64 => "사막",
                _ when attr >= 86 && attr <= 116 => $"기후대({attr})",
                _ => $"지형={attr}"
            },
            _ => $"해안비율={GetCoastLandRatio(terrain):P0} attr={attr}"
        };

        string latDir = lat >= 0 ? "N" : "S";
        string lonDir = lon >= 0 ? "E" : "W";
        _txtCellInfo.Text = $"셀({unfoldedX},{ry}) | {Math.Abs(lat):F1}°{latDir}, {Math.Abs(lon):F1}°{lonDir} | {terrainName} | {attrName} | 0x{_mapData[off]:X2} 0x{attr:X2}";
    }

    private void OpenFile()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "CDS 파일 (*.CDS)|*.CDS|모든 파일 (*.*)|*.*",
            Title = "WORLD.CDS 파일 선택"
        };
        if (dlg.ShowDialog() == true)
            LoadAndRender(dlg.FileName);
    }

    private void LoadAndRender(string path)
    {
        try
        {
            _mapData = File.ReadAllBytes(path);
            if (_mapData.Length != RawStride * CellH * 2)
            {
                if (_txtStatus != null)
                    _txtStatus.Text = $"파일 크기 불일치: {_mapData.Length} (예상: {RawStride * CellH * 2})";
                _mapData = null;
                return;
            }
            if (_txtStatus != null)
                _txtStatus.Text = $"로드 완료: {Path.GetFileName(path)} (펼침: {UnfoldedW}x{CellH}, {RenderW}x{RenderH}px)";
            Rerender();
        }
        catch (Exception ex)
        {
            if (_txtStatus != null)
                _txtStatus.Text = $"로드 실패: {ex.Message}";
        }
    }

    private void Rerender()
    {
        if (_mapData == null || _mapImage == null) return;

        bool showCoast = _chkShowCoast?.IsChecked == true;
        bool showWind = _chkShowWind?.IsChecked == true;

        // Render one tile (2500 wide), then copy 3x for infinite scroll
        var tilePixels = new int[UnfoldedW * RenderH];

        for (int ry = 0; ry < CellH; ry++)
        {
            int evenRow = ry * 2;
            int oddRow = ry * 2 + 1;

            for (int cx = 0; cx < CellW; cx++)
            {
                int offE = evenRow * RawStride + cx * 2;
                byte tE = (byte)(_mapData[offE] & 0x7F);
                byte aE = _mapData[offE + 1];

                int offO = oddRow * RawStride + cx * 2;
                byte tO = (byte)(_mapData[offO] & 0x7F);
                byte aO = _mapData[offO + 1];

                tilePixels[ry * UnfoldedW + cx] = ColorToInt(GetCellColor(tE, aE, showWind, showCoast));
                tilePixels[ry * UnfoldedW + cx + CellW] = ColorToInt(GetCellColor(tO, aO, showWind, showCoast));
            }
        }

        // Tile 3 copies horizontally
        var bmp = new WriteableBitmap(RenderW, RenderH, 96, 96, PixelFormats.Bgr32, null);
        var pixels = new int[RenderW * RenderH];
        for (int ry = 0; ry < RenderH; ry++)
        {
            for (int t = 0; t < TileCount; t++)
            {
                Array.Copy(tilePixels, ry * UnfoldedW, pixels, ry * RenderW + t * UnfoldedW, UnfoldedW);
            }
        }

        bmp.WritePixels(new Int32Rect(0, 0, RenderW, RenderH), pixels, RenderW * 4, 0);
        _mapImage.Source = bmp;
        CenterScrollToMiddleTile();
    }

    private static Color GetCellColor(byte terrain, byte attr, bool showWind, bool showCoast)
    {
        if (terrain == 0)
            return showWind ? GetWindColor(attr) : SeaBase;
        if (terrain == 1)
            return GetLandColor(attr);
        // Coast: blend sea and land colors using land ratio
        float landRatio = GetCoastLandRatio(terrain);
        if (!showCoast)
            landRatio = Math.Clamp(landRatio, 0.2f, 0.8f); // flatten when detail off
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

    // Game-accurate colors extracted from screenshot analysis
    private static readonly Color SeaBase = Color.FromRgb(57, 83, 103);

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
            64 => Color.FromRgb(161, 134, 102),  // desert
            66 => Color.FromRgb(148, 139, 108),   // grassland
            67 => Color.FromRgb(95, 120, 72),     // dense forest/jungle
            68 => Color.FromRgb(85, 110, 65),     // temperate forest
            _ when attr >= 86 && attr <= 116 => GetClimateColor(attr),
            _ => Color.FromRgb(137, 126, 94),
        };
    }

    private static Color GetLandAttrColor(byte attr)
    {
        // For coast blending: resolve attr to land-side color
        if (attr <= 10) return Color.FromRgb(137, 126, 94); // coast near sea uses default land
        return GetLandColor(attr);
    }

    private static Color GetClimateColor(byte attr)
    {
        // attrs 86-116 appear to be latitude/climate zone variants
        float t = (attr - 86f) / 30f;
        // Warm (low) = desert-like, Cool (high) = tundra-like
        return Color.FromRgb(
            (byte)(140 + t * 30),
            (byte)(120 + t * 20),
            (byte)(85 + t * 25));
    }

    // Coast land ratios: extracted from game tiles
    // terrain value -> fraction of tile that is land (vs sea)
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
            _ => Math.Min(terrain / 127f, 1f), // fallback: linear estimate
        };
    }

    private void ZoomAtCursor(double delta, MouseWheelEventArgs e)
    {
        if (_scrollViewer == null || _scaleTransform == null) return;

        var mousePos = e.GetPosition(_scrollViewer);
        double oldScale = _currentScale;
        _currentScale = Math.Clamp(_currentScale + delta, MinScale, MaxScale);
        if (Math.Abs(_currentScale - oldScale) < 0.001) return;

        double ratio = _currentScale / oldScale;

        double pointX = _scrollViewer.HorizontalOffset + mousePos.X;
        double pointY = _scrollViewer.VerticalOffset + mousePos.Y;

        _scaleTransform.ScaleX = _currentScale;
        _scaleTransform.ScaleY = _currentScale;
        _scrollViewer.UpdateLayout();

        _isWrapping = true;
        _scrollViewer.ScrollToHorizontalOffset(pointX * ratio - mousePos.X);
        _scrollViewer.ScrollToVerticalOffset(pointY * ratio - mousePos.Y);
        _isWrapping = false;
    }

    private void Zoom(double delta)
    {
        _currentScale = Math.Clamp(_currentScale + delta, MinScale, MaxScale);
        ApplyScale();
    }

    private void ApplyScale()
    {
        if (_scaleTransform == null) return;
        _scaleTransform.ScaleX = _currentScale;
        _scaleTransform.ScaleY = _currentScale;
    }
}
