using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CdsHelper.Main.UI.Views;

public class MapContent : ContentControl
{
    private Button? _btnZoomIn;
    private Button? _btnZoomOut;
    private Button? _btnZoomReset;
    private TextBlock? _txtMapCoordinates;
    private ScrollViewer? _mapScrollViewer;
    private Image? _imgMap;
    private Canvas? _mapCanvas;
    private ScaleTransform? _mapScaleTransform;
    private ScaleTransform? _canvasScaleTransform;

    private double _currentScale = 0.3;
    private const double ScaleStep = 0.1;
    private const double MinScale = 0.1;
    private const double MaxScale = 2.0;

    private bool _isDragging;
    private Point _lastMousePosition;

    static MapContent()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(MapContent),
            new FrameworkPropertyMetadata(typeof(MapContent)));
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        // 지도 컨트롤 찾기
        _btnZoomIn = GetTemplateChild("PART_BtnZoomIn") as Button;
        _btnZoomOut = GetTemplateChild("PART_BtnZoomOut") as Button;
        _btnZoomReset = GetTemplateChild("PART_BtnZoomReset") as Button;
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

        if (_mapScrollViewer != null)
        {
            _mapScrollViewer.PreviewMouseWheel += MapScrollViewer_PreviewMouseWheel;
            _mapScrollViewer.PreviewMouseLeftButtonDown += MapScrollViewer_PreviewMouseLeftButtonDown;
            _mapScrollViewer.PreviewMouseMove += MapScrollViewer_PreviewMouseMove;
            _mapScrollViewer.PreviewMouseLeftButtonUp += MapScrollViewer_PreviewMouseLeftButtonUp;
        }

        if (_imgMap != null)
        {
            _imgMap.MouseMove += ImgMap_MouseMove;
            _imgMap.MouseLeave += ImgMap_MouseLeave;
        }

        // 지도 이미지 로드
        LoadMapImage();
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
            }
            catch { }
        }
    }

    private void ZoomIn()
    {
        if (_currentScale < MaxScale)
        {
            _currentScale += ScaleStep;
            ApplyScale();
        }
    }

    private void ZoomOut()
    {
        if (_currentScale > MinScale)
        {
            _currentScale -= ScaleStep;
            ApplyScale();
        }
    }

    private void ZoomReset()
    {
        _currentScale = 0.3;
        ApplyScale();
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

    private void MapScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (e.Delta > 0)
                ZoomIn();
            else
                ZoomOut();
            e.Handled = true;
        }
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
}
