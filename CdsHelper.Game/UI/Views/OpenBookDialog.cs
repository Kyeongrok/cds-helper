using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 펼친 책 — 도서관에서 읽을 수 있는 책을 누르면 이 화면으로 힌트를 읽는다.
/// </summary>
/// <remarks>
/// 그림은 <see cref="OpenBookArt"/> 가 낸다. 붉은 가죽 틀(544x304) 위에 낱장 둘을
/// <c>(8,8)</c> 과 <c>(280,8)</c> 에 얹고, <b>오른쪽 면에만 글이 앉는다</b> —
/// 왼쪽 면은 낱장 그림에 이미 찍혀 있는 라틴어 흉내다.
///
/// 글은 힌트 표의 <see cref="HintTable.Hint.Text"/> 다(힌트 줄 <c>+0x1C</c> 가 가리키는
/// 글, 표 <c>0x00543FA0</c>). 쪽 번호는 두 면 아래 가운데에 <c>-3-</c> · <c>-4-</c> 처럼
/// 붙는다.
/// </remarks>
public sealed class OpenBookDialog : Window
{
    /// <summary>글이 앉는 자리(책 틀 안의 그림 점).</summary>
    private const double TextLeft = OpenBookArt.RightPageX + 24;
    private const double TitleTop = OpenBookArt.PageY + 18;
    private const double BodyTop = OpenBookArt.PageY + 56;
    private const double LineHeight = 20, TextWidth = 208;

    /// <summary>쪽 번호가 앉는 높이.</summary>
    private const double PageNumberTop = OpenBookArt.PageY + OpenBookArt.PageHeight - 26;

    /// <summary>한 줄에 드는 글자 폭. 게임 글꼴은 한글 한 자가 두 칸(16점)이다.</summary>
    private const double CellWidth = 8;

    private OpenBookDialog(OpenBookArt art, string title, string text, int leftPage, int scale)
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        // 책 틀이 네모라 비침이 필요 없다 — 레이어드 창은 겹칠 때마다 깜빡인다.
        Background = GameUi.Back;

        var canvas = new Canvas
        {
            Width = OpenBookArt.FrameWidth * scale,
            Height = OpenBookArt.FrameHeight * scale,
        };

        Put(canvas, art, OpenBookArt.Frame, 0, 0, scale);
        Put(canvas, art, OpenBookArt.LeftPage, OpenBookArt.LeftPageX, OpenBookArt.PageY, scale);
        Put(canvas, art, OpenBookArt.RightPage, OpenBookArt.RightPageX, OpenBookArt.PageY, scale);

        Ink(canvas, title, TextLeft, TitleTop, scale);
        double y = BodyTop;
        foreach (string line in Wrap(text, TextWidth))
        {
            Ink(canvas, line, TextLeft, y, scale);
            y += LineHeight;
        }

        // 쪽 번호는 게임처럼 두 면 아래 가운데다. 어느 쪽을 폈는지는 우리가 정한다.
        Ink(canvas, $"-{leftPage}-", OpenBookArt.LeftPageX + OpenBookArt.PageWidth / 2 - 12,
            PageNumberTop, scale);
        Ink(canvas, $"-{leftPage + 1}-", OpenBookArt.RightPageX + OpenBookArt.PageWidth / 2 - 12,
            PageNumberTop, scale);

        // 닫기 단추는 오른쪽 위 모서리다.
        var close = new Border
        {
            Width = 16 * scale,
            Height = 16 * scale,
            Background = GameUi.ItemFill,
            BorderBrush = GameUi.ItemEdge,
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            Child = new TextBlock
            {
                Text = "×",
                FontSize = 12 * scale,
                Foreground = Brushes.Black,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        // 누른 자리에서 바로 닫는다. 창 끌기(EnableDrag)가 DragMove 로 마우스를 붙들어
        // 버려서 ButtonUp 이 이 단추까지 오지 않는다 — 그래서 눌러도 안 닫혔다.
        close.MouseLeftButtonDown += (_, e) => { e.Handled = true; Close(); };
        Canvas.SetLeft(close, (OpenBookArt.FrameWidth - 32) * scale);
        Canvas.SetTop(close, 16 * scale);
        canvas.Children.Add(close);

        Content = canvas;
        KeyDown += (_, e) => { if (e.Key is Key.Escape or Key.Enter or Key.Space) Close(); };
        MouseRightButtonUp += (_, _) => Close();
        GameUi.EnableDrag(this, canvas);
    }

    /// <summary>그림 한 장을 얹는다.</summary>
    private static void Put(Canvas canvas, OpenBookArt art, int picture, double x, double y, int scale)
    {
        var px = art.TryGetBgra(picture);
        if (px == null) return;

        var (w, h) = OpenBookArt.SizeOf(picture);
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4);
        bmp.Freeze();

        var image = new Image { Source = bmp, Width = w * scale, Height = h * scale };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(image, EdgeMode.Aliased);
        Canvas.SetLeft(image, x * scale);
        Canvas.SetTop(image, y * scale);
        canvas.Children.Add(image);
    }

    /// <summary>종이에 글 한 줄을 앉힌다 — 게임 글꼴의 검은 벌이다.</summary>
    private static void Ink(Canvas canvas, string line, double x, double y, int scale)
    {
        if (line.Length == 0) return;
        var label = new GameUi.GameLabel(GameFont.BlackColor, GameUi.ItemTextHeight * scale)
        {
            Text = line,
            Bold = true,
            FallbackBrush = Brushes.Black,
        };
        Canvas.SetLeft(label, x * scale);
        Canvas.SetTop(label, y * scale);
        canvas.Children.Add(label);
    }

    /// <summary>종이 너비에 맞춰 끊는다. 한글 한 자가 두 칸이다.</summary>
    private static List<string> Wrap(string text, double width)
    {
        var lines = new List<string>();
        var line = new StringBuilder();
        double used = 0;

        foreach (char c in text)
        {
            if (c == '\n') { lines.Add(line.ToString()); line.Clear(); used = 0; continue; }
            double w = c < 0x80 ? CellWidth : CellWidth * 2;
            if (used + w > width) { lines.Add(line.ToString()); line.Clear(); used = 0; }
            line.Append(c);
            used += w;
        }
        if (line.Length > 0) lines.Add(line.ToString());
        return lines;
    }

    /// <summary>펼친 책을 띄운다. 그림을 못 읽으면 아무 일도 없다(글 알림은 부른 쪽이 낸다).</summary>
    /// <param name="leftPage">왼쪽 면의 쪽 번호. 오른쪽은 그 다음이다.</param>
    public static bool Show(Window owner, OpenBookArt? art, string title, string text, int leftPage)
    {
        if (art == null || art.TryGetBgra(OpenBookArt.Frame) == null) return false;

        int scale = owner.ActualHeight > 800 ? 2 : 1;
        new OpenBookDialog(art, title, text, leftPage, scale) { Owner = owner }.ShowDialog();
        return true;
    }
}
