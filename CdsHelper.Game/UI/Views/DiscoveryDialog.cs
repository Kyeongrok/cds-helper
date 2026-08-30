using System.IO;
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
/// 그림에는 <b>액자</b>가 둘린다 — 밤색 판에 까만 줄 두 겹이다. 그림 안쪽의 크림빛
/// 테는 그림에 그려진 것이고, 액자는 그 바깥에 따로 있다.
///
/// 아래 칸은 게임 알림창과 같은 꼴이다(<see cref="ConfirmDialog"/> 와 같은 자리값).
/// </remarks>
public sealed class DiscoveryDialog : Window
{
    /// <summary>글 칸의 여백과 단추 자리. 게임 알림창에서 그대로 가져왔다.</summary>
    private const double SidePad = 7, TopPad = 7, BottomPad = 15;
    private const double EdgeThickness = 1, TextGap = 10;

    /// <summary>
    /// 그림에 두르는 액자 — 까만 줄 · 밤색 판 · 까만 줄이다.
    /// </summary>
    /// <remarks>게임 갈무리에서 잰 값이다. 판이 여덟 점쯤이고 줄은 한 점씩이다.</remarks>
    private const double FrameLine = 1, FrameWide = 8;

    /// <summary>액자가 그림 좌우로 더 먹는 폭.</summary>
    private const double FrameGrow = (FrameLine + FrameWide + FrameLine) * 2;

    /// <summary>액자의 까만 줄과 밤색 판. 알림 칸 바탕보다 조금 밝다.</summary>
    private static readonly Brush FrameEdge = Frozen(Color.FromRgb(0x11, 0x09, 0x09));
    private static readonly Brush FrameFill = Frozen(Color.FromRgb(0x4A, 0x2E, 0x24));

    /// <summary>동영상 칸의 크기. 게임 것이 320x240 이다.</summary>
    private const double MovieWidth = 320, MovieHeight = 240;

    /// <summary>그림에 바짝 붙는 까만 줄, 그 바깥에 밤색 판, 다시 까만 줄.</summary>
    private static UIElement Framed(UIElement inner) => new Border
    {
        Background = FrameFill,
        BorderBrush = FrameEdge,
        BorderThickness = new Thickness(FrameLine),
        Padding = new Thickness(FrameWide),
        HorizontalAlignment = HorizontalAlignment.Center,
        Child = new Border
        {
            BorderBrush = FrameEdge,
            BorderThickness = new Thickness(FrameLine),
            Child = inner,
        },
    };

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
    private const double ButtonWidth = 64, ButtonHeight = UiSprites.BandHeight;

    private readonly GameUi.FocusGroup _focus = new();

    private DiscoveryDialog(BitmapSource? picture, double width, string text, string? movie,
                            string? title)
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        // 액자가 좌우로 더 먹으므로 창도 그만큼 넓어야 한다.
        var stack = new StackPanel
        {
            Width = movie != null ? MovieWidth + FrameGrow
                  : picture == null ? width : width + FrameGrow,
        };

        if (movie != null)
        {
            // 동영상은 그림 자리에 그대로 얹는다. 코덱이 없어 못 틀면 그 칸만 비고
            // 글은 그대로 나온다 — 발견은 이미 적혔고 그림은 덤이다.
            var player = new MediaElement
            {
                Source = new Uri(movie),
                LoadedBehavior = MediaState.Manual,
                UnloadedBehavior = MediaState.Close,
                Stretch = Stretch.Uniform,
                Width = MovieWidth,
                Height = MovieHeight,
            };
            player.MediaFailed += (_, _) => player.Visibility = Visibility.Collapsed;
            player.MediaEnded += (_, _) => player.Stop();
            Loaded += (_, _) => player.Play();
            Closed += (_, _) => player.Close();
            stack.Children.Add(Framed(player));
        }
        else if (picture != null)
        {
            var image = new Image
            {
                Source = picture,
                Width = picture.PixelWidth,
                Height = picture.PixelHeight,
            };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
            RenderOptions.SetEdgeMode(image, EdgeMode.Aliased);

            stack.Children.Add(Framed(image));
        }

        // 해설은 여러 줄이라 왼쪽에 붙이고, 발견 알림은 한 줄이라 가운데다.
        var lines = Wrap(text, stack.Width - SidePad * 2);
        var words = new StackPanel();
        foreach (string line in lines)
            words.Children.Add(new GameUi.GameLabel(GameFont.WhiteColor, GameUi.ItemTextHeight)
            {
                Text = line,
                Bold = true,
                FallbackBrush = GameUi.Text,
                HorizontalAlignment = lines.Count == 1 ? HorizontalAlignment.Center
                                                       : HorizontalAlignment.Left,
            });

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

        var below = new StackPanel();
        // 해설에는 제목 띠가 붙는다 — 발견물 이름이다.
        if (!string.IsNullOrEmpty(title)
            && GameUi.TitleFrame(GameUi.Sprites, title!) is { } bar)
        {
            bar.Margin = new Thickness(0, 0, 0, 6);
            below.Children.Add(bar);
        }
        below.Children.Add(words);
        below.Children.Add(buttons);
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
    /// <param name="movie">틀 동영상 파일. 없으면 null 이고 그때 그림을 본다.</param>
    /// <param name="title">제목 띠에 적을 이름. 없으면 띠가 안 붙는다.</param>
    public static void Show(Window owner, DiscoveryStills? stills, int picture, string text,
                            string? movie = null, string? title = null)
    {
        BitmapSource? art = null;
        double width = MinWidth_;

        if (movie != null && !File.Exists(movie)) movie = null;

        if (movie == null && stills != null && picture >= 0
            && stills.TryGetBgra(picture, out int w, out int h) is { } bgra)
        {
            var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, bgra, w * 4);
            bmp.Freeze();
            art = bmp;
            width = w;
        }

        new DiscoveryDialog(art, width, text, movie, title) { Owner = owner }.ShowDialog();
    }

    /// <summary>그 발견물의 동영상 파일 자리. 없으면 null.</summary>
    public static string? MovieOf(string gameDirectory, int movie) =>
        movie < 0 || gameDirectory.Length == 0
            ? null
            : System.IO.Path.Combine(gameDirectory, DiscoveryTable.MovieFolder,
                                     $"I{movie:00}_0000.AVI");

    /// <summary>그림이 없을 때의 글 칸 너비. 게임 알림창의 가장 좁은 폭이다.</summary>
    private const double MinWidth_ = 272;

    /// <summary>글자 한 칸 — 한글 한 자가 두 칸이다.</summary>
    private const double CellWidth = 8;

    /// <summary>칸 너비에 맞춰 끊는다.</summary>
    private static List<string> Wrap(string text, double width)
    {
        var lines = new List<string>();
        var line = new System.Text.StringBuilder();
        double used = 0;

        foreach (char c in text)
        {
            double w = c < 0x80 ? CellWidth : CellWidth * 2;
            if (used + w > width) { lines.Add(line.ToString()); line.Clear(); used = 0; }
            line.Append(c);
            used += w;
        }
        if (line.Length > 0) lines.Add(line.ToString());
        return lines.Count > 0 ? lines : [text];
    }
}
