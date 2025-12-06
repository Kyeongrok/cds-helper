using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using IOPath = System.IO.Path;

namespace CdsHelper.Form.UI.Views;

[TemplatePart(Name = PART_PixelXTextBox, Type = typeof(TextBox))]
[TemplatePart(Name = PART_PixelYTextBox, Type = typeof(TextBox))]
[TemplatePart(Name = PART_HasLibraryCheckBox, Type = typeof(CheckBox))]
[TemplatePart(Name = PART_MapScrollViewer, Type = typeof(ScrollViewer))]
[TemplatePart(Name = PART_MapImage, Type = typeof(Image))]
[TemplatePart(Name = PART_MapCanvas, Type = typeof(Canvas))]
[TemplatePart(Name = PART_OkButton, Type = typeof(Button))]
[TemplatePart(Name = PART_CancelButton, Type = typeof(Button))]
public class EditCityPixelDialog : Window
{
    private const string PART_PixelXTextBox = "PART_PixelXTextBox";
    private const string PART_PixelYTextBox = "PART_PixelYTextBox";
    private const string PART_HasLibraryCheckBox = "PART_HasLibraryCheckBox";
    private const string PART_MapScrollViewer = "PART_MapScrollViewer";
    private const string PART_MapImage = "PART_MapImage";
    private const string PART_MapCanvas = "PART_MapCanvas";
    private const string PART_OkButton = "PART_OkButton";
    private const string PART_CancelButton = "PART_CancelButton";

    private TextBox? _pixelXTextBox;
    private TextBox? _pixelYTextBox;
    private CheckBox? _hasLibraryCheckBox;
    private ScrollViewer? _mapScrollViewer;
    private Image? _mapImage;
    private Canvas? _mapCanvas;
    private Ellipse? _currentMarker;

    public static readonly DependencyProperty CityNameProperty =
        DependencyProperty.Register(nameof(CityName), typeof(string), typeof(EditCityPixelDialog),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty PixelXProperty =
        DependencyProperty.Register(nameof(PixelX), typeof(int?), typeof(EditCityPixelDialog),
            new PropertyMetadata(null));

    public static readonly DependencyProperty PixelYProperty =
        DependencyProperty.Register(nameof(PixelY), typeof(int?), typeof(EditCityPixelDialog),
            new PropertyMetadata(null));

    public static readonly DependencyProperty HasLibraryProperty =
        DependencyProperty.Register(nameof(HasLibrary), typeof(bool), typeof(EditCityPixelDialog),
            new PropertyMetadata(false));

    public string CityName
    {
        get => (string)GetValue(CityNameProperty);
        set => SetValue(CityNameProperty, value);
    }

    public int? PixelX
    {
        get => (int?)GetValue(PixelXProperty);
        set => SetValue(PixelXProperty, value);
    }

    public int? PixelY
    {
        get => (int?)GetValue(PixelYProperty);
        set => SetValue(PixelYProperty, value);
    }

    public bool HasLibrary
    {
        get => (bool)GetValue(HasLibraryProperty);
        set => SetValue(HasLibraryProperty, value);
    }

    static EditCityPixelDialog()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(EditCityPixelDialog),
            new FrameworkPropertyMetadata(typeof(EditCityPixelDialog)));
    }

    public EditCityPixelDialog(string cityName, int? currentX, int? currentY, bool hasLibrary = false)
    {
        Title = "도시 정보 수정";
        Width = 700;
        Height = 550;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;

        CityName = $"도시: {cityName}";
        PixelX = currentX;
        PixelY = currentY;
        HasLibrary = hasLibrary;
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        _pixelXTextBox = GetTemplateChild(PART_PixelXTextBox) as TextBox;
        _pixelYTextBox = GetTemplateChild(PART_PixelYTextBox) as TextBox;
        _hasLibraryCheckBox = GetTemplateChild(PART_HasLibraryCheckBox) as CheckBox;
        _mapScrollViewer = GetTemplateChild(PART_MapScrollViewer) as ScrollViewer;
        _mapImage = GetTemplateChild(PART_MapImage) as Image;
        _mapCanvas = GetTemplateChild(PART_MapCanvas) as Canvas;

        if (GetTemplateChild(PART_OkButton) is Button okButton)
            okButton.Click += OnOkClick;

        if (GetTemplateChild(PART_CancelButton) is Button cancelButton)
            cancelButton.Click += OnCancelClick;

        if (_mapImage != null)
            _mapImage.MouseLeftButtonDown += OnMapClick;

        if (_pixelXTextBox != null)
            _pixelXTextBox.Text = PixelX?.ToString() ?? "";

        if (_pixelYTextBox != null)
            _pixelYTextBox.Text = PixelY?.ToString() ?? "";

        if (_hasLibraryCheckBox != null)
            _hasLibraryCheckBox.IsChecked = HasLibrary;

        LoadMapImage();

        if (PixelX.HasValue && PixelY.HasValue)
        {
            AddMarkerAt(PixelX.Value, PixelY.Value);
            ScrollToPosition(PixelX.Value, PixelY.Value);
        }

        _pixelXTextBox?.Focus();
        _pixelXTextBox?.SelectAll();
    }

    private void LoadMapImage()
    {
        if (_mapImage == null || _mapCanvas == null) return;

        var basePath = AppDomain.CurrentDomain.BaseDirectory;
        var mapPath = IOPath.Combine(basePath, "대항해시대3-지도(발견물-이름-기준).jpg");

        if (!File.Exists(mapPath))
        {
            MessageBox.Show($"지도 파일을 찾을 수 없습니다:\n{mapPath}", "오류",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri(mapPath, UriKind.Absolute);
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();

        _mapImage.Source = bitmap;
        _mapImage.Width = bitmap.PixelWidth;
        _mapImage.Height = bitmap.PixelHeight;

        _mapCanvas.Width = bitmap.PixelWidth;
        _mapCanvas.Height = bitmap.PixelHeight;
    }

    private void ScrollToPosition(int x, int y)
    {
        if (_mapScrollViewer == null) return;

        Dispatcher.BeginInvoke(new Action(() =>
        {
            var viewportWidth = _mapScrollViewer.ViewportWidth;
            var viewportHeight = _mapScrollViewer.ViewportHeight;

            var scrollX = Math.Max(0, x - viewportWidth / 2);
            var scrollY = Math.Max(0, y - viewportHeight / 2);

            _mapScrollViewer.ScrollToHorizontalOffset(scrollX);
            _mapScrollViewer.ScrollToVerticalOffset(scrollY);
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void AddMarkerAt(int x, int y)
    {
        if (_mapCanvas == null) return;

        if (_currentMarker != null)
            _mapCanvas.Children.Remove(_currentMarker);

        const int markerSize = 12;
        _currentMarker = new Ellipse
        {
            Width = markerSize,
            Height = markerSize,
            Fill = Brushes.Red,
            Stroke = Brushes.White,
            StrokeThickness = 2
        };

        Canvas.SetLeft(_currentMarker, x - markerSize / 2);
        Canvas.SetTop(_currentMarker, y - markerSize / 2);
        _mapCanvas.Children.Add(_currentMarker);
    }

    private void OnMapClick(object sender, MouseButtonEventArgs e)
    {
        if (_mapImage == null) return;

        var position = e.GetPosition(_mapImage);
        var x = (int)position.X;
        var y = (int)position.Y;

        if (_pixelXTextBox != null)
            _pixelXTextBox.Text = x.ToString();

        if (_pixelYTextBox != null)
            _pixelYTextBox.Text = y.ToString();

        AddMarkerAt(x, y);
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        PixelX = int.TryParse(_pixelXTextBox?.Text, out var x) ? x : null;
        PixelY = int.TryParse(_pixelYTextBox?.Text, out var y) ? y : null;
        HasLibrary = _hasLibraryCheckBox?.IsChecked ?? false;
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
