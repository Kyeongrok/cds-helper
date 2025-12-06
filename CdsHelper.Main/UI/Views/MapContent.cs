using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Main.UI.ViewModels;
using CdsHelper.Support.Local.Helpers;
using CdsHelper.Support.Local.Settings;
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
    private ScrollViewer? _mapScrollViewer;
    private Image? _imgMap;
    private Canvas? _mapCanvas;
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
        _mapScrollViewer = GetTemplateChild("PART_MapScrollViewer") as ScrollViewer;
        _imgMap = GetTemplateChild("PART_ImgMap") as Image;
        _mapCanvas = GetTemplateChild("PART_MapCanvas") as Canvas;
        _mapScaleTransform = GetTemplateChild("PART_MapScaleTransform") as ScaleTransform;
        _canvasScaleTransform = GetTemplateChild("PART_CanvasScaleTransform") as ScaleTransform;

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
            _mapScrollViewer.Loaded += MapScrollViewer_Loaded;
        }

        // 탭 전환 시 위치 저장/복원
        IsVisibleChanged += OnIsVisibleChanged;

        if (_imgMap != null)
        {
            _imgMap.MouseMove += ImgMap_MouseMove;
            _imgMap.MouseLeave += ImgMap_MouseLeave;
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

        _isDragging = true;
        _lastMousePosition = e.GetPosition(_mapScrollViewer);
        _mapScrollViewer.CaptureMouse();
        e.Handled = true;
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
        if (_imgMap == null || _txtMapCoordinates == null) return;

        var pos = e.GetPosition(_imgMap);
        _txtMapCoordinates.Text = $"좌표: X={pos.X:F0}, Y={pos.Y:F0}";
    }

    private void ImgMap_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_txtMapCoordinates != null)
            _txtMapCoordinates.Text = "좌표: -";
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
}
