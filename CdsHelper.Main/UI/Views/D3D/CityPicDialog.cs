using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Support.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Main.UI.Views.D3D;

/// <summary>
/// 입항한 도시의 그림(CITYCG.CDS)을 지도 한가운데에 띄운다. 게임처럼 건물(항구·조선소)을
/// 누르면 그 건물의 명령 창이 열린다.
/// </summary>
/// <remarks>
/// <see cref="PortDialog"/> 와 같은 수를 쓴다 — 창(HWND)을 따로 쓰므로 D3D 자식 창 위에
/// 제대로 뜬다(airspace 를 안 탄다). 그림은 400x320 도트 그림이라 정수배로만 늘린다.
///
/// 건물 자리·이름·가르치는 기능은 게임 EXE 의 건물 표(<see cref="CityBuildingTable"/>)에서
/// 그대로 온다. 표에 항구가 없는 도시라면 그림 아무 데나 눌러도 항구 명령 창이 열리게 해
/// 두었다 — 출항할 길은 어디서나 있어야 한다.
/// </remarks>
public sealed class CityPicDialog : Window
{
    /// <summary>건물 이름표와 명령 창을 얹는 자리. 그림과 같은 격자 칸에 둔다.</summary>
    private readonly Canvas _layer = new();

    /// <summary>지금 열린 명령 창이 들어앉는 자리. 그림 한가운데에 놓는다.</summary>
    private readonly Border _menuHost = new() { Visibility = Visibility.Collapsed };

    /// <summary>건물 이름표들. 명령 창이 열리면 다 감춘다.</summary>
    private readonly List<Border> _tags = [];

    /// <summary>배를 사면 여기서 돈이 빠진다.</summary>
    private readonly Player _player;

    /// <summary>건물에 들어갈 때 곡을 바꾼다. 없으면(시험용) 아무것도 안 한다.</summary>
    private readonly BgmPlayer? _bgm;

    /// <summary>건물 표. 가르치는 기능을 이름으로 풀 때 쓴다.</summary>
    private readonly CityBuildingTable _table;

    /// <summary>힌트 표(CDS_95.EXE). 왕궁 설득에서 등급·자금·기한이 여기서 온다.</summary>
    private HintTable? _hintTable;
    private bool _hintTableTried;

    /// <summary>후원자 자료(patrons.json). 한 번만 읽어 둔다.</summary>
    private static List<Patron>? _patrons;

    // 도서관 열람에 쓰는 것들. 책 표를 못 읽었으면 열람 줄이 흐린 채로 남는다.
    private readonly BookTable? _library;
    private readonly Func<int, string> _hintName;
    private readonly string _gameDirectory;
    private readonly string _cityName;
    private readonly int _cityId;

    /// <summary>출항을 골랐는지. 창을 그냥 닫으면 false.</summary>
    public bool Sailed { get; private set; }

    private CityPicDialog(string cityName, BitmapSource picture, int scale, int cityId,
                          CityBuildingTable table, Player player, BgmPlayer? bgm, Rect mapArea,
                          BookTable? library, Func<int, string>? hintName, string gameDirectory)
    {
        _player = player;
        _bgm = bgm;
        _table = table;
        _library = library;
        _hintName = hintName ?? (h => $"힌트 {h}");
        _gameDirectory = gameDirectory;
        _cityName = cityName;
        _cityId = cityId;

        Title = cityName;                 // 화면에는 안 나온다 — 창 목록에서만 쓴다
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        Background = Brushes.Black;       // 그림에 가려 안 보인다

        // 지도를 덮는 남색 막은 이 창이 아니라 지도(D3D) 쪽에서 씌운다 — 그래야 이 그림을
        // 끌어 옮겨도 막이 따라오지 않는다. 게임도 막과 그림이 따로다.
        // 처음 자리는 게임처럼 지도 한가운데다(옮기는 것은 손으로).
        if (mapArea.Width > 0 && mapArea.Height > 0)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            double w = CityPictures.Width * scale, h = CityPictures.Height * scale;
            Left = mapArea.X + (mapArea.Width - w) / 2;
            Top = mapArea.Y + (mapArea.Height - h) / 2;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        var image = new Image
        {
            Source = picture,
            Width = CityPictures.Width * scale,
            Height = CityPictures.Height * scale,
            Stretch = Stretch.Fill,
        };
        // 도트 그림이라 늘릴 때 섞으면 뭉개진다 — 게임 화면처럼 각을 살린다.
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);

        var picBox = new Grid
        {
            Width = image.Width,
            Height = image.Height,
            Children = { image, _layer },
        };
        _layer.Children.Add(_menuHost);
        // 명령 창은 건물 판보다 위에 둔다 — 겹치면 투명한 판이 누르기를 가로챈다.
        Panel.SetZIndex(_menuHost, 10);
        // 명령 창을 누른 것도 여기서 삼킨다 — 안 그러면 그림 끌기가 먼저 걸린다.
        _menuHost.MouseLeftButtonDown += (_, e) => e.Handled = true;

        // 게임 건물 표에 적힌 그대로 얹는다 — 그 도시에 있는 건물만, 게임이 쓰는 자리에.
        bool harborPlaced = false;
        foreach (var building in table.InCity(cityId))
        {
            AddSpot(building, scale);
            if (building.Kind == "항구") harborPlaced = true;
        }

        // 표에 항구가 없는 도시는 아무 데나 눌러도 항구 명령 창이 열린다(건물 판이 먼저 먹는다).
        if (!harborPlaced)
        {
            var harbor = Facility.For("항구");
            picBox.Cursor = Cursors.Hand;
            picBox.MouseLeftButtonUp += (_, _) =>
                ShowMenu(BuildMenu(harbor, harbor.Name, 0), harbor.BgmTrack);
        }

        // 게임 화면에는 제목 줄도 안내 줄도 없다. 그림 한 장이 곧 창이다.
        Content = picBox;

        // 제목 줄이 없어도 옮길 수는 있어야 한다 — 그림의 아무 데나 잡으면 끌린다.
        // 건물 판과 명령 창은 누르는 자리라 제 몫으로 삼키므로 여기까지 오지 않는다.
        // 항구를 못 찾은 그림은 그림 전체가 누르는 자리라 끌기를 달지 않는다.
        if (harborPlaced)
            picBox.MouseLeftButtonDown += (_, _) =>
            {
                if (Mouse.LeftButton == MouseButtonState.Pressed) DragMove();
            };

        // 그림을 옮기면 옆에 붙은 커맨드 창도 같이 옮긴다. 함대 창이 옮겨져 이 그림이
        // 끌려갈 때에도 같은 길로 이어진다.
        GameUi.CarryOwnedWindows(this);

        // 오른쪽 단추는 게임처럼 도시 커맨드 창을 연다. 창을 닫는 것은 ESC 다.
        KeyDown += (_, e) => { if (e.Key is Key.Escape) Close(); };
        MouseRightButtonUp += (_, e) => { e.Handled = true; ShowCityMenu(cityName); };
        Closed += (_, _) => CloseCityMenu();   // 그림 창을 닫으면 커맨드 창도 같이 닫는다
    }

    /// <summary>
    /// 건물 하나를 누를 수 있게 한다. 커서를 올리면 이름표가 밑에 뜨고, 누르면 명령 창이 열린다.
    /// </summary>
    private void AddSpot(CityBuildingTable.Building building, int scale)
    {
        var facility = Facility.For(building.Kind);
        var tag = GameUi.NameTag(building.Kind);   // 지도 이름표에는 종류가 뜬다("술집")
        _layer.Children.Add(tag);
        _tags.Add(tag);

        // 표의 상자는 96x80 이라 건물끼리 겹친다. 가운데는 그대로 두고 누를 자리만 좁힌다.
        var a = new Rect(building.CenterX - HitWidth / 2.0, building.CenterY - HitHeight / 2.0,
                         HitWidth, HitHeight);
        var spot = new Border
        {
            Width = a.Width * scale,
            Height = a.Height * scale,
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
        };
        Canvas.SetLeft(spot, a.X * scale);
        Canvas.SetTop(spot, a.Y * scale);
        spot.MouseEnter += (_, _) => ShowTag(tag, a, scale);
        spot.MouseLeave += (_, _) => tag.Visibility = Visibility.Collapsed;
        // 건물을 누른 것은 여기서 삼킨다 — 안 그러면 그림 끌기가 먼저 걸려 메뉴가 안 열린다.
        spot.MouseLeftButtonDown += (_, e) => e.Handled = true;
        spot.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            // 명령 창 제목은 건물 이름이다 — 게임도 "베렌의 탑", "홍경정" 으로 낸다.
            ShowMenu(BuildMenu(facility, building.Name, building.TeachMask), facility.BgmTrack);
        };
        _layer.Children.Add(spot);
    }

    /// <summary>누를 자리의 크기(그림 점). 건물끼리 겹치지 않을 만큼만 잡았다.</summary>
    private const int HitWidth = 44, HitHeight = 38;

    /// <summary>건물 밑에 이름표를 띄운다. 게임도 건물 밑에 붙여 준다.</summary>
    private void ShowTag(Border tag, Rect area, int scale)
    {
        tag.Visibility = Visibility.Visible;
        tag.UpdateLayout();
        double w = tag.ActualWidth > 0 ? tag.ActualWidth : 52;
        double x = (area.X + area.Width / 2) * scale - w / 2;
        Canvas.SetLeft(tag, Math.Clamp(x, 0, Math.Max(0, CityPictures.Width * scale - w)));
        Canvas.SetTop(tag, (area.Y + area.Height) * scale + 2);
    }

    /// <summary>
    /// 명령 창을 연다. 건물마다 도는 곡이 다르면 <paramref name="track"/> 으로 준다 —
    /// 안 주면 도시 곡으로 돌아간다(다른 건물로 옮겨 갈 때 술집 곡이 따라오지 않게).
    /// </summary>
    private void ShowMenu(UIElement box, int? track = null)
    {
        foreach (var tag in _tags) tag.Visibility = Visibility.Collapsed;
        _menuHost.Child = box;
        _menuHost.Visibility = Visibility.Visible;
        _menuHost.UpdateLayout();
        CenterMenu();
        _bgm?.Play(track ?? BgmPlayer.CityTrack);
    }

    /// <summary>명령 창을 닫고 도시로 돌아간다 — 곡도 도시 것으로 되돌린다.</summary>
    private void CloseMenu()
    {
        _menuHost.Visibility = Visibility.Collapsed;
        _bgm?.Play(BgmPlayer.CityTrack);
    }

    private void CenterMenu()
    {
        if (_layer.ActualWidth <= 0) return;
        Canvas.SetLeft(_menuHost, (_layer.ActualWidth - _menuHost.ActualWidth) / 2);
        Canvas.SetTop(_menuHost, (_layer.ActualHeight - _menuHost.ActualHeight) / 2);
    }

    /// <summary>
    /// 시설에서 "기능" 을 골랐을 때 뜨는 창. 제목이 없고 줄만 넷이다 —
    /// 게임 재개를 고르면 하던 화면으로 돌아간다. 저장·로드는 아직 흉내내지 않는다.
    /// </summary>
    private Border SystemMenu() => GameUi.MenuBox(
        [.. Facility.SystemMenu.Select(item => (item, SystemAction(item)))]);

    private Action? SystemAction(string item) => item switch
    {
        "저장" => SaveGame,
        "게임 재개" => CloseMenu,
        _ => null,
    };

    /// <summary>
    /// 지금 상태(소지금·날짜·있는 도시·배운 기술)를 적는다.
    /// 게임 폴더가 아니라 우리 자리에 쓴다 — <see cref="GameSave"/> 참고.
    /// </summary>
    private void SaveGame()
    {
        var error = GameSave.Save(_player);
        NoticeDialog.Show(this, error.Length == 0 ? "기록했다!" : $"기록하지 못했다 — {error}");
    }

    /// <summary>
    /// 도시 커맨드 창. 도시 화면에서 오른쪽 단추를 누르면 뜬다 — 제목은 도시 이름이고
    /// 제목 줄에 닫기(X)가 있다. 지금은 취소만 살아 있다.
    /// </summary>
    private Border CityMenu(string cityName) => GameUi.CommandBox(cityName, CloseCityMenu,
        ("맵 포인트에 들어간다", null),
        ("인물 정보", null),
        ("함대 정보", null),
        ("소지품 정보", null),
        ("도시 정보", null),
        ("힌트 정보", ShowHints),
        ("계약 정보", null),
        ("후원자 정보", null),
        ("지도를 본다", null),
        ("게임 종료", null),
        ("취소", CloseCityMenu));

    /// <summary>도시 커맨드 창. 그림 안이 아니라 그림 창 옆에 제 창으로 띄운다.</summary>
    private MenuWindow? _cityMenu;

    private void ShowCityMenu(string cityName)
    {
        if (_cityMenu != null) { _cityMenu.Activate(); return; }
        _cityMenu = MenuWindow.ShowBeside(this, CityMenu(cityName));
        _cityMenu.Closed += (_, _) => _cityMenu = null;
    }

    private void CloseCityMenu() => _cityMenu?.Close();

    /// <summary>
    /// 왕궁의 "설득" — 후원자에게 힌트를 내밀어 자금을 받아 낸다.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x004AEF50</c> 을 따라간다. 차례가 이렇다.
    /// <list type="number">
    /// <item>후원자를 찾는다. 없으면 "이 마을에는 아는 스폰서가 없습니다".</item>
    /// <item>힌트를 고르게 한다(<c>0x004AE8E0</c>). 안 고르면 "용건이 없는가".</item>
    /// <item>안목이 힌트 등급에 못 미치면 물린다 — "이야기가 막연하네".
    ///       게임 판정은 <c>안목/20 + (등급==5 ? 1 : 2) &gt;= 등급</c> 이다(<c>0x004AF0F0</c>).</item>
    /// <item>재력이 낼 돈에 못 미치면 물린다 — "돈이…".</item>
    /// <item>통과하면 선금·기한·사례금을 걸고 승낙을 묻는다.</item>
    /// </list>
    /// 대사는 게임 EXE 에 있는 말을 그대로 옮겼다(<c>0x00546778</c>~<c>0x00546D80</c>).
    /// 게임은 신분(왕궁·총독부·성)에 따라 세 벌을 갈아 쓰는데, 여기서는 왕궁 것 한 벌만 쓴다.
    ///
    /// <b>아직 안 되는 것</b> — 계약을 맺어 두지 않는다. 승낙해도 돈이 들어오거나 기한이
    /// 걸리지 않는다. 계약 상태를 어디에 적어 둘지(세이브 자리)를 아직 못 풀었다.
    /// </remarks>
    private void Persuade()
    {
        var patron = FindPatron();
        if (patron == null)
        {
            NoticeDialog.Show(this, "이 마을에는 아는 스폰서가 없습니다");
            return;
        }

        // 얻은 힌트만 내밀 수 있다. 게임도 상태가 맞는 것만 목록에 올린다(0x0044E7B0).
        var mine = _player.Hints.Order().ToList();
        var names = mine.Select(HintNameOf).ToList();
        int row = HintListDialog.Pick(this, names);
        if (row < 0)
        {
            NoticeDialog.Show(this, "뭔가, 용건이 없는가? 이쪽은 바쁘네, 빨리 나가주게.");
            return;
        }

        var hint = Hints()?.Find(mine[row]);
        if (hint == null)
        {
            NoticeDialog.Show(this, "흠, 원조해 주고 싶은 마음은 많지만.");
            return;
        }

        var it = hint.Value;

        // 안목 판정 — 후원자가 이야기의 크기를 가늠하지 못하면 물린다.
        if (patron.Discernment / 20 + (it.Grade == 5 ? 1 : 2) < it.Grade)
        {
            NoticeDialog.Show(this, "가능한 한 원조해 주고 싶지만, 너무나 이야기가 막연하네.");
            return;
        }

        int funds = HintTable.FundsFor(it, patron.SupportRatePercent);

        // 재력 판정 — 낼 돈이 없으면 물린다.
        if (patron.Wealth < funds)
        {
            NoticeDialog.Show(this, "원조는 해 주고 싶지만, 흐~음, 돈이... , 또 다음번이다.");
            return;
        }

        // 좋아하는 갈래면 사례가 후하다. 게임에도 후원자마다 좋아하는 갈래가 적혀 있다.
        int reward = patron.Likes(it.Category) ? funds * 2 : funds;

        bool yes = ConfirmDialog.Ask(this,
            $"모험하는데 돈은 필요하겠지. 먼저 금화 {funds}닢을 주겠다. " +
            $"{it.Deadline}년 내에 성공하면 {reward}닢의 사례를 약속하겠네. 이것으로 어떤가.\n\n" +
            $" 기간{it.Deadline}년 금화 {funds}닢 ");
        if (!yes) return;

        NoticeDialog.Show(this,
            "그러면, 기대하고 있겠네. 훌륭히 성공을 거두고 돌아오게.\n" +
            "(계약을 적어 두는 것은 아직 흉내내지 못한다)");
    }

    /// <summary>이 도시의 후원자. 여럿이면 고르게 하고, 없으면 null.</summary>
    private Patron? FindPatron()
    {
        var all = LoadPatrons();
        var here = new PatronService().ActiveInCity(all, _cityName, _player.Date.Year);
        if (here.Count == 0) return null;
        if (here.Count == 1) return here[0];

        var labels = here.Select(p => $"{p.Name} ({p.Occupation})").ToList();
        int pick = HintListDialog.Pick(this, labels, "스폰서 일람",
                                       "이 마을에는 아는 스폰서가 없습니다");
        return pick < 0 ? null : here[pick];
    }

    /// <summary>힌트 이름. 게임 표를 읽었으면 그것으로, 아니면 DB 이름으로 물러선다.</summary>
    private string HintNameOf(int id) => _hintTable?.NameOf(id) ?? _hintName(id);

    /// <summary>힌트 표를 처음 쓸 때 연다. 못 읽으면 이름만 DB 것으로 물러선다.</summary>
    private HintTable? Hints()
    {
        if (_hintTable != null || _hintTableTried) return _hintTable;
        _hintTableTried = true;
        if (_gameDirectory.Length == 0) return null;
        _hintTable = HintTable.Open(_gameDirectory);
        if (_hintTable == null)
            System.Diagnostics.Debug.WriteLine($"[City] 힌트 표 없음: {HintTable.LastError}");
        return _hintTable;
    }

    /// <summary>후원자 자료. 못 읽으면 빈 목록이다 — 그렇다고 도시 화면까지 막을 일은 아니다.</summary>
    private static List<Patron> LoadPatrons()
    {
        if (_patrons != null) return _patrons;
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "patrons.json");
            _patrons = new PatronService().LoadPatrons(path);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[City] 후원자 자료 없음: {ex.Message}");
            _patrons = [];
        }
        return _patrons;
    }

    /// <summary>얻은 힌트를 늘어놓는다. 이름은 DB 에서 온다.</summary>
    private void ShowHints() =>
        HintListDialog.Show(_cityMenu ?? (Window)this,
                            [.. _player.Hints.Order().Select(_hintName)]);

    /// <summary>
    /// 시설의 명령 창을 짓는다. 줄은 <see cref="Facility"/> 표에서 오고, 그중 흉내낼 수 있는
    /// 것만 <see cref="ActionFor"/> 가 손을 달아 준다. 나머지는 흐린 채로 둔다.
    /// 제목은 건물 이름이다(게임도 그렇다).
    /// </summary>
    private Border BuildMenu(Facility facility, string title, uint teachMask)
    {
        var items = facility.Menu.ToList();
        // 가르치는 건물인데 줄에 수련이 없으면(학자 저택 따위) 맨 앞에 붙여 준다.
        if (teachMask != 0 && !items.Contains("수련")) items.Insert(0, "수련");

        return GameUi.CommandBox(title,
            [.. items.Select(item => (item, ActionFor(facility, item, teachMask)))]);
    }

    /// <summary>
    /// 그 줄이 하는 일. 지금 되는 것은 나가기와 출항·구입·수련뿐이다 —
    /// 보급·함대편성 따위는 이 창이 흉내내는 범위 밖이라 손을 달지 않는다(흐리게 나온다).
    /// </summary>
    private Action? ActionFor(Facility facility, string item, uint teachMask)
    {
        if (item == facility.ExitItem) return CloseMenu;
        if (item == "수련" && teachMask != 0)
            return () => SkillLearnDialog.Show(this, _player, _table.Teaches(teachMask));
        if (item == "기능") return () => ShowMenu(SystemMenu());

        return (facility.Kind, item) switch
        {
            (FacilityKind.Palace, "설득") => Persuade,
            (FacilityKind.Harbor, "출항") => () => { Sailed = true; Close(); },
            (FacilityKind.Shipyard, "구입") => () => HullSelectDialog.Show(this, _player),
            (FacilityKind.Library, "열람") when _library != null => () =>
                LibraryDialog.Show(this, _gameDirectory, _cityName, _cityId,
                                   _player, _library, _table, _hintName),
            _ => null,
        };
    }

    /// <summary>
    /// 도시 그림 창을 연다. 그림을 못 풀면 null 이다 — 그림이 없다고 입항까지 막을 일은 아니다.
    /// </summary>
    /// <remarks>
    /// <b>모달로 띄우지 않는다.</b> 모달이면 같은 앱의 다른 창이 입력을 못 받아 함대 창
    /// 제목 줄(옮기기·닫기)이 죽어 버린다. 배는 부르는 쪽에서 멈춰 두고, 창이 닫히면
    /// <see cref="Window.Closed"/> 로 풀어 준다.
    /// </remarks>
    /// <param name="mapArea">
    /// 지도가 놓인 자리(화면 좌표, WPF 단위). 그 자리를 통째로 덮는다. 비워 두면 그림 크기에
    /// 맞춰 owner 한가운데에 띄운다.
    /// </param>
    public static CityPicDialog? Open(Window owner, CityPictures pictures, CityBuildingTable table,
                                      int cityId, string cityName,
                                      Player player, BgmPlayer? bgm = null, Rect mapArea = default,
                                      BookTable? library = null, Func<int, string>? hintName = null,
                                      string gameDirectory = "")
    {
        var bgra = pictures.TryGetBgra(cityId);
        if (bgra == null) return null;

        var picture = BitmapSource.Create(CityPictures.Width, CityPictures.Height, 96, 96,
                                          PixelFormats.Bgra32, null, bgra, CityPictures.Width * 4);
        picture.Freeze();

        double areaW = mapArea.Width > 0 ? mapArea.Width : owner.ActualWidth;
        double areaH = mapArea.Height > 0 ? mapArea.Height : owner.ActualHeight;

        var dlg = new CityPicDialog(cityName, picture, PickScale(areaW, areaH), cityId,
                                    table, player, bgm, mapArea, library, hintName, gameDirectory)
        {
            Owner = owner,
        };
        dlg.Show();
        return dlg;
    }

    /// <summary>
    /// 그림 배율. 게임처럼 지도의 반쯤을 덮는 크기로 잡는다(자리가 좁아도 1배는 쓴다).
    /// </summary>
    private static int PickScale(double areaWidth, double areaHeight)
    {
        int scale = (int)Math.Min(areaWidth * 0.6 / CityPictures.Width,
                                  areaHeight * 0.7 / CityPictures.Height);
        return Math.Max(1, Math.Min(scale, 4));
    }
}
