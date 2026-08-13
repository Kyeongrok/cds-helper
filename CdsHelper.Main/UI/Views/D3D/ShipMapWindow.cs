using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Support.Local.Helpers;
using CdsHelper.Support.Local.Settings;
using Prism.Ioc;

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
    private readonly BgmPlayer _bgm = new();

    /// <summary>지도 쪽 화면. 타이틀에서 고르면 이것으로 갈아 끼운다.</summary>
    private FrameworkElement _mapRoot = null!;

    /// <summary>게임 폴더. WORLD.CDS 도 bgm 도 여기서 읽는다.</summary>
    private string _gameDir = "";

    /// <summary>지도를 한 번 띄웠는지. <see cref="ShipMapHost.Start"/> 는 한 번만 부른다.</summary>
    private bool _started;

    /// <summary>도시 ID -> 이름. 한 번만 불러 둔다.</summary>
    private Dictionary<int, string>? _cityNames;

    /// <summary>방금 물어본 도시. 떠났다 다시 와야 다시 묻는다.</summary>
    private int _askedCity = -1;

    /// <summary>다이얼로그가 떠 있는 동안 또 묻지 않게.</summary>
    private bool _asking;

    /// <summary>지금 이동 모드 — 해상인지 육지인지.</summary>
    private readonly TextBlock _mode = new()
    {
        Foreground = Brushes.Black,
        FontWeight = FontWeights.Bold,
        FontSize = 14,
        VerticalAlignment = VerticalAlignment.Center,
    };

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

    // 게임 커맨드 창에서 뽑은 색. 짙은 밤색 바탕에 밝은 테를 두르고, 항목만 양피지다.
    private static readonly Brush MenuBack = new SolidColorBrush(Color.FromRgb(0x4A, 0x2A, 0x22));
    private static readonly Brush MenuEdge = new SolidColorBrush(Color.FromRgb(0xC8, 0xB4, 0x90));
    private static readonly Brush MenuTitleFg = new SolidColorBrush(Color.FromRgb(0xEC, 0xDF, 0xC0));

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
            Text = "왼쪽 클릭: 정박/닻 올리기 · Ctrl+클릭: 배 놓기",
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
        gameCells.Children.Add(GameCell(_mode));
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
        _mapRoot = root;

        // 타이틀을 지도 위에 겹쳐 둘 수는 없다 — airspace 규칙상 D3D 자식 창이 WPF 를 덮는다.
        // 그래서 겹치지 않고 통째로 갈아 끼운다. 타이틀이 떠 있는 동안은 자식 창 자체가 없다.
        Content = BuildTitleScreen();
        input.MouseWheel += (_, e) => _host.Zoom(e.Delta > 0 ? 1 : -1, e.GetPosition(input));
        // 왼쪽 끌기는 배 조종에 양보하고, 지도 밀기는 오른쪽 끌기로 옮겼다.
        // 오른쪽 단추는 둘을 겸한다 — 끌면 지도 밀기, 움직이지 않고 떼면 커맨드 메뉴.
        Point rightDownAt = default;
        input.MouseRightButtonDown += (_, e) =>
        {
            rightDownAt = e.GetPosition(input);
            _host.BeginDrag(rightDownAt);
            input.CaptureMouse();
        };
        input.MouseRightButtonUp += (_, e) =>
        {
            _host.EndDrag();
            input.ReleaseMouseCapture();
            var up = e.GetPosition(input);
            bool moved = Math.Abs(up.X - rightDownAt.X) > 3 || Math.Abs(up.Y - rightDownAt.Y) > 3;
            if (moved) { follow.IsChecked = false; return; }   // 끈 것이면 메뉴를 안 띄운다
            ShowCommandMenu(input);
        };
        input.MouseLeftButtonDown += (_, e) =>
        {
            // Ctrl 을 누른 채 찍으면 그 자리에 배를 놓는다. 시작 자리를 손으로 잡는 길이다.
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                _host.PlaceShipAt(e.GetPosition(input));
                return;
            }
            // 그냥 찍으면 닻을 내리고 그 자리에 선다. 한 번 더 찍으면 올리고 다시 간다.
            _host.ToggleAnchor();
        };
        input.MouseMove += (_, e) => { var p = e.GetPosition(input); _host.SetMouse(p, true); _host.Drag(p); };
        input.MouseLeave += (_, _) => _host.SetMouse(default, false);

        _statusTimer = new DispatcherTimerLite(TimeSpan.FromMilliseconds(100), () =>
        {
            _status.Text = _host.Status;
            _mode.Text = _host.IsOnLand ? "육지 이동" : _host.IsAnchored ? "정 박" : "해상 이동";
            CheckPort();
            var (lat, lon) = _host.ShipLatLon;
            // 게임과 같은 말투로 적는다 — 북위/남위, 동경/서경에 정수 도.
            _coord.Text = $"{(lat >= 0 ? "북위" : "남위")} {Math.Abs(lat),3:F0}    " +
                          $"{(lon >= 0 ? "동경" : "서경")} {Math.Abs(lon),3:F0}";
        });
        Loaded += OnLoaded;
        Closed += (_, _) => { _statusTimer.Stop(); _bgm.Dispose(); };
    }

    /// <summary>
    /// 게임 첫 화면을 흉내낸 타이틀. 무늬를 깐 바탕 한가운데에 커맨드 창처럼 생긴 메뉴 상자를 둔다.
    /// </summary>
    private FrameworkElement BuildTitleScreen()
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
                Text = "메인메뉴",
                Foreground = MenuTitleFg,
                FontWeight = FontWeights.Bold,
                FontSize = 17,
                HorizontalAlignment = HorizontalAlignment.Center,
            },
        });
        items.Children.Add(TitleMenuItem("NEW GAME", () => StartMap(fresh: true)));
        items.Children.Add(TitleMenuItem("LOAD GAME", () => StartMap(fresh: false)));
        items.Children.Add(TitleMenuItem("MINI GAME", null));
        items.Children.Add(TitleMenuItem("END GAME", Close));

        var box = new Border
        {
            Background = MenuBack,
            BorderBrush = MenuEdge,
            BorderThickness = new Thickness(3),
            Padding = new Thickness(6),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = items,
        };
        return new Grid { Background = TitleBackground(), Children = { box } };
    }

    /// <summary>
    /// 타이틀 바탕. <c>asset/title/title-tile.png</c> 가 있으면 바둑판처럼 깔고,
    /// 없으면 무늬 없이 양피지색만 채운다.
    /// </summary>
    private static Brush TitleBackground()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "asset", "title", "title-tile.png");
        if (!File.Exists(path)) return BarFill;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(path);
            bmp.CacheOption = BitmapCacheOption.OnLoad;   // 파일을 잡고 있지 않게 다 읽고 놓는다
            bmp.EndInit();
            bmp.Freeze();
            return new ImageBrush(bmp)
            {
                TileMode = TileMode.Tile,
                ViewportUnits = BrushMappingMode.Absolute,
                Viewport = new Rect(0, 0, bmp.PixelWidth, bmp.PixelHeight),
                Stretch = Stretch.Fill,
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ShipMap] 타이틀 무늬 로드 실패: {ex.Message}");
            return BarFill;
        }
    }

    /// <summary>타이틀 메뉴 한 줄. <paramref name="run"/> 이 null 이면 흐리게 두고 못 고른다.</summary>
    private static Border TitleMenuItem(string text, Action? run)
    {
        var label = new TextBlock
        {
            Text = text,
            Foreground = run != null ? Brushes.Black : Brushes.Gray,
            FontWeight = FontWeights.Bold,
            FontSize = 17,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        var item = new Border
        {
            Background = CellFill,
            BorderBrush = BarEdge,
            BorderThickness = new Thickness(2),
            Margin = new Thickness(0, 0, 0, 3),
            Padding = new Thickness(28, 3, 28, 3),
            Cursor = run != null ? Cursors.Hand : Cursors.Arrow,
            Child = label,
        };
        if (run == null) return item;

        // 게임처럼 커서가 올라간 줄만 짙게 뒤집는다.
        item.MouseEnter += (_, _) => { item.Background = MenuBack; label.Foreground = MenuTitleFg; };
        item.MouseLeave += (_, _) => { item.Background = CellFill; label.Foreground = Brushes.Black; };
        item.MouseLeftButtonUp += (_, _) => run();
        return item;
    }

    /// <summary>
    /// 타이틀을 걷고 지도를 띄운다. <paramref name="fresh"/> 면 배를 리스본 앞바다에 새로 놓는다.
    /// </summary>
    private void StartMap(bool fresh)
    {
        Content = _mapRoot;

        if (string.IsNullOrEmpty(_gameDir))
        {
            _status.Text = "세이브 파일 경로가 없습니다 — 먼저 세이브를 열어 주세요";
            return;
        }

        if (!_started)
        {
            if (!_host.Start(_gameDir)) { _status.Text = _host.Status; return; }
            _started = true;
            _statusTimer.Start();
        }

        if (fresh) _host.ResetToLisbon();
        _bgm.Play(BgmPlayer.SeaTrack);
    }

    /// <summary>
    /// 게임 커맨드 창을 흉내낸 우클릭 메뉴. 뭍이 가까울 때만 "상륙" 을 낸다.
    /// </summary>
    /// <remarks>
    /// 팝업은 제 창(HWND)을 따로 쓰므로 D3D 자식 창 위에 제대로 뜬다 — airspace 를 안 탄다.
    /// 그래서 지도 위에 얹는 것 중 메뉴만은 WPF 로 둘 수 있다.
    /// </remarks>
    private void ShowCommandMenu(UIElement anchor)
    {
        var menu = new ContextMenu
        {
            Background = MenuBack,
            BorderBrush = MenuEdge,
            BorderThickness = new Thickness(2),
            Padding = new Thickness(6),
            PlacementTarget = anchor,
        };

        menu.Items.Add(new Border
        {
            Background = MenuBack,
            BorderBrush = MenuEdge,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(18, 2, 18, 2),
            Margin = new Thickness(0, 0, 0, 4),
            Child = new TextBlock
            {
                Text = "커맨드",
                Foreground = MenuTitleFg,
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center,
            },
        });

        // 바다에 있으면 상륙, 뭍에 있으면 출항. 갈 데가 없으면 흐리게 보여만 준다.
        if (_host.IsOnLand)
            menu.Items.Add(GameMenuItem("출항", _host.IsNearWater() ? () => _host.Embark() : null));
        else
            menu.Items.Add(GameMenuItem("상륙", _host.IsNearLand() ? () => _host.Land() : null));

        menu.IsOpen = true;
    }

    /// <summary>양피지 바탕의 커맨드 항목 하나. <paramref name="run"/> 이 null 이면 못 고른다.</summary>
    private static MenuItem GameMenuItem(string text, Action? run)
    {
        var item = new MenuItem
        {
            IsEnabled = run != null,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Header = new Border
            {
                Background = CellFill,
                BorderBrush = BarEdge,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(24, 2, 24, 2),
                Child = new TextBlock
                {
                    Text = text,
                    Foreground = run != null ? Brushes.Black : Brushes.Gray,
                    FontWeight = FontWeights.Bold,
                    FontSize = 14,
                    HorizontalAlignment = HorizontalAlignment.Center,
                },
            },
        };
        if (run != null) item.Click += (_, _) => run();
        return item;
    }

    /// <summary>도시에 다가가면 한 번 물어본다. 떠났다 다시 와야 또 묻는다.</summary>
    private void CheckPort()
    {
        if (_asking || _host.IsOnLand) return;

        int city = _host.NearestCity();
        if (city < 0) { _askedCity = -1; return; }      // 도시를 벗어났다
        if (city == _askedCity) return;                 // 이미 물어본 도시다
        _askedCity = city;

        var name = CityName(city);
        // 물음창이 떠 있는 동안 배가 계속 가면 대답할 새가 없다.
        _asking = true;
        _host.Paused = true;
        try
        {
            if (PortDialog.Ask(this, name)) _host.EnterPort(name);
        }
        finally
        {
            _host.Paused = false;
            _asking = false;
        }
    }

    /// <summary>도시 이름. DB 를 못 읽으면 번호로 물러선다.</summary>
    private string CityName(int id)
    {
        if (_cityNames == null)
        {
            _cityNames = [];
            try
            {
                var svc = ContainerLocator.Container.Resolve<CityService>();
                foreach (var c in svc.GetCitiesWithCoordinatesFromDbAsync().Result)
                    _cityNames[c.Id] = c.Name;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ShipMap] 도시 이름 로드 실패: {ex.Message}");
            }
        }
        return _cityNames.TryGetValue(id, out var n) ? n : $"도시 {id}";
    }

    /// <summary>
    /// 게임 폴더를 잡고 타이틀 곡을 튼다. 지도는 아직 띄우지 않는다 —
    /// 메뉴에서 NEW/LOAD 를 골라야 <see cref="StartMap"/> 로 넘어간다.
    /// </summary>
    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        var dir = Path.GetDirectoryName(AppSettings.LastSaveFilePath);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;

        _gameDir = dir;
        _bgm.SetGameDirectory(dir);
        _bgm.Play(BgmPlayer.TitleTrack);
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
