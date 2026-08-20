using System.IO;
using System.Windows.Controls.Primitives;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows;
using CdsHelper.Game.Engine;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Helpers;
using CdsHelper.Support.Local.Models;
using CdsHelper.Support.Local.Settings;
using Prism.Ioc;

namespace CdsHelper.Game.UI.Views;

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

    /// <summary>효과음. 게임 폴더를 알게 되면 연다. 못 열면 소리가 안 날 뿐이다.</summary>
    private SoundBank? _sfx;

    /// <summary>지도 쪽 화면. 타이틀에서 고르면 이것으로 갈아 끼운다.</summary>
    private FrameworkElement _mapRoot = null!;

    /// <summary>
    /// 타이틀과 지도를 갈아 끼우는 자리. 제목 줄을 우리가 그리게 되면서
    /// <see cref="ContentControl.Content"/> 는 제목 줄까지 담게 되었으므로,
    /// 화면만 바꿔 끼울 칸을 따로 두었다.
    /// </summary>
    private readonly ContentControl _screen = new()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        VerticalContentAlignment = VerticalAlignment.Stretch,
    };

    /// <summary>타이틀 쪽 화면. 키를 이 화면에서만 받으려고 들고 있는다.</summary>
    private FrameworkElement? _titleRoot;

    /// <summary>지도 위의 까만 조작 줄. 개발 창에서 끄고 켠다.</summary>
    private Border? _toolBar;

    /// <summary>게임 폴더. WORLD.CDS 도 bgm 도 여기서 읽는다.</summary>
    private string _gameDir = "";

    /// <summary>지도를 한 번 띄웠는지. <see cref="ShipMapHost.Start"/> 는 한 번만 부른다.</summary>
    private bool _started;

    /// <summary>방금 물어본 도시. 떠났다 다시 와야 다시 묻는다.</summary>
    private int _askedCity = -1;

    /// <summary>다이얼로그가 떠 있는 동안 또 묻지 않게.</summary>
    private bool _asking;

    /// <summary>주인공 — 소지금과 가진 배. 조선소에서 배를 사면 여기서 돈이 빠진다.</summary>
    private readonly Player _player = new();

    /// <summary>지도 위에 겹쳐 둔 투명한 입력 판. 커서 자리를 이것 기준으로 잰다.</summary>
    private Border _input = null!;

    /// <summary>도시 그림(CITYCG.CDS). 20MB 라 입항을 처음 할 때에야 연다.</summary>
    private CityPictures? _cityPics;

    /// <summary>건물 표(CDS_95.EXE). 건물 자리·이름·가르치는 기능이 여기서 온다.</summary>
    private CityBuildingTable? _buildings;

    /// <summary>책 표(CDS_95.EXE). 도서관 서가를 채운다.</summary>
    private BookTable? _bookTable;

    /// <summary>힌트 번호 -> 이름. DB 에서 한 번만 불러 둔다.</summary>
    private Dictionary<int, string>? _hintNames;

    /// <summary>한 번 열어 봤는지. 파일이 없으면 입항할 때마다 다시 찾지 않는다.</summary>
    private bool _cityPicsTried;

    // 상단 띠의 칸들. 글자는 게임 비트맵 글꼴로 찍는다(<see cref="GameUi.GameLabel"/>) —
    // 윈도 글꼴은 같은 자리에서 획이 굵고 커서 게임 화면과 결이 안 맞는다.

    /// <summary>게임 상단 바의 날짜 칸. 조합에서 기술을 배우면 달이 넘어간다.</summary>
    private readonly GameUi.GameLabel _date = new();

    /// <summary>게임 상단 바의 소지금·함선 칸.</summary>
    private readonly GameUi.GameLabel _purse = new();

    /// <summary>게임 상단 바의 명성 칸. 후원자를 만날 수 있는지가 이 값으로 갈린다.</summary>
    private readonly GameUi.GameLabel _fame = new();

    /// <summary>게임 상단 바의 위경도 칸.</summary>
    private readonly GameUi.GameLabel _coord = new();

    /// <summary>게임 상단 바의 도시명 칸. 바다에서는 빈 채로 둔다.</summary>
    private readonly GameUi.GameLabel _cityLabel = new();

    /// <summary>지도 위에 겹쳐 띄우는 좌표 상자의 글.</summary>
    private readonly TextBlock _overlayText = new()
    {
        Foreground = Brushes.White,
        FontFamily = new FontFamily("Consolas"),
        FontSize = 12,
        LineHeight = 16,
        LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
    };

    /// <summary>
    /// 좌표 상자. <see cref="Popup"/> 은 제 창(HWND)을 쓰므로 D3D 자식 창 위에 제대로 뜬다 —
    /// 커맨드 메뉴와 같은 수를 쓴 것이다. 보통 WPF 요소로는 airspace 에 막혀 얹을 수 없다.
    /// </summary>
    private Popup _overlay = null!;

    /// <summary>좌표 상자를 켜 두었는지. 실제로 뜨는지는 <see cref="SyncOverlay"/> 가 정한다.</summary>
    private bool _overlayWanted = AppSettings.ShowCoordOverlay;

    // 게임 화면 위쪽 띠에서 뽑은 색. 누런 양피지 바탕에 어두운 테두리다.
    private static readonly Brush BarFill = new SolidColorBrush(Color.FromRgb(0xC8, 0xBF, 0xA0));
    private static readonly Brush CellFill = new SolidColorBrush(Color.FromRgb(0xD2, 0xCA, 0xAD));
    private static readonly Brush BarEdge = new SolidColorBrush(Color.FromRgb(0x4A, 0x40, 0x30));

    // 게임 커맨드 창에서 뽑은 색. 짙은 밤색 바탕에 밝은 테를 두르고, 항목만 양피지다.
    private static readonly Brush MenuBack = new SolidColorBrush(Color.FromRgb(0x4A, 0x2A, 0x22));
    private static readonly Brush MenuEdge = new SolidColorBrush(Color.FromRgb(0xC8, 0xB4, 0x90));
    private static readonly Brush MenuTitleFg = new SolidColorBrush(Color.FromRgb(0xEC, 0xDF, 0xC0));

    /// <summary>
    /// 게임 띠 안에 놓는 칸 하나. 테에 구슬 무늬가 있고 속이 반짝이는 밝은 상자다
    /// (<see cref="FrameArt.DrawCell"/>).
    /// </summary>
    private static FrameworkElement GameCell(UIElement content)
    {
        var inner = new Border
        {
            // 게임 띠는 꽤 얇다. 칸이 두꺼우면 액자가 그만큼 벌어져 비율이 어긋난다.
            Padding = new Thickness(4, 0, 4, 0),
            Child = content,
        };
        // 칸끼리는 붙여 놓는다 — 게임 띠도 칸 사이가 벌어져 있지 않고 테끼리 맞닿는다.
        var cell = GameUi.CellFrame(inner);
        return cell;
    }

    /// <summary>
    /// 도시정보 창에서 켜고 끄는 칸. 켠 상태를 따로 들고 있지 않고 칸의
    /// <see cref="UIElement.Visibility"/> 를 그대로 본다 — 둘로 나누면 어긋난다.
    /// </summary>
    private readonly Dictionary<string, FrameworkElement> _infoCells = [];

    /// <summary>도시정보 창의 줄 이름을 달아 띠에 놓는 칸.</summary>
    private FrameworkElement InfoCell(string name, UIElement content, bool on)
    {
        var cell = GameCell(content);
        cell.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        _infoCells[name] = cell;
        return cell;
    }

    public ShipMapWindow()
    {
        Title = "대항해시대3";
        Width = 1200;
        Height = 800;
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

        // 도시 화면이 떠 있는 동안 잠그는 줄. 셋 다 바다에서만 뜻이 있는 조작이다.
        _seaControls = [steer, follow, recenter, toLisbon];

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
        // 지도 위에서도 보통 화살표를 쓴다 — 십자는 조준하는 것처럼 보여 게임 화면과 안 맞는다.
        var input = new Border { Background = Brushes.Transparent, Cursor = Cursors.Arrow };
        _input = input;
        var surface = new Grid();
        surface.Children.Add(_host);
        surface.Children.Add(input);

        // 좌표 상자는 지도 왼쪽 위에 겹쳐 둔다. 히트테스트를 꺼서 그 밑으로 배를 몰 수 있게 한다.
        _overlay = new Popup
        {
            PlacementTarget = input,
            Placement = PlacementMode.Relative,
            HorizontalOffset = 10,
            VerticalOffset = 10,
            AllowsTransparency = true,
            StaysOpen = true,
            IsHitTestVisible = false,
            Child = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xB4, 0x10, 0x10, 0x10)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0xC8, 0xC8, 0xB4, 0x90)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 6, 10, 6),
                IsHitTestVisible = false,
                Child = _overlayText,
            },
        };
        surface.Children.Add(_overlay);   // 자리만 잡아 둔다 — 실제로는 제 창에 뜬다

        // 게임 상단 띠. 어느 칸을 띄울지는 도시정보 창에서 켜고 끈다(띠를 오른쪽 단추로 누른다).
        // 이동 모드(정박·해상 이동) 칸은 뺐다 — 게임 띠에 없는 칸이다.
        var gameCells = new StackPanel { Orientation = Orientation.Horizontal };
        gameCells.Children.Add(InfoCell(CityInfoMenu.Date, _date, on: true));
        gameCells.Children.Add(InfoCell(CityInfoMenu.Coord, _coord, on: true));
        gameCells.Children.Add(InfoCell(CityInfoMenu.Gold, _purse, on: true));
        gameCells.Children.Add(InfoCell(CityInfoMenu.Fame, _fame, on: true));
        gameCells.Children.Add(InfoCell(CityInfoMenu.City, _cityLabel, on: false));

        // 게임 띠 끝에 설정 칸. 누르면 배경음악을 켜고 끄는 창이 뜬다.
        var settings = GameCell(new GameUi.GameLabel { Text = "설정" });
        settings.Cursor = Cursors.Hand;
        settings.MouseLeftButtonUp += (_, _) => SettingsDialog.Show(this, _bgm);
        gameCells.Children.Add(settings);

        // 개발 칸. 소지금과 명성을 손으로 넣는 창이 뜬다 — 놀이에는 없는 자리다.
        var dev = GameCell(new GameUi.GameLabel { Text = "개발" });
        dev.Cursor = Cursors.Hand;
        dev.MouseLeftButtonUp += (_, _) => DevDialog.Show(this, _player, new DevDialog.Options
        {
            CoordsOn = () => _overlayWanted,
            SetCoords = on =>
            {
                _overlayWanted = on;
                AppSettings.ShowCoordOverlay = on;   // 다음에 켤 때도 그대로
                SyncOverlay();
            },
            ToolBarOn = () => AppSettings.ShowToolBar,
            SetToolBar = on =>
            {
                AppSettings.ShowToolBar = on;
                if (_toolBar != null)
                    _toolBar.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            },
            GameDirectory = _gameDir,
        });
        gameCells.Children.Add(dev);

        // 게임처럼 액자를 깔고 그 위에 칸들을 얹는다(asset/ui/misc-00.png).
        // 그림이 없으면 예전처럼 민색 띠로 물러선다.
        FrameworkElement gameBar = (FrameworkElement?)GameUi.BarFrame(gameCells)
            ?? new Border
            {
                Background = BarFill,
                BorderBrush = BarEdge,
                BorderThickness = new Thickness(0, 0, 0, 2),
                Child = gameCells,
            };

        // 띠를 오른쪽 단추로 누르면 도시정보 창이 뜬다 — 게임처럼 도시 안에서만 낸다.
        gameBar.MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            ShowCityInfoMenu(gameBar, e.GetPosition(gameBar));
        };

        var root = new DockPanel();
        DockPanel.SetDock(gameBar, Dock.Top);
        root.Children.Add(gameBar);
        // 지도 위의 까만 조작 줄. 놀이에는 없는 것이라 개발 창에서 끄고 켤 수 있다.
        _toolBar = new Border
        {
            Child = bar,
            Height = 30,
            Visibility = AppSettings.ShowToolBar ? Visibility.Visible : Visibility.Collapsed,
        };
        DockPanel.SetDock(_toolBar, Dock.Top);
        root.Children.Add(_toolBar);

        // 게임은 지도 아래에도 같은 띠를 하나 둔다. 안은 비어 있다.
        var footer = TitleBarStrip(null);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(surface);
        _mapRoot = root;

        // 타이틀을 지도 위에 겹쳐 둘 수는 없다 — airspace 규칙상 D3D 자식 창이 WPF 를 덮는다.
        // 그래서 겹치지 않고 통째로 갈아 끼운다. 타이틀이 떠 있는 동안은 자식 창 자체가 없다.
        _titleRoot = BuildTitleScreen();
        _screen.Content = _titleRoot;

        // 윈도 제목 줄 대신 크롬처럼 우리가 그린 줄을 얹는다.
        // 왼쪽 위 햄버거에는 앱이 적어 둔 것을 들여다보는 줄을 단다.
        var shell = new DockPanel { LastChildFill = true };
        var titleBar = ChromeTitleBar.Attach(this,
            ("게임데이터", () => GameDataDialog.Show(this)),
            ("다이얼로그", () => GameDialog.Show(this, "출항합니다.")));
        DockPanel.SetDock(titleBar, Dock.Top);
        shell.Children.Add(titleBar);
        shell.Children.Add(_screen);
        Content = shell;

        PreviewKeyDown += OnTitleKey;   // 타이틀에서만 먹는다(그 안에서 화면을 본다)
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
            // 도시 안에서는 함대 커맨드 창을 안 낸다 — 도시 화면이 제 커맨드 창을 따로 낸다.
            if (_host.SeaBlocked) return;
            ShowCommandMenu(input);
        };
        input.MouseLeftButtonDown += (_, e) =>
        {
            // 도시 화면이 떠 있으면 지도는 남색 막 아래다 — 닻도 배 놓기도 받지 않는다.
            if (_host.SeaBlocked) return;
            // Ctrl 을 누른 채 찍으면 그 자리에 배를 놓는다. 시작 자리를 손으로 잡는 길이다.
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                _host.PlaceShipAt(e.GetPosition(input));
                return;
            }
            // 그냥 찍으면 닻을 내리고 그 자리에 선다. 한 번 더 찍으면 올리고 다시 간다.
            _host.ToggleAnchor();
            // 내릴 때도 올릴 때도 같은 소리가 난다.
            if (_bgm.Enabled) _sfx?.Play(SoundBank.AnchorPart);
        };
        input.MouseMove += (_, e) => { var p = e.GetPosition(input); _host.SetMouse(p, true); _host.Drag(p); };
        input.MouseLeave += (_, _) => _host.SetMouse(default, false);

        _statusTimer = new DispatcherTimerLite(TimeSpan.FromMilliseconds(100), () =>
        {
            SyncMouse();
            _status.Text = _host.Status;
            CheckPort();
            var (lat, lon) = _host.ShipLatLon;
            // 게임과 같은 말투로 적는다 — 북위/남위, 동경/서경에 정수 도.
            _coord.Text = $"{(lat >= 0 ? "북위" : "남위")} {Math.Abs(lat),3:F0}    " +
                          $"{(lon >= 0 ? "동경" : "서경")} {Math.Abs(lon),3:F0}";
            _purse.Text = $"{_player.Gold}닢 · 함선 {_player.Ships.Count}/{Player.MaxShips}";
            _fame.Text = $"명성 {_player.Fame}";
            // 가진 배 중 가장 큰 것이 기함이다 — 그 벌의 그림으로 그린다(게임이 안 떠 있을 때).
            ShipSprites.Skin = _player.Ships.Max(s => s.Skin);
            // 게임 상단 띠와 같은 말투로 적는다.
            _date.Text = $"{_player.Date.Year}년 {_player.Date.Month}월{_player.Date.Day}일";
            _cityLabel.Text = _player.CityName.Length > 0 ? _player.CityName : "—";
            if (_overlay.IsOpen) _overlayText.Text = BuildOverlayText(lat, lon);
        });
        Loaded += OnLoaded;

        // 창을 옮기면 그 위에 얹힌 도시 그림·커맨드 창도 같이 옮긴다 — 게임에서는 지도 안에
        // 그려진 것이라 따로 남을 수가 없다.
        GameUi.CarryOwnedWindows(this);

        // 창이 물러나거나 접히면 좌표 상자도 같이 감춘다 — 제 창이라 그냥 두면 남의 앱 위에 뜬다.
        Activated += (_, _) => SyncOverlay();
        Deactivated += (_, _) => SyncOverlay();
        StateChanged += (_, _) => SyncOverlay();
        Closed += (_, _) => { _overlay.IsOpen = false; _statusTimer.Stop(); _bgm.Dispose(); _cityPics = null; };
    }

    /// <summary>
    /// 지도가 화면에서 차지한 자리(WPF 단위). 도시 화면이 이 자리를 통째로 덮는다 —
    /// 게임도 도시에 들어가면 지도 영역이 남색으로 덮인다.
    /// </summary>
    private Rect MapAreaOnScreen()
    {
        var source = PresentationSource.FromVisual(this);
        if (source == null || _input.ActualWidth <= 0 || _input.ActualHeight <= 0) return default;

        // PointToScreen 은 실픽셀을 내므로 WPF 단위로 되돌린다(고해상도 화면에서 어긋난다).
        var device = _input.PointToScreen(new Point(0, 0));
        var topLeft = source.CompositionTarget.TransformFromDevice.Transform(device);
        return new Rect(topLeft.X, topLeft.Y, _input.ActualWidth, _input.ActualHeight);
    }

    /// <summary>요소 안의 한 자리를 화면 좌표(WPF 단위)로 옮긴다.</summary>
    private Point ToScreen(FrameworkElement element, Point at)
    {
        var device = element.PointToScreen(at);
        var source = PresentationSource.FromVisual(this);
        return source == null
            ? device
            : source.CompositionTarget.TransformFromDevice.Transform(device);
    }

    /// <summary>도시 화면이 떠 있는 동안 잠그는 조작 줄 단추들.</summary>
    private Control[] _seaControls = [];

    /// <summary>
    /// 도시 화면을 여닫는다. 들어가 있는 동안은 바다 명령이 전부 막힌다 —
    /// 막는 일 자체는 <see cref="ShipMapHost.SeaBlocked"/> 가 하고, 여기서는 그 김에
    /// 조작 줄 단추도 흐려 둔다. 눌러도 안 먹는 단추가 멀쩡해 보이면 헷갈린다.
    /// </summary>
    private void SetInCity(bool on)
    {
        _host.InCity = on;
        foreach (var c in _seaControls) c.IsEnabled = !on;
    }

    /// <summary>도시정보 창. 상단 띠 밑에 붙여 띄운다.</summary>
    private MenuWindow? _infoMenu;

    /// <summary>
    /// 상단 띠에 무엇을 띄울지 고르는 창을 낸다. 게임은 도시 안에서만 이 창을 내므로
    /// 바다에서는 아무 일도 안 한다.
    /// </summary>
    private void ShowCityInfoMenu(FrameworkElement bar, Point at)
    {
        if (!_host.InCity) return;
        if (_infoMenu != null) { _infoMenu.Activate(); return; }

        _infoMenu = MenuWindow.ShowAt(this, BuildCityInfo(),
                                      ToScreen(bar, new Point(at.X, bar.ActualHeight)));
        _infoMenu.Closed += (_, _) => _infoMenu = null;
    }

    /// <summary>
    /// 도시정보 창의 지금 모습. 줄을 하나 뒤집을 때마다 다시 지어 갈아 끼운다 —
    /// <c>:ON</c>·<c>:OFF</c> 글자는 게임 글꼴로 찍은 그림이라 고쳐 쓸 수가 없다.
    /// </summary>
    private Border BuildCityInfo() => CityInfoMenu.Build(
        name => _infoCells.TryGetValue(name, out var cell)
            ? cell.Visibility == Visibility.Visible
            : null,
        name =>
        {
            var cell = _infoCells[name];
            cell.Visibility = cell.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
            _infoMenu?.SetContent(BuildCityInfo());
        },
        () => _infoMenu?.Close());

    /// <summary>
    /// 커서가 지금 지도 위 어디에 있는지 다시 잰다.
    /// </summary>
    /// <remarks>
    /// 예전에는 <c>MouseMove</c> 로만 알려 줬다. 그러다 보니 창(입항 물음·도시 그림)이 떴다
    /// 닫히거나 커서가 잠깐 지도를 벗어나면 <c>MouseLeave</c> 가 "밖" 으로 표시해 놓고,
    /// 커서를 <b>움직이기 전까지</b> 그대로였다 — 배가 뱃머리를 못 잡고 그 자리에 서 있었다.
    /// 입항 직후에는 뱃머리가 0 이라 특히 티가 났다. 그래서 틱마다 직접 재 둔다.
    /// </remarks>
    private void SyncMouse()
    {
        if (!_started || !ReferenceEquals(_screen.Content, _mapRoot) || _input.ActualWidth <= 0) return;

        var p = Mouse.GetPosition(_input);
        bool inside = p.X >= 0 && p.Y >= 0 && p.X < _input.ActualWidth && p.Y < _input.ActualHeight;
        _host.SetMouse(p, inside);
    }

    /// <summary>좌표 상자를 띄울 때인지 다시 따진다 — 켜 두었고, 지도가 떠 있고, 이 창이 앞일 때만.</summary>
    private void SyncOverlay() =>
        _overlay.IsOpen = _overlayWanted && _started && IsActive
                          && WindowState != WindowState.Minimized
                          && ReferenceEquals(_screen.Content, _mapRoot);

    /// <summary>
    /// 좌표 상자에 적을 글. 배가 선 칸을 WORLD.CDS 파일 안의 자리까지 풀어서 보여 준다.
    /// </summary>
    private string BuildOverlayText(double lat, double lon)
    {
        var c = _host.ShipCell;
        if (c == null) return "배가 아직 지도에 없습니다";

        var v = c.Value;
        var lines = new List<string>
        {
            $"칸        {v.X,7:F1}, {v.Y,6:F1}   (칸 {v.CellX}, {v.CellY})",
            $"WORLD.CDS 행 {v.Row,4} · 열 {v.Col,4} · 0x{v.Offset:X5}",
            $"칸 값     지형 {v.Terrain,3} · 속성 {v.Attr,3} · 타일 {v.Tile,5} · 육지 {v.LandRatio * 100,3:F0}%",
            $"위경도    {(lat >= 0 ? "북위" : "남위")} {Math.Abs(lat):F2} · {(lon >= 0 ? "동경" : "서경")} {Math.Abs(lon):F2}",
        };

        // 40칸까지만 본다. 더 넓히면 도시마다 항구 칸을 찾느라 100ms 틱이 무거워진다.
        var (city, cells) = _host.NearestDock(40);
        if (city >= 0) lines.Add($"가까운 항구 [{CityName(city)}] {cells:F1}칸");

        var m = _host.MouseCell;
        if (m != null)
            lines.Add($"커서      {m.Value.X,7:F1}, {m.Value.Y,6:F1}   0x{m.Value.Offset:X5}");

        return string.Join("\n", lines);
    }

    /// <summary>
    /// 게임 첫 화면을 흉내낸 타이틀. 무늬를 깐 바탕 한가운데에 커맨드 창처럼 생긴 메뉴 상자를 둔다.
    /// </summary>
    private FrameworkElement BuildTitleScreen()
    {
        var items = new StackPanel();

        // 제목 상자는 게임 원본 조각(MISC.CDS)으로 짓는다. 못 읽으면 민색 상자로 물러선다.
        // 한 번 넣어 두면 제목 줄이 있는 창들이 다 같이 쓴다(GameUi.TitleBar).
        LoadSprites();

        var titleBar = GameUi.TitleFrame(GameUi.Sprites, "메인메뉴");
        if (titleBar != null)
        {
            titleBar.Margin = new Thickness(0, 0, 0, 6);
            items.Children.Add(titleBar);
        }
        else
        {
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
                    FontSize = 13,
                    HorizontalAlignment = HorizontalAlignment.Center,
                },
            });
        }
        _titleItems.Clear();
        items.Children.Add(TitleMenuItem("NEW GAME", () => StartMap(fresh: true)));
        // 게임도 로드 전에 한 번 묻는다 — 제목은 "게임 로드".
        items.Children.Add(TitleMenuItem("LOAD GAME", () =>
        {
            if (ConfirmDialog.Ask(this, "마지막에 저장한 데이터를 로드합니다", "게임 로드"))
                StartMap(fresh: false);
        }));
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
        var middle = new Grid { Background = TitleBackground(), Children = { box } };

        // 게임 타이틀에도 위아래로 액자 띠가 있다. 위 띠에는 날짜 칸 하나만 있고 나머지는 비었다.
        var screen = new DockPanel();
        var top = TitleBarStrip($"{_player.Date.Year}년 {_player.Date.Month}월 {_player.Date.Day}일");
        DockPanel.SetDock(top, Dock.Top);
        screen.Children.Add(top);

        var bottom = TitleBarStrip(null);
        DockPanel.SetDock(bottom, Dock.Bottom);
        screen.Children.Add(bottom);

        screen.Children.Add(middle);

        FocusTitle(0);   // 게임처럼 첫 줄에 초점을 두고 시작한다
        return screen;
    }

    /// <summary>
    /// 타이틀 화면 위아래에 두는 액자 띠. <paramref name="text"/> 를 주면 왼쪽에 칸 하나를 둔다.
    /// </summary>
    private static FrameworkElement TitleBarStrip(string? text)
    {
        var inside = new StackPanel { Orientation = Orientation.Horizontal };
        if (text != null)
        {
            var label = new TextBlock
            {
                Text = text,
                Foreground = Brushes.Black,
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center,
            };
            inside.Children.Add(GameCell(label));
        }
        else
        {
            // 빈 띠도 높이는 있어야 한다 — 글자 한 줄만큼 자리를 잡아 둔다.
            inside.Children.Add(new Border { Height = 24 });
        }

        FrameworkElement? framed = GameUi.BarFrame(inside);
        return framed ?? new Border
        {
            Background = BarFill,
            BorderBrush = BarEdge,
            BorderThickness = new Thickness(0, 0, 0, 2),
            Child = inside,
        };
    }

    /// <summary>
    /// 타이틀 바탕. <c>asset/title/title-tile.png</c> 가 있으면 바둑판처럼 깔고,
    /// 없으면 무늬 없이 양피지색만 채운다.
    /// </summary>
    /// <summary>바탕 무늬를 얼마로 줄여 깔지. 1 이면 원본 크기다.</summary>
    private const double TilePack = 0.72;

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
                // 무늬 원본은 큰 창에서 찍은 것이라 그대로 깔면 성기다. 게임 화면과 견주어
                // 촘촘한 정도를 맞춘다.
                Viewport = new Rect(0, 0, bmp.PixelWidth * TilePack, bmp.PixelHeight * TilePack),
                Stretch = Stretch.Fill,
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ShipMap] 타이틀 무늬 로드 실패: {ex.Message}");
            return BarFill;
        }
    }

    /// <summary>타이틀 메뉴의 고를 수 있는 줄들과, 지금 초점이 가 있는 자리.</summary>
    private readonly List<(Border Item, SolidColorBrush Inner, Action Run)> _titleItems = [];
    private int _titleIndex = -1;

    /// <summary>타이틀 메뉴 줄의 최소 폭. 글자 좌우 여백까지 넣은 게임 비율이다.</summary>
    private const double TitleItemMinWidth = 124;

    /// <summary>초점 표시가 오가는 두 색. 게임도 이 둘을 번갈아 보인다.</summary>
    private static readonly Color FocusLight = Color.FromRgb(0xEC, 0xE4, 0xD2);
    private static readonly Color FocusDark = Color.FromRgb(0x14, 0x0C, 0x0A);

    /// <summary>초점이 깜빡이는 참. 0.5초마다 색이 바뀐다.</summary>
    private static readonly TimeSpan FocusBlink = TimeSpan.FromSeconds(0.5);

    /// <summary>
    /// 타이틀 메뉴 한 줄. <paramref name="run"/> 이 null 이면 흐리게 두고 못 고른다.
    /// </summary>
    /// <remarks>
    /// 초점은 안쪽 테 한 줄로 낸다 — 게임은 그 테를 밝은 색과 검은색으로 0.5초마다 갈아
    /// 깜빡이게 해서 지금 고른 줄을 알린다. 색을 서서히 섞지 않고 딱딱 바꾸는 것이 요령이라
    /// <see cref="DiscreteColorKeyFrame"/> 을 쓴다(<c>ColorAnimation</c> 은 스며들듯 바뀐다).
    /// </remarks>
    private Border TitleMenuItem(string text, Action? run)
    {
        // 안쪽 테 — 평소에는 안 보이고, 초점이 오면 깜빡인다.
        var innerBrush = new SolidColorBrush(Colors.Transparent);

        // 게임 원본 베이지 버튼 띠. 조각을 못 읽었을 때만 민색 상자로 물러선다.
        var band = GameUi.BandFrame(GameUi.Sprites, BandStyle.Button, text,
                                    run != null ? GameFont.ButtonColor : (byte)21,
                                    shadow: false, 1, null);
        Border item;
        if (band?.Child is Grid grid)
        {
            // 띠 위에 테만 겹친다(바탕 없음). 나중에 넣은 것이 위에 그려진다.
            var inner = new Border
            {
                BorderBrush = innerBrush,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(2),
            };
            Grid.SetColumnSpan(inner, 3);
            grid.Children.Add(inner);

            item = band;
            item.Margin = new Thickness(0, 0, 0, 2);
            item.Cursor = run != null ? Cursors.Hand : Cursors.Arrow;

            // 게임은 글자 좌우로 넉넉히 비운다 — 글자에 딱 붙이면 띠가 쪼그라들어 보인다.
            // 가장 긴 "LOAD GAME"(72점)의 1.7배쯤이 게임 비율이다.
            item.MinWidth = TitleItemMinWidth;
        }
        else
        {
            item = new Border
            {
                Background = CellFill,
                BorderBrush = BarEdge,
                BorderThickness = new Thickness(2),
                Margin = new Thickness(0, 0, 0, 2),
                Padding = new Thickness(1),
                Cursor = run != null ? Cursors.Hand : Cursors.Arrow,
                Child = new Border
                {
                    BorderBrush = innerBrush,
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(20, 1, 20, 1),
                    Child = new TextBlock
                    {
                        Text = text,
                        Foreground = run != null ? Brushes.Black : Brushes.Gray,
                        FontWeight = FontWeights.Bold,
                        FontSize = 13,
                        HorizontalAlignment = HorizontalAlignment.Center,
                    },
                },
            };
        }
        if (run == null) return item;

        int index = _titleItems.Count;
        _titleItems.Add((item, innerBrush, run));

        // 커서가 올라가면 그 줄로 초점이 옮겨 간다 — 게임도 고른 줄이 하나뿐이다.
        item.MouseEnter += (_, _) => FocusTitle(index);
        item.MouseLeftButtonUp += (_, _) => run();
        return item;
    }

    /// <summary>그 줄로 초점을 옮긴다. 옛 줄의 깜빡임은 멎는다.</summary>
    private void FocusTitle(int index)
    {
        if (index < 0 || index >= _titleItems.Count || index == _titleIndex) return;

        if (_titleIndex >= 0 && _titleIndex < _titleItems.Count)
        {
            var (_, old, _) = _titleItems[_titleIndex];
            old.BeginAnimation(SolidColorBrush.ColorProperty, null);
            old.Color = Colors.Transparent;
        }

        _titleIndex = index;
        var (_, brush, _) = _titleItems[index];

        var blink = new ColorAnimationUsingKeyFrames
        {
            Duration = new Duration(FocusBlink + FocusBlink),
            RepeatBehavior = RepeatBehavior.Forever,
        };
        blink.KeyFrames.Add(new DiscreteColorKeyFrame(FocusLight, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        blink.KeyFrames.Add(new DiscreteColorKeyFrame(FocusDark, KeyTime.FromTimeSpan(FocusBlink)));
        brush.BeginAnimation(SolidColorBrush.ColorProperty, blink);
    }

    /// <summary>타이틀에서 위아래로 옮기고 엔터로 고른다. 지도가 뜨면 아무것도 안 한다.</summary>
    private void OnTitleKey(object sender, KeyEventArgs e)
    {
        if (_titleItems.Count == 0 || !ReferenceEquals(_screen.Content, _titleRoot)) return;

        switch (e.Key)
        {
            case Key.Up:
                FocusTitle((_titleIndex - 1 + _titleItems.Count) % _titleItems.Count);
                e.Handled = true;
                break;
            case Key.Down:
                FocusTitle((_titleIndex + 1) % _titleItems.Count);
                e.Handled = true;
                break;
            case Key.Enter or Key.Space:
                if (_titleIndex >= 0) _titleItems[_titleIndex].Run();
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// 타이틀을 걷고 지도를 띄운다. <paramref name="fresh"/> 면 배를 리스본 앞바다에 새로 놓고,
    /// 아니면 적어 둔 기록(<see cref="GameSave"/>)을 되돌린다.
    /// </summary>
    /// <summary>
    /// 놀이를 그만두고 첫 화면으로 돌아간다. 자택의 "게임 종료" 가 부른다.
    /// </summary>
    /// <remarks>
    /// 창을 닫지는 않는다 — 게임도 첫 화면으로 되돌아갈 뿐이다. 그래서 D3D 자식 창도
    /// 멈추지 않고 그대로 둔다(<c>Content</c> 에서 빠지면 안 보인다). 다시 시작할 때
    /// <see cref="StartMap"/> 이 <c>_started</c> 를 보고 켜는 일을 건너뛴다.
    ///
    /// 도시 그림·명령 창은 이 창이 거느린 것들이라 모두 닫는다. 닫는 동안 목록이 바뀌므로
    /// 먼저 베껴 놓고 돈다.
    /// </remarks>
    public void ReturnToTitle()
    {
        if (_titleRoot == null || ReferenceEquals(_screen.Content, _titleRoot)) return;

        foreach (var child in OwnedWindows.OfType<Window>().ToList()) child.Close();

        _overlay.IsOpen = false;
        _statusTimer.Stop();
        _askedCity = -1;                 // 다시 들어가면 도시를 새로 묻게

        _screen.Content = _titleRoot;
        _bgm.Play(BgmPlayer.TitleTrack);
        _status.Text = "";
    }

    private void StartMap(bool fresh)
    {
        // 불러올 것이 없으면 타이틀에 그대로 머문다 — 화면부터 갈아 끼우면 되돌리기 번거롭다.
        GameSave.Data? saved = null;
        if (!fresh)
        {
            saved = GameSave.Load();
            if (saved == null)
            {
                NoticeDialog.Show(this, "적어 둔 기록이 없다.");
                return;
            }
        }

        _screen.Content = _mapRoot;

        if (string.IsNullOrEmpty(_gameDir))
        {
            _status.Text = "세이브 파일 경로가 없습니다 — 먼저 세이브를 열어 주세요";
            return;
        }

        // 타이틀을 지을 때 게임 폴더를 몰랐을 수 있다. 여기서 한 번 더 챙긴다.
        LoadSprites();

        _sfx ??= SoundBank.Shared(_gameDir);

        if (!_started)
        {
            if (!_host.Start(_gameDir)) { _status.Text = _host.Status; return; }
            _started = true;
            _statusTimer.Start();
        }

        if (fresh)
        {
            _host.ResetToLisbon();
        }
        else if (saved != null)
        {
            _player.Restore(saved.Gold, saved.Date, saved.CityId, saved.CityName,
                            saved.Skills, saved.Hints, saved.Mates, saved.Met, saved.Items);
            // 적어 둔 도시 앞바다에 배를 놓는다. 그 도시는 이미 들렀으니 곧바로 다시 묻지 않는다.
            if (saved.CityId >= 0 && _host.PlaceAtCity(saved.CityId)) _askedCity = saved.CityId;
            _status.Text = saved.CityId >= 0
                ? $"[{saved.CityName}] 에서 이어 간다 — {saved.Date:yyyy년 M월 d일}"
                : $"바다에서 이어 간다 — {saved.Date:yyyy년 M월 d일}";
        }

        _bgm.Play(BgmPlayer.SeaTrack);
        SyncOverlay();

        // 적어 둔 자리가 도시면 도시 화면부터 연다. 바다에서 적었으면(CityId 가 -1) 그대로 둔다 —
        // 어디에서 적었는지는 그 값 하나로 갈린다.
        //
        // 지도가 자리를 잡은 뒤에 열어야 도시 그림이 지도 한가운데에 놓인다
        // (MapAreaOnScreen 이 아직 0 이면 엉뚱한 데 뜬다).
        if (!fresh && saved is { CityId: >= 0 })
        {
            int city = saved.CityId;
            string name = saved.CityName.Length > 0 ? saved.CityName : CityName(city);
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (ShowCityPicture(city, name)) _host.Paused = true;
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    /// <summary>
    /// 게임 커맨드 창을 흉내낸 우클릭 메뉴. 떠 있는 동안 <b>게임이 멈춘다</b> —
    /// 배도 시간도 그 자리에 선다(닻을 내리는 것과는 다르다. 닻은 그대로 두고 멈추기만 한다).
    /// </summary>
    /// <remarks>
    /// 팝업은 제 창(HWND)을 따로 쓰므로 D3D 자식 창 위에 제대로 뜬다 — airspace 를 안 탄다.
    /// 그래서 지도 위에 얹는 것 중 메뉴만은 WPF 로 둘 수 있다. 도시 창의 명령 창과 같은
    /// <see cref="GameUi.CommandBox"/> 를 쓴다 — 예전에 쓰던 <c>ContextMenu</c> 는 왼쪽에
    /// 그림 자리(빈 흰 칸)를 남기고 줄 너비도 제각각이라 게임 것과 달랐다.
    /// </remarks>
    private void ShowCommandMenu(UIElement anchor)
    {
        if (_host.SeaBlocked) return;

        var popup = new Popup
        {
            PlacementTarget = anchor,
            Placement = PlacementMode.MousePoint,
            StaysOpen = false,          // 바깥을 누르면 닫힌다
            AllowsTransparency = true,
            Focusable = true,
        };
        void Close() => popup.IsOpen = false;

        // 바다에 있으면 상륙, 뭍에 있으면 출항. 갈 데가 없으면 흐리게 보여만 준다.
        // 게임 커맨드 창에는 없는 줄이지만 이 창에서는 이것으로 뭍을 오간다.
        (string, Action?) ashore = _host.IsOnLand
            ? ("출항", _host.IsNearWater() ? () => { _host.Embark(); Close(); } : null)
            : ("상륙", _host.IsNearLand() ? () => { _host.Land(); Close(); } : null);

        popup.Child = GameUi.CommandBox("커맨드",
            ("정보", null),
            ("편성", null),
            ("대열", null),
            ("항해일지를 본다", null),
            ("기능", () => { Close(); SaveGame(); }),
            ashore,
            ("취소", Close));

        // 메뉴가 떠 있는 동안은 게임을 멈춘다. 닫히면 어떻게 닫혔든 다시 흐른다.
        popup.Closed += (_, _) => _host.Paused = false;
        _host.Paused = true;
        popup.IsOpen = true;
    }

    /// <summary>
    /// 바다에서 적는다. 도시 안에서 적는 것(<c>CityPicDialog</c> 의 기능 창)과 같은 자리에
    /// 쓰는데, 도시에 들어가 있지 않으므로 <see cref="Player.CityId"/> 가 -1 로 남는다 —
    /// 그 값이 곧 "바다에서 적었다" 는 표시다.
    /// </summary>
    private void SaveGame()
    {
        var error = GameSave.Save(_player);
        NoticeDialog.Show(this, error.Length == 0 ? "기록했다!" : $"기록하지 못했다 — {error}");
    }

    /// <summary>도시에 다가가면 한 번 물어본다. 떠났다 다시 와야 또 묻는다.</summary>
    private void CheckPort()
    {
        // 멈춰 있으면(커맨드 창) 아무것도 묻지 않는다 — 멈춘 동안 창이 겹쳐 뜨면 안 된다.
        if (_asking || _host.Paused) return;

        // 배는 항구 칸으로, 말은 도시 칸으로 잰다. 게임도 그렇게 갈라 본다.
        bool byLand = _host.IsOnLand;
        int city = byLand ? _host.NearestTown() : _host.NearestCity();
        if (city < 0) { _askedCity = -1; return; }      // 도시를 벗어났다
        if (city == _askedCity) return;                 // 이미 물어본 도시다
        _askedCity = city;

        var name = CityName(city);
        // 물음창이 떠 있는 동안 배가 계속 가면 대답할 새가 없다.
        _asking = true;
        _host.Paused = true;
        bool inCity = false;
        try
        {
            if (PortDialog.Ask(this, name, byLand))
            {
                _host.EnterPort(name);
                inCity = ShowCityPicture(city, name);
            }
        }
        finally
        {
            // 도시 창이 열렸으면 그 창이 닫힐 때 푼다(그동안 배는 서 있는다).
            if (!inCity)
            {
                _host.Paused = false;
                _asking = false;
            }
        }
    }

    /// <summary>
    /// 입항한 도시의 그림을 지도 한가운데에 띄운다. CITYCG.CDS 가 없거나 그림을 못 풀면
    /// 조용히 넘어간다 — 그림은 덤이고, 입항은 이미 끝났다.
    /// </summary>
    /// <remarks>
    /// 도시에 들어가 있는 동안에는 곡이 바뀌고 지도에 남색 막이 씌워진다. 창은 모달이 아니다 —
    /// 모달이면 함대 창 제목 줄이 죽는다. 그래서 창이 닫힐 때 곡·막·멈춤을 함께 푼다.
    /// </remarks>
    /// <returns>도시 창을 띄웠으면 true.</returns>
    private bool ShowCityPicture(int city, string name)
    {
        if (_cityPics == null)
        {
            if (_cityPicsTried || string.IsNullOrEmpty(_gameDir)) return false;
            _cityPicsTried = true;
            _cityPics = CityPictures.Open(_gameDir);
            if (_cityPics == null)
            {
                System.Diagnostics.Debug.WriteLine($"[ShipMap] 도시 그림 없음: {CityPictures.LastError}");
                return false;
            }
            _buildings = CityBuildingTable.Open(_gameDir);
            if (_buildings == null)
                System.Diagnostics.Debug.WriteLine($"[ShipMap] 건물 표 없음: {CityBuildingTable.LastError}");
        }
        if (_buildings == null) return false;   // 건물 표가 없으면 도시 화면을 열지 않는다

        _bookTable ??= BookTable.Open(_gameDir);   // 도서관 열람에 쓴다. 없으면 그 줄만 흐리다

        // 도는 곡은 문화권마다 다르다 — 세우타 같은 중근동 도시는 딴 곡이다.
        // 문화권은 건물에 들어갈 때 뜨는 타원 사진을 고르는 데도 쓴다(BuildingPhoto).
        string culture = CultureOf(city);
        int track = BgmPlayer.CityTrackFor(culture);

        var dialog = CityPicDialog.Open(this, _cityPics, _buildings, city, name,
                                        _player, _bgm, MapAreaOnScreen(),
                                        _bookTable, HintName, _gameDir, track, culture);
        if (dialog == null) return false;

        _bgm.Play(track);
        SetInCity(true);          // 지도에 남색 막을 씌운다(그림 창과는 따로 논다)
        _player.EnterCity(city, name);
        dialog.Closed += (_, _) =>
        {
            SetInCity(false);
            _bgm.Play(BgmPlayer.SeaTrack);
            _host.Paused = false;
            _asking = false;
            _player.EnterCity(-1);
            _infoMenu?.Close();      // 도시를 나오면 도시정보 창도 같이 걷는다
        };
        return true;
    }

    /// <summary>
    /// 게임 원본 화면 조각과 비트맵 글꼴을 한 번만 읽어 <see cref="GameUi"/> 에 넣는다.
    /// 게임 폴더를 아직 모르면 그냥 넘어간다 — 세이브를 열면 다시 부른다.
    /// </summary>
    private void LoadSprites()
    {
        if (_spritesTried || string.IsNullOrEmpty(_gameDir)) return;
        _spritesTried = true;

        GameUi.Sprites = UiSprites.Open(_gameDir);
        if (GameUi.Sprites == null)
            System.Diagnostics.Debug.WriteLine($"[ShipMap] 화면 조각 없음: {UiSprites.LastError}");

        GameUi.Font = GameFont.Open(_gameDir);
        if (GameUi.Font == null)
            System.Diagnostics.Debug.WriteLine($"[ShipMap] 게임 글꼴 없음: {GameFont.LastError}");
    }

    private bool _spritesTried;

    /// <summary>
    /// 도시 표(번호·이름·문화권). 건물 표의 도시 번호가 가리키는 쪽이다.
    /// 처음 쓸 때 한 번만 연다.
    /// </summary>
    private CityTable? _cities;

    private CityTable CityTable => _cities ??= Local.Helpers.CityTable.Open();

    /// <summary>그 도시의 문화권("이슬람", "북유럽" …). 모르면 빈 문자열.</summary>
    private string CultureOf(int city) => CityTable.CultureOf(city);

    /// <summary>힌트 이름. DB 를 못 읽으면 번호로 물러선다.</summary>
    private string HintName(int id)
    {
        if (_hintNames == null)
        {
            _hintNames = [];
            try
            {
                var svc = ContainerLocator.Container.Resolve<HintService>();
                svc.InitializeAsync(Path.Combine(AppContext.BaseDirectory, "cdshelper.db")).Wait();
                _hintNames = svc.GetAllHintNames();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ShipMap] 힌트 이름 로드 실패: {ex.Message}");
            }
        }
        return _hintNames.TryGetValue(id, out var name) && name.Length > 0 ? name : $"힌트 {id}";
    }

    /// <summary>도시 이름. 표에 없으면 번호로 물러선다.</summary>
    private string CityName(int id) => CityTable.NameOf(id);

    /// <summary>
    /// 게임 폴더를 잡고 타이틀 곡을 튼다. 지도는 아직 띄우지 않는다 —
    /// 메뉴에서 NEW/LOAD 를 골라야 <see cref="StartMap"/> 로 넘어간다.
    /// </summary>
    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        var dir = Path.GetDirectoryName(AppSettings.LastSaveFilePath);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
        {
            // 곡을 못 트는 흔한 까닭이 이것이다 — 세이브를 한 번도 안 열었으면 게임 폴더를 모른다.
            _status.Text = "세이브 파일 경로가 없습니다 — 먼저 세이브를 열어 주세요";
            System.Diagnostics.Debug.WriteLine("[ShipMap] 게임 폴더를 몰라 BGM 을 못 틉니다");
            return;
        }

        _gameDir = dir;

        // 타이틀 화면은 생성자에서 지었는데, 그때는 게임 폴더를 몰라 원본 조각도 글꼴도
        // 없었다(민색 상자로 물러선 채였다). 이제 알았으니 다시 짓는다.
        LoadSprites();
        if (_titleRoot != null && ReferenceEquals(_screen.Content, _titleRoot))
        {
            _titleIndex = -1;               // 새로 지은 줄에 초점이 다시 가게
            _titleRoot = BuildTitleScreen();
            _screen.Content = _titleRoot;
        }

        _bgm.SetGameDirectory(dir);
        _bgm.Enabled = AppSettings.BgmEnabled;   // 설정 창에서 꺼 뒀으면 조용히 시작한다
        _bgm.Play(BgmPlayer.TitleTrack);   // 메뉴 화면에서는 bgm/Track23.mp3
        if (_bgm.LastError.Length > 0)
        {
            _status.Text = _bgm.LastError;
            System.Diagnostics.Debug.WriteLine($"[ShipMap] BGM — {_bgm.LastError}");
        }
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
