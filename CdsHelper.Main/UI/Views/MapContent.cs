using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using CdsHelper.Main.UI.ViewModels;
using CdsHelper.Support.Local.Events;
using CdsHelper.Support.Local.Helpers;
using CdsHelper.Support.Local.Settings;
using CdsHelper.Support.UI.Units;
using Microsoft.Win32;
using Prism.Events;
using Prism.Ioc;

namespace CdsHelper.Main.UI.Views;

public class MapContent : ContentControl
{
    private Button? _btnZoomIn;
    private Button? _btnZoomOut;
    private Button? _btnZoomReset;
    private Button? _btnTrackCoordinate;
    private Button? _btnClearTrail;
    private CheckBox? _chkShowCityLabels;
    private CheckBox? _chkShowCoordinates;
    private CheckBox? _chkShowCulturalSpheres;
    private CheckBox? _chkShowShipyards;
    private TextBlock? _txtMapCoordinates;
    private TextBlock? _txtCenterPosition;
    private TextBlock? _txtCurrentCoordinate;
    private TextBlock? _txtNavStatus;
    private Button? _btnStartNav;
    private Button? _btnStopNav;
    private ComboBox? _cmbNavLatDir;
    private ComboBox? _cmbNavLonDir;
    private TextBox? _txtNavLat;
    private TextBox? _txtNavLon;
    private ScrollViewer? _mapScrollViewer;
    private Image? _imgMap;
    private Canvas? _mapCanvas;
    private Canvas? _latitudeScale;
    private Canvas? _longitudeScale;
    private ScaleTransform? _mapScaleTransform;
    private ScaleTransform? _canvasScaleTransform;

    // 현재 위치 마커
    private Ellipse? _currentPositionMarker;
    private Ellipse? _currentPositionPulse;

    // 이동 경로
    private Polyline? _trailLine;
    private static readonly PointCollection _trailPoints = new();
    private static readonly List<DateTime> _trailTimestamps = new();

    // 목적지 마커
    private Ellipse? _destinationMarker;

    private double _currentScale = 1.0;
    private const double ScaleStep = 0.5;
    private const double MinScale = 0.5;
    private const double MaxScale = 5.0;

    private bool _isDragging;
    private Point _lastMousePosition;
    private static bool _autoScrollEnabled = true;

    private MapContentViewModel? _viewModel;

    // 스크롤 위치 저장용 (탭 전환 시 유지)
    private static double _savedHorizontalOffset = -1;
    private static double _savedVerticalOffset = -1;
    private static double _savedScale = 1.0;
    private static bool _hasSavedPosition = false;

    // 대기 중인 도시 네비게이션 (MapContent 로드 전 이벤트 수신 시)
    private static NavigateToCityEventArgs? _pendingNavigation = null;
    // 사용자가 수동으로 추적을 끈 경우 true
    private static bool _manuallyStoppedTracking = false;

    /// <summary>
    /// 대기 중인 네비게이션 설정 (MapContent 로드 전 호출됨)
    /// </summary>
    public static void SetPendingNavigation(NavigateToCityEventArgs args)
    {
        _pendingNavigation = args;
        System.Diagnostics.Debug.WriteLine($"[MapContent] SetPendingNavigation: {args.CityName} ({args.PixelX}, {args.PixelY})");
    }

    static MapContent()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(MapContent),
            new FrameworkPropertyMetadata(typeof(MapContent)));
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        // ViewModel 초기화 (DI에서 CityService 가져오기)
        var cityService = ContainerLocator.Container.Resolve<CityService>();
        _viewModel = new MapContentViewModel(cityService);

        // 지도 컨트롤 찾기
        _btnZoomIn = GetTemplateChild("PART_BtnZoomIn") as Button;
        _btnZoomOut = GetTemplateChild("PART_BtnZoomOut") as Button;
        _btnZoomReset = GetTemplateChild("PART_BtnZoomReset") as Button;
        _chkShowCityLabels = GetTemplateChild("PART_ChkShowCityLabels") as CheckBox;
        _chkShowCoordinates = GetTemplateChild("PART_ChkShowCoordinates") as CheckBox;
        _txtMapCoordinates = GetTemplateChild("PART_TxtMapCoordinates") as TextBlock;
        _txtCenterPosition = GetTemplateChild("PART_TxtCenterPosition") as TextBlock;
        _mapScrollViewer = GetTemplateChild("PART_MapScrollViewer") as ScrollViewer;
        _imgMap = GetTemplateChild("PART_ImgMap") as Image;
        _mapCanvas = GetTemplateChild("PART_MapCanvas") as Canvas;
        _mapScaleTransform = GetTemplateChild("PART_MapScaleTransform") as ScaleTransform;
        _canvasScaleTransform = GetTemplateChild("PART_CanvasScaleTransform") as ScaleTransform;
        _latitudeScale = GetTemplateChild("PART_LatitudeScale") as Canvas;
        _longitudeScale = GetTemplateChild("PART_LongitudeScale") as Canvas;

        // 이벤트 연결
        if (_btnZoomIn != null)
            _btnZoomIn.Click += (s, e) => ZoomIn();

        if (_btnZoomOut != null)
            _btnZoomOut.Click += (s, e) => ZoomOut();

        if (_btnZoomReset != null)
            _btnZoomReset.Click += (s, e) => ZoomReset();

        if (_chkShowCityLabels != null)
        {
            _chkShowCityLabels.Checked += (s, e) => OnShowCityLabelsChanged(true);
            _chkShowCityLabels.Unchecked += (s, e) => OnShowCityLabelsChanged(false);
        }

        if (_chkShowCoordinates != null)
        {
            _chkShowCoordinates.Checked += (s, e) => OnShowCoordinatesChanged(true);
            _chkShowCoordinates.Unchecked += (s, e) => OnShowCoordinatesChanged(false);
        }

        _chkShowCulturalSpheres = GetTemplateChild("PART_ChkShowCulturalSpheres") as CheckBox;
        if (_chkShowCulturalSpheres != null)
        {
            _chkShowCulturalSpheres.Checked += (s, e) => OnShowCulturalSpheresChanged(true);
            _chkShowCulturalSpheres.Unchecked += (s, e) => OnShowCulturalSpheresChanged(false);
        }

        _chkShowShipyards = GetTemplateChild("PART_ChkShowShipyards") as CheckBox;
        if (_chkShowShipyards != null)
        {
            _chkShowShipyards.Checked += (s, e) => OnShowShipyardsChanged(true);
            _chkShowShipyards.Unchecked += (s, e) => OnShowShipyardsChanged(false);
        }

        if (_mapScrollViewer != null)
        {
            _mapScrollViewer.PreviewMouseWheel += MapScrollViewer_PreviewMouseWheel;
            _mapScrollViewer.PreviewMouseLeftButtonDown += MapScrollViewer_PreviewMouseLeftButtonDown;
            _mapScrollViewer.PreviewMouseMove += MapScrollViewer_PreviewMouseMove;
            _mapScrollViewer.PreviewMouseLeftButtonUp += MapScrollViewer_PreviewMouseLeftButtonUp;
            _mapScrollViewer.ScrollChanged += MapScrollViewer_ScrollChanged;
            _mapScrollViewer.Loaded += MapScrollViewer_Loaded;
        }

        // 탭 전환 시 위치 저장/복원
        IsVisibleChanged += OnIsVisibleChanged;

        if (_imgMap != null)
        {
            _imgMap.MouseMove += ImgMap_MouseMove;
            _imgMap.MouseLeave += ImgMap_MouseLeave;
        }

        if (_mapCanvas != null)
        {
            _mapCanvas.MouseMove += ImgMap_MouseMove;
            _mapCanvas.MouseRightButtonDown += ImgMap_MouseRightButtonDown;
        }

        // 좌표 추적 버튼
        _btnTrackCoordinate = GetTemplateChild("PART_BtnTrackCoordinate") as Button;
        _btnClearTrail = GetTemplateChild("PART_BtnClearTrail") as Button;
        _btnStartNav = GetTemplateChild("PART_BtnStartNav") as Button;
        _btnStopNav = GetTemplateChild("PART_BtnStopNav") as Button;
        _cmbNavLatDir = GetTemplateChild("PART_CmbNavLatDir") as ComboBox;
        _cmbNavLonDir = GetTemplateChild("PART_CmbNavLonDir") as ComboBox;
        _txtNavLat = GetTemplateChild("PART_TxtNavLat") as TextBox;
        _txtNavLon = GetTemplateChild("PART_TxtNavLon") as TextBox;
        _txtCurrentCoordinate = GetTemplateChild("PART_TxtCurrentCoordinate") as TextBlock;
        _txtNavStatus = GetTemplateChild("PART_TxtNavStatus") as TextBlock;
        if (_btnTrackCoordinate != null)
        {
            _btnTrackCoordinate.Click += (s, e) => ToggleTracking();
        }
        if (_btnClearTrail != null)
        {
            _btnClearTrail.Click += (s, e) => { _trailPoints.Clear(); _trailTimestamps.Clear(); };
        }
        var btnSaveTrail = GetTemplateChild("PART_BtnSaveTrail") as Button;
        if (btnSaveTrail != null)
        {
            btnSaveTrail.Click += (s, e) => SaveTrail();
        }
        var btnLoadTrail = GetTemplateChild("PART_BtnLoadTrail") as Button;
        if (btnLoadTrail != null)
        {
            btnLoadTrail.Click += (s, e) => LoadTrail();
        }
        var btnMyLocation = GetTemplateChild("PART_BtnMyLocation") as Button;
        if (btnMyLocation != null)
        {
            btnMyLocation.Click += (s, e) => GoToMyLocation();
        }
        if (_btnStartNav != null)
        {
            _btnStartNav.Click += (s, e) => StartNavigationFromUI();
        }
        if (_btnStopNav != null)
        {
            _btnStopNav.Click += (s, e) => StopNavigation();
        }

        // CityMarker 이벤트 핸들러 등록
        if (_mapCanvas != null)
        {
            _mapCanvas.AddHandler(CityMarker.MarkerClickedEvent, new RoutedEventHandler(OnCityMarkerClicked));
            _mapCanvas.AddHandler(CityMarker.LibraryClickedEvent, new RoutedEventHandler(OnLibraryClicked));
        }

        // 지도 이미지 로드
        LoadMapImage();
        // 도시 마커 로드
        LoadCityMarkers();
        // 현재 위치 마커 생성
        CreateCurrentPositionMarker();

        // 이벤트 구독
        var eventAggregator = ContainerLocator.Container.Resolve<IEventAggregator>();
        eventAggregator.GetEvent<NavigateToCityEvent>().Subscribe(OnNavigateToCity);
        eventAggregator.GetEvent<CurrentCoordinateEvent>().Subscribe(OnCurrentCoordinateUpdated);
    }

    private void OnNavigateToCity(NavigateToCityEventArgs args)
    {
        if (!args.PixelX.HasValue || !args.PixelY.HasValue)
        {
            MessageBox.Show($"도시 '{args.CityName}'의 좌표 정보가 없습니다.",
                "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // MapContent가 아직 로드되지 않은 경우 대기
        if (_mapScrollViewer == null)
        {
            _pendingNavigation = args;
            System.Diagnostics.Debug.WriteLine($"[MapContent] Pending navigation to: {args.CityName}");
            return;
        }

        // UI 스레드에서 실행
        Dispatcher.Invoke(() =>
        {
            ScrollToImagePosition(args.PixelX.Value, args.PixelY.Value);
            System.Diagnostics.Debug.WriteLine($"[MapContent] Navigated to city: {args.CityName} ({args.PixelX}, {args.PixelY})");
        });
    }

    private void LoadMapImage()
    {
        if (_imgMap == null) return;

        var basePath = AppDomain.CurrentDomain.BaseDirectory;
        var mapPath = System.IO.Path.Combine(basePath, "대항해시대3-지도(발견물-이름-기준).jpg");

        if (System.IO.File.Exists(mapPath))
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(mapPath);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                _imgMap.Source = bitmap;

                // Canvas 크기를 이미지 크기에 맞춤
                if (_mapCanvas != null)
                {
                    _mapCanvas.Width = bitmap.PixelWidth;
                    _mapCanvas.Height = bitmap.PixelHeight;
                }
            }
            catch { }
        }
    }

    private void LoadCityMarkers()
    {
        if (_mapCanvas == null || _viewModel == null) return;

        try
        {
            var cities = _viewModel.GetCitiesWithCoordinates();
            var showLabels = _chkShowCityLabels?.IsChecked ?? false;
            var showCoordinates = _chkShowCoordinates?.IsChecked ?? false;
            MapMarkerHelper.AddCityMarkers(_mapCanvas, cities, showLabels, showCoordinates, AppSettings.MarkerSize);

            // 설정 변경 이벤트 구독
            AppSettings.SettingsChanged += OnSettingsChanged;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LoadCityMarkers] Error: {ex.Message}");
        }
    }

    private void RefreshCityMarkers()
    {
        if (_mapCanvas == null || _viewModel == null) return;

        try
        {
            // 기존 마커 제거 후 새로 로드
            MapMarkerHelper.ClearMarkers(_mapCanvas);
            var cities = _viewModel.GetCitiesWithCoordinates();
            var showLabels = _chkShowCityLabels?.IsChecked ?? false;
            var showCoordinates = _chkShowCoordinates?.IsChecked ?? false;
            var showCulturalSpheres = _chkShowCulturalSpheres?.IsChecked ?? false;

            // 문화권 영역 먼저 추가 (도시 마커 뒤에 표시되도록)
            if (showCulturalSpheres)
            {
                MapMarkerHelper.AddAreaMarkers(_mapCanvas, cities, true);
            }

            MapMarkerHelper.AddCityMarkers(_mapCanvas, cities, showLabels, showCoordinates, AppSettings.MarkerSize);

            // 현재 위치 마커 다시 추가 (ClearMarkers로 삭제되므로)
            CreateCurrentPositionMarker();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RefreshCityMarkers] Error: {ex.Message}");
        }
    }

    private void OnSettingsChanged()
    {
        if (_mapCanvas == null) return;
        Dispatcher.Invoke(() => MapMarkerHelper.SetMarkerSize(_mapCanvas, AppSettings.MarkerSize));
    }

    private void MapScrollViewer_Loaded(object sender, RoutedEventArgs e)
    {
        // 대기 중인 네비게이션이 있으면 우선 처리
        if (_pendingNavigation != null)
        {
            var nav = _pendingNavigation;
            _pendingNavigation = null;

            ApplyScale();
            if (nav.PixelX.HasValue && nav.PixelY.HasValue)
            {
                ScrollToImagePosition(nav.PixelX.Value, nav.PixelY.Value);
                System.Diagnostics.Debug.WriteLine($"[MapContent] Processed pending navigation to: {nav.CityName}");
            }
        }
        else if (_hasSavedPosition)
        {
            // 저장된 위치 복원
            _currentScale = _savedScale;
            ApplyScale();
            _mapScrollViewer?.ScrollToHorizontalOffset(_savedHorizontalOffset);
            _mapScrollViewer?.ScrollToVerticalOffset(_savedVerticalOffset);
        }
        else
        {
            // 초기 스케일 적용
            ApplyScale();
            // 초기 위치 설정 (3529, 899가 가운데 오도록)
            ScrollToImagePosition(3529, 899);
        }

        // 초기 중심 좌표 표시
        UpdateCenterPosition();

        // 게임이 켜져 있으면 자동으로 추적 시작
        TryAutoStartTracking();
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is bool isVisible)
        {
            if (!isVisible && _mapScrollViewer != null)
            {
                // 탭 떠날 때 위치 저장
                _savedHorizontalOffset = _mapScrollViewer.HorizontalOffset;
                _savedVerticalOffset = _mapScrollViewer.VerticalOffset;
                _savedScale = _currentScale;
                _hasSavedPosition = true;
            }
            else if (isVisible && _mapScrollViewer != null)
            {
                // 탭 돌아올 때 마커 새로고침
                RefreshCityMarkers();

                if (_hasSavedPosition)
                {
                    // 위치 복원
                    _currentScale = _savedScale;
                    ApplyScale();
                    _mapScrollViewer.ScrollToHorizontalOffset(_savedHorizontalOffset);
                    _mapScrollViewer.ScrollToVerticalOffset(_savedVerticalOffset);
                }

                // 게임이 켜져 있으면 자동으로 추적 시작
                TryAutoStartTracking();
            }
        }
    }

    private void ScrollToImagePosition(double imageX, double imageY)
    {
        if (_mapScrollViewer == null) return;

        var offsetX = (imageX * _currentScale) - (_mapScrollViewer.ViewportWidth / 2);
        var offsetY = (imageY * _currentScale) - (_mapScrollViewer.ViewportHeight / 2);

        _mapScrollViewer.ScrollToHorizontalOffset(Math.Max(0, offsetX));
        _mapScrollViewer.ScrollToVerticalOffset(Math.Max(0, offsetY));
    }

    private void ZoomIn()
    {
        if (_currentScale < MaxScale)
        {
            ApplyScaleAtViewportCenter(_currentScale + ScaleStep);
        }
    }

    private void ZoomOut()
    {
        if (_currentScale > MinScale)
        {
            ApplyScaleAtViewportCenter(_currentScale - ScaleStep);
        }
    }

    private void ZoomReset()
    {
        _currentScale = 1.0;
        ApplyScale();
        ScrollToImagePosition(3529, 899);
    }

    private void ApplyScale()
    {
        if (_mapScaleTransform != null)
        {
            _mapScaleTransform.ScaleX = _currentScale;
            _mapScaleTransform.ScaleY = _currentScale;
        }
        if (_canvasScaleTransform != null)
        {
            _canvasScaleTransform.ScaleX = _currentScale;
            _canvasScaleTransform.ScaleY = _currentScale;
        }
    }

    private void ApplyScaleAtViewportCenter(double newScale)
    {
        if (_mapScrollViewer == null) return;

        // 현재 뷰포트 중심점의 콘텐츠 좌표 계산
        var viewportCenterX = _mapScrollViewer.HorizontalOffset + (_mapScrollViewer.ViewportWidth / 2);
        var viewportCenterY = _mapScrollViewer.VerticalOffset + (_mapScrollViewer.ViewportHeight / 2);

        // 현재 스케일 기준 실제 이미지 좌표
        var imageCenterX = viewportCenterX / _currentScale;
        var imageCenterY = viewportCenterY / _currentScale;

        // 새 스케일 적용
        _currentScale = newScale;
        ApplyScale();

        // 새 스케일에서 같은 이미지 좌표가 뷰포트 중심에 오도록 스크롤 조정
        var newOffsetX = (imageCenterX * _currentScale) - (_mapScrollViewer.ViewportWidth / 2);
        var newOffsetY = (imageCenterY * _currentScale) - (_mapScrollViewer.ViewportHeight / 2);

        _mapScrollViewer.ScrollToHorizontalOffset(Math.Max(0, newOffsetX));
        _mapScrollViewer.ScrollToVerticalOffset(Math.Max(0, newOffsetY));
    }

    private void MapScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Delta > 0)
            ZoomIn();
        else
            ZoomOut();
        e.Handled = true;
    }

    private void MapScrollViewer_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_mapScrollViewer == null) return;

        // CityMarker 클릭인지 확인 - CityMarker 클릭 시에는 드래그 시작하지 않음
        if (IsCityMarkerClick(e.OriginalSource))
        {
            return; // CityMarker가 클릭 이벤트를 처리하도록 함
        }

        _isDragging = true;
        _lastMousePosition = e.GetPosition(_mapScrollViewer);
        _mapScrollViewer.CaptureMouse();
        e.Handled = true;
    }

    private bool IsCityMarkerClick(object originalSource)
    {
        // OriginalSource부터 시작해서 visual tree를 따라 올라가며 CityMarker 찾기
        var element = originalSource as DependencyObject;
        while (element != null)
        {
            if (element is CityMarker)
                return true;
            element = VisualTreeHelper.GetParent(element);
        }
        return false;
    }

    private void MapScrollViewer_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging || _mapScrollViewer == null) return;

        _autoScrollEnabled = false;

        var currentPosition = e.GetPosition(_mapScrollViewer);
        var delta = currentPosition - _lastMousePosition;

        _mapScrollViewer.ScrollToHorizontalOffset(_mapScrollViewer.HorizontalOffset - delta.X);
        _mapScrollViewer.ScrollToVerticalOffset(_mapScrollViewer.VerticalOffset - delta.Y);

        _lastMousePosition = currentPosition;
    }

    private void MapScrollViewer_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isDragging = false;
        _mapScrollViewer?.ReleaseMouseCapture();
    }

    private void ImgMap_MouseMove(object sender, MouseEventArgs e)
    {
        if (_txtMapCoordinates == null) return;

        // Canvas 또는 Image 기준으로 좌표 가져오기
        var target = _mapCanvas ?? (UIElement?)_imgMap;
        if (target == null) return;

        var pos = e.GetPosition(target);
        var (lat, lon) = PixelToLatLon(pos.X, pos.Y);
        var latDir = lat >= 0 ? "N" : "S";
        var lonDir = lon >= 0 ? "E" : "W";
        _txtMapCoordinates.Text = $"마우스: {Math.Abs(lat):F1}°{latDir}, {Math.Abs(lon):F1}°{lonDir}";
    }

    private void ImgMap_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_txtMapCoordinates != null)
            _txtMapCoordinates.Text = "마우스: -";
    }

    private void ImgMap_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_mapCanvas == null || _viewModel == null) return;

        var pos = e.GetPosition(_mapCanvas);
        var (lat, lon) = PixelToLatLon(pos.X, pos.Y);
        var latDir = lat >= 0 ? "N" : "S";
        var lonDir = lon >= 0 ? "E" : "W";
        var coordText = $"{Math.Abs(lat):F0}°{latDir}, {Math.Abs(lon):F0}°{lonDir}";

        var menu = new ContextMenu();

        var setDestItem = new MenuItem { Header = $"목적지 설정 ({coordText})" };
        setDestItem.Click += (s, args) => SetNavigationDestination(lat, lon, pos.X, pos.Y);
        menu.Items.Add(setDestItem);

        var navItem = new MenuItem { Header = $"이 위치로 바로 이동 ({coordText})" };
        navItem.Click += (s, args) =>
        {
            SetNavigationDestination(lat, lon, pos.X, pos.Y);
            StartNavigationFromUI();
        };
        menu.Items.Add(navItem);

        menu.IsOpen = true;
        e.Handled = true;
    }

    private void SetNavigationDestination(double lat, double lon, double pixelX, double pixelY)
    {
        // 위경도 방향 & 값 세팅
        if (_cmbNavLatDir != null)
            _cmbNavLatDir.SelectedIndex = lat >= 0 ? 0 : 1; // 0=북위, 1=남위
        if (_cmbNavLonDir != null)
            _cmbNavLonDir.SelectedIndex = lon >= 0 ? 0 : 1; // 0=동경, 1=서경
        if (_txtNavLat != null)
            _txtNavLat.Text = $"{Math.Abs(lat):F0}";
        if (_txtNavLon != null)
            _txtNavLon.Text = $"{Math.Abs(lon):F0}";

        // 목적지 마커 표시
        ShowDestinationMarker(pixelX, pixelY);
    }

    /// <summary>
    /// 픽셀 좌표를 위도/경도로 변환
    /// 기준점: 리스본 (pixelX=3525, pixelY=914, lat=38, lon=-9)
    /// </summary>
    private (double lat, double lon) PixelToLatLon(double pixelX, double pixelY)
    {
        // 기준점: 리스본
        const double refPixelX = 3525;
        const double refPixelY = 914;
        const double refLat = 38;
        const double refLon = -9;

        // 스케일 (픽셀 per 도) — 리스본/미틀라 기준 보정
        const double pixelsPerDegreeLon = 22.12;
        const double pixelsPerDegreeLat = 22.05;

        var lon = refLon + (pixelX - refPixelX) / pixelsPerDegreeLon;
        var lat = refLat - (pixelY - refPixelY) / pixelsPerDegreeLat;

        return (lat, lon);
    }

    private void MapScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        UpdateCenterPosition();
    }

    private void UpdateCenterPosition()
    {
        if (_mapScrollViewer == null || _txtCenterPosition == null) return;

        // 뷰포트 중심점의 콘텐츠 좌표 계산
        var viewportCenterX = _mapScrollViewer.HorizontalOffset + (_mapScrollViewer.ViewportWidth / 2);
        var viewportCenterY = _mapScrollViewer.VerticalOffset + (_mapScrollViewer.ViewportHeight / 2);

        // 현재 스케일 기준 실제 이미지 좌표
        var imageCenterX = viewportCenterX / _currentScale;
        var imageCenterY = viewportCenterY / _currentScale;

        // 위도/경도로 변환
        var (lat, lon) = PixelToLatLon(imageCenterX, imageCenterY);
        var latDir = lat >= 0 ? "N" : "S";
        var lonDir = lon >= 0 ? "E" : "W";

        _txtCenterPosition.Text = $"중심: {Math.Abs(lat):F1}°{latDir}, {Math.Abs(lon):F1}°{lonDir}";

        // 눈금 업데이트
        UpdateLatitudeScale();
        UpdateLongitudeScale();
    }

    private void UpdateLatitudeScale()
    {
        if (_latitudeScale == null || _mapScrollViewer == null) return;

        _latitudeScale.Children.Clear();

        var viewportHeight = _mapScrollViewer.ViewportHeight;
        var verticalOffset = _mapScrollViewer.VerticalOffset;

        // 보이는 영역의 위/아래 픽셀 Y 좌표 (이미지 기준)
        var topPixelY = verticalOffset / _currentScale;
        var bottomPixelY = (verticalOffset + viewportHeight) / _currentScale;

        // 해당 픽셀의 위도 계산
        var (topLat, _) = PixelToLatLon(0, topPixelY);
        var (bottomLat, _) = PixelToLatLon(0, bottomPixelY);

        // 5도 단위로 눈금 표시
        var startLat = (int)Math.Ceiling(Math.Min(topLat, bottomLat) / 5) * 5;
        var endLat = (int)Math.Floor(Math.Max(topLat, bottomLat) / 5) * 5;

        for (var lat = startLat; lat <= endLat; lat += 5)
        {
            // 위도를 픽셀 Y로 변환
            var pixelY = LatToPixelY(lat);
            // 스케일 적용 후 뷰포트 기준 Y 좌표
            var screenY = (pixelY * _currentScale) - verticalOffset;

            if (screenY >= 0 && screenY <= viewportHeight)
            {
                var latDir = lat >= 0 ? "N" : "S";
                var text = new TextBlock
                {
                    Text = $"{Math.Abs(lat)}°{latDir}",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromRgb(16, 79, 137)), // #104F89
                    FontFamily = new FontFamily("Consolas"),
                    FontWeight = FontWeights.SemiBold
                };

                Canvas.SetLeft(text, 2);
                Canvas.SetTop(text, screenY - 7);
                _latitudeScale.Children.Add(text);

                // 눈금선
                var line = new System.Windows.Shapes.Line
                {
                    X1 = 35,
                    X2 = 40,
                    Y1 = screenY,
                    Y2 = screenY,
                    Stroke = new SolidColorBrush(Color.FromRgb(16, 79, 137)),
                    StrokeThickness = 1
                };
                _latitudeScale.Children.Add(line);
            }
        }
    }

    /// <summary>
    /// 위도를 픽셀 Y 좌표로 변환
    /// </summary>
    private double LatToPixelY(double lat)
    {
        const double refPixelY = 914;
        const double refLat = 38;
        const double pixelsPerDegreeLat = 22.05;

        return refPixelY - (lat - refLat) * pixelsPerDegreeLat;
    }

    /// <summary>
    /// 경도를 픽셀 X 좌표로 변환
    /// </summary>
    private double LonToPixelX(double lon)
    {
        const double refPixelX = 3525;
        const double refLon = -9;
        const double pixelsPerDegreeLon = 22.12;

        return refPixelX + (lon - refLon) * pixelsPerDegreeLon;
    }

    private void UpdateLongitudeScale()
    {
        if (_longitudeScale == null || _mapScrollViewer == null) return;

        _longitudeScale.Children.Clear();

        var viewportWidth = _mapScrollViewer.ViewportWidth;
        var horizontalOffset = _mapScrollViewer.HorizontalOffset;

        // 보이는 영역의 좌/우 픽셀 X 좌표 (이미지 기준)
        var leftPixelX = horizontalOffset / _currentScale;
        var rightPixelX = (horizontalOffset + viewportWidth) / _currentScale;

        // 해당 픽셀의 경도 계산
        var (_, leftLon) = PixelToLatLon(leftPixelX, 0);
        var (_, rightLon) = PixelToLatLon(rightPixelX, 0);

        // 5도 단위로 눈금 표시
        var startLon = (int)Math.Ceiling(Math.Min(leftLon, rightLon) / 5) * 5;
        var endLon = (int)Math.Floor(Math.Max(leftLon, rightLon) / 5) * 5;

        for (var lon = startLon; lon <= endLon; lon += 5)
        {
            // 경도를 픽셀 X로 변환
            var pixelX = LonToPixelX(lon);
            // 스케일 적용 후 뷰포트 기준 X 좌표
            var screenX = (pixelX * _currentScale) - horizontalOffset;

            if (screenX >= 0 && screenX <= viewportWidth)
            {
                var lonDir = lon >= 0 ? "E" : "W";
                var text = new TextBlock
                {
                    Text = $"{Math.Abs(lon)}°{lonDir}",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromRgb(16, 79, 137)), // #104F89
                    FontFamily = new FontFamily("Consolas"),
                    FontWeight = FontWeights.SemiBold
                };

                Canvas.SetLeft(text, screenX - 15);
                Canvas.SetTop(text, 5);
                _longitudeScale.Children.Add(text);

                // 눈금선
                var line = new System.Windows.Shapes.Line
                {
                    X1 = screenX,
                    X2 = screenX,
                    Y1 = 20,
                    Y2 = 25,
                    Stroke = new SolidColorBrush(Color.FromRgb(16, 79, 137)),
                    StrokeThickness = 1
                };
                _longitudeScale.Children.Add(line);
            }
        }
    }

    private void OnShowCityLabelsChanged(bool showLabels)
    {
        if (_mapCanvas == null) return;
        MapMarkerHelper.SetLabelsVisibility(_mapCanvas, showLabels);
    }

    private void OnShowCoordinatesChanged(bool showCoordinates)
    {
        if (_mapCanvas == null) return;
        MapMarkerHelper.SetCoordinatesVisibility(_mapCanvas, showCoordinates);
    }

    private void OnShowCulturalSpheresChanged(bool showCulturalSpheres)
    {
        if (_mapCanvas == null || _viewModel == null) return;

        if (showCulturalSpheres)
        {
            // 영역 마커 추가
            var cities = _viewModel.GetCitiesWithCoordinates();
            MapMarkerHelper.AddAreaMarkers(_mapCanvas, cities, true);
        }
        else
        {
            // 영역 마커 제거
            MapMarkerHelper.ClearAreaMarkers(_mapCanvas);
        }
    }

    private void OnShowShipyardsChanged(bool showShipyards)
    {
        if (_mapCanvas == null) return;
        MapMarkerHelper.SetShipyardMarkersVisibility(_mapCanvas, showShipyards);
    }

    private void OnCityMarkerClicked(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is CityMarker marker)
        {
            System.Diagnostics.Debug.WriteLine($"[MapContent] CityMarker clicked: {marker.CityName} (ID: {marker.CityId})");
            MessageBox.Show($"도시: {marker.CityName}\nID: {marker.CityId}\n좌표: {marker.LatitudeDisplay}, {marker.LongitudeDisplay}\n도서관: {(marker.HasLibrary ? "있음" : "없음")}",
                "도시 정보", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void OnLibraryClicked(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not CityMarker marker) return;

        try
        {
            var bookService = ContainerLocator.Container.Resolve<BookService>();
            var dialog = new LibraryBookListDialog(marker.CityId, marker.CityName, bookService)
            {
                Owner = Window.GetWindow(this)
            };
            dialog.ShowDialog();
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MapContent] Library dialog error: {ex.Message}");
            MessageBox.Show($"도서 목록을 표시할 수 없습니다: {ex.Message}", "오류",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    #region 좌표 추적

    private void TryAutoStartTracking()
    {
        if (_viewModel == null || _viewModel.IsTracking)
        {
            if (_viewModel?.IsTracking == true && _btnTrackCoordinate != null)
                _btnTrackCoordinate.Content = "추적 중지";
            return;
        }

        if (_manuallyStoppedTracking) return;

        var hWnd = GameWindowHelper.FindGameWindow();
        if (hWnd == IntPtr.Zero) return;

        _viewModel.StartTracking();
        if (_btnTrackCoordinate != null)
            _btnTrackCoordinate.Content = "추적 중지";
    }

    private void ToggleTracking()
    {
        if (_viewModel == null) return;

        if (_viewModel.IsTracking)
        {
            _viewModel.StopTracking();
            _manuallyStoppedTracking = true;
            if (_btnTrackCoordinate != null)
                _btnTrackCoordinate.Content = "좌표 추적";
            if (_txtCurrentCoordinate != null)
                _txtCurrentCoordinate.Text = "";
            if (_currentPositionMarker != null)
                _currentPositionMarker.Visibility = Visibility.Collapsed;
            if (_currentPositionPulse != null)
                _currentPositionPulse.Visibility = Visibility.Collapsed;
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

            _manuallyStoppedTracking = false;
            _viewModel.StartTracking();
            if (_btnTrackCoordinate != null)
                _btnTrackCoordinate.Content = "추적 중지";
        }
    }

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

        var fileName = $"trail_{DateTime.Now:yyyyMMdd_HHmmss}.trail.json";
        var filePath = System.IO.Path.Combine(dir, fileName);

        var coords = _trailPoints.Select((p, i) =>
        {
            var (lat, lon) = PixelToLatLon(p.X, p.Y);
            var time = i < _trailTimestamps.Count
                ? _trailTimestamps[i].ToString("yyyy-MM-dd HH:mm:ss.fff")
                : "";
            return new { lat, lon, time };
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

        // 파일 선택 다이얼로그
        var selectWindow = new Window
        {
            Title = "경로 불러오기",
            Width = 450,
            Height = 350,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize
        };

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var listBox = new ListBox { Margin = new Thickness(10, 10, 10, 5) };
        foreach (var f in files)
        {
            var fi = new FileInfo(f);
            var item = new ListBoxItem
            {
                Content = $"{fi.Name}  ({fi.LastWriteTime:yyyy-MM-dd HH:mm})",
                Tag = f
            };
            listBox.Items.Add(item);
        }
        if (listBox.Items.Count > 0)
            listBox.SelectedIndex = 0;

        Grid.SetRow(listBox, 0);
        grid.Children.Add(listBox);

        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(10, 5, 10, 10)
        };
        var btnOk = new Button { Content = "열기", Width = 80, Height = 28, IsDefault = true };
        var btnCancel = new Button { Content = "취소", Width = 80, Height = 28, Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
        btnPanel.Children.Add(btnOk);
        btnPanel.Children.Add(btnCancel);
        Grid.SetRow(btnPanel, 1);
        grid.Children.Add(btnPanel);

        selectWindow.Content = grid;

        string? selectedFile = null;
        btnOk.Click += (s, e) =>
        {
            if (listBox.SelectedItem is ListBoxItem sel)
                selectedFile = sel.Tag as string;
            selectWindow.DialogResult = true;
        };

        listBox.MouseDoubleClick += (s, e) =>
        {
            if (listBox.SelectedItem is ListBoxItem sel)
            {
                selectedFile = sel.Tag as string;
                selectWindow.DialogResult = true;
            }
        };

        if (selectWindow.ShowDialog() != true || selectedFile == null) return;

        try
        {
            var json = File.ReadAllText(selectedFile);
            var coords = JsonSerializer.Deserialize<List<TrailCoord>>(json);
            if (coords == null || coords.Count == 0)
            {
                MessageBox.Show("경로 데이터가 비어 있습니다.", "경로 불러오기",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _trailPoints.Clear();
            _trailTimestamps.Clear();
            foreach (var c in coords)
            {
                var px = LonToPixelX(c.lon);
                var py = LatToPixelY(c.lat);
                _trailPoints.Add(new Point(px, py));
                _trailTimestamps.Add(
                    DateTime.TryParse(c.time, out var t) ? t : DateTime.MinValue);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"경로 파일 로드 실패:\n{ex.Message}", "오류",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private record TrailCoord(double lat, double lon, string? time = null);

    private void CreateCurrentPositionMarker()
    {
        if (_mapCanvas == null) return;

        // 이동 경로 선 (마커보다 먼저 추가해서 뒤에 표시)
        _trailLine = new Polyline
        {
            Stroke = new SolidColorBrush(Color.FromArgb(180, 255, 80, 80)),
            StrokeThickness = 2,
            IsHitTestVisible = false,
            Points = _trailPoints
        };
        _mapCanvas.Children.Add(_trailLine);

        // 외곽 펄스 원 (반투명)
        _currentPositionPulse = new Ellipse
        {
            Width = 20,
            Height = 20,
            Fill = new SolidColorBrush(Color.FromArgb(60, 255, 0, 0)),
            Stroke = null,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };
        _mapCanvas.Children.Add(_currentPositionPulse);

        // 중심 점 (빨간색)
        _currentPositionMarker = new Ellipse
        {
            Width = 10,
            Height = 10,
            Fill = new SolidColorBrush(Colors.Red),
            Stroke = new SolidColorBrush(Colors.White),
            StrokeThickness = 2,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };
        _mapCanvas.Children.Add(_currentPositionMarker);
    }

    private void OnCurrentCoordinateUpdated(CurrentCoordinateEventArgs args)
    {
        Dispatcher.Invoke(() =>
        {
            if (!args.IsTracking)
            {
                if (_currentPositionMarker != null)
                    _currentPositionMarker.Visibility = Visibility.Collapsed;
                if (_currentPositionPulse != null)
                    _currentPositionPulse.Visibility = Visibility.Collapsed;
                if (_txtCurrentCoordinate != null)
                    _txtCurrentCoordinate.Text = "";
                return;
            }

            var pixelX = LonToPixelX(args.Longitude);
            var pixelY = LatToPixelY(args.Latitude);

            // 이동 경로에 포인트 추가
            var newPoint = new Point(pixelX, pixelY);
            if (_trailPoints.Count == 0 || DistanceSq(_trailPoints[^1], newPoint) > 4)
            {
                _trailPoints.Add(newPoint);
                _trailTimestamps.Add(DateTime.Now);
            }

            // 마커 위치 업데이트
            if (_currentPositionMarker != null)
            {
                Canvas.SetLeft(_currentPositionMarker, pixelX - 5);
                Canvas.SetTop(_currentPositionMarker, pixelY - 5);
                _currentPositionMarker.Visibility = Visibility.Visible;
            }

            if (_currentPositionPulse != null)
            {
                Canvas.SetLeft(_currentPositionPulse, pixelX - 10);
                Canvas.SetTop(_currentPositionPulse, pixelY - 10);
                _currentPositionPulse.Visibility = Visibility.Visible;
            }

            // 좌표 텍스트 업데이트
            if (_txtCurrentCoordinate != null)
            {
                var latDir = args.Latitude >= 0 ? "N" : "S";
                var lonDir = args.Longitude >= 0 ? "E" : "W";
                var prefix = args.IsStale ? "최근" : "현재";
                var coordText = $"{prefix}: {Math.Abs(args.Latitude):F0}°{latDir}, {Math.Abs(args.Longitude):F0}°{lonDir}";
                if (!string.IsNullOrEmpty(args.GameDate))
                    coordText += $"  |  {args.GameDate}";
                _txtCurrentCoordinate.Text = coordText;
                _txtCurrentCoordinate.Foreground = new SolidColorBrush(args.IsStale ? Colors.Gray : Colors.Red);
            }

            // 마커가 뷰포트 80% 밖이면 스크롤
            ScrollToMarkerIfNeeded(pixelX, pixelY);
        });
    }

    private void StartNavigationFromUI()
    {
        if (_viewModel == null) return;

        if (!int.TryParse(_txtNavLat?.Text, out var latVal) ||
            !int.TryParse(_txtNavLon?.Text, out var lonVal))
        {
            MessageBox.Show("위도/경도를 숫자로 입력하세요.", "알림",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var isNorth = _cmbNavLatDir?.SelectedIndex == 0;
        var isEast = _cmbNavLonDir?.SelectedIndex == 0;
        var destLat = isNorth ? latVal : -latVal;
        var destLon = isEast ? lonVal : -lonVal;

        // 목적지 마커 표시
        var pixelX = LonToPixelX(destLon);
        var pixelY = LatToPixelY(destLat);
        ShowDestinationMarker(pixelX, pixelY);

        // 상태 이벤트 연결
        _viewModel.NavigationStatusChanged += OnNavStatusChanged;
        _viewModel.StartNavigation(destLat, destLon);

        if (_btnStopNav != null)
            _btnStopNav.Visibility = Visibility.Visible;
        if (_btnStartNav != null)
            _btnStartNav.Visibility = Visibility.Collapsed;
    }

    private void StopNavigation()
    {
        if (_viewModel == null) return;

        _viewModel.StopNavigation();
        _viewModel.NavigationStatusChanged -= OnNavStatusChanged;

        if (_btnStopNav != null)
            _btnStopNav.Visibility = Visibility.Collapsed;
        if (_btnStartNav != null)
            _btnStartNav.Visibility = Visibility.Visible;
        if (_destinationMarker != null)
            _destinationMarker.Visibility = Visibility.Collapsed;
        if (_txtNavStatus != null)
            _txtNavStatus.Text = "";
    }

    private void OnNavStatusChanged(string status)
    {
        Dispatcher.Invoke(() =>
        {
            if (_txtNavStatus != null)
                _txtNavStatus.Text = status;

            // 도착하면 UI 정리
            if (_viewModel != null && !_viewModel.IsNavigating)
            {
                _viewModel.NavigationStatusChanged -= OnNavStatusChanged;
                if (_btnStopNav != null)
                    _btnStopNav.Visibility = Visibility.Collapsed;
                if (_btnStartNav != null)
                    _btnStartNav.Visibility = Visibility.Visible;
                if (_destinationMarker != null)
                    _destinationMarker.Visibility = Visibility.Collapsed;
            }
        });
    }

    private void ShowDestinationMarker(double pixelX, double pixelY)
    {
        if (_mapCanvas == null) return;

        if (_destinationMarker == null)
        {
            _destinationMarker = new Ellipse
            {
                Width = 14,
                Height = 14,
                Fill = new SolidColorBrush(Color.FromArgb(100, 0, 120, 255)),
                Stroke = new SolidColorBrush(Colors.Blue),
                StrokeThickness = 2,
                IsHitTestVisible = false
            };
            _mapCanvas.Children.Add(_destinationMarker);
        }

        Canvas.SetLeft(_destinationMarker, pixelX - 7);
        Canvas.SetTop(_destinationMarker, pixelY - 7);
        _destinationMarker.Visibility = Visibility.Visible;
    }

    private static double DistanceSq(Point a, Point b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }

    private void GoToMyLocation()
    {
        if (_currentPositionMarker == null ||
            _currentPositionMarker.Visibility != Visibility.Visible) return;

        var pixelX = Canvas.GetLeft(_currentPositionMarker) + 5;
        var pixelY = Canvas.GetTop(_currentPositionMarker) + 5;

        _autoScrollEnabled = true;
        ScrollToImagePosition(pixelX, pixelY);
    }

    /// <summary>
    /// 마커가 뷰포트 중심 기준 80% 밖에 있으면 마커 위치로 스크롤
    /// </summary>
    private void ScrollToMarkerIfNeeded(double pixelX, double pixelY)
    {
        if (_mapScrollViewer == null || !_autoScrollEnabled) return;

        var screenX = (pixelX * _currentScale) - _mapScrollViewer.HorizontalOffset;
        var screenY = (pixelY * _currentScale) - _mapScrollViewer.VerticalOffset;

        var viewW = _mapScrollViewer.ViewportWidth;
        var viewH = _mapScrollViewer.ViewportHeight;

        var marginX = viewW * 0.1; // 좌우 10% 여백
        var marginY = viewH * 0.1;

        if (screenX < marginX || screenX > viewW - marginX ||
            screenY < marginY || screenY > viewH - marginY)
        {
            ScrollToImagePosition(pixelX, pixelY);
        }
    }

    #endregion
}
