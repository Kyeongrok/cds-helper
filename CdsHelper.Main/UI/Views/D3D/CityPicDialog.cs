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
/// 건물 자리는 <see cref="CityBuildings"/> 표에서 온다. 표에 없는 도시(유럽식이 아닌 그림)는
/// 그림 아무 데나 눌러도 항구 명령 창이 열리게 해 두었다 — 출항할 길은 어디서나 있어야 한다.
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

    /// <summary>출항을 골랐는지. 창을 그냥 닫으면 false.</summary>
    public bool Sailed { get; private set; }

    private CityPicDialog(string cityName, BitmapSource picture, int scale, int cityId,
                          Player player, BgmPlayer? bgm)
    {
        _player = player;
        _bgm = bgm;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

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

        // 그림에서 자리를 아는 시설만 얹는다. 게임도 시설 표 하나를 돌리는 모양이라
        // 여기서도 창을 시설마다 만들지 않고 표를 훑는다.
        var harbor = Facility.All[0];
        bool harborPlaced = false;
        foreach (var facility in Facility.All)
        {
            var spot = CityBuildings.Of(facility.Kind, cityId);
            if (spot is not { } s) continue;
            AddSpot(new Rect(s.X, s.Y, s.Width, s.Height), facility, scale);
            if (facility.Kind == FacilityKind.Harbor) harborPlaced = true;
        }

        // 항구를 못 찾은 그림은 아무 데나 눌러도 항구 명령 창이 열린다(건물 판이 먼저 먹는다).
        if (!harborPlaced)
        {
            picBox.Cursor = Cursors.Hand;
            picBox.MouseLeftButtonUp += (_, _) => ShowMenu(BuildMenu(harbor), harbor.BgmTrack);
        }

        var hint = new TextBlock
        {
            Text = !harborPlaced ? "그림을 누르면 명령 창 · ESC 로 닫기"
                                 : "건물을 누르면 명령 창 · ESC 로 닫기",
            Foreground = Brushes.Gray,
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 6),
        };

        var stack = new StackPanel();
        stack.Children.Add(GameUi.TitleBar(cityName, Close));
        stack.Children.Add(new Border
        {
            BorderBrush = GameUi.Edge,
            BorderThickness = new Thickness(2),
            Margin = new Thickness(0, 4, 0, 0),
            Child = picBox,
        });
        stack.Children.Add(hint);

        Content = new Border
        {
            BorderBrush = GameUi.Edge,
            BorderThickness = new Thickness(2),
            Margin = new Thickness(4),
            Padding = new Thickness(6, 6, 6, 0),
            Child = stack,
        };

        // 제목 줄이 없으니(WindowStyle.None) 키와 오른쪽 단추로도 닫는다.
        KeyDown += (_, e) => { if (e.Key is Key.Escape or Key.Enter) Close(); };
        MouseRightButtonUp += (_, _) => Close();
    }

    /// <summary>
    /// 건물 하나를 누를 수 있게 한다. 커서를 올리면 이름표가 밑에 뜨고, 누르면 명령 창이 열린다.
    /// </summary>
    private void AddSpot(Rect a, Facility facility, int scale)
    {
        var tag = GameUi.NameTag(facility.Name);
        _layer.Children.Add(tag);
        _tags.Add(tag);

        // 1배로 보면 건물이 20~30점밖에 안 돼 겨누기 어려우므로 판만 사방으로 조금 넓힌다
        // (이름표는 건물에 맞춘다).
        var hit = Rect.Inflate(a, 3, 3);
        var spot = new Border
        {
            Width = hit.Width * scale,
            Height = hit.Height * scale,
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
        };
        Canvas.SetLeft(spot, hit.X * scale);
        Canvas.SetTop(spot, hit.Y * scale);
        spot.MouseEnter += (_, _) => ShowTag(tag, a, scale);
        spot.MouseLeave += (_, _) => tag.Visibility = Visibility.Collapsed;
        spot.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            ShowMenu(BuildMenu(facility), facility.BgmTrack);
        };
        _layer.Children.Add(spot);
    }

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
    /// 시설의 명령 창을 짓는다. 줄은 <see cref="Facility"/> 표에서 오고, 그중 흉내낼 수 있는
    /// 것만 <see cref="ActionFor"/> 가 손을 달아 준다. 나머지는 흐린 채로 둔다.
    /// </summary>
    private Border BuildMenu(Facility facility) =>
        GameUi.CommandBox(facility.Name,
                          [.. facility.Menu.Select(item => (item, ActionFor(facility, item)))]);

    /// <summary>
    /// 그 줄이 하는 일. 지금 되는 것은 나가기와 출항·구입 셋뿐이다 —
    /// 보급·함대편성 따위는 이 창이 흉내내는 범위 밖이라 손을 달지 않는다(흐리게 나온다).
    /// </summary>
    private Action? ActionFor(Facility facility, string item)
    {
        if (item == facility.ExitItem) return CloseMenu;
        return (facility.Kind, item) switch
        {
            (FacilityKind.Harbor, "출항") => () => { Sailed = true; Close(); },
            (FacilityKind.Shipyard, "구입") => () => HullSelectDialog.Show(this, _player),
            _ => null,
        };
    }

    /// <summary>
    /// 도시 그림을 띄운다. 그림을 못 풀면 아무것도 안 하고 false —
    /// 그림이 없다고 입항까지 막을 일은 아니다.
    /// </summary>
    public static bool Show(Window owner, CityPictures pictures, int cityId, string cityName,
                            Player player, BgmPlayer? bgm = null)
    {
        var bgra = pictures.TryGetBgra(cityId);
        if (bgra == null) return false;

        var picture = BitmapSource.Create(CityPictures.Width, CityPictures.Height, 96, 96,
                                          PixelFormats.Bgra32, null, bgra, CityPictures.Width * 4);
        picture.Freeze();

        var dlg = new CityPicDialog(cityName, picture, PickScale(owner), cityId, player, bgm)
        {
            Owner = owner,
        };
        dlg.ShowDialog();
        return true;
    }

    /// <summary>창에 들어가는 가장 큰 정수 배율. 창이 작아도 1배는 쓴다.</summary>
    private static int PickScale(Window owner)
    {
        // 제목 줄과 안내 글로 세로 70점쯤 더 먹으므로 그만큼 뺀 자리에 맞춘다.
        double w = owner.ActualWidth * 0.9;
        double h = owner.ActualHeight * 0.9 - 70;
        int scale = (int)Math.Min(w / CityPictures.Width, h / CityPictures.Height);
        return Math.Max(1, Math.Min(scale, 4));
    }
}
