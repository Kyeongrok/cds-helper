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

        page.Children.Add(stack);
        Content = page;

        // 그림이 다 깔린 뒤에 물음창을 그 위로 얹는다. 물음은 <b>여느 물음창</b>이다 —
        // 게임도 알림과 물음이 한 함수라(0x00469060) 여기만 딴 상자를 지을 까닭이 없다.
        Loaded += (_, _) =>
        {
            _again = ConfirmDialog.Ask(this, "게임을 다시 시작하겠습니까?", "CONTINUE?");
            Close();
        };
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
