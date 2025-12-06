using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Main.UI.ViewModels;
using CdsHelper.Support.Local.Helpers;

namespace CdsHelper.Main.UI.Views;

public class MapContent : ContentControl
{
    private Button? _btnZoomIn;
    private Button? _btnZoomOut;
    private Button? _btnZoomReset;
    private CheckBox? _chkShowCityLabels;
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

    static MapContent()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(MapContent),
            new FrameworkPropertyMetadata(typeof(MapContent)));
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        // ViewModel 초기화
        _viewModel = new MapContentViewModel(new CityService());

        // 지도 컨트롤 찾기
        _btnZoomIn = GetTemplateChild("PART_BtnZoomIn") as Button;
        _btnZoomOut = GetTemplateChild("PART_BtnZoomOut") as Button;
        _btnZoomReset = GetTemplateChild("PART_BtnZoomReset") as Button;
        _chkShowCityLabels = GetTemplateChild("PART_ChkShowCityLabels") as CheckBox;
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
            _chkShowCityLabels.Checked += (s, e) => OnShowCityLabelsChanged(true);
        if (_chkShowCityLabels != null)
            _chkShowCityLabels.Unchecked += (s, e) => OnShowCityLabelsChanged(false);

        if (_mapScrollViewer != null)
        {
            _mapScrollViewer.PreviewMouseWheel += MapScrollViewer_PreviewMouseWheel;
            _mapScrollViewer.PreviewMouseLeftButtonDown += MapScrollViewer_PreviewMouseLeftButtonDown;
            _mapScrollViewer.PreviewMouseMove += MapScrollViewer_PreviewMouseMove;
            _mapScrollViewer.PreviewMouseLeftButtonUp += MapScrollViewer_PreviewMouseLeftButtonUp;
            _mapScrollViewer.Loaded += MapScrollViewer_Loaded;
        }

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
            MapMarkerHelper.AddCityMarkers(_mapCanvas, cities);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LoadCityMarkers] Error: {ex.Message}");
        }
    }

    private void MapScrollViewer_Loaded(object sender, RoutedEventArgs e)
    {
        // 초기 스케일 적용
        ApplyScale();
        // 초기 위치 설정 (3529, 899가 가운데 오도록)
        ScrollToImagePosition(3529, 899);
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
}
