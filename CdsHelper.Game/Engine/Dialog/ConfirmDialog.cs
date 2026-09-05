using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 게임 물음창 — 한 마디 건네고 YES/NO 를 받는다. 단추를 확인 하나로 세우면
/// 알림창이 된다(<see cref="Tell"/>).
/// </summary>
/// <remarks>
/// 게임도 이 둘이 <b>한 함수</b>다(<c>0x00469060</c>). 첫 인자가 창의 종류라서 <c>0</c> 이면
/// 확인만, <c>2</c> 면 YES/NO 다 — 부르는 자리 146 곳 가운데 117 곳이 확인, 21 곳이 YES/NO 다.
/// YES 를 고르면 <c>2</c> 가 나온다.
///
/// 게임은 말하는 사람 얼굴을 왼쪽에 같이 띄우지만 초상화는 아직 안 꺼내 오므로 글만 낸다.
/// 입항 물음도 이 창이다 — 예전에는 손으로 지은 딴 창(PortDialog)이 따로 있었다.
///
/// <b>자리는 재지 않고 게임 코드에서 그대로 옮겼다</b> — 창을 짓는 곳이 <c>0x0049D7B0</c>,
/// 글을 찍는 곳이 <c>0x0049DFD0</c> 다. 갈무리를 재어 맞추던 예전 값(308 x 100)은 갈무리가
/// 몇 배로 늘어난 것인지를 잘못 짚어 가로로 부풀고 위 여백이 모자랐다.
/// <code>
///   칸수 = max(30, 줄 가운데 가장 긴 것)   ; 한 칸 8점, 한글 한 자가 두 칸
///   너비 = 칸수 * 8 + 32                   ; 글자 자리(칸수*8+16) 에 좌우 8 씩
///   높이 = (줄수 * 20 + 71) &amp; ~15          ; 제목 띠가 있으면 + 24
///   단추 = 64 x 24, 사이 16, 창 아래에서 40 자리
/// </code>
/// 그래서 <b>한 줄짜리 창은 늘 272 x 80</b> 이다. 세로로 쌓이는 차례가 이렇다.
/// <code>
///   테        1      본문 글    16
///   위 여백   7      사이       10
///   사이      6      단추 줄    24
///                    아래 여백  15 + 테 1   →  모두 80
/// </code>
/// 글은 창 한가운데에 놓인다 — 게임도 한 줄일 때는 남는 칸의 반만큼 밀어 가운데로 맞춘다.
/// 상자에는 <b>밝은 테가 한 점</b> 둘린다(게임 색표의 <c>0x2B</c> = 212,200,176).
/// </remarks>
public sealed class ConfirmDialog : Window
{
    private readonly GameUi.FocusGroup _focus = new();

    private ConfirmDialog(string text, string? title, bool yesNo, uint[]? face,
                          double indent)
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Height = ButtonHeight,
            Margin = new Thickness(0, TextGap, 0, 0),
        };
        // 초점이 간 단추의 안쪽 테가 깜빡인다 — 게임이 지금 고른 것을 그렇게 알린다.
        if (yesNo)
        {
            var yes = _focus.Add("YES", () => { DialogResult = true; }, ButtonWidth);
            var no = _focus.Add("NO", () => { DialogResult = false; }, ButtonWidth);
            yes.Height = no.Height = ButtonHeight;
            yes.Margin = new Thickness(0, 0, ButtonGap / 2, 0);
            no.Margin = new Thickness(ButtonGap / 2, 0, 0, 0);
            buttons.Children.Add(yes);
            buttons.Children.Add(no);
        }
        else
        {
            // 알림창은 확인 하나뿐이다. 폭은 YES/NO 와 같다 — 게임 갈무리에서 잰 폭이 같다.
            var ok = _focus.Add("확인", () => { DialogResult = true; }, ButtonWidth);
            ok.Height = ButtonHeight;
            buttons.Children.Add(ok);
        }

        // 글이 길면 창도 그만큼 넓어진다 — 게임처럼 8점 칸으로 세되 서른 칸 밑으로는 안 줄인다.
        var lines = Wrap(text);
        double widest = 0;
        foreach (string line in lines) widest = Math.Max(widest, GameUi.Font?.TextWidth(line) ?? 0);
        int cells = Math.Max(MinCells, (int)Math.Ceiling(widest / CellWidth));
        // 얼굴이 서면 그만큼 창이 넓어진다 — 게임도 96 을 더한다(0x0049DA18 의 and eax,0x60).
        double barWidth = cells * CellWidth + CellWidth * 2 + (face != null ? FaceColumn : 0);

        var stack = new StackPanel { Width = barWidth };

        // 게임은 물음창에도 진홍 장식 띠로 제목을 얹는다("게임 로드" 따위).
        bool titled = !string.IsNullOrEmpty(title);
        if (titled && GameUi.TitleFrame(GameUi.Sprites, title!) is { } bar)
        {
            bar.Width = barWidth;
            bar.Height = BarHeight;
            stack.Children.Add(bar);
        }

        // 본문은 게임 비트맵 글꼴로 찍는다 — 윈도 글꼴로 두면 이 크기에서 획이 뭉갠다.
        //
        // <b>가운데로 미는 것은 한 줄짜리뿐이다.</b> 얼굴이 서면 글은 그 오른쪽에
        // 왼쪽맞춤으로 붙고, 두 줄 넘게 넘어가는 말도 왼쪽에 붙는다("모험 중단" 창이
        // 그렇다). 한 줄이면 남는 칸의 반만큼 밀어 가운데로 맞춘다.
        var words = new StackPanel();
        foreach (string line in lines)
            words.Children.Add(new GameUi.GameLabel(GameFont.WhiteColor, TextHeight)
            {
                Text = line,
                Bold = true,
                FallbackBrush = GameUi.Text,
                HorizontalAlignment = face == null && lines.Count == 1
                    ? HorizontalAlignment.Center : HorizontalAlignment.Left,
            });

        var body = new StackPanel { Margin = new Thickness(0, BodyGap, 0, 0) };
        if (face == null)
        {
            // 미니게임 설명처럼 <b>왼쪽을 한 번 더 들이는</b> 글이 있다. 게임도 설명 글은
            // 테에서 한 뼘 떨어져 시작한다 — 얼굴이 서는 창은 이미 얼굴만큼 들어가 있으므로
            // 이쪽만 손댄다.
            if (indent > 0) words.Margin = new Thickness(indent, 0, 0, 0);
            body.Children.Add(words);
        }
        else
        {
            // 얼굴은 왼쪽 위에 조각 그대로 80x96 으로 선다. 글은 그 오른쪽 8 부터다.
            var row = new StackPanel { Orientation = Orientation.Horizontal, MinHeight = FaceHeight };
            row.Children.Add(Portrait(face));
            words.Margin = new Thickness(CellWidth, 0, 0, 0);
            words.VerticalAlignment = VerticalAlignment.Top;
            row.Children.Add(words);
            body.Children.Add(row);
        }
        stack.Children.Add(body);
        stack.Children.Add(buttons);

        Content = new Border
        {
            Background = GameUi.Back,
            BorderBrush = GameUi.Edge,
            BorderThickness = new Thickness(EdgeThickness),
            Padding = new Thickness(SidePad, TopPad, SidePad, BottomPad),
            Child = stack,
        };

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { DialogResult = false; return; }
            // 방향키로 옮기고 엔터로 고른다 — 어느 단추에 초점이 가 있는지는 깜빡임이 알린다.
            if (_focus.HandleKey(e.Key)) e.Handled = true;
        };
        GameUi.EnableDrag(this, stack);
    }

    /// <summary>
    /// 본문을 <see cref="MaxTextWidth"/> 안에 들도록 띄어쓰기에서 끊는다. 게임 글꼴이 아직
    /// 없으면 재 볼 것이 없어 통째로 한 줄이다.
    /// </summary>
    private static List<string> Wrap(string text)
    {
        var font = GameUi.Font;
        if (font == null) return [text];

        var lines = new List<string>();
        var line = new StringBuilder();
        foreach (string word in text.Split(' '))
        {
            string joined = line.Length == 0 ? word : $"{line} {word}";
            if (line.Length > 0 && font.TextWidth(joined) > MaxTextWidth)
            {
                lines.Add(line.ToString());
                line.Clear();
                line.Append(word);
            }
            else
            {
                line.Clear();
                line.Append(joined);
            }
        }
        if (line.Length > 0) lines.Add(line.ToString());
        return lines.Count > 0 ? lines : [text];
    }

    // ── 게임 화면에서 잰 자리(그림 점) ──────────────────────────────────────

    /// <summary>테 안쪽 여백. 밝은 테 한 점까지 세면 좌우·위가 8, 아래가 16 이다.</summary>
    private const double SidePad = 7, TopPad = 7, BottomPad = 15;

    /// <summary>상자를 두르는 밝은 테. 게임 물음창에도 한 점 있다.</summary>
    private const double EdgeThickness = 1;

    /// <summary>제목 띠의 높이. 게임도 제목이 붙으면 창이 이만큼 길어진다.</summary>
    private const double BarHeight = UiSprites.BandHeight;

    /// <summary>창 위(또는 제목 띠)와 본문 사이, 본문과 단추 줄 사이.</summary>
    private const double BodyGap = 6, TextGap = 10;

    /// <summary>본문 한 줄의 높이.</summary>
    private const int TextHeight = GameUi.ItemTextHeight;

    /// <summary>글자 한 칸. 반각 한 자 · 한글 반 자가 이만큼이다.</summary>
    private const double CellWidth = UiSprites.MidWidth;

    /// <summary>가장 좁은 창의 칸 수. 게임도 서른 칸(= 272점) 밑으로는 안 줄인다.</summary>
    private const int MinCells = 30;

    /// <summary>얼굴 왼쪽에 더 두는 여백. 창 안쪽 여백(7)만으로는 얼굴이 테에 붙어 보인다.</summary>
    private const double FacePad = 3;

    /// <summary>얼굴이 설 때 창이 넓어지는 만큼 — 얼굴 80 에 좌우 8 씩이다.</summary>
    private const double FaceColumn = 96;

    /// <summary>얼굴 높이. 글이 한 줄이어도 창은 이만큼 자리를 낸다.</summary>
    private const double FaceHeight = Portraits.Height;

    /// <summary>얼굴 한 장. 조각 그대로 걸고 위에 붙인다.</summary>
    private static UIElement Portrait(uint[] face)
    {
        var bmp = BitmapSource.Create(Portraits.Width, Portraits.Height, 96, 96,
                                      PixelFormats.Bgra32, null, face, Portraits.Width * 4);
        bmp.Freeze();

        var image = new Image
        {
            Source = bmp,
            Width = Portraits.Width,
            Height = Portraits.Height,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(FacePad, 0, 0, 0),
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(image, EdgeMode.Aliased);
        return image;
    }

    /// <summary>한 줄이 이보다 길면 끊는다 — 게임이 예순 칸에서 끊는다.</summary>
    private const double MaxTextWidth = 480;

    /// <summary>단추 크기와 사이. 폭은 마구리 둘에 가운데 넉 칸이다(16+8*4+16).</summary>
    private const double ButtonWidth = 64, ButtonHeight = UiSprites.BandHeight, ButtonGap = 16;

    /// <summary>
    /// 물어보고 YES 를 골랐으면 true. <paramref name="title"/> 을 주면 제목 띠를 얹는다.
    /// </summary>
    public static bool Ask(Window owner, string text, string? title = null,
                           uint[]? face = null) =>
        new ConfirmDialog(text, title, yesNo: true, face, 0) { Owner = owner }
            .ShowDialog() == true;

    /// <summary>
    /// 한 마디 알리고 확인만 받는다 — 게임 물음창의 <b>종류 0</b> 이다.
    /// </summary>
    public static void Tell(Window owner, string text, string? title = null,
                            uint[]? face = null, double indent = 0) =>
        new ConfirmDialog(text, title, yesNo: false, face, indent) { Owner = owner }.ShowDialog();
}
