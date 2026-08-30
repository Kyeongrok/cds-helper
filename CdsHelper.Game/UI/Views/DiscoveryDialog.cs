using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 발견 알림 — 그림 한 장을 세우고 그 아래에 "…을 발견했다!" 를 적는다.
/// </summary>
/// <remarks>
/// 세빌리아 교회처럼 <b>건물 자체가 발견물</b>인 자리에서 뜬다. 그림은 DSTILL.CDS 에서
/// 오고(<see cref="DiscoveryStills"/>), 어느 그림인지는 건물 표가 들고 있다
/// (<see cref="CityBuildingTable.Building.Picture"/>).
///
/// 액자는 그리지 않는다 — 그림 안에 이미 크림빛 테가 그려져 있다. 아래 칸만 게임
/// 알림창과 같은 꼴로 두른다(<see cref="ConfirmDialog"/> 와 같은 자리값이다).
/// </remarks>
public sealed class DiscoveryDialog : Window
{
    /// <summary>글 칸의 여백과 단추 자리. 게임 알림창에서 그대로 가져왔다.</summary>
    private const double SidePad = 7, TopPad = 7, BottomPad = 15;
    private const double EdgeThickness = 1, TextGap = 10;
    private const double ButtonWidth = 64, ButtonHeight = UiSprites.BandHeight;

    private readonly GameUi.FocusGroup _focus = new();

    private DiscoveryDialog(BitmapSource? picture, double width, string text)
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        var stack = new StackPanel { Width = width };

        if (picture != null)
        {
            var image = new Image
            {
                Source = picture,
                Width = picture.PixelWidth,
                Height = picture.PixelHeight,
            };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
            RenderOptions.SetEdgeMode(image, EdgeMode.Aliased);
            stack.Children.Add(image);
        }

        var words = new GameUi.GameLabel(GameFont.WhiteColor, GameUi.ItemTextHeight)
        {
            Text = text,
            Bold = true,
            FallbackBrush = GameUi.Text,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        var ok = _focus.Add("확인", () => { DialogResult = true; }, ButtonWidth);
        ok.Height = ButtonHeight;
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Height = ButtonHeight,
            Margin = new Thickness(0, TextGap, 0, 0),
            Children = { ok },
        };

        var below = new StackPanel { Children = { words, buttons } };
        stack.Children.Add(new Border
        {
            Background = GameUi.Back,
            BorderBrush = GameUi.Edge,
            BorderThickness = new Thickness(EdgeThickness),
            Padding = new Thickness(SidePad, TopPad, SidePad, BottomPad),
            Child = below,
        });

        Content = stack;

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { DialogResult = true; return; }
            if (_focus.HandleKey(e.Key)) e.Handled = true;
        };
        GameUi.EnableDrag(this, stack);
    }

    /// <summary>
    /// 발견을 알린다. 그림을 못 구하면 글만 낸다 — 발견은 이미 적혔고 그림은 덤이다.
    /// </summary>
    /// <param name="owner">알림을 얹을 창.</param>
    /// <param name="stills">발견물 그림. 없으면 글만 낸다.</param>
    /// <param name="picture">그림 번호. -1 이면 그림이 없는 발견물이다.</param>
    /// <param name="text">적을 글("히랄다탑을 발견했다!").</param>
    public static void Show(Window owner, DiscoveryStills? stills, int picture, string text)
    {
        BitmapSource? art = null;
        double width = MinWidth_;

        if (stills != null && picture >= 0
            && stills.TryGetBgra(picture, out int w, out int h) is { } bgra)
        {
            var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, bgra, w * 4);
            bmp.Freeze();
            art = bmp;
            width = w;
        }

        new DiscoveryDialog(art, width, text) { Owner = owner }.ShowDialog();
    }

    /// <summary>그림이 없을 때의 글 칸 너비. 게임 알림창의 가장 좁은 폭이다.</summary>
    private const double MinWidth_ = 272;
}
