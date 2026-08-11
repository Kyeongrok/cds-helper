using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Support.Local.Settings;

namespace CdsHelper.Main.UI.Views.D3D;

/// <summary>
/// 게임 화면처럼 지도 위에 함대만 띄우는 창. Direct3D 로 그린다.
/// </summary>
/// <remarks>
/// 세계지도 탭과 별개다. 그쪽은 도시·발견물 마커와 라벨이 WPF 요소로 얹혀 있어 손대지 않았고,
/// 이 창은 마커가 없는 대신 자식 창에 스왑체인을 곧바로 걸어 짧은 길로 그린다.
/// airspace 규칙상 D3D 화면 위에는 WPF 를 얹을 수 없으므로, 조작 줄은 화면 위가 아니라
/// 위아래로 나눠 놓았다.
/// </remarks>
public sealed class ShipMapWindow : Window
{
    private readonly ShipMapHost _host = new();
    private readonly TextBlock _status = new()
    {
        Foreground = Brushes.Gainsboro,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(8, 0, 0, 0),
        FontFamily = new FontFamily("Consolas"),
    };
    private readonly DispatcherTimerLite _statusTimer;

    /// <summary>게임 상단 바의 위경도 칸.</summary>
    private readonly TextBlock _coord = new()
    {
        Foreground = Brushes.Black,
        FontWeight = FontWeights.Bold,
        FontSize = 14,
        VerticalAlignment = VerticalAlignment.Center,
    };

    // 게임 화면 위쪽 띠에서 뽑은 색. 누런 양피지 바탕에 어두운 테두리다.
    private static readonly Brush BarFill = new SolidColorBrush(Color.FromRgb(0xC8, 0xBF, 0xA0));
    private static readonly Brush CellFill = new SolidColorBrush(Color.FromRgb(0xD2, 0xCA, 0xAD));
    private static readonly Brush BarEdge = new SolidColorBrush(Color.FromRgb(0x4A, 0x40, 0x30));

    /// <summary>게임처럼 두 겹 테두리를 두른 칸 하나를 만든다.</summary>
    private static Border GameCell(UIElement content) => new()
    {
        Background = CellFill,
        BorderBrush = BarEdge,
        BorderThickness = new Thickness(2),
        Margin = new Thickness(2, 3, 2, 3),
        Padding = new Thickness(10, 1, 10, 1),
        Child = new Border
        {
            BorderBrush = BarEdge,
            BorderThickness = new Thickness(0),
            Child = content,
        },
    };

    public ShipMapWindow()
    {
        Title = "함대 보기 (Direct3D)";
        Width = 1000;
        Height = 700;
        Background = Brushes.Black;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var steer = new CheckBox
        {
            Content = "커서로 몰기",
            IsChecked = true,
            Foreground = Brushes.Gainsboro,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 4, 0),
            ToolTip = "끄면 게임 함대의 실제 자리를 따라갑니다",
        };
        steer.Checked += (_, _) => _host.SteerWithMouse = true;
        steer.Unchecked += (_, _) => _host.SteerWithMouse = false;

        var follow = new CheckBox
        {
            Content = "화면 따라가기",
            IsChecked = true,
            Foreground = Brushes.Gainsboro,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 4, 0),
        };
        follow.Checked += (_, _) => _host.RecenterOnShip();
        follow.Unchecked += (_, _) => _host.Follow = false;

        var recenter = new Button { Content = "배로", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(4, 0, 0, 0) };
        recenter.Click += (_, _) => { follow.IsChecked = true; _host.RecenterOnShip(); };

        var toLisbon = new Button
        {
            Content = "리스본",
            Padding = new Thickness(8, 2, 8, 2),
            Margin = new Thickness(4, 0, 0, 0),
            ToolTip = "배를 리스본 앞바다로 되돌립니다",
        };
        toLisbon.Click += (_, _) => { follow.IsChecked = true; _host.ResetToLisbon(); };

        var hint = new TextBlock
        {
            Text = "Ctrl+클릭: 배 놓기",
            Foreground = Brushes.Gray,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
        };

        var bar = new DockPanel { Background = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22)), LastChildFill = true };
        bar.Children.Add(steer);
        DockPanel.SetDock(steer, Dock.Left);
        bar.Children.Add(follow);
        DockPanel.SetDock(follow, Dock.Left);
        bar.Children.Add(recenter);
        DockPanel.SetDock(recenter, Dock.Left);
        bar.Children.Add(toLisbon);
        DockPanel.SetDock(toLisbon, Dock.Left);
        bar.Children.Add(hint);
        DockPanel.SetDock(hint, Dock.Left);
        bar.Children.Add(_status);

        // HwndHost 자체는 WPF 에 아무것도 그리지 않아 히트테스트에 안 걸린다.
        // 같은 자리에 투명 Border 를 겹쳐 두고 마우스는 그쪽에서 받는다.
        // (자식 창이 D3D 로 덮으므로 이 Border 는 보이지 않는다 — 입력만 받는다.)
        var input = new Border { Background = Brushes.Transparent, Cursor = Cursors.Cross };
        var surface = new Grid();
        surface.Children.Add(_host);
        surface.Children.Add(input);

        // 게임 상단 띠 — 지금은 위경도 한 칸만 둔다.
        var gameCells = new StackPanel { Orientation = Orientation.Horizontal };
        gameCells.Children.Add(GameCell(_coord));
        var gameBar = new Border
        {
            Background = BarFill,
            BorderBrush = BarEdge,
            BorderThickness = new Thickness(0, 0, 0, 2),
            Child = gameCells,
        };

        var root = new DockPanel();
        DockPanel.SetDock(gameBar, Dock.Top);
        root.Children.Add(gameBar);
        var barHost = new Border { Child = bar, Height = 30 };
        DockPanel.SetDock(barHost, Dock.Top);
        root.Children.Add(barHost);
        root.Children.Add(surface);
        Content = root;
        input.MouseWheel += (_, e) => _host.Zoom(e.Delta > 0 ? 1 : -1, e.GetPosition(input));
        // 왼쪽 끌기는 배 조종에 양보하고, 지도 밀기는 오른쪽 끌기로 옮겼다.
        input.MouseRightButtonDown += (_, e) => { follow.IsChecked = false; _host.BeginDrag(e.GetPosition(input)); input.CaptureMouse(); };
        input.MouseRightButtonUp += (_, _) => { _host.EndDrag(); input.ReleaseMouseCapture(); };
        input.MouseLeftButtonDown += (_, e) =>
        {
            // Ctrl 을 누른 채 찍으면 그 자리에 배를 놓는다. 시작 자리를 손으로 잡는 길이다.
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) _host.PlaceShipAt(e.GetPosition(input));
        };
        input.MouseMove += (_, e) => { var p = e.GetPosition(input); _host.SetMouse(p, true); _host.Drag(p); };
        input.MouseLeave += (_, _) => _host.SetMouse(default, false);

        _statusTimer = new DispatcherTimerLite(TimeSpan.FromMilliseconds(100), () =>
        {
            _status.Text = _host.Status;
            var (lat, lon) = _host.ShipLatLon;
            // 게임과 같은 말투로 적는다 — 북위/남위, 동경/서경에 정수 도.
            _coord.Text = $"{(lat >= 0 ? "북위" : "남위")} {Math.Abs(lat),3:F0}    " +
                          $"{(lon >= 0 ? "동경" : "서경")} {Math.Abs(lon),3:F0}";
        });
        Loaded += OnLoaded;
        Closed += (_, _) => _statusTimer.Stop();
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        var dir = Path.GetDirectoryName(AppSettings.LastSaveFilePath);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
        {
            _status.Text = "세이브 파일 경로가 없습니다 — 먼저 세이브를 열어 주세요";
            return;
        }
        if (!_host.Start(dir))
        {
            _status.Text = _host.Status;
            return;
        }
        _statusTimer.Start();
    }
}

/// <summary>상태 줄만 이따금 갱신하려고 쓰는 간단한 타이머.</summary>
internal sealed class DispatcherTimerLite
{
    private readonly System.Windows.Threading.DispatcherTimer _t;

    public DispatcherTimerLite(TimeSpan interval, Action tick)
    {
        _t = new System.Windows.Threading.DispatcherTimer { Interval = interval };
        _t.Tick += (_, _) => tick();
    }

    public void Start() => _t.Start();
    public void Stop() => _t.Stop();
}
