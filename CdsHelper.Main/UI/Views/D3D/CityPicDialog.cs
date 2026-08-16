using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Support.Local.Helpers;
using CdsHelper.Support.Local.Models;
using CdsHelper.Support.Local.Settings;

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

    /// <summary>후원자 표와 초상화(게임 자료). 설득할 때에야 연다.</summary>
    private SponsorTable? _sponsorTable;
    private bool _sponsorTried;
    private Portraits? _faces;
    private bool _facesTried;

    // 도서관 열람에 쓰는 것들. 책 표를 못 읽었으면 열람 줄이 흐린 채로 남는다.
    private readonly BookTable? _library;
    private readonly Func<int, string> _hintName;
    private readonly string _gameDirectory;
    private readonly string _cityName;
    private readonly int _cityId;

    /// <summary>
    /// 이 도시에서 도는 곡. 문화권마다 다르다 — 시설에서 나오면 이 곡으로 돌아간다.
    /// </summary>
    private readonly int _cityTrack;

    /// <summary>출항을 골랐는지. 창을 그냥 닫으면 false.</summary>
    public bool Sailed { get; private set; }

    /// <summary>펼치기 시작하는 크기(제 크기의 몇 곱).</summary>
    private const double OpenFrom = 0.1;

    /// <summary>다 펼쳐졌을 때의 자리와 크기.</summary>
    private double _openLeft, _openTop, _openWidth, _openHeight;

    /// <summary>
    /// 도시 그림이 가운데서 펼쳐지며 열린다.
    /// </summary>
    /// <remarks>
    /// 창(HWND)을 따로 쓰므로 창의 크기와 자리를 함께 움직인다 — 크기만 키우면 왼쪽 위가
    /// 붙박이라 한쪽으로 자라는 것처럼 보인다. 네 값을 같은 박자로 움직여야 가운데가 안 흔들린다.
    ///
    /// 끝나면 <b>애니메이션을 떼고</b> 값을 손으로 박는다. 안 떼면 그 값이 물려 있어
    /// 나중에 <see cref="GameUi.CarryOwnedWindows"/> 가 창을 옮기려 해도 먹히지 않는다.
    /// </remarks>
    private void PlayOpening(CityOpenEffect effect)
    {
        var span = TimeSpan.FromMilliseconds(effect switch
        {
            CityOpenEffect.Expand => 220,
            CityOpenEffect.Zoom => 320,     // 넘쳤다 돌아올 참이 있어야 해서 조금 길다
            _ => 250,
        });
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        DoubleAnimation To(double from, double to) =>
            new(from, to, span) { EasingFunction = ease, FillBehavior = FillBehavior.Stop };

        // 끝나면 물려 있던 값을 떼고 손으로 박는다.
        void Settle()
        {
            foreach (var prop in new[] { WidthProperty, HeightProperty, LeftProperty, TopProperty, OpacityProperty })
                BeginAnimation(prop, null);
            Width = _openWidth;
            Height = _openHeight;
            Left = _openLeft;
            Top = _openTop;
            Opacity = 1;
        }

        DoubleAnimation lead;
        switch (effect)
        {
            case CityOpenEffect.Expand:
                double w0 = _openWidth * OpenFrom, h0 = _openHeight * OpenFrom;
                lead = To(w0, _openWidth);
                BeginAnimation(HeightProperty, To(h0, _openHeight));
                BeginAnimation(LeftProperty, To(_openLeft + (_openWidth - w0) / 2, _openLeft));
                BeginAnimation(TopProperty, To(_openTop + (_openHeight - h0) / 2, _openTop));
                lead.Completed += (_, _) => Settle();
                BeginAnimation(WidthProperty, lead);
                break;

            case CityOpenEffect.Slide:
                lead = To(SystemParameters.VirtualScreenWidth, _openLeft);
                lead.Completed += (_, _) => Settle();
                BeginAnimation(LeftProperty, lead);
                break;

            case CityOpenEffect.Fade:
                lead = To(0, 1);
                lead.Completed += (_, _) => Settle();
                BeginAnimation(OpacityProperty, lead);
                break;

            case CityOpenEffect.Zoom:
                ZoomIn(span);
                break;
        }
    }

    /// <summary>
    /// 파워포인트의 "확대/축소" 처럼 그림만 키운다 — 커지면서 흐림이 걷히고 끝에서 살짝 넘친다.
    /// </summary>
    /// <remarks>
    /// 창은 건드리지 않고 안의 그림에만 <see cref="ScaleTransform"/> 을 건다. 창의 크기·자리를
    /// 움직이지 않으므로 <see cref="GameUi.CarryOwnedWindows"/> 와도 부딪히지 않는다.
    /// 넘쳤다 돌아오는 맛은 <see cref="BackEase"/> 가 낸다.
    /// </remarks>
    /// <remarks>
    /// <b>깜빡이지 않게 하는 요령이 둘 있다.</b>
    ///
    /// 하나, 애니메이션을 <see cref="FillBehavior.HoldEnd"/> 로 둔다. <c>Stop</c> 으로 두면
    /// 끝나는 순간 값이 <i>처음 값</i>으로 되돌아간다 — 흐림처럼 짧게 끝나는 것을 <c>Stop</c>
    /// 으로 두면 중간에 그림이 한 번 사라졌다 돌아온다.
    ///
    /// 둘, 다 끝난 뒤 값을 먼저 박고 그 다음에 애니메이션을 뗀다. 차례가 바뀌면 떼는 순간
    /// 한 틱 동안 옛 값이 드러난다.
    ///
    /// 창 바탕도 건드리지 않는다. <c>AllowsTransparency</c> 가 켜진 창은 바탕을 갈면 통째로
    /// 다시 그려져 눈에 띈다 — 다 자란 그림이 창을 꽉 채우므로 비워 둔 채로 두어도 된다.
    /// </remarks>
    private void ZoomIn(TimeSpan span)
    {
        if (Content is not FrameworkElement box) return;

        var scale = new ScaleTransform(ZoomFrom, ZoomFrom);
        box.RenderTransformOrigin = new Point(0.5, 0.5);
        box.RenderTransform = scale;
        box.Opacity = 0;

        var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 };
        DoubleAnimation Grow() => new(ZoomFrom, 1, span) { EasingFunction = ease };

        var lead = Grow();
        lead.Completed += (_, _) =>
        {
            // 값부터 박고 나서 뗀다.
            box.Opacity = 1;
            box.BeginAnimation(OpacityProperty, null);
            box.RenderTransform = Transform.Identity;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        };

        scale.BeginAnimation(ScaleTransform.ScaleXProperty, lead);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, Grow());
        // 흐림은 먼저 걷힌다 — 끝까지 끌면 넘쳤다 돌아오는 동안 반투명해 보인다.
        box.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(span.TotalMilliseconds * 0.6)));
    }

    /// <summary>확대/축소가 시작하는 배율.</summary>
    private const double ZoomFrom = 0.3;

    private CityPicDialog(string cityName, BitmapSource picture, int scale, int cityId,
                          CityBuildingTable table, Player player, BgmPlayer? bgm, Rect mapArea,
                          BookTable? library, Func<int, string>? hintName, string gameDirectory,
                          int cityTrack)
    {
        _cityTrack = cityTrack;
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
        Background = Brushes.Black;       // 그림에 가려 안 보인다

        // 창 크기는 그림 크기 그대로다. 제목 줄이 없어(WindowStyle.None) 테가 붙지 않는다.
        double fullW = CityPictures.Width * scale, fullH = CityPictures.Height * scale;

        // 지도를 덮는 남색 막은 이 창이 아니라 지도(D3D) 쪽에서 씌운다 — 그래야 이 그림을
        // 끌어 옮겨도 막이 따라오지 않는다. 게임도 막과 그림이 따로다.
        // 처음 자리는 게임처럼 지도 한가운데다(옮기는 것은 손으로).
        if (mapArea.Width > 0 && mapArea.Height > 0)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            // 다 펼쳐졌을 때의 자리. 펼치는 동안에는 이 한가운데를 축으로 커진다.
            _openLeft = mapArea.X + (mapArea.Width - fullW) / 2;
            _openTop = mapArea.Y + (mapArea.Height - fullH) / 2;
            _openWidth = fullW;
            _openHeight = fullH;

            // 효과는 개발 창에서 고른다. 크기를 움직이려면 SizeToContent 를 꺼야 한다.
            SizeToContent = SizeToContent.Manual;
            Width = fullW;
            Height = fullH;
            Left = _openLeft;
            Top = _openTop;

            var effect = AppSettings.CityOpenEffect;
            if (effect == CityOpenEffect.Expand)
            {
                Width = fullW * OpenFrom;
                Height = fullH * OpenFrom;
                Left = _openLeft + (fullW - Width) / 2;
                Top = _openTop + (fullH - Height) / 2;
            }
            else if (effect == CityOpenEffect.Slide)
            {
                Left = SystemParameters.VirtualScreenWidth;      // 화면 오른쪽 바깥
            }
            else if (effect == CityOpenEffect.Fade)
            {
                AllowsTransparency = true;   // 이것을 켜야 Opacity 가 창에 먹는다
                Opacity = 0;
            }
            else if (effect == CityOpenEffect.Zoom)
            {
                // 창은 제 크기 그대로 두고 그림만 키운다. 창 바탕이 검으면 다 자라기 전에
                // 검은 네모가 먼저 보이므로, 바탕을 비우고 그림만 뜨게 한다.
                AllowsTransparency = true;
                Background = Brushes.Transparent;
            }

            if (effect != CityOpenEffect.None) Loaded += (_, _) => PlayOpening(effect);
        }
        else
        {
            // 지도 자리를 모르면 그냥 owner 한가운데에 제 크기로 띄운다(펼치지 않는다).
            SizeToContent = SizeToContent.WidthAndHeight;
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
                ShowMenu(BuildMenu(harbor, harbor.Name, 0, harbor.Name), harbor.BgmTrack);
        }

        // 게임 화면에는 제목 줄도 안내 줄도 없다. 그림 한 장이 곧 창이다.
        // 펼치는 동안 창이 작아지므로 그림도 같이 줄어야 한다 — Viewbox 가 창에 맞춰 준다.
        // 다 펼쳐지면 창과 그림이 같은 크기라 배율이 1 이 되어, 건물 누르는 자리도 그대로 맞는다.
        Content = new Viewbox { Child = picBox, Stretch = Stretch.Fill };

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
            ShowMenu(BuildMenu(facility, building.Name, building.TeachMask, building.Kind),
                     facility.BgmTrack);
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
        _bgm?.Play(track ?? _cityTrack);
    }

    /// <summary>명령 창을 닫고 도시로 돌아간다 — 곡도 도시 것으로 되돌린다.</summary>
    private void CloseMenu()
    {
        _menuHost.Visibility = Visibility.Collapsed;
        _bgm?.Play(_cityTrack);
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
    private void Persuade(Patron patron)
    {
        var sponsor = Sponsors()?.FindByName(patron.Name);
        string shown = sponsor?.Name ?? patron.Name;             // 게임 이름은 가운뎃점이 들어간다
        string sir = sponsor?.Honorific ?? "각하";
        string me = _player.Name;

        var face = FaceOf(patron);
        void Say(string words) => TalkDialog.Say(this, face, "", words);
        void Steward(string words) => TalkDialog.Say(this, StewardFace(), "", words);

        // 첫 관문은 명성이다. 모자라면 집사가 문간에서 돌려보낸다(게임 0x004AE1F0).
        //
        // 게임은 이 관문을 시설 종류 하나에만 건다(vtbl+0x48 이 3 일 때). 그 3 이 어느
        // 건물인지는 아직 못 갈라서 여기서는 다 건다.
        //
        // "명성치가 모자랍니다" 는 내지 않는다 — 게임에서도 그 줄은 디버그 깃발
        // (0x00580C6C 의 2비트)이 서 있을 때만 나오는 기록용이지 사람에게 보이는 말이 아니다.
        //
        // 아직 안 하는 것 — 게임에는 집사에게 뇌물을 주어 이 관문을 뚫는 길이 있다
        // ("매수한다" / "포기하고 돌아간다" → "집사에게 뇌물을 주겠습니다. 좋습니까?").
        if (_player.Fame < patron.Fame)
        {
            Steward($"{shown}님은 바쁘셔서 만나실 수 없습니다.");
            return;
        }

        // 관문을 넘으면 집사가 맞고, 무기를 맡기고, 안에 들여보낸 뒤 주인에게 알린다.
        // 게임의 알현 순서 그대로다(문구도 EXE 0x005459B8~ 에서 옮겼다).
        //
        // 곡도 여기서 바뀐다 — 돌려보낼 때는 그대로고, 인사를 받고 들어갈 때부터 알현 곡이다.
        // 나갈 때 도시 곡으로 되돌리는 것은 아래 try/finally 가 맡는다.
        _bgm?.Play(BgmPlayer.SponsorTrack);
        try
        {
            Audience(patron, shown, sir, me, face, Say, Steward);
        }
        finally
        {
            _bgm?.Play(_cityTrack);   // 그 자리를 나오면 도시 곡으로 돌아간다
        }
    }

    /// <summary>알현 — 집사가 들여보낸 뒤부터 계약을 묻기까지.</summary>
    private void Audience(Patron patron, string shown, string sir, string me,
                          uint[]? face, Action<string> Say, Action<string> Steward)
    {
        Steward($"오래 기다리셨습니다. 제가 {shown} {sir}의 집사입니다.");
        Steward("무기는 여기서 보관하겠습니다. 그러면 안으로 들어가십시오.");
        Steward($"{sir}. {me}{Particle(me)} 데리고 왔습니다.");

        // 주인이 용건을 묻는다. 게임은 신분마다 말이 다르고 첫 방문이냐에 따라 또 갈리는데,
        // 여기서는 화면에서 본 한 벌만 쓴다.
        Say($"흐음. {me}, 이번 모험 목적은 무엇인가?");

        // 얻은 힌트만 내밀 수 있다. 게임도 상태가 맞는 것만 목록에 올린다(0x0044E7B0).
        var mine = _player.Hints.Order().ToList();
        var names = mine.Select(HintNameOf).ToList();
        int row = HintListDialog.Pick(this, names, "제안 선택");
        if (row < 0)
        {
            Say("뭔가, 용건이 없는가? 이쪽은 바쁘네, 빨리 나가주게.");
            return;
        }

        var hint = Hints()?.Find(mine[row]);
        if (hint == null)
        {
            Say("흠, 원조해 주고 싶은 마음은 많지만.");
            return;
        }

        var it = hint.Value;

        // 안목 판정 — 후원자가 이야기의 크기를 가늠하지 못하면 물린다.
        if (patron.Discernment / 20 + (it.Grade == 5 ? 1 : 2) < it.Grade)
        {
            Say("가능한 한 원조해 주고 싶지만, 너무나 이야기가 막연하네.");
            return;
        }

        int funds = HintTable.FundsFor(it, patron.SupportRatePercent);

        // 재력 판정 — 낼 돈이 없으면 물린다.
        if (patron.Wealth < funds)
        {
            Say("원조는 해 주고 싶지만, 흐~음, 돈이... , 또 다음번이다.");
            return;
        }

        // 좋아하는 갈래면 사례가 후하다. 게임에도 후원자마다 좋아하는 갈래가 적혀 있다.
        int reward = patron.Likes(it.Category) ? funds * 2 : funds;

        int pick = TalkDialog.Ask(this, face, "",
            $"모험하는데 돈은 필요하겠지. 먼저 금화 {funds}닢을 주겠다. " +
            $"{it.Deadline}년 내에 성공하면 {reward}닢의 사례를 약속하겠네. 이것으로 어떤가.\n\n" +
            $" 기간{it.Deadline}년 금화 {funds}닢 ",
            "승낙한다", "교섭한다");
        if (pick != 0) return;      // 교섭은 아직 흉내내지 않는다

        Say("그러면, 기대하고 있겠네. 훌륭히 성공을 거두고 돌아오게. " +
            "(계약을 적어 두는 것은 아직 흉내내지 못한다)");
    }

    /// <summary>그 후원자의 얼굴. 표나 그림을 못 읽으면 null 이고, 그러면 대사만 나온다.</summary>
    private uint[]? FaceOf(Patron patron)
    {
        var sponsor = Sponsors()?.FindByName(patron.Name);
        if (sponsor == null) return null;
        return Faces()?.TryGetBgra(sponsor.Value.Face, sponsor.Value.IsFemale);
    }

    /// <summary>취차(집사)의 얼굴. 어느 후원자에게 가든 같은 사람이다.</summary>
    private uint[]? StewardFace() =>
        Faces()?.TryGetBgra(SponsorTable.StewardFace, female: false);

    /// <summary>이름 뒤에 붙는 목적격 조사. 받침이 있으면 "을", 없으면 "를".</summary>
    /// <remarks>
    /// 게임도 조사를 따로 끼워 넣는다 — "%s%s 데리고 왔습니다" 의 두 번째 자리가 이것이다.
    /// </remarks>
    private static string Particle(string name)
    {
        if (name.Length == 0) return "를";
        char last = name[^1];
        if (last is < '가' or > '힣') return "를";       // 한글이 아니면 그냥 둔다
        return (last - '가') % 28 == 0 ? "를" : "을";
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

    /// <summary>후원자 표(CDS_95.EXE). 얼굴 번호가 여기서 온다.</summary>
    private SponsorTable? Sponsors()
    {
        if (_sponsorTable != null || _sponsorTried) return _sponsorTable;
        _sponsorTried = true;
        if (_gameDirectory.Length == 0) return null;
        _sponsorTable = SponsorTable.Open(_gameDirectory);
        if (_sponsorTable == null)
            System.Diagnostics.Debug.WriteLine($"[City] 후원자 표 없음: {SponsorTable.LastError}");
        return _sponsorTable;
    }

    /// <summary>초상화(MALE.CDS · FEMALE.CDS). 처음 쓸 때 연다.</summary>
    private Portraits? Faces()
    {
        if (_faces != null || _facesTried) return _faces;
        _facesTried = true;
        if (_gameDirectory.Length == 0) return null;
        _faces = Portraits.Open(_gameDirectory);
        if (_faces == null)
            System.Diagnostics.Debug.WriteLine($"[City] 초상화 없음: {Portraits.LastError}");
        return _faces;
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
    private Border BuildMenu(Facility facility, string title, uint teachMask, string kind)
    {
        var items = facility.Menu.ToList();
        // 가르치는 건물인데 줄에 수련이 없으면(학자 저택 따위) 맨 앞에 붙여 준다.
        if (teachMask != 0 && !items.Contains("수련")) items.Insert(0, "수련");

        // 후원자가 앉은 건물이면 "설득" 이 맨 앞에 붙는다 — 왕궁만이 아니라 총독부·상관·
        // 학자 저택 어디든 그렇다. 게임도 물린 후원자가 없으면 그 줄을 아예 감춘다.
        var patron = PatronAt(kind);
        if (patron != null) items.Insert(0, "설득");

        return GameUi.CommandBox(title,
            [.. items.Select(item => (item, ActionFor(facility, item, teachMask, patron)))]);
    }

    /// <summary>이 건물에 앉아 있는 후원자. 없으면 null.</summary>
    private Patron? PatronAt(string kind) =>
        new PatronService().SeatedAt(LoadPatrons(), _cityName, _player.Date.Year, kind, KindsHere);

    /// <summary>이 도시에 있는 건물 종류들. 후원자를 앉힐 자리를 고를 때 쓴다.</summary>
    private HashSet<string> KindsHere =>
        _kindsHere ??= [.. _table.InCity(_cityId).Select(b => b.Kind)];

    private HashSet<string>? _kindsHere;

    /// <summary>
    /// 그 줄이 하는 일. 지금 되는 것은 나가기와 출항·구입·수련뿐이다 —
    /// 보급·함대편성 따위는 이 창이 흉내내는 범위 밖이라 손을 달지 않는다(흐리게 나온다).
    /// </summary>
    private Action? ActionFor(Facility facility, string item, uint teachMask, Patron? patron)
    {
        if (item == facility.ExitItem) return CloseMenu;
        if (item == "수련" && teachMask != 0)
            return () => SkillLearnDialog.Show(this, _player, _table.Teaches(teachMask));
        if (item == "기능") return () => ShowMenu(SystemMenu());
        if (item == "설득" && patron != null) return () => Persuade(patron);

        return (facility.Kind, item) switch
        {
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
                                      string gameDirectory = "",
                                      int cityTrack = BgmPlayer.CityTrack)
    {
        var bgra = pictures.TryGetBgra(cityId);
        if (bgra == null) return null;

        var picture = BitmapSource.Create(CityPictures.Width, CityPictures.Height, 96, 96,
                                          PixelFormats.Bgra32, null, bgra, CityPictures.Width * 4);
        picture.Freeze();

        double areaW = mapArea.Width > 0 ? mapArea.Width : owner.ActualWidth;
        double areaH = mapArea.Height > 0 ? mapArea.Height : owner.ActualHeight;

        var dlg = new CityPicDialog(cityName, picture, PickScale(areaW, areaH), cityId,
                                    table, player, bgm, mapArea, library, hintName, gameDirectory,
                                    cityTrack)
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
