using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Main.UI.ViewModels;
using CdsHelper.Support.Local.Helpers;
using CdsHelper.Support.Local.Settings;
using CdsHelper.Support.UI.Units;
using Prism.Ioc;

namespace CdsHelper.Main.UI.Views;

public class MapContent : ContentControl
{
    private Button? _btnZoomIn;
    private Button? _btnZoomOut;
    private Button? _btnZoomReset;
    private CheckBox? _chkShowCityLabels;
    private CheckBox? _chkShowCoordinates;
    private CheckBox? _chkShowCulturalSpheres;
    private TextBlock? _txtMapCoordinates;
    private TextBlock? _txtCenterPosition;
    private ScrollViewer? _mapScrollViewer;
    private Image? _imgMap;
    private Canvas? _mapCanvas;
    private Canvas? _latitudeScale;
    private Canvas? _longitudeScale;
    private ScaleTransform? _mapScaleTransform;
    private ScaleTransform? _canvasScaleTransform;

    private double _currentScale = 1.0;
    private const double ScaleStep = 0.5;
    private const double MinScale = 0.5;
    private const double MaxScale = 5.0;

    private bool _isDragging;
    private Point _lastMousePosition;

    private MapContentViewModel? _viewModel;

    // 스크롤 위치 저장용 (탭 전환 시 유지)
    private static double _savedHorizontalOffset = -1;
    private static double _savedVerticalOffset = -1;
    private static double _savedScale = 1.0;
    private static bool _hasSavedPosition = false;

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
        if (_hasSavedPosition)
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
        if (_mapCanvas == null) return;

        var pos = e.GetPosition(_mapCanvas);
        var (lat, lon) = PixelToLatLon(pos.X, pos.Y);
        var latDir = lat >= 0 ? "N" : "S";
        var lonDir = lon >= 0 ? "E" : "W";

        MessageBox.Show($"위도: {Math.Abs(lat):F1}°{latDir}\n경도: {Math.Abs(lon):F1}°{lonDir}",
            "좌표 정보", MessageBoxButton.OK, MessageBoxImage.Information);

        e.Handled = true;
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

        // 스케일 (픽셀 per 도)
        const double pixelsPerDegreeLon = 24.0;
        const double pixelsPerDegreeLat = 21.5;

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
        const double pixelsPerDegreeLat = 21.5;

        return refPixelY - (lat - refLat) * pixelsPerDegreeLat;
    }

    /// <summary>
    /// 경도를 픽셀 X 좌표로 변환
    /// </summary>
    private double LonToPixelX(double lon)
    {
        const double refPixelX = 3525;
        const double refLon = -9;
        const double pixelsPerDegreeLon = 24.0;

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
        if (e.OriginalSource is CityMarker marker)
        {
            System.Diagnostics.Debug.WriteLine($"[MapContent] Library clicked: {marker.CityName}");
        }
    }
}
