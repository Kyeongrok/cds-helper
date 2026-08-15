using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Support.Local.Helpers;

namespace CdsHelper.Main.UI.Views.D3D;

/// <summary>
/// 입항한 도시의 그림(CITYCG.CDS)을 지도 한가운데에 띄운다. 게임처럼 항구(탑)를 누르면
/// 명령 창이 열리고, 거기서 출항하면 다시 항해로 돌아간다.
/// </summary>
/// <remarks>
/// <see cref="PortDialog"/> 와 같은 수를 쓴다 — 창(HWND)을 따로 쓰므로 D3D 자식 창 위에
/// 제대로 뜬다(airspace 를 안 탄다). 그림은 400x320 도트 그림이라 정수배로만 늘린다.
///
/// 항구 자리는 <see cref="CityHarbors"/> 표에서 온다. 표에 없는 도시(유럽식이 아닌 그림)는
/// 그림 아무 데나 눌러도 명령 창이 열리게 해 두었다 — 출항할 길은 어디서나 있어야 한다.
/// </remarks>
public sealed class CityPicDialog : Window
{
    private static readonly Brush Back = new SolidColorBrush(Color.FromRgb(0x3A, 0x24, 0x1E));
    private static readonly Brush Edge = new SolidColorBrush(Color.FromRgb(0xC8, 0xB4, 0x90));
    private static readonly Brush Text = new SolidColorBrush(Color.FromRgb(0xF2, 0xEA, 0xD6));

    // 게임 명령 창에서 뽑은 색. 짙은 밤색 바탕에 밝은 테를 두르고, 항목만 양피지다.
    private static readonly Brush MenuBack = new SolidColorBrush(Color.FromRgb(0x4A, 0x2A, 0x22));
    private static readonly Brush MenuEdge = new SolidColorBrush(Color.FromRgb(0xC8, 0xB4, 0x90));
    private static readonly Brush ItemFill = new SolidColorBrush(Color.FromRgb(0xD2, 0xCA, 0xAD));
    private static readonly Brush ItemEdge = new SolidColorBrush(Color.FromRgb(0x4A, 0x40, 0x30));

    /// <summary>항구 이름표와 명령 창을 얹는 자리. 그림과 같은 격자 칸에 둔다.</summary>
    private readonly Canvas _layer = new();

    private readonly Border _menu;
    private readonly Border _label;

    /// <summary>출항을 골랐는지. 창을 그냥 닫으면 false.</summary>
    public bool Sailed { get; private set; }

    private CityPicDialog(string cityName, BitmapSource picture, int scale, Rect? harbor)
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = Back;

        var image = new Image
        {
            Source = picture,
            Width = CityPictures.Width * scale,
            Height = CityPictures.Height * scale,
            Stretch = Stretch.Fill,
        };
        // 도트 그림이라 늘릴 때 섞으면 뭉개진다 — 게임 화면처럼 각을 살린다.
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);

        _label = MakeLabel("항구");
        _menu = MakeMenu(cityName);
        _layer.Children.Add(_label);
        _layer.Children.Add(_menu);

        var picBox = new Grid
        {
            Width = image.Width,
            Height = image.Height,
            Children = { image, _layer },
        };

        if (harbor is { } h)
        {
            // 탑만 누를 수 있게 그 자리에 투명한 판을 얹는다. 1배로 보면 탑이 14x36 점밖에
            // 안 돼 겨누기 어려우므로 판만 사방으로 조금 넓힌다(이름표는 탑에 맞춘다).
            var hit = Rect.Inflate(h, 3, 3);
            var spot = new Border
            {
                Width = hit.Width * scale,
                Height = hit.Height * scale,
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
            };
            Canvas.SetLeft(spot, hit.X * scale);
            Canvas.SetTop(spot, hit.Y * scale);
            spot.MouseEnter += (_, _) => ShowLabel(h, scale, image.Width);
            spot.MouseLeave += (_, _) => _label.Visibility = Visibility.Collapsed;
            spot.MouseLeftButtonUp += (_, _) => OpenMenu();
            _layer.Children.Add(spot);
        }
        else
        {
            // 항구 자리를 모르는 그림이면 어디를 눌러도 명령 창이 열린다.
            picBox.Cursor = Cursors.Hand;
            picBox.MouseLeftButtonUp += (_, _) => OpenMenu();
        }

        var caption = new TextBlock
        {
            Text = cityName,
            Foreground = Text,
            FontWeight = FontWeights.Bold,
            FontSize = 16,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 2),
        };

        var hint = new TextBlock
        {
            Text = harbor == null ? "그림을 누르면 명령 창 · ESC 로 닫기"
                                  : "항구(탑)를 누르면 명령 창 · ESC 로 닫기",
            Foreground = Brushes.Gray,
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8),
        };

        var stack = new StackPanel();
        stack.Children.Add(new Border
        {
            BorderBrush = Edge,
            BorderThickness = new Thickness(2),
            Child = picBox,
        });
        stack.Children.Add(caption);
        stack.Children.Add(hint);

        Content = new Border
        {
            BorderBrush = Edge,
            BorderThickness = new Thickness(2),
            Margin = new Thickness(4),
            Padding = new Thickness(6, 6, 6, 0),
            Child = stack,
        };

        // 제목 줄이 없으니(WindowStyle.None) 키로 닫는다. 오른쪽 단추도 같다.
        KeyDown += (_, e) => { if (e.Key is Key.Escape or Key.Enter) Close(); };
        MouseRightButtonUp += (_, _) => Close();
    }

    /// <summary>탑 밑에 이름표를 띄운다. 게임도 건물 밑에 붙여 준다.</summary>
    private void ShowLabel(Rect harbor, int scale, double picWidth)
    {
        _label.Visibility = Visibility.Visible;
        _label.UpdateLayout();
        double w = _label.ActualWidth > 0 ? _label.ActualWidth : 44;
        double x = (harbor.X + harbor.Width / 2) * scale - w / 2;
        Canvas.SetLeft(_label, Math.Clamp(x, 0, Math.Max(0, picWidth - w)));
        Canvas.SetTop(_label, (harbor.Y + harbor.Height) * scale + 2);
    }

    private void OpenMenu()
    {
        _label.Visibility = Visibility.Collapsed;
        _menu.Visibility = Visibility.Visible;
    }

    private static Border MakeLabel(string text) => new()
    {
        Background = ItemFill,
        BorderBrush = ItemEdge,
        BorderThickness = new Thickness(2),
        Padding = new Thickness(8, 0, 8, 0),
        Visibility = Visibility.Collapsed,
        Child = new TextBlock
        {
            Text = text,
            Foreground = Brushes.Black,
            FontWeight = FontWeights.Bold,
            FontSize = 14,
        },
    };

    /// <summary>
    /// 게임 항구 명령 창. 지금 되는 것은 출항뿐이라 나머지는 흐려 둔다 —
    /// 보급·함대편성 따위는 이 창이 흉내내는 범위 밖이다.
    /// </summary>
    private Border MakeMenu(string cityName)
    {
        var items = new StackPanel();
        items.Children.Add(new Border
        {
            Background = MenuBack,
            BorderBrush = MenuEdge,
            BorderThickness = new Thickness(2),
            Padding = new Thickness(18, 2, 18, 2),
            Margin = new Thickness(0, 0, 0, 6),
            Child = new TextBlock
            {
                Text = cityName,
                Foreground = Text,
                FontWeight = FontWeights.Bold,
                FontSize = 15,
                HorizontalAlignment = HorizontalAlignment.Center,
            },
        });
        items.Children.Add(MenuItem("출항", () => { Sailed = true; Close(); }));
        items.Children.Add(MenuItem("보급", null));
        items.Children.Add(MenuItem("함대편성", null));
        items.Children.Add(MenuItem("선원편성", null));
        items.Children.Add(MenuItem("마을정보", null));
        items.Children.Add(MenuItem("마을로 돌아간다",
                                    () => _menu.Visibility = Visibility.Collapsed));

        var box = new Border
        {
            Background = MenuBack,
            BorderBrush = MenuEdge,
            BorderThickness = new Thickness(3),
            Padding = new Thickness(6),
            Visibility = Visibility.Collapsed,
        };
        box.Child = items;

        // 그림 한가운데에 놓는다. 크기는 자식이 정하므로 다 재고 나서 자리를 잡는다.
        box.Loaded += (_, _) => CenterMenu(box);
        box.SizeChanged += (_, _) => CenterMenu(box);
        return box;
    }

    private void CenterMenu(Border box)
    {
        if (_layer.ActualWidth <= 0) return;
        Canvas.SetLeft(box, (_layer.ActualWidth - box.ActualWidth) / 2);
        Canvas.SetTop(box, (_layer.ActualHeight - box.ActualHeight) / 2);
    }

    private static Border MenuItem(string text, Action? run)
    {
        var item = new Border
        {
            Background = ItemFill,
            BorderBrush = ItemEdge,
            BorderThickness = new Thickness(2),
            Margin = new Thickness(0, 0, 0, 3),
            Padding = new Thickness(24, 2, 24, 2),
            Cursor = run != null ? Cursors.Hand : Cursors.Arrow,
            Child = new TextBlock
            {
                Text = text,
                Foreground = run != null ? Brushes.Black : Brushes.Gray,
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center,
            },
        };
        if (run != null) item.MouseLeftButtonUp += (_, e) => { e.Handled = true; run(); };
        return item;
    }

    /// <summary>
    /// 도시 그림을 띄운다. 그림을 못 풀면 아무것도 안 하고 false —
    /// 그림이 없다고 입항까지 막을 일은 아니다.
    /// </summary>
    public static bool Show(Window owner, CityPictures pictures, int cityId, string cityName)
    {
        var bgra = pictures.TryGetBgra(cityId);
        if (bgra == null) return false;

        var picture = BitmapSource.Create(CityPictures.Width, CityPictures.Height, 96, 96,
                                          PixelFormats.Bgra32, null, bgra, CityPictures.Width * 4);
        picture.Freeze();

        Rect? harbor = CityHarbors.TryGet(cityId, out int hx, out int hy)
            ? new Rect(hx, hy, CityHarbors.Width, CityHarbors.Height)
            : null;

        var dlg = new CityPicDialog(cityName, picture, PickScale(owner), harbor) { Owner = owner };
        dlg.ShowDialog();
        return true;
    }

    /// <summary>창에 들어가는 가장 큰 정수 배율. 창이 작아도 1배는 쓴다.</summary>
    private static int PickScale(Window owner)
    {
        // 글자와 테두리로 세로 60점쯤 더 먹으므로 그만큼 뺀 자리에 맞춘다.
        double w = owner.ActualWidth * 0.9;
        double h = owner.ActualHeight * 0.9 - 60;
        int scale = (int)Math.Min(w / CityPictures.Width, h / CityPictures.Height);
        return Math.Max(1, Math.Min(scale, 4));
    }
}
