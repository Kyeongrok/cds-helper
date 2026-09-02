using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 놀이가 끝났을 때 — 사건 스틸 한 장과 <c>CONTINUE?</c> 물음이다.
/// </summary>
/// <remarks>
/// 게임의 <c>0x00410CC2</c> 어름이다.
/// <code>
///   410CC2  끝난 까닭에 따라 그림 번호를 고른다 — 0x0B · 0x0C · 0x0D
///   410CD0  0x00472FA0(그림번호)          ; EVSTILL 한 장을 화면 가운데에 세운다
///   410CDA  0x0049E3E0(0x2002, "CONTINUE?", "게임을 다시 시작하겠습니까?")
/// </code>
/// 그림은 <c>EVSTILL.CDS</c> 에 있다 — 발견물 스틸과 짜임이 같아 같은 손으로 읽는다
/// (<see cref="Engine.Game.EventStills"/>).
///
/// <b>어느 까닭에 어느 번호인지는 아직 못 갈랐다.</b> <c>0x00410CA1</c> 의 뜀표를 따라가야
/// 하는데 거기까지는 안 훑어, 반란에 진 자리는 <c>0x0B</c> 로 둔다.
///
/// 바탕의 벽지 무늬는 타이틀 화면 것과 같은 그림이라 그것을 깔아 쓴다.
/// </remarks>
public sealed class GameOverDialog : Window
{
    /// <summary>놀이가 끝나는 까닭마다의 그림 번호(<c>0x00410CC2</c>).</summary>
    public const int MutinyLost = 0x0B, Ending2 = 0x0C, Ending3 = 0x0D;

    private bool _again;

    private GameOverDialog(DiscoveryStills? stills, int picture, Window owner)
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Background = Brushes.Black;

        // 주인 창을 그대로 덮는다 — 놀이가 끝나는 자리라 화면을 통째로 가린다.
        Left = owner.Left;
        Top = owner.Top;
        Width = owner.ActualWidth > 0 ? owner.ActualWidth : owner.Width;
        Height = owner.ActualHeight > 0 ? owner.ActualHeight : owner.Height;

        var page = new Grid { Background = Wallpaper() };

        var stack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        if (Picture(stills, picture) is { } art) stack.Children.Add(art);

        stack.Children.Add(new Border
        {
            Margin = new Thickness(0, 24, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = Ask(),
        });

        page.Children.Add(stack);
        Content = page;

        KeyDown += (_, e) => { if (e.Key == Key.Escape) { _again = false; Close(); } };
    }

    /// <summary>벽지 무늬 — 타이틀 화면 것을 그대로 깐다.</summary>
    private static Brush Wallpaper() => ShipMapWindow.TitleBackground();

    /// <summary>사건 스틸 한 장을 밤색 액자에 넣는다. 못 읽으면 null.</summary>
    private static UIElement? Picture(DiscoveryStills? stills, int picture)
    {
        if (stills?.TryGetBgra(picture, out int w, out int h) is not { } bgra) return null;

        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, bgra, w * 4);
        bmp.Freeze();

        var image = new Image { Source = bmp, Width = w, Height = h };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(image, EdgeMode.Aliased);

        return new Border
        {
            Background = GameUi.Back,
            BorderBrush = GameUi.Edge,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = image,
        };
    }

    /// <summary>「CONTINUE?」 물음 — 제목 띠에 영문, 아래 한 줄과 YES·NO 다.</summary>
    private UIElement Ask()
    {
        var focus = new GameUi.FocusGroup();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 12),
        };
        buttons.Children.Add(focus.Add("YES", () => { _again = true; Close(); }, 96));
        buttons.Children.Add(focus.Add("NO", () => { _again = false; Close(); }, 96));

        var body = new StackPanel { MinWidth = 300 };
        body.Children.Add(GameUi.TitleBar("CONTINUE?", null));
        body.Children.Add(new TextBlock
        {
            Text = "게임을 다시 시작하겠습니까?",
            Foreground = GameUi.Text,
            FontWeight = FontWeights.Bold,
            FontSize = 16,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(20, 14, 20, 4),
        });
        body.Children.Add(buttons);

        KeyDown += (_, e) => { if (focus.HandleKey(e.Key)) e.Handled = true; };

        return new Border
        {
            Background = GameUi.Back,
            BorderBrush = GameUi.Edge,
            BorderThickness = new Thickness(2),
            Child = body,
        };
    }

    /// <summary>
    /// 놀이 끝을 알린다. 다시 시작하겠다고 하면 true.
    /// </summary>
    public static bool Show(Window owner, DiscoveryStills? stills, int picture = MutinyLost)
    {
        var dialog = new GameOverDialog(stills, picture, owner) { Owner = owner };
        dialog.ShowDialog();
        return dialog._again;
    }
}
