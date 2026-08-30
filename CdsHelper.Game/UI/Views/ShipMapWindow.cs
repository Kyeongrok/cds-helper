using System.IO;
using System.Windows.Documents;
using System.Windows.Controls.Primitives;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows;
using CdsHelper.Game.Engine;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Helpers;
using CdsHelper.Support.Local.Models;
using CdsHelper.Support.Local.Settings;
using Prism.Ioc;
using CdsHelper.Game.Engine.Discovery;
using CdsHelper.Game.Engine.Menu;
using CdsHelper.Game.Engine.Sea;
using CdsHelper.Game.Local.Settings;

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

    /// <summary>
    /// 지도 아래 띠에 적는 글. 게임은 이 자리에 짧은 알림을 낸다 —
    /// "명성치가 모자랍니다." 처럼 창을 띄울 것도 없는 한마디다.
    /// </summary>
    /// <remarks>
    /// 글은 <b>띠 위에 바로</b> 찍는다 — 베이지 단추를 깔고 그 위에 얹지 않는다.
    /// 게임 갈무리를 보면 위쪽 정보 띠와 달리 이 자리에는 칸이 없고 액자 바탕에 글자만
    /// 놓여 있다. 단추로 두면 짧은 한마디마다 띠 위에 밝은 조각이 서서 어색하다.
    ///
    /// 글자는 <b>검정</b>이다 — 밝은 베이지 띠 위라 흰 글씨는 읽히지 않는다.
    /// </remarks>
    private readonly GameUi.GameLabel _note = new(GameFont.BlackColor)
    {
        Margin = new Thickness(12, 0, 0, 0),
        HorizontalAlignment = HorizontalAlignment.Left,
    };

    /// <summary>한 판 — 게임 폴더 · 주인공 · 표들 · 소리. 화면들이 이것을 받아 쓴다.</summary>
    private readonly Engine.Game _game = new();

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

    /// <summary>지도를 한 번 띄웠는지. <see cref="ShipMapHost.Start"/> 는 한 번만 부른다.</summary>
    private bool _started;

    /// <summary>방금 물어본 도시. 떠났다 다시 와야 다시 묻는다.</summary>
    private int _askedCity = -1;

    /// <summary>다이얼로그가 떠 있는 동안 또 묻지 않게.</summary>
    private bool _asking;

    /// <summary>초점 진단이 마지막으로 찍은 줄. 상태줄 뒤에 붙는다.</summary>
    private string _focusNote = "";

    /// <summary>지도 위에 겹쳐 둔 투명한 입력 판. 커서 자리를 이것 기준으로 잰다.</summary>
    private Border _input = null!;

    // 상단 띠의 칸들. 게임 것은 <b>베이지 버튼 띠</b>다 — MISC.CDS 파트 4 의 왼끝(16) ·
    // 가운데(8, 되풀이) · 오른끝(16) 을 이어 붙이고 그 위에 비트맵 글꼴을 짙은 갈색(색인 17)
    // 으로 찍는다(<see cref="GameButton"/>). 칸을 따로 그리던 것을 이것으로 바꿨다 —
    // 확대해 보면 칸 사이 이음매가 마구리 둘이 맞닿은 모양이라 버튼임이 드러난다.
    // 모드 쪽 ButtonMakerKR 이 같은 길로 짓는다(진홍=타이틀 · 베이지=버튼 · 회녹색=다른 상태).

    /// <summary>게임 상단 바의 날짜 칸. 조합에서 기술을 배우면 달이 넘어간다.</summary>
    private readonly GameButton _date = new("") { Lit = true, Margin = default };

    /// <summary>게임 상단 바의 소지금·함선 칸.</summary>
    private readonly GameButton _purse = new("") { Lit = true, Margin = default };

    /// <summary>게임 상단 바의 명성 칸. 후원자를 만날 수 있는지가 이 값으로 갈린다.</summary>
    private readonly GameButton _fame = new("") { Lit = true, Margin = default };

    /// <summary>선원들이 지친 만큼. 폭풍을 맞으면 오르고 자택 휴양이 푼다.</summary>
    private readonly GameButton _tired = new("") { Lit = true, Margin = default };

    /// <summary>태우고 있는 선원 수.</summary>
    private readonly GameButton _crew = new("") { Lit = true, Margin = default };

    /// <summary>바람과 배 속도. 게임 띠에는 없는 칸이라 꺼 둔 채로 낸다.</summary>
    private readonly GameButton _windText = new("") { Lit = true, Margin = default };

    /// <summary>실어 둔 물과 식량(통).</summary>
    private readonly GameButton _stores = new("") { Lit = true, Margin = default };

    /// <summary>보급이 며칠 갈지. 게임 셈(<c>0x00494010</c>)을 그대로 낸다.</summary>
    private readonly GameButton _left = new("") { Lit = true, Margin = default };

    /// <summary>게임 상단 바의 위경도 칸.</summary>
    private readonly GameButton _coord = new("") { Lit = true, Margin = default };

    /// <summary>게임 상단 바의 도시명 칸. 바다에서는 빈 채로 둔다.</summary>
    private readonly GameButton _cityLabel = new("") { Lit = true, Margin = default };

    /// <summary>들어와 있는 도시의 말. 바다에서는 줄표만 나온다.</summary>
    private readonly GameButton _language = new("") { Lit = true, Margin = default };

    /// <summary>들어와 있는 도시의 시세(백분율). 바다에서는 줄표만 나온다.</summary>
    private readonly GameButton _rate = new("") { Lit = true, Margin = default };

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

    /// <summary>
    /// 만난 사람 상자 — 말을 걸어 본 여급(친밀도·궁합)과 만난 인물을 지도 위에 겹쳐 낸다.
    /// </summary>
    /// <remarks>
    /// 놀이에는 없는 것이라 개발 창의 "정보" 로만 켠다(<see cref="GameSettings.ShowPeopleOverlay"/>).
    /// 좌표 상자와 같은 꼴이고, 자리만 지도 오른쪽 위다.
    /// </remarks>
    private Popup _people = null!;

    /// <summary>만난 사람 상자의 글.</summary>
    private readonly TextBlock _peopleText = new()
    {
        Foreground = Brushes.White,
        FontFamily = new FontFamily("Consolas"),
        FontSize = 12,
        LineHeight = 16,
        LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
    };

    /// <summary>만난 사람 상자를 켜 두었는지.</summary>
    private bool _peopleWanted = GameSettings.ShowPeopleOverlay;

    /// <summary>좌표 상자를 켜 두었는지. 실제로 뜨는지는 <see cref="SyncOverlay"/> 가 정한다.</summary>
    private bool _overlayWanted = GameSettings.ShowCoordOverlay;

    // 게임 화면 위쪽 띠에서 뽑은 색. 누런 양피지 바탕에 어두운 테두리다.
    private static readonly Brush BarFill = new SolidColorBrush(Color.FromRgb(0xC8, 0xBF, 0xA0));
    private static readonly Brush BarEdge = new SolidColorBrush(Color.FromRgb(0x4A, 0x40, 0x30));

    // 게임 커맨드 창에서 뽑은 색. 짙은 밤색 바탕에 밝은 테를 두르고, 항목만 양피지다.
    private static readonly Brush MenuBack = new SolidColorBrush(Color.FromRgb(0x4A, 0x2A, 0x22));
    private static readonly Brush MenuEdge = new SolidColorBrush(Color.FromRgb(0xC8, 0xB4, 0x90));
    private static readonly Brush MenuTitleFg = new SolidColorBrush(Color.FromRgb(0xEC, 0xDF, 0xC0));

    /// <summary>
    /// 도시정보 창에서 켜고 끄는 칸. 켠 상태를 따로 들고 있지 않고 칸의
    /// <see cref="UIElement.Visibility"/> 를 그대로 본다 — 둘로 나누면 어긋난다.
    /// </summary>
    private readonly Dictionary<string, FrameworkElement> _infoCells = [];

    /// <summary>도시정보 창의 줄 이름을 달아 띠에 놓는 칸.</summary>
    /// <summary>
    /// 상단 띠 칸마다의 서식 — 게임 것을 <b>자리 수까지</b> 그대로 옮겼다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   0x0056BE98  "%4d년%2d월%2d일"
    ///   0x0056BEA8  "%s%4d명"              %s 는 "선원"(0x56BEB8) 또는 "대원"(0x56BEB0)
    ///   0x0056BEC0  "물%4d통 식량%4d통"
    ///   0x0056BED8  "%s위 %3d  %s경 %3d  " 북·남 / 동·서 (0x56BEF0~)
    ///   0x0056BF18  "소지금%6d닢"
    ///   0x0056BF28  "피로도%4d"
    ///   0x0056BF38  "명성%6d"
    /// </code>
    /// 칸 너비는 글자 수를 따라가므로 <b>서식이 맞으면 너비도 맞는다</b> — 예전에는
    /// "1499년 5월8일" · "1770닢" 처럼 자리를 안 맞춰 칸마다 폭이 어긋났다.
    /// </remarks>
    private FrameworkElement InfoCell(string name, GameButton cell, bool on)
    {
        // 지난번에 켜고 끈 것이 있으면 그것이 먼저다. 한 번도 안 건드렸으면(null)
        // 여기 적힌 기본값으로 선다.
        var saved = GameSettings.BarCells;
        cell.Visibility = (saved?.Contains(name) ?? on) ? Visibility.Visible : Visibility.Collapsed;
        _infoCells[name] = cell;
        return cell;
    }

    /// <summary>지금 띠에 켜져 있는 칸을 적어 둔다. 다음에 켤 때 이대로 선다.</summary>
    private void SaveBarCells() =>
        GameSettings.BarCells =
            [.. _infoCells.Where(p => p.Value.Visibility == Visibility.Visible).Select(p => p.Key)];

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
        // 눌렀을 때 초점을 받게 둔다. 지도는 WPF 가 모르는 자식 창이라, 이것이 없으면
        // 지도를 눌러도 창 안에 초점 가진 것이 없는 상태가 된다.
        // 자식 창이 덮어 보이지 않으니 초점 테두리는 끈다.
        var input = new Border
        {
            Background = Brushes.Transparent,
            Cursor = Cursors.Arrow,
            Focusable = true,
            FocusVisualStyle = null,
        };
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

        // 만난 사람 상자는 지도 오른쪽 위에 겹쳐 둔다. 좌표 상자와 같은 꼴이되 자리만 반대다.
        _people = new Popup
        {
            PlacementTarget = input,
            Placement = PlacementMode.Right,
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
                Child = _peopleText,
            },
        };
        surface.Children.Add(_people);

        // 게임 상단 띠. 어느 칸을 띄울지는 도시정보 창에서 켜고 끈다(띠를 오른쪽 단추로 누른다).
        // 이동 모드(정박·해상 이동) 칸은 뺐다 — 게임 띠에 없는 칸이다.
        var gameCells = new StackPanel { Orientation = Orientation.Horizontal };
        gameCells.Children.Add(InfoCell(CityInfoMenu.Date, _date, on: true));
        gameCells.Children.Add(InfoCell(CityInfoMenu.Crew, _crew, on: false));
        gameCells.Children.Add(InfoCell(CityInfoMenu.Stores, _stores, on: false));
        gameCells.Children.Add(InfoCell(CityInfoMenu.DaysLeft, _left, on: false));
        gameCells.Children.Add(InfoCell(CityInfoMenu.Wind, _windText, on: false));
        gameCells.Children.Add(InfoCell(CityInfoMenu.Coord, _coord, on: true));
        gameCells.Children.Add(InfoCell(CityInfoMenu.Gold, _purse, on: true));
        gameCells.Children.Add(InfoCell(CityInfoMenu.Fame, _fame, on: true));
        gameCells.Children.Add(InfoCell(CityInfoMenu.Fatigue, _tired, on: false));
        gameCells.Children.Add(InfoCell(CityInfoMenu.City, _cityLabel, on: false));
        gameCells.Children.Add(InfoCell(CityInfoMenu.Language, _language, on: false));
        gameCells.Children.Add(InfoCell(CityInfoMenu.Rate, _rate, on: false));

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

        // 왼쪽 단추로 누르면 커맨드 창이 뜬다 — 게임도 상단 띠를 누르면 이것이 나온다.
        // 지도에서는 오른쪽 단추가 같은 창을 내는데, 띠에서는 오른쪽이 도시정보 몫이라
        // 왼쪽을 준다. 창은 띠 <b>바로 밑</b>에 붙는다.
        gameBar.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            ShowCommandMenu(gameBar, new Point(e.GetPosition(gameBar).X, gameBar.ActualHeight));
        };

        var root = new DockPanel();
        DockPanel.SetDock(gameBar, Dock.Top);
        root.Children.Add(gameBar);
        // 지도 위의 까만 조작 줄. 놀이에는 없는 것이라 개발 창에서 끄고 켤 수 있다.
        _toolBar = new Border
        {
            Child = bar,
            Height = 30,
            Visibility = GameSettings.ShowToolBar ? Visibility.Visible : Visibility.Collapsed,
        };
        DockPanel.SetDock(_toolBar, Dock.Top);
        root.Children.Add(_toolBar);

        // 게임은 지도 아래에도 같은 띠를 하나 둔다 — 짧은 알림이 이 자리에 뜬다.
        var footer = TitleBarStrip(null, _note);
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
            // 설정은 게임 띠에 두었다가 햄버거로 옮겼다 — 게임 띠에 없는 칸이라
            // 섞여 있으면 원본과 달라 보인다(개발 창을 옮긴 것과 같은 까닭이다).
            ("설정", () => SettingsDialog.Show(this, _game.Bgm)),
            ("게임데이터", () => GameDataDialog.Show(this)),
            // 낯을 튼 여급과 그 궁합. 궁합은 초상화 번호 하나로 갈리는데 화면에서는
            // 볼 길이 없어 여기에 둔다.
            ("여급 수첩", () => BarmaidBookDialog.Show(this, _game)),
            ("개발", ShowDevDialog));
        DockPanel.SetDock(titleBar, Dock.Top);
        shell.Children.Add(titleBar);
        shell.Children.Add(_screen);
        Content = shell;

        PreviewKeyDown += OnTitleKey;   // 타이틀에서만 먹는다(그 안에서 화면을 본다)
        input.MouseWheel += (_, e) => _host.Zoom(e.Delta > 0 ? 1 : -1, e.GetPosition(input));
        // 오른쪽 단추는 커맨드 창만 낸다. 예전에는 끌면 지도가 밀렸는데, 게임에 없는
        // 조작인 데다 커맨드를 내려다 손이 조금만 흔들려도 지도가 밀려 걷어냈다.
        input.MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            // 도시 안에서는 함대 커맨드 창을 안 낸다 — 도시 화면이 제 커맨드 창을 따로 낸다.
            if (_host.SeaBlocked) return;
            ShowCommandMenu(input, e.GetPosition(input));
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
            // 뭍에서도 같은 스위치로 말이 서고 다시 간다.
            _host.ToggleAnchor();
            // 내릴 때도 올릴 때도 같은 소리가 난다.
            _game.Sfx?.Play(SoundBank.AnchorPart);
        };
        input.MouseMove += (_, e) => _host.SetMouse(e.GetPosition(input), true);
        input.MouseLeave += (_, _) => _host.SetMouse(default, false);

        // 초점이 어디로 가는지 보려고 둔 진단(FocusWatch). 다 잡고 나면 지운다.
        FocusWatch.Sink = note => _focusNote = note;

        _statusTimer = new DispatcherTimerLite(TimeSpan.FromMilliseconds(100), () =>
        {
            SyncMouse();
            _status.Text = _focusNote.Length > 0 ? $"{_host.Status}    {_focusNote}"
                                                 : _host.Status;
            CheckPort();
            CheckDiscovery();
            PassTime();
            MarkSeen();
            var (lat, lon) = _host.ShipLatLon;
            // 칸마다의 서식은 게임 것을 자리 수까지 그대로 옮겼다(BarFormats 참고).
            _coord.Text = $"{(lat >= 0 ? "북" : "남")}위 {Math.Abs(lat),3:F0}  " +
                          $"{(lon >= 0 ? "동" : "서")}경 {Math.Abs(lon),3:F0}  ";
            _purse.Text = $"소지금{_game.Player.Gold,6}닢";
            _fame.Text = $"명성{_game.Player.Fame,6}";
            _tired.Text = $"피로도{_game.Player.Fatigue,4}";
            _windText.Text = WindLine();
            _crew.Text = $"선원{_game.Player.Crew,4}명";
            _stores.Text = $"물{_game.Player.SupplyOf(SupplyKind.Water),4}통" +
                           $" 식량{_game.Player.SupplyOf(SupplyKind.Food),4}통";
            _left.Text = $"남은 {_game.Player.SupplyDaysLeft}일";
            // 가진 배 중 가장 큰 것이 기함이다 — 그 벌의 그림으로 그린다(게임이 안 떠 있을 때).
            // 그림은 기함 것으로 그린다 — 항구 함대편성에서 기함을 바꾸면 배 모양도 바뀐다.
            ShipSprites.Use(_game.Player.FlagshipHull?.Hull);
            _date.Text = $"{_game.Player.Date.Year,4}년{_game.Player.Date.Month,2}월{_game.Player.Date.Day,2}일";
            _cityLabel.Text = _game.Player.CityName.Length > 0 ? _game.Player.CityName : "—";
            _language.Text = CityLanguage();
            _rate.Text = CityRate();
            if (_overlay.IsOpen) FillOverlay(lat, lon);
            SyncSeaMusic();
        });
        Loaded += OnLoaded;

        // 창을 옮기면 그 위에 얹힌 도시 그림·커맨드 창도 같이 옮긴다 — 게임에서는 지도 안에
        // 그려진 것이라 따로 남을 수가 없다.
        GameUi.CarryOwnedWindows(this);

        // 창이 물러나거나 접히면 좌표 상자도 같이 감춘다 — 제 창이라 그냥 두면 남의 앱 위에 뜬다.
        Activated += (_, _) => SyncOverlay();
        Deactivated += (_, _) =>
        {
            SyncOverlay();
            FocusWatch.After("지도창 초점 잃음");
        };
        StateChanged += (_, _) => SyncOverlay();
        Closed += (_, _) =>
        {
            _overlay.IsOpen = false;
            _statusTimer.Stop();
            _game.Close();
            FocusWatch.Sink = null;   // 진단 — 다 잡고 나면 지운다
        };
    }

    /// <summary>
    /// 지도가 화면에서 차지한 자리(WPF 단위). 도시 화면이 이 자리를 통째로 덮는다 —
    /// 게임도 도시에 들어가면 지도 영역이 남색으로 덮인다.
    /// </summary>
    private Rect MapAreaOnScreen()
    {
        var source = PresentationSource.FromVisual(this);
        if (source == null) return default;

        // 지도를 막 갈아 끼운 참이면 아직 자리를 안 잡았을 수 있다. 그때 빈 자리를 내면
        // 도시 창이 제 크기를 못 잡는다 — 한 번 재워 두고 다시 본다.
        //
        // <b>크기만 봐서는 모자란다.</b> WPF 는 <c>Content</c> 를 갈아 끼워도 그 아래
        // 것들을 <b>다음 자리잡기 때</b> 트리에 붙인다. 앞서 한 번 떠 있었던 지도는
        // 크기가 남아 있어서 이 검사를 그냥 지나치는데, 그 참에 <c>PointToScreen</c> 을
        // 부르면 "이 Visual이 PresentationSource에 연결되지 않았습니다" 로 터진다 —
        // 타이틀로 돌아갔다가 NEW GAME 을 다시 고르면 늘 이 자리였다.
        if (!Ready(_input)) UpdateLayout();
        if (!Ready(_input)) return default;

        // PointToScreen 은 실픽셀을 내므로 WPF 단위로 되돌린다(고해상도 화면에서 어긋난다).
        var device = _input.PointToScreen(new Point(0, 0));
        var topLeft = source.CompositionTarget.TransformFromDevice.Transform(device);
        return new Rect(topLeft.X, topLeft.Y, _input.ActualWidth, _input.ActualHeight);
    }

    /// <summary>트리에 붙었고 자리도 잡았는가 — <c>PointToScreen</c> 을 부르기 전에 본다.</summary>
    private static bool Ready(FrameworkElement element) =>
        PresentationSource.FromVisual(element) != null
        && element.ActualWidth > 0 && element.ActualHeight > 0;

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

    /// <summary>
    /// 상단 띠의 <b>언어</b> 칸 — 들어와 있는 도시가 쓰는 말이다.
    /// </summary>
    /// <remarks>
    /// 게임 자리는 <c>0x0047DE39</c> 다. 도시 번호가 0~225 밖이면(바다에 있으면) 이름 대신
    /// 줄표(<c>0x0056BF58</c>)를 낸다. 말 번호는 도시가 딸린 나라의 것이고
    /// (<see cref="CityExeTable.NationOf"/>), 이름표는 <c>0x00560A48</c> 이다.
    /// </remarks>
    private string CityLanguage()
    {
        int city = _game.Player.CityId;
        if (city < 0 || _game.CityRows == null || _game.Nations == null) return NoValue;

        int nation = _game.CityRows.NationOf(city);
        var names = _game.Buildings?.LanguageNames;
        if (names == null || _game.Nations.Find(nation) is not { } row) return NoValue;

        return row.Language >= 0 && row.Language < names.Count ? names[row.Language] : NoValue;
    }

    /// <summary>
    /// 상단 띠의 <b>시세</b> 칸. 게임 서식은 <c>"시세%4d%"</c> 고, 도시 밖에서는
    /// <c>"시세 ---%"</c> 다(<c>0x0047DE94</c>).
    /// </summary>
    private string CityRate()
    {
        int city = _game.Player.CityId;
        return city < 0 ? "시세 ---%" : $"시세{_game.Rates.Of(city),4}%";
    }

    /// <summary>도시 밖일 때 언어 칸에 나오는 줄표(<c>0x0056BF58</c>, 열여덟 개).</summary>
    private const string NoValue = "------------------";

    /// <summary>도시정보 창. 상단 띠 밑에 붙여 띄운다.</summary>
    private GameMenuHost? _infoMenuHost;

    private GameMenuHost InfoMenu => _infoMenuHost ??= new GameMenuHost(this);

    /// <summary>
    /// 상단 띠에 무엇을 띄울지 고르는 창을 낸다. 게임은 도시 안에서만 이 창을 내므로
    /// 바다에서는 아무 일도 안 한다.
    /// </summary>
    private void ShowCityInfoMenu(FrameworkElement bar, Point at)
    {
        if (!_host.InCity) return;
        if (InfoMenu.IsOpen) { InfoMenu.Focus(); return; }

        InfoMenu.Open(BuildCityInfo, ToScreen(bar, new Point(at.X, bar.ActualHeight)));
    }

    /// <summary>
    /// 도시정보 창의 지금 모습. 줄을 하나 뒤집을 때마다 다시 지어 갈아 끼운다 —
    /// <c>:ON</c>·<c>:OFF</c> 글자는 게임 글꼴로 찍은 그림이라 고쳐 쓸 수가 없다.
    /// </summary>
    private GameMenu BuildCityInfo() => CityInfoMenu.Build(
        name => _infoCells.TryGetValue(name, out var cell)
            ? cell.Visibility == Visibility.Visible
            : null,
        name =>
        {
            var cell = _infoCells[name];
            cell.Visibility = cell.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
            SaveBarCells();
            InfoMenu.Refresh();
        },
        InfoMenu.Close);

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

    /// <summary>
    /// 해상에서 자리에 맞는 곡으로 갈아탄다. <b>지금 곡이 끝난 뒤에</b> 바뀐다 —
    /// 게임도 이 갈래에서만 그렇게 한다(<see cref="BgmPlayer.PlayWhenDone"/>).
    /// </summary>
    /// <remarks>
    /// 도시에 들어가 있거나 뭍에 올라 있으면 손대지 않는다 — 그때는 도시 곡·말 곡이 돈다.
    /// 멈춰 있을 때(커맨드 창)도 그대로 둔다.
    /// </remarks>
    private void SyncSeaMusic()
    {
        if (!_started || _host.SeaBlocked || _host.IsOnLand || _host.Paused) return;
        if (_host.ShipCell is not { } cell) return;

        _game.Bgm.PlayWhenDone(BgmPlayer.SeaTrackAt(cell.X, cell.Y));
    }

    /// <summary>좌표 상자를 띄울 때인지 다시 따진다 — 켜 두었고, 지도가 떠 있고, 이 창이 앞일 때만.</summary>
    private void SyncOverlay()
    {
        bool room = _started && IsActive
                    && WindowState != WindowState.Minimized
                    && ReferenceEquals(_screen.Content, _mapRoot);
        _overlay.IsOpen = _overlayWanted && room;

        bool people = _peopleWanted && room;
        if (people) FillPeople();
        _people.IsOpen = people;
    }

    /// <summary>
    /// 만난 사람 상자를 채운다 — 왼쪽에 여급(친밀도·궁합), 오른쪽에 만난 인물이다.
    /// </summary>
    /// <remarks>
    /// 궁합은 여급의 운명 얼굴 코드와 내 것을 견주어 가른다(<see cref="BarmaidTable.Destined"/>) —
    /// 같거나 하나 차이면 맞는 것이다. 친밀도는 말을 걸어야 생기므로, 여기 뜨는 여급은
    /// <b>한 번이라도 말을 걸어 본</b> 이들이다.
    /// </remarks>
    private void FillPeople()
    {
        var player = _game.Player;
        var left = new List<string> { "여급 (친밀도 · 궁합)" };

        int mine = Engine.Town.Barmaids.FortuneOf(player);
        var table = _game.Barmaids;
        foreach (var (id, liking) in player.Liking.OrderByDescending(p => p.Value))
        {
            string name = table?.Find(id)?.Name ?? $"{id}번";
            string fit = table?.Find(id) is { } her
                ? BarmaidTable.Destined(mine, her.Fortune) ? "궁합 ○" : "궁합 ×"
                : "";
            left.Add($"  {PadCells(name, 16)}{liking,4}  {fit}");
        }
        if (left.Count == 1) left.Add("  아직 말을 걸어 본 여급이 없다");

        var right = new List<string> { "만난 인물" };
        foreach (string name in player.Met.OrderBy(n => n, StringComparer.Ordinal))
            right.Add($"  {name}");
        if (right.Count == 1) right.Add("  아직 만난 인물이 없다");

        var lines = new List<string>();
        for (int i = 0; i < Math.Max(left.Count, right.Count); i++)
            lines.Add(PadCells(i < left.Count ? left[i] : "", PeopleColumn)
                      + (i < right.Count ? right[i] : ""));

        _peopleText.Text = string.Join(Environment.NewLine, lines);
    }

    /// <summary>여급 칸의 너비(글자 칸). 한글 한 자를 두 칸으로 센다.</summary>
    private const int PeopleColumn = 34;

    /// <summary>한글을 두 칸으로 세어 그 칸 수만큼 빈칸을 채운다.</summary>
    private static string PadCells(string text, int cells)
    {
        int used = 0;
        foreach (char c in text) used += c < 0x80 ? 1 : 2;
        return used >= cells ? text : text + new string(' ', cells - used);
    }

    /// <summary>
    /// 좌표 상자를 채운다. 배가 선 칸을 WORLD.CDS 파일 안의 자리까지 풀어서 보여 주고,
    /// <b>타일 번호 뒤에는 그 타일 그림</b>을 한 장 끼워 넣는다 — 번호만 봐서는 어떤 칸인지
    /// 알 수가 없어서다.
    /// </summary>
    private void FillOverlay(double lat, double lon)
    {
        _overlayText.Inlines.Clear();

        var c = _host.ShipCell;
        if (c == null) { _overlayText.Inlines.Add(new Run("배가 아직 지도에 없습니다")); return; }

        var v = c.Value;
        _overlayText.Inlines.Add(new Run(
            $"칸        {v.X,7:F1}, {v.Y,6:F1}   (칸 {v.CellX}, {v.CellY})\n" +
            $"WORLD.CDS 행 {v.Row,4} · 열 {v.Col,4} · 0x{v.Offset:X5}\n" +
            $"칸 값     지형 {v.Terrain,3} · 속성 {v.Attr,3} · 타일 {v.Tile,5} "));

        if (TileImage(v.Tile) is { } tile)
            _overlayText.Inlines.Add(new InlineUIContainer(tile)
            {
                BaselineAlignment = BaselineAlignment.Center,
            });

        var lines = new List<string>
        {
            $" · 육지 {v.LandRatio * 100,3:F0}%",
            $"위경도    {(lat >= 0 ? "북위" : "남위")} {Math.Abs(lat):F2} · {(lon >= 0 ? "동경" : "서경")} {Math.Abs(lon):F2}",
        };

        lines.AddRange(SpeedLines());

        // 40칸까지만 본다. 더 넓히면 도시마다 항구 칸을 찾느라 100ms 틱이 무거워진다.
        var (city, cells) = _host.NearestDock(40);
        if (city >= 0) lines.Add($"가까운 항구 [{_game.CityName(city)}] {cells:F1}칸");

        var m = _host.MouseCell;
        if (m != null)
            lines.Add($"커서      {m.Value.X,7:F1}, {m.Value.Y,6:F1}   0x{m.Value.Offset:X5}");

        _overlayText.Inlines.Add(new Run(string.Join("\n", lines)));
    }

    /// <summary>
    /// 좌표 상자의 속도 줄 — 바람 · 돛 · 함대 속도 · 해류를 한 줄씩 낸다.
    /// </summary>
    /// <remarks>
    /// 게임과 속도가 어긋날 때 <b>어디서 어긋나는지</b>를 보려고 둔 것이다. 셈은
    /// <see cref="Sailing.SpeedOf"/> 가 하고(게임 <c>0x0048BCF0</c>), 여기서는 그 셈에
    /// 들어간 값들을 그대로 늘어놓는다.
    /// <code>
    ///   속도   = 추진력 x (풍속 + 1) x 돛효율 / 100      (배마다)
    ///   함대   = (기함 + 배들 평균) / 2
    ///   한 틱  = 빠른 칸이면 9 x 속도 / 10, 아니면 (3 x 속도 + 54) / 10, 둘 다 / 64
    /// </code>
    /// 상대각은 <b>0 이 정순풍, 8 이 정면 역풍</b>이다. 돛효율은 기함 것이다 — 배마다
    /// 다르지만 한 줄에 다 적을 수는 없다.
    /// </remarks>
    private IEnumerable<string> SpeedLines()
    {
        var (dir, speed, relative) = _host.LastWind;
        string where = ShipMapHost.Compass[(dir & 0xF) >> 1];

        var ships = _game.Player.Ships;
        var flag = ships.Count > 0
            ? ships[Math.Clamp(_game.Player.Flagship, 0, ships.Count - 1)] : null;
        int sail = flag != null && _game.Sails is { } table
            ? table.Efficiency(flag.Sails, relative) : 0;

        yield return $"바람      {where} {speed}  · 상대각 {relative,2} (0 순풍 · 8 역풍)"
                   + $" · 돛효율 {sail,3}%";

        string ground = _host.LastFast ? "빠른 칸" : "느린 칸";
        yield return $"속도      함대 {_host.LastSpeed,3} · 한 틱 {_host.LastStep:F3}칸 · {ground}"
                   + (flag != null ? $" · 기함 추진력 {flag.Speed}" : "");

        var flow = _host.LastFlow;
        yield return flow.Speed > 0
            ? $"해류      {ShipMapHost.Compass[(flow.Dir & 0xF) >> 1]} {flow.Speed}"
            : "해류      없음(느린 칸에서는 안 받는다)";
    }

    /// <summary>좌표 상자에 끼우는 타일 그림의 배율. 글줄 높이에 맞춰 두 배로 키운다.</summary>
    private const int OverlayTileScale = 2;

    private int _overlayTile = -1;
    private BitmapSource? _overlayTileBitmap;

    /// <summary>
    /// 타일 번호의 그림. <b>그림판만 담아 두고 <see cref="Image"/> 는 부를 때마다 새로 짓는다</b> —
    /// 한 번 만든 것을 계속 끼우면 앞서 끼운 <see cref="InlineUIContainer"/> 에 아직 매여 있어
    /// "이미 다른 요소의 논리 자식입니다" 로 터진다. 그림판은 얼려 두었으니 나눠 써도 된다.
    /// </summary>
    private Image? TileImage(int tile)
    {
        if (tile < 0 || tile >= OceanTiles.TileCount) return null;

        if (tile != _overlayTile)
        {
            var ocean = OceanTiles.LoadFromDirectory(_game.Directory);
            if (ocean == null) return null;

            int w = OceanTiles.TileW;
            var pixels = new uint[w * w];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = 0xFF000000u
                            | (uint)ocean.PaletteRgb[ocean.TileData[tile * OceanTiles.TilePixels + i]];

            var made = BitmapSource.Create(w, w, 96, 96, PixelFormats.Bgra32, null, pixels, w * 4);
            made.Freeze();
            _overlayTileBitmap = made;
            _overlayTile = tile;
        }
        if (_overlayTileBitmap == null) return null;

        int side = OceanTiles.TileW * OverlayTileScale;
        var image = new Image
        {
            Source = _overlayTileBitmap,
            Width = side,
            Height = side,
            Margin = new Thickness(1, 0, 1, 0),
            IsHitTestVisible = false,
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(image, EdgeMode.Aliased);
        return image;
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

        FrameworkElement? handle = GameUi.TitleFrame(GameUi.Sprites, "메인메뉴");
        handle ??= new Border
        {
            Background = MenuBack,
            BorderBrush = MenuEdge,
            BorderThickness = new Thickness(2),
            Padding = new Thickness(18, 2, 18, 2),
            Child = new TextBlock
            {
                Text = "메인메뉴",
                Foreground = MenuTitleFg,
                FontWeight = FontWeights.Bold,
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Center,
            },
        };
        // 게임 메인메뉴는 제목 띠와 첫 줄이 맞붙어 있다 — 사이를 띄우지 않는다.
        items.Children.Add(handle);
        _titleFocus = new GameUi.FocusGroup();
        items.Children.Add(TitleMenuItem("NEW GAME", NewGame));
        // 게임도 로드 전에 한 번 묻는다. 제목 줄은 안 단다 — 게임 물음창에는 없다.
        items.Children.Add(TitleMenuItem("LOAD GAME", () =>
        {
            // 게임도 제목 띠를 얹는다 — 0x00571A78 "게임 로드" · 0x00571A88 본문.
            if (ConfirmDialog.Ask(this, "마지막에 저장한 데이터를 로드합니다", "게임 로드"))
                StartMap(fresh: false);
        }));
        items.Children.Add(TitleMenuItem("MINI GAME", MiniGames));
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
        _titleMenuBox = box;
        // 게임 메뉴처럼 제목 띠를 잡아 옮길 수 있게 한다. 가운데 놓는 것은 그대로 두고
        // 옮긴 만큼만 얹으므로, 창 크기가 바뀌어도 가운데가 기준이 된다.
        var move = new TranslateTransform(_titleMenuOffset.X, _titleMenuOffset.Y);
        box.RenderTransform = move;

        var middle = new Grid { Background = TitleBackground(), Children = { box } };

        // 게임 메인메뉴는 화면 한가운데보다 조금 위에 앉는다. 아래에 빈칸을 두면 가운데맞춤이
        // 그만큼 올라가는데, 올라가는 것은 빈칸의 <b>절반</b>이라 두 배로 잡는다.
        middle.SizeChanged += (_, e) =>
            box.Margin = new Thickness(0, 0, 0, e.NewSize.Height * TitleMenuRise * 2);

        EnableMenuDrag(handle, box, middle, move);

        // 게임 타이틀에도 위아래로 액자 띠가 있다. 위 띠에는 날짜 칸 하나만 있고 나머지는 비었다.
        var screen = new DockPanel();
        var top = TitleBarStrip($"{_game.Player.Date.Year}년 {_game.Player.Date.Month}월 {_game.Player.Date.Day}일");
        DockPanel.SetDock(top, Dock.Top);
        screen.Children.Add(top);

        var bottom = TitleBarStrip(null);
        DockPanel.SetDock(bottom, Dock.Bottom);
        screen.Children.Add(bottom);

        screen.Children.Add(middle);

        return screen;
    }

    /// <summary>
    /// 메인메뉴를 가운데에서 얼마나 옮겼는지. 타이틀 화면을 다시 지어도 그 자리에 남는다 —
    /// 게임 폴더를 알게 되면 화면을 새로 짓기 때문이다.
    /// </summary>
    private Point _titleMenuOffset;

    /// <summary>
    /// 제목 띠를 잡아 메뉴 상자를 옮길 수 있게 한다.
    /// </summary>
    /// <remarks>
    /// 창을 옮기는 <see cref="GameUi.EnableDrag"/> 와 달리 이것은 <b>화면 안에서</b> 상자만
    /// 옮긴다 — 타이틀 메뉴는 제 창이 아니라 타이틀 화면 위에 얹힌 칸이기 때문이다.
    ///
    /// 손잡이를 제목 띠로 좁힌 까닭은 상자 속이 죄다 누르는 줄이어서다. 아무 데나 잡게 두면
    /// NEW GAME 을 누르려다 조금만 흔들려도 끌기로 새어 눌리지 않는다.
    ///
    /// 상자가 화면 밖으로 아주 나가지 않게 가장자리에서 막는다. 제목 띠가 남아 있어야
    /// 다시 잡아 끌 수 있다.
    /// </remarks>
    private void EnableMenuDrag(FrameworkElement handle, FrameworkElement box,
                                FrameworkElement area, TranslateTransform move)
    {
        Point grabbed = default;
        Point start = default;
        // 커서는 그대로 둔다 — 게임은 끌 수 있는 자리라고 십자로 바꿔 알리지 않는다.

        handle.MouseLeftButtonDown += (_, e) =>
        {
            grabbed = e.GetPosition(area);
            start = new Point(move.X, move.Y);
            handle.CaptureMouse();
            e.Handled = true;
        };

        handle.MouseMove += (_, e) =>
        {
            if (!handle.IsMouseCaptured) return;
            var now = e.GetPosition(area);

            // 가운데 놓인 상자가 얼마나 갈 수 있는지 — 좌우·위아래로 각각 절반씩이다.
            double roomX = Math.Max(0, (area.ActualWidth - box.ActualWidth) / 2);
            double roomY = Math.Max(0, (area.ActualHeight - box.ActualHeight) / 2);

            move.X = Math.Clamp(start.X + (now.X - grabbed.X), -roomX, roomX);
            move.Y = Math.Clamp(start.Y + (now.Y - grabbed.Y), -roomY, roomY);
            _titleMenuOffset = new Point(move.X, move.Y);
        };

        handle.MouseLeftButtonUp += (_, e) =>
        {
            handle.ReleaseMouseCapture();
            e.Handled = true;
        };
    }

    /// <summary>
    /// 타이틀 화면 위아래에 두는 액자 띠. <paramref name="text"/> 를 주면 왼쪽에 칸 하나를 둔다.
    /// </summary>
    private static FrameworkElement TitleBarStrip(string? text, FrameworkElement? slot = null)
    {
        var inside = new StackPanel { Orientation = Orientation.Horizontal };
        if (slot != null)
        {
            inside.Children.Add(slot);
        }
        else if (text != null)
        {
            inside.Children.Add(new GameButton(text) { Lit = true, Margin = default });
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
    /// <summary>
    /// 바탕 무늬를 얼마로 줄여 깔지. 1 이면 원본 크기다.
    /// </summary>
    /// <remarks>
    /// 원본 <c>140x112</c> 은 <b>1.75배로 늘어난 화면</b>에서 뜬 것이다 — 게임 무늬는
    /// <c>80x64</c> 다(갈무리에서 잰 마디 가로 114 · 세로 91 을 그 갈무리 배율 1.425 로
    /// 나누면 딱 떨어진다). 그 배로 도로 줄여야 창들과 결이 맞는다.
    /// </remarks>
    private const double TilePack = 1.0 / 1.75;

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
                // 무늬 원본은 1.75배 화면에서 찍은 것이라 그대로 깔면 성기다.
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

    /// <summary>타이틀 메뉴에서 초점이 오가는 줄 묶음. 화면을 다시 지을 때 새로 잡는다.</summary>
    private GameUi.FocusGroup _titleFocus = new();

    /// <summary>메인메뉴를 화면 높이의 몇 만큼 위로 올릴지.</summary>
    private const double TitleMenuRise = 0.05;

    /// <summary>타이틀 메뉴 줄의 최소 폭. 글자 좌우 여백까지 넣은 게임 비율이다.</summary>
    private const double TitleItemMinWidth = 124;

    /// <summary>
    /// 타이틀 메뉴 한 줄. <paramref name="run"/> 이 null 이면 흐리게 두고 못 고른다.
    /// </summary>
    /// <remarks>
    /// 예전에는 여기서 띠를 짓고 초점 테를 얹고 깜빡임까지 손수 굴렸다 — <see cref="GameUi"/>
    /// 에 같은 것이 이미 있는데도 <c>FocusLight</c>·<c>FocusDark</c>·<c>FocusBlink</c> 를
    /// 다시 선언해 두었다. 이제 <see cref="GameButton"/> 과 <see cref="GameUi.FocusGroup"/>
    /// 이 맡는다.
    ///
    /// 못 고르는 줄은 묶음에 안 넣는다 — 초점이 그 줄을 건너뛴다.
    /// </remarks>
    /// <summary>
    /// 미궁 64 퍼즐을 여는 자리. <b>그 놀이는 CdsHelper.Maze 에 따로 있다</b> —
    /// 그쪽이 여기를 물고 있어서 반대로는 못 부른다. 띄우는 쪽(CdsHelper.Form)이
    /// 이 자리에 걸어 준다.
    /// </summary>
    public static Action<Window, Random>? MazeGame { get; set; }

    /// <summary>
    /// 일기토를 여는 자리. <b>그 놀이는 CdsHelper.Duel 에 따로 있다</b> — 미궁과
    /// 같은 까닭으로 반대로는 못 부른다.
    /// </summary>
    public static Action<Window, Random>? DuelGame { get; set; }

    /// <summary>
    /// MINI GAME — 일곱 줄을 늘어놓는다(<c>0x0045F957</c> 벌).
    /// </summary>
    /// <remarks>
    /// 이름은 <c>0x00571E00</c> 부터 열여섯 바이트씩이고, 고르면 <c>0x0045FCCC</c> 의
    /// 뜀표로 갈린다.
    /// <code>
    ///   MG00 성배 퍼즐          0x004684D0
    ///   MG01 스핑크스 퀴즈      0x0047BFE0
    ///   MG02 미궁 64 퍼즐       0x0042C8A0
    ///   MG03 낚시 게임          0x0047BDD0
    ///   MG04 코인 게임          0x004531F0
    ///   MG05 발라몬의 탑 퍼즐   0x0045FB60
    ///   MG06 화살표 입방체 퍼즐 0x0045FBBD
    /// </code>
    /// 게임은 줄마다 레지스트리를 읽어 <b>풀어 놓은 것만</b> 켠다 —
    /// <c>Software\KOEI\CostaDelSol.0</c> 의 <c>MG00</c>~<c>MG06</c> 이 1 이어야 한다
    /// (<c>0x0045FA54</c> 벌). 여기서는 만든 것만 켠다.
    ///
    /// <b>여덟째 「일기토」는 원본 차림표에 없다.</b> 게임에서는 해전에서 기함끼리
    /// 붙었을 때만 열리는데(<c>0x0043A347</c>) 아직 해전이 없어서 여기에 붙여 둔다.
    /// </remarks>
    private void MiniGames()
    {
        string[] names =
        [
            "성배 퍼즐", "스핑크스 퀴즈", "미궁 64 퍼즐", "낚시 게임",
            "코인 게임", "발라몬의 탑 퍼즐", "화살표 입방체 퍼즐",
            "일기토",
        ];

        int pick = MapPointDialog.Ask(this, names, "미니 게임");
        if (pick < 0) return;

        switch (pick)
        {
            case 0: GrailPuzzleDialog.Play(this, _game.Player, _game.Random); break;
            case 1: SphinxQuizDialog.Play(this, _game.Random); break;
            case 2:
                if (MazeGame == null) NoticeDialog.Show(this, "아직 만들지 않았습니다");
                else MazeGame(this, _game.Random);
                break;
            case 3: FishingGameDialog.Play(this, _game.Random); break;
            case 4: CoinPuzzleDialog.Play(this, _game.Random); break;
            case 5: TowerPuzzleDialog.Play(this, _game.Random); break;
            case 6: CubePuzzleDialog.Play(this, _game.Player, _game.Random); break;
            case 7:
                if (DuelGame == null) NoticeDialog.Show(this, "아직 만들지 않았습니다");
                else DuelGame(this, _game.Random);
                break;
            default: NoticeDialog.Show(this, "아직 만들지 않았습니다"); break;
        }
    }

    private Border TitleMenuItem(string text, Action? run)
    {
        var item = run != null
            ? _titleFocus.Add(text, run, 0)
            : new GameButton(text, null);

        // 게임은 글자 좌우로 넉넉히 비운다 — 글자에 딱 붙이면 띠가 쪼그라들어 보인다.
        // 가장 긴 "LOAD GAME"(72점)의 1.7배쯤이 게임 비율이다.
        item.MinWidth = TitleItemMinWidth;
        // 줄과 줄 사이는 붙인다 — 게임 메뉴는 띠가 맞닿아 있고 빈 자리가 없다.
        item.Margin = default;
        return item;
    }

    /// <summary>타이틀에서 위아래로 옮기고 엔터로 고른다. 지도가 뜨면 아무것도 안 한다.</summary>
    private void OnTitleKey(object sender, KeyEventArgs e)
    {
        if (!ReferenceEquals(_screen.Content, _titleRoot)) return;
        if (_titleFocus.HandleKey(e.Key)) e.Handled = true;
    }

    /// <summary>
    /// 타이틀을 걷고 지도를 띄운다. <paramref name="fresh"/> 면 배를 리스본 앞바다에 새로 놓고,
    /// 아니면 적어 둔 기록(<see cref="GameSave"/>)을 되돌린다.
    /// </summary>
    /// <summary>
    /// NEW GAME — 초심자로 할지 새로 지을지 묻고, 새로 지으면 신상부터 받는다.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x0045EBE0</c> 이다.
    /// <code>
    ///   0x00552728  표 두 줄
    ///     0x00571A18  초심자용 주인공으로 시작한다(EASY)
    ///     0x00571A40  새로운 주인공으로 시작한다(NORMAL)
    ///   45ec61  EASY   → 0x0045E670  "시작할 주인공을 선택해 주십시오"
    ///   45ec6e            [0x5A4D1A] |= 8      ; 이 비트 때문에 나중에 은퇴를 못 한다
    ///   45ec7e  NORMAL → 0x0045BF80 신상 → 0x0045D6C0 능력치 → 0x0045DE20 지식·언어 → 0x0045E260
    /// </code>
    /// 우리는 아직 <b>NORMAL 의 첫 걸음(신상)</b>만 옮겼다. 능력치와 지식·언어는 우리 쪽에
    /// 보너스 포인트가 없어 그대로 시작한다. EASY 는 미리 만든 주인공 둘(라몬·데·마르시아스,
    /// 에밀리오·알발레스)이 STORY0/1.CDS 에 걸려 있어 아직 못 옮겼다 —
    /// 고르면 그 이름으로만 시작한다.
    ///
    /// 자세한 것은 볼트 <c>39.분석-NEW GAME(주인공 만들기와 은퇴)</c>.
    /// </remarks>
    private void NewGame()
    {
        // 게임도 여기부터는 메인메뉴를 걷는다 — 고르는 창이 그 자리에 뜬다.
        HideTitleMenu(true);

        // 적어 둔 판이 있으면 그것부터 어떻게 할지 묻는다.
        if (!BreakOff()) { HideTitleMenu(false); return; }

        // 앞 판이 묻어 오지 않게 주인공을 새로 앉힌다 — 새 놀이는 1480년 1월 1일부터다.
        // 짓다 말고 물러나면 하던 판을 도로 앉혀야 한다.
        var before = _game.NewPlayer();
        bool made = false;
        try
        {
            int at = ChoiceDialog.Ask(this, "NEW GAME",
                ["초심자용 주인공으로 시작한다(EASY)", "새로운 주인공으로 시작한다(NORMAL)"]);
            if (at < 0) return;

            if (at == 0)
            {
                // 미리 만든 주인공 둘. 이야기(STORY0/1.CDS)는 아직 안 읽어 이름만 쓴다.
                int who = ChoiceDialog.Ask(this, "시작할 주인공을 선택해 주십시오",
                                           ["라몬·데·마르시아스", "에밀리오·알발레스"]);
                if (who < 0) return;
                _game.Player.SetProfile(who == 0 ? "데·마르시아스" : "알발레스",
                                   who == 0 ? "라몬" : "에밀리오",
                                   25, 1, 1, 0, 0, who == 0 ? 0 : 1);
            }
            else if (!MakeCharacter())
            {
                return;
            }
            made = true;
        }
        finally
        {
            if (!made) _game.UsePlayer(before);
            // 물러났으면 메뉴가 도로 나와야 한다. 놀이로 들어갔으면 타이틀째로 사라진다.
            HideTitleMenu(false);
        }

        StartMap(fresh: true);
        OpenHome();
    }

    /// <summary>
    /// 주인공을 짓는 네 걸음. 어느 걸음에서 물러도 앞 걸음으로 되돌아간다.
    /// </summary>
    /// <remarks>
    /// 게임도 걸음마다 0 을 내면 한 걸음 되돌아간다(<c>0x0045EC7E</c> 벌).
    /// <code>
    ///   0x0045BF80  신상        → CharacterMakeDialog
    ///   0x0045D6C0  능력치·직업 → AbilityMakeDialog
    ///   0x0045DE20  기술·언어   → SkillMakeDialog
    ///   0x0045E260  마무리      → CharacterSheetDialog
    /// </code>
    /// </remarks>
    private bool MakeCharacter()
    {
        var rng = new Random();
        int step = 0;

        while (true)
            switch (step)
            {
                case 0:
                    if (!CharacterMakeDialog.Show(this, _game.Player, _game.Directory)) return false;
                    step = 1;
                    break;

                case 1:
                    step = AbilityMakeDialog.Show(this, _game.Player, rng) < 0 ? 0 : 2;
                    break;

                case 2:
                    // 보너스는 기술 화면이 제 손으로 센다 — 앞 걸음의 잔량이 아니다.
                    step = SkillMakeDialog.Show(this, _game.Player) ? 3 : 1;
                    break;

                default:
                    if (CharacterSheetDialog.Show(this, _game.Player)) return true;
                    step = 2;
                    break;
            }
    }
    /// <summary>
    /// 새 놀이는 <b>고른 국적의 자택</b>에서 시작한다 — 포르투갈이면 리스본,
    /// 에스파니아면 세빌리아다.
    /// </summary>
    private void OpenHome()
    {
        string want = _game.Player.Nation == 1 ? "세빌리아" : "리스본";
        var found = _game.CityTable.Cities.FirstOrDefault(c => c.Name == want);
        if (found.Name != want) return;

        if (!_host.PlaceAtCity(found.Id)) return;
        _askedCity = found.Id;                    // 곧바로 다시 묻지 않게
        _host.EnterPort(found.Name);
        if (ShowCityPicture(found.Id, found.Name)) _host.Paused = true;
    }

    /// <summary>
    /// <b>모험 중단</b> — 적어 둔 판이 있을 때 NEW GAME 이 먼저 묻는 것.
    /// </summary>
    /// <remarks>
    /// 게임은 <c>0x0045F60E</c> 에서 지금 놀고 있는 캐릭터가 있는지 보고, 있으면
    /// 이 창을 낸다(<c>0x0045F65B</c>).
    /// <code>
    ///   0045F65B  "현재 게임중의 캐릭터인 %s%s 있습니다만 어떻게 하겠습니까?"  제목 "모험 중단"
    ///   0045F66C  은퇴시킨다 · 삭제한다 · 신규작성을 중지한다
    ///   0045F700  은퇴 줄은 [0x005A4D1A] &amp; 0x40 — <b>누적 캐릭터 자리가 비어야</b> 켜진다
    ///   0045F8CE  삭제한다 → "[%s]%s 삭제합니다. 좋습니까?"
    ///   0045F8F2  YES 면 C:SAVEDATA.CDS · C:SAVEDATA.TMP · C:ACCDATA.CDS 를 지우고 만들기로
    /// </code>
    ///
    /// <b>지우는 것은 우리 세이브뿐이다</b>(<c>%APPDATA%\CdsHelper\SAVEDATA.CDS</c>).
    /// 게임 폴더의 SAVEDATA.CDS 는 사람이 진짜로 놀던 것이라 우리는 읽기만 한다 —
    /// 그것을 지우면 되돌릴 길이 없다.
    ///
    /// <b>은퇴는 아직 없다.</b> 누적 캐릭터 다섯 자리를 우리 쪽에 안 지어서(볼트
    /// <c>39.분석-NEW GAME</c>) 고르면 그 까닭만 이르고 물러난다.
    /// </remarks>
    /// <returns>새로 만들어도 되면 true, 물러났으면 false.</returns>
    private bool BreakOff()
    {
        var saved = GameSave.Load();
        if (saved == null) return true;

        string name = !string.IsNullOrEmpty(saved.Name) ? saved.Name : "이름 없는 제독";

        while (true)
        {
            ConfirmDialog.Tell(this,
                $"현재 게임중의 캐릭터인 {name}{GameUi.Josa(name, "이", "가")} 있습니다만 " +
                "어떻게 하겠습니까?", "모험 중단");

            int at = ChoiceDialog.Ask(this, "", ["은퇴시킨다", "삭제한다"], "신규작성을 중지한다");

            if (at == 0)
            {
                ConfirmDialog.Tell(this,
                    $"[{name}]{GameUi.Josa(name, "은", "는")} 은퇴시킬 수 없습니다. " +
                    "누적 캐릭터 자리가 아직 없습니다.", "모험 중단");
                continue;
            }

            if (at != 1) return false;      // 신규작성을 중지한다 · ESC

            if (!ConfirmDialog.Ask(this, $"[{name}]{GameUi.Josa(name, "을", "를")} 삭제합니다. 좋습니까?"))
                return false;

            if (GameSave.Delete()) return true;

            NoticeDialog.Show(this, "적어 둔 것을 지우지 못했습니다.");
            return false;
        }
    }

    /// <summary>
    /// 타이틀의 메인메뉴 상자를 걷거나 도로 낸다.
    /// </summary>
    /// <remarks>
    /// 게임은 NEW GAME 을 고르면 메인메뉴를 지우고 그 자리에 고르는 창을 낸다. 우리 타이틀은
    /// 무늬 바탕 위에 상자를 얹은 것이라 <b>상자만 감춘다</b> — 바탕은 그대로 남는다.
    /// </remarks>
    private void HideTitleMenu(bool hide)
    {
        if (_titleMenuBox != null)
            _titleMenuBox.Visibility = hide ? Visibility.Hidden : Visibility.Visible;
    }

    /// <summary>타이틀의 메인메뉴 상자. NEW GAME 으로 들어갈 때 잠깐 걷는다.</summary>
    private FrameworkElement? _titleMenuBox;

    /// <summary>
    /// 「지도를 본다」 — 도시 그림을 잠깐 걷고 지도만 본다.
    /// </summary>
    /// <remarks>
    /// 게임 커맨드의 그 줄이다(<c>0x0053BE98</c> · 아래에 <c>0x00533240</c> "항해지도" ·
    /// <c>0x00533250</c> "주변지도"). 게임은 지도 화면을 따로 그리는데 우리 지도는 그 자체가
    /// 세계지도라 <b>배율만 갈아 준다</b>. 도시에서 부르면 그림을 잠깐 감추고, 돌아가면
    /// 도로 낸다.
    /// </remarks>
    /// <param name="wide">참이면 항해지도(통째로), 거짓이면 주변지도(배 둘레).</param>
    /// <param name="hide">잠깐 감출 창. 도시 그림이다.</param>
    public void LookAtMap(bool wide, Window? hide = null)
    {
        if (wide) _host.ShowWorld(); else _host.ShowAround();

        if (hide == null) return;
        hide.Visibility = Visibility.Hidden;
        _host.InCity = false;

        // 지도 위에 "돌아간다" 한 줄만 띄운다 — 누르면 도시로 되돌아간다.
        var back = new GameMenuHost(this);
        back.Closed += () =>
        {
            hide.Visibility = Visibility.Visible;
            _host.InCity = true;
            hide.Activate();
        };
        back.Open(() => new GameMenu(wide ? "항해지도" : "주변지도", null,
            ("돌아간다", back.Close)));
    }

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
        _game.Bgm.Play(BgmPlayer.TitleTrack);
        _status.Text = "";
    }

    /// <summary>
    /// 적어 둔 판을 도로 불러온다 — 자택·여관의 <b>기능 · 로드</b> 가 부른다.
    /// </summary>
    /// <remarks>
    /// 게임도 그 자리에서 곧바로 불러온다(<c>0x004A2830</c>). 도시 창이며 명령 창이
    /// 떠 있으므로 먼저 걷는다 — 불러온 판은 세이브에 적힌 자리에서 다시 시작한다.
    /// </remarks>
    public void LoadGame()
    {
        foreach (var child in OwnedWindows.OfType<Window>().ToList()) child.Close();
        _overlay.IsOpen = false;
        _askedCity = -1;
        StartMap(fresh: false);
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

        if (string.IsNullOrEmpty(_game.Directory))
        {
            _status.Text = "세이브 파일 경로가 없습니다 — 먼저 세이브를 열어 주세요";
            return;
        }

        // 타이틀을 지을 때 게임 폴더를 몰랐을 수 있다. 여기서 한 번 더 챙긴다.
        LoadSprites();

        if (!_started)
        {
            // 바람 표는 달에 따라 갈린다. 지도가 날짜를 들고 있지 않으니 물어보게 해 둔다.
            _host.MonthOf = () => _game.Player.Date.Month;
            // 배가 얼마나 빨리 가는지는 함대와 돛 효율표가 정한다 — 지도는 그 둘을 모른다.
            _host.FleetSpeed = (dir, speed, heading, onLand) =>
                Sailing.SpeedOf(_game.Player, _game.Sails, dir, speed, heading, onLand);
            if (!_host.Start(_game.Directory)) { _status.Text = _host.Status; return; }
            _host.ShowFlowArrows = GameSettings.ShowFlowArrows;
            _started = true;
            _statusTimer.Start();
        }

        if (fresh)
        {
            _host.ResetToLisbon();
        }
        else if (saved != null)
        {
            _game.Player.Restore(saved.Gold, saved.Date, saved.CityId, saved.CityName,
                            saved.Skills, saved.Hints, saved.Mates, saved.Met, saved.Items,
                            saved.Supplies, saved.Discoveries, saved.Crew, saved.Announced,
                            saved.Stored, saved.Savings,
                            // 판 16 앞에는 식량·물도 통으로 적혔다.
                            supplyInBarrels: saved.Version < GameSave.SupplyUnitsFrom);
            _game.Player.RestoreFleet(saved.Ships, saved.Flagship, saved.Docked,
                                 saved.ShipHp, saved.DockedHp,
                                 saved.ShipStats, saved.DockedStats,
                                 saved.ShipNames, saved.DockedNames,
                                 gunsInStats: saved.Version >= GameSave.GunsInStatsFrom,
                                 sailsInStats: saved.Version >= GameSave.SailsInStatsFrom);
            _game.Player.RestoreMateBook(saved.MateBook);
            if (saved.Fatigue is { } tired) _game.Player.SetFatigue(tired);
            if (saved.DaysAtSea is { } atSea) _game.Player.SetDaysAtSea(atSea);
            // 밝힌 바다. 판 21 앞의 세이브에는 없어 빈 채로 시작한다.
            _game.Player.Explored.Restore(saved.Explored);
            // 아내와 후손. 판 22 앞의 세이브에는 없어 홀로 시작한다.
            _game.Player.RestoreFamily(saved.Spouse, saved.Heirs,
                                       saved.SpouseId ?? -1, saved.Liking);

            // 이름은 판 24 부터 적힌다 — 그 앞 세이브에서는 빈 채로 둔다.
            if (!string.IsNullOrEmpty(saved.Name)) _game.Player.Name = saved.Name;
            if (saved.Family != null) _game.Player.Family = saved.Family;
            if (saved.Given != null) _game.Player.Given = saved.Given;
            _game.Player.RestoreTongues(saved.Tongues);

            // 얼굴과 운명 코드는 판 25 부터 적힌다. 운명 코드가 없으면 얼굴 번호로
            // 물러선다 — 그때까지는 새 놀이가 앞의 열여섯만 고르게 해 둘이 같았다.
            if (saved.Face is { } face) _game.Player.Face = face;
            _game.Player.SetFortune(saved.Fortune ?? _game.Player.Face);
            if (saved.Morale is { } morale) _game.Player.SetMorale(morale);
            _game.Player.RestoreContract(GameSave.ContractOf(saved));
            if (saved.Fame is { } fame) _game.Player.Fame = fame;
            // 적어 둔 도시 앞바다에 배를 놓는다. 그 도시는 이미 들렀으니 곧바로 다시 묻지 않는다.
            if (saved.CityId >= 0 && _host.PlaceAtCity(saved.CityId)) _askedCity = saved.CityId;
            _status.Text = saved.CityId >= 0
                ? $"[{saved.CityName}] 에서 이어 간다 — {saved.Date:yyyy년 M월 d일}"
                : $"바다에서 이어 간다 — {saved.Date:yyyy년 M월 d일}";
        }

        _game.Bgm.Play(BgmPlayer.SeaTrack);
        SyncOverlay();

        // 적어 둔 자리가 도시면 도시 화면부터 연다. 바다에서 적었으면(CityId 가 -1) 그대로 둔다 —
        // 어디에서 적었는지는 그 값 하나로 갈린다.
        //
        // 지도가 자리를 잡은 뒤에 열어야 도시 그림이 지도 한가운데에 놓인다
        // (MapAreaOnScreen 이 아직 0 이면 엉뚱한 데 뜬다).
        if (!fresh && saved is { CityId: >= 0 })
        {
            int city = saved.CityId;
            string name = saved.CityName.Length > 0 ? saved.CityName : _game.CityName(city);
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (ShowCityPicture(city, name)) _host.Paused = true;
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    /// <summary>
    /// 개발 창 — 소지금과 명성을 손으로 넣고, 놀이에 없는 것들을 켜고 끈다.
    /// </summary>
    /// <remarks>
    /// 게임 상단 띠에 칸으로 두었던 것을 제목 줄 햄버거로 옮겼다. 놀이에는 없는 자리라
    /// 게임 띠에 섞여 있으면 원본과 달라 보인다 — 앱이 얹은 것은 앱 쪽 차림표에 둔다.
    /// </remarks>
    private void ShowDevDialog() => DevDialog.Show(this, _game.Player, new DevDialog.Options
    {
        PeopleOn = () => _peopleWanted,
        SetPeople = on =>
        {
            _peopleWanted = on;
            GameSettings.ShowPeopleOverlay = on;   // 다음에 켤 때도 그대로
            SyncOverlay();
        },
        CoordsOn = () => _overlayWanted,
        SetCoords = on =>
        {
            _overlayWanted = on;
            GameSettings.ShowCoordOverlay = on;   // 다음에 켤 때도 그대로
            SyncOverlay();
        },
        ToolBarOn = () => GameSettings.ShowToolBar,
        SetToolBar = on =>
        {
            GameSettings.ShowToolBar = on;
            if (_toolBar != null)
                _toolBar.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        },
        GameDirectory = _game.Directory,
    });

    /// <summary>
    /// 게임 커맨드 창을 흉내낸 우클릭 메뉴. 떠 있는 동안 <b>게임이 멈춘다</b> —
    /// 배도 시간도 그 자리에 선다(닻을 내리는 것과는 다르다. 닻은 그대로 두고 멈추기만 한다).
    /// </summary>
    /// <remarks>
    /// 제 창(HWND)으로 띄운다 — D3D 자식 창 위에 제대로 뜨고(airspace 를 안 탄다),
    /// 제목 줄을 잡아 <b>끌어 옮길 수 있다</b>. 도시정보 창과 같은
    /// <see cref="MenuWindow"/> 를 쓴다.
    ///
    /// 예전에는 <c>Popup</c> 이었는데 두 가지가 걸렸다. 옮길 수가 없었고, 닫힐 때 초점이
    /// 갈 데를 잃어 다른 앱으로 넘어갔다(팝업이 활성창을 가져가는데 지도는 WPF 가 모르는
    /// 자식 창이라 돌려줄 데가 없다). 주인을 둔 창은 닫히면 주인이 되살아나므로 둘 다 없다.
    /// </remarks>
    private void ShowCommandMenu(FrameworkElement anchor, Point at)
    {
        if (_host.SeaBlocked) return;
        if (CommandMenu.IsOpen) { CommandMenu.Focus(); return; }

        void Close() => CommandMenu.Close();

        // 바다에 있으면 상륙, 뭍에 있으면 출항. <b>갈 데가 없으면 줄 자체를 안 낸다</b> —
        // 흐린 줄로 남겨 두면 창 높이만 잡아먹고 게임에도 없는 모습이다.
        // 게임 커맨드 창에는 없는 줄이지만 이 창에서는 이것으로 뭍을 오간다.
        var items = new List<(string Text, Action? Run)>();
        if (_host.IsOnLand)
        {
            // 도시에 닿아 있으면 <b>맨 위</b>가 그 도시로 들어가는 줄이다. 다가갈 때 한 번
            // 물어보는 창(<see cref="CheckPort"/>)에서 아니오를 눌렀어도 이 줄로 다시 들어간다.
            int town = _host.NearestTown();
            if (town >= 0)
                items.Add(($"{_game.CityName(town)}에 들어간다", () => { Close(); EnterCity(town); }));

            if (_host.IsNearWater())
                items.Add(("출항", () => { if (_host.Embark()) _game.Bgm.Play(BgmPlayer.SeaTrack); Close(); }));
        }
        else if (_host.IsNearLand())
        {
            items.Add(("상륙", () => { if (_host.Land()) _game.Bgm.Play(BgmPlayer.LandTrack); Close(); }));
        }

        items.Add(("정보", () => CommandMenu.Push(InfoMenuBox)));
        items.Add(("편성", null));
        items.Add(("대열", null));
        items.Add(("항해일지를 본다", () => { Close(); ShowLogbook(); }));
        // 게임에는 없는 줄이다. 원본은 화살표 없이 물결로 해류를 보이는데, 지도로 읽을 때는
        // 방위를 바로 아는 편이 낫다 — 그래서 켜고 끌 수 있게 여기에 둔다.
        items.Add((_host.ShowFlowArrows ? "화살표를 감춘다" : "바람과 해류를 본다", () =>
        {
            bool on = !_host.ShowFlowArrows;
            _host.ShowFlowArrows = on;
            GameSettings.ShowFlowArrows = on;   // 다음에 켤 때도 그대로
            Close();
        }));
        items.Add(("기능", () => CommandMenu.Push(SeaSystemMenuBox)));
        items.Add(("취소", Close));

        // 넓히는 것은 GameUi 가 창을 지으며 한다 — 커맨드 창만이 아니라 도시 창·시설 창도
        // 같이 넓어야 모양이 맞는다.
        var box = new GameMenu("커맨드", null, [.. items]);

        CommandMenu.Open(() => box, ToScreen(anchor, at));
        _host.Paused = true;
    }

    /// <summary>
    /// 커맨드의 "정보" 아래 일곱 줄. 게임 <c>0x00425E40</c> 것 그대로다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   0x005331A8  함대정보 → 0x0046F340      0x005331D8  힌트정보
    ///   0x005331B8  인물정보 → 0x0046DF70      0x005331E8  계약정보
    ///   0x005331C8  소지품정보 → 0x0044CB20    0x005331F8  지도를 본다
    ///   0x00533210  돌아간다
    /// </code>
    /// 소지품·힌트·계약은 도시 커맨드에서 쓰던 창을 그대로 쓴다 — 게임도 한 창이다.
    /// "지도를 본다" 는 창이 한 겹 더 뜬다(<see cref="MapMenuBox"/>).
    /// </remarks>
    /// <summary>
    /// 바다 커맨드의 "기능" 에서 뻗는 창 — 게임 중단 · 게임 종료 · 취소.
    /// </summary>
    /// <remarks>
    /// <b>도시 안의 기능 창과 다르다.</b> 도시에서는 저장·로드·게임 종료·게임 재개가
    /// 나오는데(<see cref="GameSystemMenu"/>), 바다에서는 이 셋뿐이다 — 바다에서는
    /// 그냥 저장할 수 없고 <b>중단</b>으로만 적는다.
    /// <code>
    ///   0048b703  "게임 중단"                       0x0056FA38
    ///   0048b715  "게임 종료"                       0x0056FA48
    ///   0048b724  "취소"                            0x0056FA58
    ///   0048b731  창 제목 "기능"                     0x0056FA60
    ///   0048b75c  "지금 플레이하고 있는 게임을 중단하겠습니까?"   0x0056FA68
    ///   0048b779  "게임을 종료합니까?"                0x0056FA98
    /// </code>
    /// 예전에는 "기능" 이 곧바로 적고 마는 줄이었다.
    /// </remarks>
    private GameMenu SeaSystemMenuBox() => new("기능", null,
    [
        ("게임 중단", Suspend),
        ("게임 종료", () =>
        {
            if (!ConfirmDialog.Ask(CommandMenu.Window ?? this, "게임을 종료합니까?")) return;
            CommandMenu.Close();
            ReturnToTitle();
        }),
        ("취소", CommandMenu.Close),
    ]);

    /// <summary>
    /// "게임 중단" — 이 자리를 적고 첫 화면으로 돌아간다.
    /// </summary>
    /// <remarks>
    /// 게임도 중단은 <b>적고 나가는</b> 한 몸이다("이 시점에서 데이터를 저장하고 게임을
    /// 중단하겠습니다." <c>0x00568C80</c>). 적지 못했으면 나가지 않는다 — 나가 버리면
    /// 그 판이 그대로 사라진다.
    ///
    /// 적는 자리는 도시에서 적는 것과 같다. 도시에 들어가 있지 않으므로
    /// <see cref="Player.CityId"/> 가 -1 로 남는데, 그 값이 곧 "바다에서 적었다" 는 표시다.
    /// </remarks>
    private void Suspend()
    {
        var owner = CommandMenu.Window ?? this;
        if (!ConfirmDialog.Ask(owner, "지금 플레이하고 있는 게임을 중단하겠습니까?")) return;

        string error = GameSave.Save(_game.Player);
        if (error.Length > 0)
        {
            NoticeDialog.Show(owner, $"기록하지 못했다 — {error}");
            return;
        }

        CommandMenu.Close();
        ReturnToTitle();
    }

    private GameMenu InfoMenuBox() => new("정보", null,
    [
        // 바다에서는 함대좌표 칸에 지금 자리를 적는다. 도시 안이라면 게임처럼 "---" 다.
        ("함대정보", () => Info(() => FleetInfoDialog.Show(this, _game.Player, CoordLine(), _game.Items))),
        // 부하가 있으면 게임처럼 누구를 볼지 먼저 묻는다 — 도시 창과 한 벌이다.
        ("인물정보", () => PersonInfoMenu.Show(this, _game, CommandMenu)),
        ("소지품정보", () => Info(() => BelongingsDialog.Show(
            this, _game.Player, _game.Items, null, null, GameInfo.DiscoveryNames(_game)))),
        ("힌트정보", () => Info(() => HintListDialog.Show(this, GameInfo.HintNames(_game)))),
        ("계약정보", () => Info(ShowContract)),
        ("지도를 본다", () => CommandMenu.Push(MapMenuBox)),
        ("돌아간다", CommandMenu.Pop),
    ]);

    /// <summary>
    /// 「지도를 본다」에서 뻗는 창 — 항해지도 · 주변지도 · 돌아간다.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x0042602E</c> 자리다(<c>0x00533240</c> · <c>0x00533250</c> ·
    /// <c>0x00533260</c>, 창 제목 <c>0x00533270</c>).
    ///
    /// 둘은 아주 다른 그림이다. <b>항해지도</b>(<c>0x00416A00</c>)는 밝힌 자리만
    /// 드러나는 양피지 지도고, <b>주변지도</b>(<c>0x00416B60</c>)는 배 둘레를 여덟 배로
    /// 키워 도시와 아직 못 찾은 발견물까지 점으로 세운다. 둘 다 <see cref="SeaChartDialog"/>
    /// 가 낸다.
    /// </remarks>
    private GameMenu MapMenuBox() => new("지도를 본다", null,
    [
        ("항해지도", () => Info(() =>
            SeaChartDialog.ShowWorld(this, _host, _game.Player.Explored))),
        ("주변지도", () => Info(() =>
            SeaChartDialog.ShowAround(this, _host, _game.Discoveries, _game.Player))),
        ("돌아간다", CommandMenu.Pop),
    ]);

    /// <summary>함대정보 판의 함대좌표 줄. 게임 말투 그대로 "북위 38도 서경 9도" 다.</summary>
    private string CoordLine()
    {
        if (_host.SeaBlocked) return "";
        var (lat, lon) = _host.ShipLatLon;
        return $"{(lat >= 0 ? "북위" : "남위")} {Math.Abs(lat),3:F0}도" +
               $"  {(lon >= 0 ? "동경" : "서경")} {Math.Abs(lon),3:F0}도";
    }

    /// <summary>
    /// 계약 정보 판. 계약이 없으면 게임처럼 한 줄로 물린다.
    /// </summary>
    /// <remarks>판에 채울 것은 도시 창과 한 벌이다 — <see cref="GameInfo.ContractSheetOf"/>.</remarks>
    private void ShowContract()
    {
        var sheet = GameInfo.ContractSheetOf(_game);
        if (sheet.Contract == null)
        {
            NoticeDialog.Show(this, "계약을 맺지 않았습니다");
            return;
        }
        ContractDialog.Show(this, sheet.Contract, _game.Player.Date,
                            sheet.HintName, sheet.Found, sheet.Evidence);
    }

    /// <summary>
    /// 지도 아래 띠에 한마디 적는다. 게임이 창을 띄우지 않고 알리는 자리다.
    /// </summary>
    /// <remarks>도시 창처럼 이 창이 거느린 쪽에서도 부른다.</remarks>
    public void Say(string text) => _note.Text = text;

    /// <summary>정보 판 하나를 띄운다 — 커맨드 창은 접고, 배는 세워 둔 채다.</summary>
    private void Info(Action show)
    {
        CommandMenu.Close();
        show();
    }

    /// <summary>해상 커맨드 창. 하나만 띄운다.</summary>
    private GameMenuHost? _commandMenuHost;

    private GameMenuHost CommandMenu
    {
        get
        {
            if (_commandMenuHost != null) return _commandMenuHost;
            _commandMenuHost = new GameMenuHost(this);
            // 메뉴가 떠 있는 동안은 게임을 멈춘다. 닫히면 어떻게 닫혔든 다시 흐른다.
            _commandMenuHost.Closed += () => _host.Paused = false;
            return _commandMenuHost;
        }
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

        var name = _game.CityName(city);
        // 물음창이 떠 있는 동안 배가 계속 가면 대답할 새가 없다.
        _asking = true;
        _host.Paused = true;
        bool inCity = false;
        try
        {
            // 게임도 그냥 물음창이다 — 짙은 밤색 판에 흰 글씨, 아래에 YES/NO 둘.
            // 배면 "항구로", 말이면 "도시로" 로 갈아 낸다(문구는 하나다).
            if (ConfirmDialog.Ask(this,
                    $"[{name}]의 {(byLand ? "도시" : "항구")}로 들어가겠습니까?"))
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
    /// 묻지 않고 그 도시로 들어간다 — 커맨드의 "…에 들어간다" 가 부른다.
    /// </summary>
    /// <remarks>
    /// 다가갈 때 한 번 묻는 <see cref="CheckPort"/> 와 들어가는 대목은 같다. 다만 이쪽은
    /// 이미 고른 뒤라 다시 묻지 않고, 그 도시를 물어본 것으로 적어 둔다 — 안 그러면 도시
    /// 창을 닫자마자 물음창이 또 뜬다.
    /// </remarks>
    private void EnterCity(int city)
    {
        if (_asking) return;

        _askedCity = city;
        var name = _game.CityName(city);
        _asking = true;
        _host.Paused = true;
        bool inCity = false;
        try
        {
            _host.EnterPort(name);
            inCity = ShowCityPicture(city, name);
        }
        finally
        {
            if (!inCity)
            {
                _host.Paused = false;
                _asking = false;
            }
        }
    }

    /// <summary>
    /// 배(또는 말)가 선 칸에 발견물이 있으면 발견한다. 게임의 <c>0x0048D3F0</c> 자리다 —
    /// 그쪽도 항해 루프를 한 번 돌 때마다 이것을 한다.
    /// </summary>
    /// <remarks>
    /// 판정은 <see cref="DiscoveryLog.At"/> 가 하고, 여기서는 <b>언제 묻는지</b>만 맡는다.
    /// 창이 떠 있거나 멈춰 있으면 건너뛴다 — 도시 물음창과 겹쳐 뜨면 안 된다.
    ///
    /// 원본은 발견물마다 DISEV.CDS 의 사건을 틀지만 여기서는 알림 한 줄로 갈음한다.
    /// 문구는 게임의 <c>0x00538490</c> ("%s%s [%s]%s 발견했습니다") 그대로다.
    /// </remarks>
    /// <summary>
    /// 바다에서 날이 가게 한다. 하루가 넘으면 사건을 굴린다.
    /// </summary>
    /// <remarks>
    /// 게임은 함대를 한 걸음 옮길 때마다 하루를 넘기고 그때 사건을 본다. 우리 지도는
    /// 걸음이 훨씬 잦으므로(<c>0.1초</c>) <see cref="TicksPerDay"/> 걸음을 하루로 묶었다 —
    /// 리스본에서 카리브까지 오백 칸 남짓이 스물다섯 날쯤 된다.
    ///
    /// 마을에 들어가 있거나 멈춰 있는 동안에는 날이 안 간다.
    /// </remarks>
    private void PassTime()
    {
        if (_asking || _host.Paused || _host.SeaBlocked) return;
        if (_host.IsAnchored || _host.IsOnLand) return;

        if (++_dayTicks < TicksPerDay) return;
        _dayTicks = 0;

        _game.Player.PassDayAtSea();
        var (lat, _) = _host.ShipLatLon;
        Tell(SeaEvents.PassDay(_game.Player, lat, _game.Random));
        CheckSeaEvent();
    }

    /// <summary>
    /// 오늘 바다에서 있었던 일을 알린다 — 보급이 줄어든 것과 지친 것.
    /// </summary>
    /// <remarks>
    /// 문구는 게임 것 그대로다.
    /// <code>
    ///   0x00535550  "제독, %s%s얼마 남지 않았습니다!"   (물이/물도 · 식량이/식량도)
    ///   0x00535590  "제독, 물도 식량도 바닥을 드러내고 있습니다. 빨리 상륙하지 않으면 전멸입니다!"
    ///   0x005355E0  "제독, %s%s 바닥을 드러내고 있습니다, 빨리 상륙합시다!"
    ///   0x00535628  "선원들이 지쳐있습니다"                        (피로 50)
    ///   0x00535640  "선원들이 지쳐있습니다. 이제 상륙합시다!"        (피로 70)
    ///   0x00535668  "선원들의 피로가 한계에 달하고 있습니다. …"      (피로 90)
    /// </code>
    /// </remarks>
    private void Tell(SeaEvents.Day day)
    {
        var lines = new List<string>();

        if (day.WaterLow || day.FoodLow)
        {
            // 둘 다 모자라면 "도", 하나뿐이면 "이" 다. 게임도 그렇게 갈라 넣는다.
            bool both = day.WaterLow && day.FoodLow;
            string water = day.WaterLow ? (both ? "물도 " : "물이 ") : "";
            string food = day.FoodLow ? (both ? "식량도 " : "식량이 ") : "";
            lines.Add($"제독, {water}{food}얼마 남지 않았습니다!");
        }

        if (day.WaterOut && day.FoodOut)
            lines.Add("제독, 물도 식량도 바닥을 드러내고 있습니다. 빨리 상륙하지 않으면 전멸입니다!");
        else if (day.WaterOut || day.FoodOut)
        {
            string what = day.WaterOut ? "물" : "식량";
            lines.Add($"제독, {what}{GameUi.Josa(what, "이", "가")} 바닥을 드러내고 있습니다, " +
                      "빨리 상륙합시다!");
        }

        if (day.Weary > 0)
            lines.Add(day.Weary switch
            {
                50 => "선원들이 지쳐있습니다",
                70 => "선원들이 지쳐있습니다. 이제 상륙합시다!",
                _ => "선원들의 피로가 한계에 달하고 있습니다. 이대로라면 죽는 사람이 나오고 맙니다!",
            });

        if (lines.Count == 0) return;

        _asking = true;
        _host.Paused = true;
        try
        {
            foreach (string line in lines) NoticeDialog.Show(this, line);
        }
        finally
        {
            _host.Paused = false;
            _asking = false;
        }
    }

    /// <summary>한 걸음(<c>0.1초</c>) 몇 번을 하루로 세는지.</summary>
    private const int TicksPerDay = 20;

    private int _dayTicks;

    /// <summary>
    /// 오늘 바다에서 무슨 일이 있었는지 보고, 있으면 겪게 한다.
    /// </summary>
    /// <remarks>
    /// 판정은 <see cref="SeaEvents"/> 가 하고 여기서는 <b>보여 주는 것</b>만 맡는다.
    /// 문구는 게임 것 그대로다 — <c>0x00535178</c> "제, 제독, 큰일입니다! %s%s 오고
    /// 있습니다!!" · <c>0x005351B8</c> "빨리 돛을 접어라!…" · <c>0x005351F0</c> "제독 %s%s
    /// 눈에 띄지 않습니다…" · <c>0x00535260</c> "간신히 빠져 나왔습니다만…".
    ///
    /// 게임은 여기서 폭풍 장면(<c>0x0048E820</c>)을 틀지만 우리는 알림 줄로 갈음한다.
    /// </remarks>
    private void CheckSeaEvent()
    {
        var (lat, _) = _host.ShipLatLon;
        if (SeaEvents.Roll(_game.Player, lat, _game.Random) is not { } kind) return;

        if (kind == SeaEventKind.Mutiny) { Mutiny(); return; }
        if (Plagued(kind)) return;

        var storm = SeaEvents.Resolve(_game.Player, kind, _game.Random);

        _asking = true;
        _host.Paused = true;
        try
        {
            string word = storm.Word;
            NoticeDialog.Show(this,
                $"제, 제독, 큰일입니다! {word}{GameUi.Josa(word, "이", "가")} 오고 있습니다!!");
            NoticeDialog.Show(this, "빨리 돛을 접어라! 어떻게 해서든지 버텨라!!");

            if (storm.Lost.Count > 0)
            {
                string names = string.Join(", ", storm.Lost.Select(n => $"{n}호"));
                NoticeDialog.Show(this,
                    $"제독 {names}{GameUi.Josa(names, "이", "가")} 눈에 띄지 않습니다. " +
                    $"{word}에서 놓친 것 같습니다.");
            }
            else
            {
                NoticeDialog.Show(this, kind == SeaEventKind.Storm
                    ? "간신히 빠져 나왔습니다만, 선원들이 지쳐 있습니다. 어디서 휴양하는 것이 좋겠습니다."
                    : "간신히 빠져 나왔습니다만, 선원들이 얼어있습니다. 어딘가 상륙해서 몸을 녹이는 것이 좋을 것 같습니다.");
            }
        }
        finally
        {
            _host.Paused = false;
            _asking = false;
        }
    }

    /// <summary>
    /// 반란 — 선원 대표가 나서서 승부를 걸어 온다.
    /// </summary>
    /// <remarks>
    /// 문구는 게임 것 그대로다 — <c>0x00535330</c> "제독, 큰일입니다. %s%s 반란을
    /// 일으켰습니다!…" · <c>0x00535390</c> "제독, 이대로 %s%s 계속할 작정이라면…" ·
    /// <c>0x00535400</c> "그러니, 모두가 보는 앞에서 나와 승부하자!…" ·
    /// <c>0x005354A0</c> "반란을 진압했습니다".
    ///
    /// 배를 탔으면 "선원"(<c>0x00535320</c>)과 "항해"(<c>0x005353F0</c>)와
    /// "물고기"(<c>0x00535478</c>), 뭍이면 "대원"·"탐험"·"새" 로 갈린다.
    /// </remarks>
    /// <summary>
    /// 쥐 · 괴혈병 · 전염병과 그 귀띔. 맡았으면 true.
    /// </summary>
    /// <remarks>
    /// 문구는 게임 것 그대로다. 말하는 이는 부관이라 <b>부관 얼굴</b>이 함께 선다 —
    /// 게임도 <c>0x0047CC60(0, 0)</c> 으로 부하 첫 자리를 집어 넘긴다.
    /// <code>
    ///   0x00534D88  쥐        제독 큰일입니다! 쥐가 대량으로 발생했습니다…
    ///   0x00534F68  괴혈병    제독 큰일입니다! 선원들이 픽픽 쓰러지기 시작했습니다…
    ///   0x005350C8  전염병    제독, 큰일입니다! 유행병이 퍼지고 있습니다…
    ///   0x00534DE0  귀띔      제독! 모두 약해져 있습니다. 슬슬 상륙하는 것이 좋겠습니다.
    ///   0x00534FC8  귀띔      제독! 이상한 병이 돌고 있습니다. 상륙하는 것이 좋겠습니다.
    /// </code>
    /// 게임은 병이 돌면 선원을 하나씩 골라 이름을 부르며 죽이는데(<c>0x00534F30</c>
    /// "%s%s 괴혈병에 걸려…") 우리는 함대가 선원을 통째로 태우므로 머릿수만 던다.
    /// </remarks>
    private bool Plagued(SeaEventKind kind)
    {
        string word = kind switch
        {
            SeaEventKind.Rats =>
                "제독 큰일입니다! 쥐가 대량으로 발생했습니다. 어디 상륙해서 퇴치하는 것이 좋겠습니다!",
            SeaEventKind.Scurvy =>
                "제독 큰일입니다! 선원들이 픽픽 쓰러지기 시작했습니다. 어디 상륙해서 휴양하는 것이 좋겠습니다!",
            SeaEventKind.Plague =>
                "제독, 큰일입니다! 유행병이 퍼지고 있습니다. 어딘가 상륙하지 않으면 전멸입니다!",
            SeaEventKind.Weakening =>
                "제독! 모두 약해져 있습니다. 슬슬 상륙하는 것이 좋겠습니다.",
            SeaEventKind.StrangeIllness =>
                "제독! 이상한 병이 돌고 있습니다. 상륙하는 것이 좋겠습니다.",
            _ => "",
        };
        if (word.Length == 0) return false;

        int toll = SeaEvents.TollOf(kind, _game.Random);
        if (toll > 0) _game.Player.SetCrew(_game.Player.Crew - toll);
        if (kind == SeaEventKind.Rats)
            _game.Player.AddSupply(SupplyKind.Food, -SeaEvents.RatsEat(_game.Random));

        _asking = true;
        _host.Paused = true;
        try
        {
            ConfirmDialog.Tell(this, word, face: MateFace());
        }
        finally
        {
            _host.Paused = false;
            _asking = false;
        }
        return true;
    }

    /// <summary>부관 얼굴. 부관 자리가 비었거나 신상을 못 찾으면 null.</summary>
    private uint[]? MateFace()
    {
        string mate = _game.Player.MateAt(0);
        if (mate.Length == 0) return null;
        return _game.MateInfo(mate) is { Face: >= 0 and < 0xFFFF } who
            ? _game.Faces?.TryGetBgra(who.Face, female: false) : null;
    }

    private void Mutiny()
    {
        bool land = _host.IsOnLand;
        string who = land ? "대원" : "선원";
        string what = land ? "탐험" : "항해";
        string beast = land ? "새" : "물고기";

        _asking = true;
        _host.Paused = true;
        try
        {
            NoticeDialog.Show(this,
                $"제독, 큰일입니다. {who}{GameUi.Josa(who, "이", "가")} 반란을 일으켰습니다!  " +
                $"{who}의 대표가 제독께 할 이야기가 있다고 합니다!");
            NoticeDialog.Show(this,
                $"제독, 이대로 {what}{GameUi.Josa(what, "을", "를")} 계속할 작정이라면 우리들은 " +
                "전멸이다. 우리들은 당신과 함께 죽을 마음이 없다.");
            NoticeDialog.Show(this,
                "그러니, 모두가 보는 앞에서 나와 승부하자! 당신이 이기면 얌전히 따르겠다. " +
                $"그러나, 내가 이기면 {beast}의 먹이가 될 줄 알아라.");

            var fight = SeaEvents.Duel(_game.Player, _game.Random);
            if (fight.Won)
            {
                NoticeDialog.Show(this, "이것으로 불만 없겠지!");
                NoticeDialog.Show(this, "반란을 진압했습니다");
            }
            else
            {
                // 게임은 여기서 놀이를 끝낸다(0x0044AF40 상태 4). 우리는 끝나는 길이 없어
                // 선원을 잃는 것으로 갈음한다 — SeaEvents.Duel 에 적어 두었다.
                NoticeDialog.Show(this,
                    $"{who} {fight.Deserted}명이 배를 버리고 떠났습니다!");
            }
        }
        finally
        {
            _host.Paused = false;
            _asking = false;
        }
    }

    /// <summary>
    /// 바람 칸의 글 — 풍향·풍속과 상대각, 그리고 그 바람이 내는 함대 속도.
    /// </summary>
    /// <remarks>
    /// 게임 띠에는 없는 칸이다. 돛 효율표가 상대각으로 갈리는 것이 눈에 보여야 삼각돛·
    /// 사각돛을 고르는 뜻이 생겨서 뒀다 — <b>0 이 정순풍, 8 이 정면 역풍</b>이다.
    /// </remarks>
    private string WindLine()
    {
        var (dir, speed, relative) = _host.LastWind;
        string where = ShipMapHost.Compass[(dir & 0xF) >> 1];
        return $"바람 {where} {speed}  각 {relative,2}  속도 {_host.LastSpeed,3}";
    }

    /// <summary>
    /// 배가 선 자리 둘레를 항해지도에 밝힌다. 게임의 <c>0x00468D90</c> 자리다 —
    /// 지금 칸을 가운데로 반지름만큼 원을 칠한다.
    /// </summary>
    private void MarkSeen()
    {
        if (_host.ShipCell is not { } cell) return;
        _game.Player.Explored.Mark(cell.CellX, cell.CellY);
    }

    private void CheckDiscovery()
    {
        if (_asking || _host.Paused || _host.SeaBlocked) return;
        if (_game.Discoveries is not { } log) return;
        if (_host.ShipCell is not { } cell) return;

        int id = log.At(_game.Player, cell.CellX, cell.CellY, _host.IsOnLand);
        if (id < 0) return;
        if (log.Table.Find(id) is not { } row) return;

        int item = log.Discover(_game.Player, id);

        // 알리는 동안 배가 계속 가면 다음 칸에서 또 뜬다.
        _asking = true;
        _host.Paused = true;
        try
        {
            string me = _game.Player.Name;

            // 게임은 글만 내지 않는다 — 발견물마다 그림(DSTILL) 아니면 동영상(AVI)이 있다.
            DiscoveryDialog.Show(this, _game.Stills, row.Picture,
                $"{me}{GameUi.Josa(me, "은", "는")} [{row.Name}]{GameUi.Josa(row.Name, "을", "를")} 발견했습니다",
                DiscoveryDialog.MovieOf(_game.Directory, row.Movie));

            if (item >= 0)
            {
                string got = _game.Items?.Find(item)?.Name ?? $"아이템 {item}";
                NoticeDialog.Show(this, $"[{got}]{GameUi.Josa(got, "을", "를")} 손에 넣었다");
            }
        }
        finally
        {
            _host.Paused = false;
            _asking = false;
        }
    }

    /// <summary>
    /// 지금까지 발견한 것을 늘어놓는다. 게임 커맨드의 "항해일지를 본다" 자리다 —
    /// 원본 일지에는 더 많은 것이 적히지만 지금 적히는 것은 발견물뿐이다.
    /// </summary>
    private void ShowLogbook()
    {
        var log = _game.Discoveries;
        var lines = _game.Player.Discoveries
            .Order()
            .Select(id => log?.Table.Find(id) is { } row
                        ? $"{row.CategoryName}  {row.Name}"
                        : $"발견물 {id}")
            .ToList();

        HintListDialog.Show(this, lines, "발견물 일람", "아직 발견한 것이 없다.");
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
        // 그림도 건물 표도 Game 이 처음 쓸 때 연다. 둘 중 하나라도 없으면 도시 화면을 안 연다.
        if (_game.CityPics == null || _game.Buildings == null) return false;

        // 도는 곡은 문화권마다 다르다 — 세우타 같은 중근동 도시는 딴 곡이다.
        // 문화권은 건물에 들어갈 때 뜨는 타원 사진을 고르는 데도 쓴다(BuildingPhoto).
        string culture = _game.CultureOf(city);
        int track = BgmPlayer.CityTrackFor(culture);

        var dialog = CityPicView.Open(this, _game, city, name, MapAreaOnScreen(), track, culture);
        if (dialog == null) return false;

        _game.Bgm.Play(track);
        SetInCity(true);          // 지도에 남색 막을 씌운다(그림 창과는 따로 논다)
        _game.Player.EnterCity(city, name);
        dialog.Closed += (_, _) =>
        {
            SetInCity(false);

            // 성문으로 나섰으면 뭍에 올라 말로 걷는다 — 곡도 뭍 것으로 바뀐다.
            bool walking = dialog.Explored && _host.Land();
            _game.Bgm.Play(walking ? BgmPlayer.LandTrack : BgmPlayer.SeaTrack);
            _host.Paused = false;
            _asking = false;
            _game.Player.EnterCity(-1);
            InfoMenu.Close();        // 도시를 나오면 도시정보 창도 같이 걷는다
        };
        return true;
    }

    /// <summary>
    /// 게임 원본 화면 조각과 비트맵 글꼴을 한 번만 읽어 <see cref="GameUi"/> 에 넣는다.
    /// 게임 폴더를 아직 모르면 그냥 넘어간다 — 세이브를 열면 다시 부른다.
    /// </summary>
    private void LoadSprites()
    {
        if (_spritesTried || string.IsNullOrEmpty(_game.Directory)) return;
        _spritesTried = true;

        GameUi.Sprites = UiSprites.Open(_game.Directory);
        if (GameUi.Sprites == null)
            System.Diagnostics.Debug.WriteLine($"[ShipMap] 화면 조각 없음: {UiSprites.LastError}");

        GameUi.Font = GameFont.Open(_game.Directory);
        if (GameUi.Font == null)
            System.Diagnostics.Debug.WriteLine($"[ShipMap] 게임 글꼴 없음: {GameFont.LastError}");
    }

    private bool _spritesTried;

    /// <summary>
    /// 게임 폴더를 잡고 타이틀 곡을 튼다. 지도는 아직 띄우지 않는다 —
    /// 메뉴에서 NEW/LOAD 를 골라야 <see cref="StartMap"/> 로 넘어간다.
    /// </summary>
    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        // <b>작업표시줄 단추를 하나로 둔다.</b> 뷰어에서 띄운 것이면 이 창까지
        // 단추를 갖는데, 대화 창을 여닫을 때마다 활성 창이 두 단추 사이를 오가는 것이
        // 눈에 보인다 — WPF 의 ShowDialog 가 앱의 창을 다 잠갔다 푸는 통에 활성 창이
        // 잠깐 주인 쪽으로 넘어가기 때문이다. 주인이 없으면(놀이 전용 exe) 제 단추를
        // 갖는다.
        ShowInTaskbar = Owner == null;

        var dir = Path.GetDirectoryName(AppSettings.LastSaveFilePath);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
        {
            // 곡을 못 트는 흔한 까닭이 이것이다 — 세이브를 한 번도 안 열었으면 게임 폴더를 모른다.
            _status.Text = "세이브 파일 경로가 없습니다 — 먼저 세이브를 열어 주세요";
            System.Diagnostics.Debug.WriteLine("[ShipMap] 게임 폴더를 몰라 BGM 을 못 틉니다");
            return;
        }

        _game.SetDirectory(dir);

        // 타이틀 화면은 생성자에서 지었는데, 그때는 게임 폴더를 몰라 원본 조각도 글꼴도
        // 없었다(민색 상자로 물러선 채였다). 이제 알았으니 다시 짓는다.
        LoadSprites();
        if (_titleRoot != null && ReferenceEquals(_screen.Content, _titleRoot))
        {
            // 묶음은 BuildTitleScreen 이 새로 잡는다 — 새 줄에 초점이 다시 간다.
            _titleRoot = BuildTitleScreen();
            _screen.Content = _titleRoot;
        }

        _game.Bgm.Enabled = GameSettings.BgmEnabled;   // 설정 창에서 꺼 뒀으면 조용히 시작한다
        _game.Bgm.Play(BgmPlayer.TitleTrack);   // 메뉴 화면에서는 bgm/Track23.mp3
        if (_game.Bgm.LastError.Length > 0)
        {
            _status.Text = _game.Bgm.LastError;
            System.Diagnostics.Debug.WriteLine($"[ShipMap] BGM — {_game.Bgm.LastError}");
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
