using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Ellipse = System.Windows.Shapes.Ellipse;
using Polyline = System.Windows.Shapes.Polyline;
using Rectangle = System.Windows.Shapes.Rectangle;
using CdsHelper.Api.Entities;
using CdsHelper.Support.Local.Helpers;
using CdsHelper.Support.Local.Settings;
using Microsoft.Win32;
using Prism.Ioc;

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
    private CheckBox? _chkShowDiscoveries;
    private Button? _btnLeaveCity;
    private Button? _btnTrackCoordinate;
    private TextBlock? _txtCurrentCoordinate;
    private TextBlock? _txtZoomLabel;
    private Canvas? _overlayCanvas;

    // 좌표 추적 / 화면 감지
    private readonly CoordinateOcrService _coordinateOcr = new();
    private readonly GameScreenDetector _screenDetector;

    public WorldMapContent()
    {
        _screenDetector = new GameScreenDetector(_coordinateOcr);
    }
    private DispatcherTimer? _trackingTimer;
    private bool _isTracking;
    private bool _isProcessing;
    private bool _centerOnFirstPosition = true;
    private Ellipse? _positionMarker;
    private Ellipse? _positionPulse;
    private Polyline? _trailLine;
    private readonly PointCollection _trailPoints = new();
    private readonly List<DateTime> _trailTimestamps = new();

    // 발견물 표시
    private readonly List<UIElement> _discoveryMarkers = new();
    private readonly List<UIElement> _cityLabels = new();
    private readonly List<UIElement> _foundMarkers = new();
    private bool _discoveriesLoaded;
    private CheckBox? _chkShowCityLabels;
    private CheckBox? _chkHideFound;
    private HashSet<int>? _discoveredHintIds;
    private HashSet<int>? _hasHintIds;

    private double _currentScale = 2.0;
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
    private bool _autoScrollEnabled = true;

    // 자동이동
    private CancellationTokenSource? _navCts;
    private bool _isNavigating;
    private Ellipse? _destMarker;

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

        _scaleTransform = new ScaleTransform(2, 2);
        var mapGrid = GetTemplateChild("PART_MapGrid") as Grid;
        if (mapGrid != null)
            mapGrid.LayoutTransform = _scaleTransform;
        if (_mapImage != null)
            _mapImage.MouseMove += MapImage_MouseMove;

        if (_btnOpen != null) _btnOpen.Click += (_, _) => OpenFile();
        if (_btnZoomIn != null) _btnZoomIn.Click += (_, _) => Zoom(ScaleStep);
        if (_btnZoomOut != null) _btnZoomOut.Click += (_, _) => Zoom(-ScaleStep);
        if (_btnZoomReset != null) _btnZoomReset.Click += (_, _) => { _currentScale = 1.0; ApplyScale(); };

        if (_chkShowCoast != null) _chkShowCoast.Click += (_, _) => Rerender();
        if (_chkShowWind != null) _chkShowWind.Click += (_, _) => Rerender();

        _chkShowDiscoveries = GetTemplateChild("PART_ShowDiscoveries") as CheckBox;
        if (_chkShowDiscoveries != null) _chkShowDiscoveries.Click += (_, _) => ToggleDiscoveries();

        _chkShowCityLabels = GetTemplateChild("PART_ShowCityLabels") as CheckBox;
        if (_chkShowCityLabels != null) _chkShowCityLabels.Click += (_, _) => ToggleCityLabels();

        _chkHideFound = GetTemplateChild("PART_HideFound") as CheckBox;
        if (_chkHideFound != null) _chkHideFound.Click += (_, _) => ToggleHideFound();

        _btnLeaveCity = GetTemplateChild("PART_BtnLeaveCity") as Button;
        if (_btnLeaveCity != null)
            _btnLeaveCity.Click += (_, _) => LeaveCityForExploration();

        _btnTrackCoordinate = GetTemplateChild("PART_BtnTrackCoordinate") as Button;
        _txtCurrentCoordinate = GetTemplateChild("PART_TxtCurrentCoordinate") as TextBlock;
        _overlayCanvas = GetTemplateChild("PART_OverlayCanvas") as Canvas;
        _txtZoomLabel = GetTemplateChild("PART_ZoomLabel") as TextBlock;
        if (_overlayCanvas != null)
        {
            _overlayCanvas.MouseRightButtonDown += OnMapRightClick;
            // 좌클릭은 드래그용이므로 Canvas를 통과시킴
            _overlayCanvas.MouseLeftButtonDown += (_, e) => e.Handled = false;
        }
        if (_btnTrackCoordinate != null)
            _btnTrackCoordinate.Click += (_, _) => ToggleTracking();

        var btnClearTrail = GetTemplateChild("PART_BtnClearTrail") as Button;
        var btnSaveTrail = GetTemplateChild("PART_BtnSaveTrail") as Button;
        var btnLoadTrail = GetTemplateChild("PART_BtnLoadTrail") as Button;
        if (btnClearTrail != null) btnClearTrail.Click += (_, _) => ClearTrail();
        if (btnSaveTrail != null) btnSaveTrail.Click += (_, _) => SaveTrail();
        if (btnLoadTrail != null) btnLoadTrail.Click += (_, _) => LoadTrail();

        var btnCenterPosition = GetTemplateChild("PART_BtnCenterPosition") as Button;
        if (btnCenterPosition != null) btnCenterPosition.Click += (_, _) => GoToMyLocation();

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

        // 좌표 추적 자동 시작
        var autoHWnd = GameWindowHelper.FindGameWindow();
        if (autoHWnd != IntPtr.Zero)
            StartTracking();
    }

    private void ScrollViewer_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_scrollViewer == null) return;
        // 클릭 가능한 라벨(Border with Tag)에서 시작된 클릭은 드래그 무시
        if (e.OriginalSource is FrameworkElement fe &&
            (fe is Border { Tag: not null } || FindParent<Border>(fe) is { Tag: not null }))
            return;
        _isDragging = true;
        _lastMousePos = e.GetPosition(_scrollViewer);
        _scrollViewer.Cursor = Cursors.Hand;
        e.Handled = true;
    }

    private static T? FindParent<T>(FrameworkElement? element) where T : FrameworkElement
    {
        while (element != null)
        {
            element = element.Parent as FrameworkElement ?? VisualTreeHelper.GetParent(element) as FrameworkElement;
            if (element is T match) return match;
        }
        return null;
    }

    private void ScrollViewer_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _isDragging = false;
        if (_scrollViewer != null) _scrollViewer.Cursor = Cursors.Arrow;
    }

    private void ScrollViewer_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging || _scrollViewer == null) return;
        _autoScrollEnabled = false;
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

            // 발견물 체크 되어 있으면 표시
            if (_chkShowDiscoveries?.IsChecked == true)
                ShowDiscoveries();
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

        // Canvas는 명시적 크기 없이 Grid에서 Image와 동일 영역 차지
        // (Width/Height를 설정하면 Grid 레이아웃에 간섭하여 렌더링 문제 발생)

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
        if (_txtZoomLabel != null)
            _txtZoomLabel.Text = $"x{_currentScale:F1}";
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
        if (_txtZoomLabel != null)
            _txtZoomLabel.Text = $"x{_currentScale:F1}";
    }

    #region 발견물 표시

    private void ToggleDiscoveries()
    {
        if (_chkShowDiscoveries?.IsChecked == true)
            ShowDiscoveries();
        else
            HideDiscoveries();
    }

    private void ShowDiscoveries()
    {
        if (_overlayCanvas == null) return;

        HideDiscoveries();

        if (!_discoveriesLoaded)
        {
            try
            {
                // 발견 여부 로드
                try
                {
                    var saveDataService = ContainerLocator.Container.Resolve<SaveDataService>();
                    if (saveDataService.CurrentSaveGameInfo?.Hints != null)
                    {
                        _discoveredHintIds = saveDataService.CurrentSaveGameInfo.Hints
                            .Where(h => h.IsDiscovered)
                            .Select(h => h.Index - 1)
                            .ToHashSet();
                        _hasHintIds = saveDataService.CurrentSaveGameInfo.Hints
                            .Where(h => h.HasHint)
                            .Select(h => h.Index - 1)
                            .ToHashSet();
                    }
                }
                catch { /* 세이브 데이터 없으면 무시 */ }

                var service = ContainerLocator.Container.Resolve<DiscoveryService>();
                var discoveries = service.GetAllDiscoveries().Values;

                foreach (var d in discoveries)
                {
                    if (d.LatFrom == null && d.LonFrom == null) continue;

                    bool isFound = d.HintId.HasValue && _discoveredHintIds?.Contains(d.HintId.Value) == true;
                    bool hasHint = d.HintId.HasValue && _hasHintIds?.Contains(d.HintId.Value) == true;
                    var isPoint = d.LatFrom == d.LatTo && d.LonFrom == d.LonTo;

                    if (isPoint && d.LatFrom != null && d.LonFrom != null)
                    {
                        AddDiscoveryPoint(d, isFound, hasHint);
                    }
                    else
                    {
                        AddDiscoveryArea(d, isFound, hasHint);
                    }
                }

                // 도시 좌표 표시 (파란색)
                try
                {
                    var cityService = ContainerLocator.Container.Resolve<CityService>();
                    var cities = cityService.GetCitiesWithCoordinatesFromDbAsync().Result;
                    foreach (var city in cities)
                    {
                        if (city.Latitude == null || city.Longitude == null) continue;
                        AddCityPoint(city.Name, city.Latitude.Value, city.Longitude.Value, city.HasLibrary);
                    }
                }
                catch (Exception cityEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[WorldMap] City load error: {cityEx.Message}");
                }

                _discoveriesLoaded = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WorldMap] Discovery load error: {ex.Message}");
            }
        }

        var showLabels = _chkShowCityLabels?.IsChecked == true;
        var hideFound = _chkHideFound?.IsChecked == true;
        foreach (var marker in _discoveryMarkers)
        {
            if (!showLabels && _cityLabels.Contains(marker))
                continue;
            if (hideFound && _foundMarkers.Contains(marker))
                continue;
            marker.Visibility = Visibility.Visible;
        }
    }

    private void HideDiscoveries()
    {
        foreach (var marker in _discoveryMarkers)
            marker.Visibility = Visibility.Collapsed;
        _cityLabels.Clear();
        _foundMarkers.Clear();
    }

    private void ToggleHideFound()
    {
        var hideFound = _chkHideFound?.IsChecked == true;
        foreach (var marker in _foundMarkers)
            marker.Visibility = hideFound ? Visibility.Collapsed : Visibility.Visible;
    }

    private void AddDiscoveryPoint(DiscoveryEntity d, bool isFound = false, bool hasHint = false)
    {
        if (_overlayCanvas == null) return;

        var (px, py) = LatLonToPixel(d.LatFrom!.Value, d.LonFrom!.Value);

        const double size = 6;
        var dot = new Ellipse
        {
            Width = size,
            Height = size,
            Fill = new SolidColorBrush(isFound
                ? Color.FromArgb(150, 100, 100, 100)
                : Color.FromArgb(200, 220, 40, 40)),
            Stroke = new SolidColorBrush(isFound
                ? Color.FromArgb(180, 80, 80, 80)
                : Color.FromArgb(220, 160, 20, 20)),
            StrokeThickness = 1,
            IsHitTestVisible = true,
            ToolTip = isFound ? $"{d.Name} (발견)" : d.Name
        };

        Canvas.SetLeft(dot, px - size / 2);
        Canvas.SetTop(dot, py - size / 2);
        _overlayCanvas.Children.Add(dot);
        _discoveryMarkers.Add(dot);
        if (isFound) _foundMarkers.Add(dot);

        var label = CreateDiscoveryLabel(d.Id, d.Name, hasHint);
        Canvas.SetLeft(label, px + size / 2 + 2);
        Canvas.SetTop(label, py - 6);
        _overlayCanvas.Children.Add(label);
        _discoveryMarkers.Add(label);
        if (isFound) _foundMarkers.Add(label);
    }

    private void AddDiscoveryArea(DiscoveryEntity d, bool isFound = false, bool hasHint = false)
    {
        if (_overlayCanvas == null) return;

        var latFrom = d.LatFrom ?? 0;
        var latTo = d.LatTo ?? latFrom;
        var lonFrom = d.LonFrom ?? 0;
        var lonTo = d.LonTo ?? lonFrom;

        // 위도/경도 범위의 min/max 정리
        var latMin = Math.Min(latFrom, latTo);
        var latMax = Math.Max(latFrom, latTo);
        var lonMin = Math.Min(lonFrom, lonTo);
        var lonMax = Math.Max(lonFrom, lonTo);

        var (x1, y1) = LatLonToPixel(latMax, lonMin); // 좌상단
        var (x2, y2) = LatLonToPixel(latMin, lonMax); // 우하단

        var w = Math.Max(x2 - x1, 2);
        var h = Math.Max(y2 - y1, 2);

        var rect = new Rectangle
        {
            Width = w,
            Height = h,
            Fill = new SolidColorBrush(isFound
                ? Color.FromArgb(20, 100, 100, 100)
                : Color.FromArgb(40, 50, 100, 220)),
            Stroke = new SolidColorBrush(isFound
                ? Color.FromArgb(100, 80, 80, 80)
                : Color.FromArgb(160, 50, 100, 220)),
            StrokeThickness = 1,
            IsHitTestVisible = true,
            ToolTip = isFound ? $"{d.Name} (발견)" : d.Name
        };

        Canvas.SetLeft(rect, x1);
        Canvas.SetTop(rect, y1);
        _overlayCanvas.Children.Add(rect);
        _discoveryMarkers.Add(rect);
        if (isFound) _foundMarkers.Add(rect);

        var label = CreateDiscoveryLabel(d.Id, d.Name, hasHint);
        Canvas.SetLeft(label, x2 + 2);
        Canvas.SetTop(label, y1);
        _overlayCanvas.Children.Add(label);
        _discoveryMarkers.Add(label);
        if (isFound) _foundMarkers.Add(label);
    }

    private void AddCityPoint(string name, int lat, int lon, bool hasLibrary = false)
    {
        if (_overlayCanvas == null) return;

        var (px, py) = LatLonToPixel(lat, lon);

        const double size = 7;
        var fillColor = hasLibrary
            ? Color.FromArgb(230, 255, 200, 0)    // 노란색 (도서관)
            : Color.FromArgb(220, 30, 120, 255);   // 파란색
        var strokeColor = hasLibrary
            ? Color.FromArgb(255, 180, 120, 0)
            : Color.FromArgb(255, 0, 40, 140);

        var dot = new Ellipse
        {
            Width = size,
            Height = size,
            Fill = new SolidColorBrush(fillColor),
            Stroke = new SolidColorBrush(strokeColor),
            StrokeThickness = 1.5,
            IsHitTestVisible = true,
            ToolTip = hasLibrary ? $"{name} (도서관)" : name
        };

        Canvas.SetLeft(dot, px - size / 2);
        Canvas.SetTop(dot, py - size / 2);
        _overlayCanvas.Children.Add(dot);
        _discoveryMarkers.Add(dot);

        // 라벨: 반투명 배경으로 가독성 확보
        var label = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(2, 0, 2, 0),
            IsHitTestVisible = false,
            Visibility = _chkShowCityLabels?.IsChecked == true ? Visibility.Visible : Visibility.Collapsed,
            Child = new TextBlock
            {
                Text = name,
                FontSize = 7,
                Foreground = new SolidColorBrush(hasLibrary ? Color.FromRgb(140, 90, 0) : Color.FromRgb(0, 30, 120)),
            }
        };
        Canvas.SetLeft(label, px + size / 2 + 1);
        Canvas.SetTop(label, py - 7);
        _overlayCanvas.Children.Add(label);
        _discoveryMarkers.Add(label);
        _cityLabels.Add(label);
    }

    private void ToggleCityLabels()
    {
        var show = _chkShowCityLabels?.IsChecked == true;
        foreach (var label in _cityLabels)
            label.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    private Border CreateDiscoveryLabel(int discoveryId, string name, bool hasHint = false)
    {
        var textBlock = new TextBlock
        {
            Text = name,
            FontSize = 7,
            Foreground = Brushes.Black
        };
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(160, 255, 255, 255)),
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(2, 0, 2, 0),
            BorderBrush = hasHint ? new SolidColorBrush(Color.FromRgb(40, 160, 40)) : null,
            BorderThickness = hasHint ? new Thickness(1) : new Thickness(0),
            IsHitTestVisible = true,
            Cursor = Cursors.Hand,
            Tag = discoveryId,
            Child = textBlock
        };
        border.MouseLeftButtonDown += OnDiscoveryLabelClick;
        return border;
    }

    private async void OnDiscoveryLabelClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border border || border.Tag is not int discoveryId) return;
        var textBlock = border.Child as TextBlock;
        if (textBlock == null) return;

        e.Handled = true;

        var service = ContainerLocator.Container.Resolve<DiscoveryService>();
        var discovery = service.GetDiscovery(discoveryId);
        if (discovery == null) return;

        var dialog = new Window
        {
            Title = $"발견물 수정 (ID: {discoveryId})",
            Width = 340, Height = 250,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize
        };

        var grid = new Grid { Margin = new Thickness(10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int r = 0; r < 6; r++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var tbName = new TextBox { Text = discovery.Name, FontSize = 13, Margin = new Thickness(0, 0, 0, 6) };
        var tbLatFrom = new TextBox { Text = discovery.LatFrom?.ToString() ?? "", Margin = new Thickness(0, 0, 0, 6) };
        var tbLatTo = new TextBox { Text = discovery.LatTo?.ToString() ?? "", Margin = new Thickness(0, 0, 0, 6) };
        var tbLonFrom = new TextBox { Text = discovery.LonFrom?.ToString() ?? "", Margin = new Thickness(0, 0, 0, 6) };
        var tbLonTo = new TextBox { Text = discovery.LonTo?.ToString() ?? "", Margin = new Thickness(0, 0, 0, 6) };

        var labels = new[] { "이름", "위도 From", "위도 To", "경도 From", "경도 To" };
        var boxes = new[] { tbName, tbLatFrom, tbLatTo, tbLonFrom, tbLonTo };
        for (int r = 0; r < 5; r++)
        {
            var lbl = new TextBlock { Text = labels[r], VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 6) };
            Grid.SetRow(lbl, r); Grid.SetColumn(lbl, 0);
            Grid.SetRow(boxes[r], r); Grid.SetColumn(boxes[r], 1);
            grid.Children.Add(lbl);
            grid.Children.Add(boxes[r]);
        }

        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0) };
        var btnOk = new Button { Content = "확인", Width = 70, Margin = new Thickness(0, 0, 6, 0), IsDefault = true };
        var btnCancel = new Button { Content = "취소", Width = 70, IsCancel = true };
        btnOk.Click += (_, _) => dialog.DialogResult = true;
        btnPanel.Children.Add(btnOk);
        btnPanel.Children.Add(btnCancel);
        Grid.SetRow(btnPanel, 5); Grid.SetColumnSpan(btnPanel, 2);
        grid.Children.Add(btnPanel);

        dialog.Content = grid;
        tbName.SelectAll();
        tbName.Focus();

        if (dialog.ShowDialog() != true) return;

        try
        {
            var oldName = discovery.Name;
            var newName = tbName.Text.Trim();

            int? ParseInt(string s) => int.TryParse(s.Trim(), out var v) ? v : null;
            var latFrom = ParseInt(tbLatFrom.Text);
            var latTo = ParseInt(tbLatTo.Text);
            var lonFrom = ParseInt(tbLonFrom.Text);
            var lonTo = ParseInt(tbLonTo.Text);

            if (!string.IsNullOrEmpty(newName) && newName != oldName)
            {
                await service.UpdateNameAsync(discoveryId, newName);
                textBlock.Text = newName;
                foreach (var marker in _discoveryMarkers)
                {
                    if (marker is FrameworkElement fe && fe.ToolTip as string == oldName)
                        fe.ToolTip = newName;
                }
            }

            if (latFrom != discovery.LatFrom || latTo != discovery.LatTo ||
                lonFrom != discovery.LonFrom || lonTo != discovery.LonTo)
            {
                await service.UpdateCoordinateAsync(discoveryId, latFrom, latTo, lonFrom, lonTo);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"수정 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    #endregion

    #region 좌표 추적

    private void ToggleTracking()
    {
        if (_isTracking)
        {
            StopTracking();
        }
        else
        {
            var hWnd = GameWindowHelper.FindGameWindow();
            if (hWnd == IntPtr.Zero)
            {
                MessageBox.Show("게임 창을 찾을 수 없습니다.", "알림",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            StartTracking();
        }
    }

    private void StartTracking()
    {
        _isTracking = true;
        _isProcessing = false;
        _centerOnFirstPosition = true;
        if (_btnTrackCoordinate != null) _btnTrackCoordinate.Content = "추적 중지";

        _trackingTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _trackingTimer.Tick += OnTrackingTick;
        _trackingTimer.Start();
    }

    private void StopTracking()
    {
        _isTracking = false;
        _trackingTimer?.Stop();
        _trackingTimer = null;

        if (_btnTrackCoordinate != null) _btnTrackCoordinate.Content = "좌표 추적";
        if (_txtCurrentCoordinate != null) _txtCurrentCoordinate.Text = "";
        if (_positionMarker != null) _positionMarker.Visibility = Visibility.Collapsed;
        if (_positionPulse != null) _positionPulse.Visibility = Visibility.Collapsed;
    }

    private async void OnTrackingTick(object? sender, EventArgs e)
    {
        if (_isProcessing) return;
        _isProcessing = true;

        try
        {
            var hWnd = GameWindowHelper.FindGameWindow();
            if (hWnd == IntPtr.Zero) return;

            using var bitmap = GameWindowHelper.CaptureClient(hWnd);
            if (bitmap == null) return;

            bool inCity = GameScreenDetector.IsInCity(bitmap);

            var prediction = await Task.Run(() => _coordinateOcr.PredictOcrAsync(bitmap));

            if (prediction == null)
            {
                if (_txtCurrentCoordinate != null)
                    _txtCurrentCoordinate.Text = inCity ? "📍 도시 안" : "🧭 탐험 중 - 좌표 인식 실패";
                return;
            }

            if (_txtCurrentCoordinate != null)
                _txtCurrentCoordinate.Text = inCity
                    ? $"📍 도시 안 ({prediction})"
                    : $"🧭 탐험 중 - {prediction}";

            if (_mapData != null)
                UpdatePositionMarker(prediction.ToLat(), prediction.ToLon());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WorldMap] Tracking error: {ex.Message}");
        }
        finally
        {
            _isProcessing = false;
        }
    }

    private (double px, double py) LatLonToPixel(double lat, double lon)
    {
        double cellX = (lon + 180.0) / 360.0 * UnfoldedW;
        double cellY = (90.0 - lat) / 180.0 * CellH;
        // 중앙 타일(index 1) 오프셋 적용
        return (cellX + UnfoldedW, cellY);
    }

    private void UpdatePositionMarker(double lat, double lon)
    {
        if (_overlayCanvas == null) return;

        var (px, py) = LatLonToPixel(lat, lon);

        const double markerSize = 7;
        const double pulseSize = 12;

        if (_trailLine == null)
        {
            _trailLine = new Polyline
            {
                Stroke = new SolidColorBrush(Color.FromArgb(180, 255, 80, 80)),
                StrokeThickness = 2,
                IsHitTestVisible = false,
                Points = _trailPoints
            };
            _overlayCanvas.Children.Add(_trailLine);
        }

        if (_positionMarker == null)
        {
            _positionPulse = new Ellipse
            {
                Width = pulseSize, Height = pulseSize,
                Fill = Brushes.Transparent,
                Stroke = new SolidColorBrush(Color.FromRgb(0, 120, 212)),
                StrokeThickness = 2,
                Opacity = 0.5
            };
            _overlayCanvas.Children.Add(_positionPulse);

            _positionMarker = new Ellipse
            {
                Width = markerSize, Height = markerSize,
                Fill = new SolidColorBrush(Color.FromRgb(255, 50, 50)),
                Stroke = Brushes.White,
                StrokeThickness = 2
            };
            _overlayCanvas.Children.Add(_positionMarker);
        }

        // 경로에 포인트 추가
        var newPoint = new Point(px, py);
        if (_trailPoints.Count == 0 || DistanceSq(_trailPoints[^1], newPoint) > 4)
        {
            _trailPoints.Add(newPoint);
            _trailTimestamps.Add(DateTime.Now);
        }

        _positionMarker.Visibility = Visibility.Visible;
        _positionPulse!.Visibility = Visibility.Visible;

        Canvas.SetLeft(_positionMarker, px - markerSize / 2);
        Canvas.SetTop(_positionMarker, py - markerSize / 2);
        Canvas.SetLeft(_positionPulse, px - pulseSize / 2);
        Canvas.SetTop(_positionPulse, py - pulseSize / 2);

        if (_centerOnFirstPosition)
        {
            _centerOnFirstPosition = false;
            ScrollToImagePosition(px, py);
        }
        else
        {
            ScrollToMarkerIfNeeded(px, py);
        }
    }

    private void GoToMyLocation()
    {
        if (_positionMarker == null || _positionMarker.Visibility != Visibility.Visible) return;

        var px = Canvas.GetLeft(_positionMarker) + 3.5; // markerSize/2
        var py = Canvas.GetTop(_positionMarker) + 3.5;

        _autoScrollEnabled = true;
        ScrollToImagePosition(px, py);
    }

    private void ScrollToImagePosition(double imageX, double imageY)
    {
        if (_scrollViewer == null) return;

        var offsetX = (imageX * _currentScale) - (_scrollViewer.ViewportWidth / 2);
        var offsetY = (imageY * _currentScale) - (_scrollViewer.ViewportHeight / 2);

        _isWrapping = true;
        _scrollViewer.ScrollToHorizontalOffset(Math.Max(0, offsetX));
        _scrollViewer.ScrollToVerticalOffset(Math.Max(0, offsetY));
        _isWrapping = false;
    }

    private void ScrollToMarkerIfNeeded(double pixelX, double pixelY)
    {
        if (_scrollViewer == null || !_autoScrollEnabled) return;

        var screenX = (pixelX * _currentScale) - _scrollViewer.HorizontalOffset;
        var screenY = (pixelY * _currentScale) - _scrollViewer.VerticalOffset;

        var viewW = _scrollViewer.ViewportWidth;
        var viewH = _scrollViewer.ViewportHeight;

        var marginX = viewW * 0.1;
        var marginY = viewH * 0.1;

        if (screenX < marginX || screenX > viewW - marginX ||
            screenY < marginY || screenY > viewH - marginY)
        {
            ScrollToImagePosition(pixelX, pixelY);
        }
    }

    private static double DistanceSq(Point a, Point b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }

    private void ClearTrail()
    {
        _trailPoints.Clear();
        _trailTimestamps.Clear();
    }

    private record TrailCoord(double lat, double lon, string? time = null);

    private void SaveTrail()
    {
        if (_trailPoints.Count == 0)
        {
            MessageBox.Show("저장할 경로가 없습니다.", "경로 저장",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dir = AppSettings.TrailDirectory;
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var fileName = $"worldmap_trail_{DateTime.Now:yyyyMMdd_HHmmss}.trail.json";
        var filePath = Path.Combine(dir, fileName);

        var coords = _trailPoints.Select((p, i) =>
        {
            // 픽셀 → 경위도 역변환 (중앙 타일 기준)
            double cellX = p.X - UnfoldedW;
            double lon = cellX * 360.0 / UnfoldedW - 180;
            double lat = 90.0 - p.Y * 180.0 / CellH;
            var time = i < _trailTimestamps.Count
                ? _trailTimestamps[i].ToString("yyyy-MM-dd HH:mm:ss.fff")
                : "";
            return new TrailCoord(lat, lon, time);
        }).ToList();

        var json = JsonSerializer.Serialize(coords, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(filePath, json);
        MessageBox.Show($"저장 완료: {fileName}", "경로 저장",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void LoadTrail()
    {
        var dir = AppSettings.TrailDirectory;
        if (!Directory.Exists(dir))
        {
            MessageBox.Show("경로 디렉토리가 없습니다.", "경로 불러오기",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var files = Directory.GetFiles(dir, "*.trail.json")
            .OrderByDescending(f => File.GetLastWriteTime(f))
            .ToArray();

        if (files.Length == 0)
        {
            MessageBox.Show("저장된 경로 파일이 없습니다.", "경로 불러오기",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new OpenFileDialog
        {
            InitialDirectory = dir,
            Filter = "Trail 파일 (*.trail.json)|*.trail.json",
            Title = "경로 파일 선택"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var json = File.ReadAllText(dlg.FileName);
            var coords = JsonSerializer.Deserialize<List<TrailCoord>>(json);
            if (coords == null || coords.Count == 0) return;

            _trailPoints.Clear();
            _trailTimestamps.Clear();

            foreach (var c in coords)
            {
                var (px, py) = LatLonToPixel(c.lat, c.lon);
                _trailPoints.Add(new Point(px, py));
                _trailTimestamps.Add(
                    DateTime.TryParse(c.time, out var t) ? t : DateTime.Now);
            }

            // trailLine이 아직 없으면 생성
            if (_trailLine == null && _overlayCanvas != null)
            {
                _trailLine = new Polyline
                {
                    Stroke = new SolidColorBrush(Color.FromArgb(180, 255, 80, 80)),
                    StrokeThickness = 2,
                    IsHitTestVisible = false,
                    Points = _trailPoints
                };
                _overlayCanvas.Children.Add(_trailLine);
            }

            if (_txtStatus != null)
                _txtStatus.Text = $"경로 로드: {coords.Count}개 포인트";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"경로 불러오기 실패: {ex.Message}", "오류",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    #endregion

    #region 우클릭 메뉴 / 자동이동

    private (double lat, double lon) PixelToLatLon(double px, double py)
    {
        // 중앙 타일 기준 오프셋 제거
        double cellX = px - UnfoldedW;
        double lon = cellX * 360.0 / UnfoldedW - 180;
        double lat = 90.0 - py * 180.0 / CellH;
        return (lat, lon);
    }

    private void OnMapRightClick(object sender, MouseButtonEventArgs e)
    {
        if (_overlayCanvas == null || _mapData == null) return;

        var pos = e.GetPosition(_overlayCanvas);
        var (lat, lon) = PixelToLatLon(pos.X, pos.Y);

        var latDir = lat >= 0 ? "N" : "S";
        var lonDir = lon >= 0 ? "E" : "W";
        var coordText = $"{Math.Abs(lat):F0}{latDir} {Math.Abs(lon):F0}{lonDir}";

        var menu = new ContextMenu();

        var navItem = new MenuItem { Header = $"이 위치로 이동 ({coordText})" };
        navItem.Click += (_, _) =>
        {
            ShowDestination(pos.X, pos.Y);
            StartNavigation(lat, lon);
        };
        menu.Items.Add(navItem);

        if (_isNavigating)
        {
            var stopItem = new MenuItem { Header = "이동 중지" };
            stopItem.Click += (_, _) => StopNavigation();
            menu.Items.Add(stopItem);
        }

        menu.IsOpen = true;
        e.Handled = true;
    }

    private void ShowDestination(double px, double py)
    {
        if (_overlayCanvas == null) return;

        const double size = 14;
        if (_destMarker == null)
        {
            _destMarker = new Ellipse
            {
                Width = size,
                Height = size,
                Fill = new SolidColorBrush(Color.FromArgb(100, 0, 200, 0)),
                Stroke = new SolidColorBrush(Color.FromRgb(0, 180, 0)),
                StrokeThickness = 2,
                IsHitTestVisible = false
            };
            _overlayCanvas.Children.Add(_destMarker);
        }

        _destMarker.Visibility = Visibility.Visible;
        Canvas.SetLeft(_destMarker, px - size / 2);
        Canvas.SetTop(_destMarker, py - size / 2);
    }

    private void StartNavigation(double destLat, double destLon)
    {
        StopNavigation();

        var hWnd = GameWindowHelper.FindGameWindow();
        if (hWnd == IntPtr.Zero)
        {
            MessageBox.Show("게임 창을 찾을 수 없습니다.", "알림",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // 추적이 꺼져 있으면 자동으로 켜기
        if (!_isTracking)
            StartTracking();

        _isNavigating = true;
        _navCts = new CancellationTokenSource();

        Task.Run(async () =>
        {
            var token = _navCts.Token;

            // 게임 창 활성화
            GameWindowHelper.BringToFront(hWnd);
            await Task.Delay(300, token);

            // 도시 안인지 확인 → 탐험 떠나기 먼저 실행
            try
            {
                using var checkBmp = GameWindowHelper.CaptureClient(hWnd);
                if (checkBmp != null && GameScreenDetector.IsInCity(checkBmp))
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (_txtCurrentCoordinate != null)
                            _txtCurrentCoordinate.Text = "📍 도시 안 → 탐험 떠나는 중...";
                    });

                    await LeaveCityAsync(hWnd, token);

                    // 탐험 출발 후 화면 전환 대기
                    await Task.Delay(2000, token);
                }
            }
            catch (OperationCanceledException) { return; }

            // 이동 루프
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var navHWnd = GameWindowHelper.FindGameWindow();
                    if (navHWnd == IntPtr.Zero)
                    {
                        await Task.Delay(2000, token);
                        continue;
                    }

                    using var bitmap = GameWindowHelper.CaptureClient(navHWnd);
                    if (bitmap == null)
                    {
                        await Task.Delay(500, token);
                        continue;
                    }

                    // 다이얼로그/힌트 화면이 떠있으면 닫기
                    var screen = await _screenDetector.DetectScreenAsync(bitmap);
                    if (screen is GameScreen.HintList or GameScreen.InfoMenu or GameScreen.CommandMenu)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            if (_txtCurrentCoordinate != null)
                                _txtCurrentCoordinate.Text = $"📋 화면 닫는 중... ({screen})";
                        });
                        await GameScreenDetector.DismissDialogAsync(navHWnd, screen, token);
                        await Task.Delay(500, token);
                        continue;
                    }

                    var prediction = await _coordinateOcr.PredictOcrAsync(bitmap);
                    if (prediction == null)
                    {
                        await Task.Delay(500, token);
                        continue;
                    }

                    var curLat = prediction.ToLat();
                    var curLon = prediction.ToLon();

                    // 도착 판정
                    if (NavigationCalculator.IsNear(curLat, curLon, destLat, destLon, threshold: 2.0))
                    {
                        GameWindowHelper.SendNumpadKey(navHWnd, 5);
                        Dispatcher.Invoke(() =>
                        {
                            if (_txtCurrentCoordinate != null)
                                _txtCurrentCoordinate.Text = $"{prediction} - 도착!";
                            _isNavigating = false;
                            if (_destMarker != null) _destMarker.Visibility = Visibility.Collapsed;
                        });
                        return;
                    }

                    // 방위각 → 숫자패드
                    var bearing = NavigationCalculator.BearingDegrees(curLat, curLon, destLat, destLon);
                    var numpad = GameWindowHelper.BearingToNumpad(bearing);
                    GameWindowHelper.SendNumpadKey(navHWnd, numpad);

                    var dirLabel = numpad switch
                    {
                        8 => "N", 9 => "NE", 6 => "E", 3 => "SE",
                        2 => "S", 1 => "SW", 4 => "W", 7 => "NW",
                        _ => "?"
                    };

                    Dispatcher.Invoke(() =>
                    {
                        if (_txtCurrentCoordinate != null)
                            _txtCurrentCoordinate.Text = $"🧭 탐험 중 - {prediction} → {dirLabel}";
                    });

                    await Task.Delay(500, token);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[WorldMap] Nav error: {ex.Message}");
                    await Task.Delay(2000, token);
                }
            }
        });
    }

    private void StopNavigation()
    {
        _navCts?.Cancel();
        _navCts = null;
        _isNavigating = false;

        var hWnd = GameWindowHelper.FindGameWindow();
        if (hWnd != IntPtr.Zero)
            GameWindowHelper.SendNumpadKey(hWnd, 5);

        if (_destMarker != null) _destMarker.Visibility = Visibility.Collapsed;
    }

    #endregion

    #region 탐험 떠나기

    /// <summary>
    /// 도시에서 성문 → 탐험을 떠난다 → YES 순서로 키를 전송한다.
    /// 건물 이름을 OCR로 인식하여 "성문"을 찾을 때까지 아래 키를 누른다.
    /// </summary>
    private async Task LeaveCityAsync(IntPtr hWnd, CancellationToken token = default)
    {
        GameWindowHelper.BringToFront(hWnd);
        await Task.Delay(300, token);

        var debugDir = Path.Combine(Path.GetTempPath(), "cds_leave_city");
        Directory.CreateDirectory(debugDir);

        // 건물 목록은 순환 구조이므로 Down 키로 한 바퀴 돌며 "성문"을 찾음
        bool foundGate = false;
        for (int i = 0; i < 15; i++)
        {
            using var bmp = GameWindowHelper.CaptureClient(hWnd);
            if (bmp != null)
            {
                int regionY = Math.Min(20, bmp.Height - 1);
                int regionH = Math.Max(bmp.Height * 70 / 100, 1);
                var region = new System.Drawing.Rectangle(0, regionY, bmp.Width, regionH);

                // 디버그 이미지 저장
                try
                {
                    var debugPath = Path.Combine(debugDir, $"gate_{i}.png");
                    using var cropped = bmp.Clone(region, bmp.PixelFormat);
                    cropped.Save(debugPath);
                    System.Diagnostics.Debug.WriteLine($"[LeaveCity] Debug image: {debugPath}");
                }
                catch { /* ignore */ }

                var text = await _coordinateOcr.RecognizeRegionAsync(bmp, region);
                System.Diagnostics.Debug.WriteLine($"[LeaveCity] OCR({i}): '{text}'");

                if (text != null && text.Contains("성문"))
                {
                    foundGate = true;
                    break;
                }
            }

            GameWindowHelper.SendDownKey(hWnd);
            await Task.Delay(500, token);
        }

        if (!foundGate)
        {
            System.Diagnostics.Debug.WriteLine("[LeaveCity] 성문을 찾지 못함");
            return;
        }

        // 2. 성문 선택
        await Task.Delay(200, token);
        GameWindowHelper.SendEnterKey(hWnd);
        await Task.Delay(500, token);

        // 3. "탐험을 떠난다" (첫 번째 옵션)
        GameWindowHelper.SendEnterKey(hWnd);
        await Task.Delay(500, token);

        // 4. "탐험을 떠납니까?" → YES
        GameWindowHelper.SendEnterKey(hWnd);
        await Task.Delay(300, token);
    }

    private async void LeaveCityForExploration()
    {
        var hWnd = GameWindowHelper.FindGameWindow();
        if (hWnd == IntPtr.Zero)
        {
            MessageBox.Show("게임 창을 찾을 수 없습니다.", "알림",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_btnLeaveCity != null) _btnLeaveCity.IsEnabled = false;
        if (_txtStatus != null) _txtStatus.Text = "도시에서 탐험 떠나는 중...";

        try
        {
            await LeaveCityAsync(hWnd);
            if (_txtStatus != null) _txtStatus.Text = "탐험 출발 완료";
        }
        catch (Exception ex)
        {
            if (_txtStatus != null) _txtStatus.Text = $"탐험 떠나기 실패: {ex.Message}";
        }
        finally
        {
            if (_btnLeaveCity != null) _btnLeaveCity.IsEnabled = true;
        }
    }

    #endregion
}
