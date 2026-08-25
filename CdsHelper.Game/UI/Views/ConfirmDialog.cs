using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 게임 대사 창처럼 한 마디 건네고 YES/NO 를 받는 창.
/// </summary>
/// <remarks>
/// 게임은 말하는 사람 얼굴을 왼쪽에 같이 띄우지만 초상화는 아직 안 꺼내 오므로 글만 낸다.
/// <see cref="PortDialog"/> 와 달리 문구를 밖에서 준다.
///
/// <b>자리는 게임 화면을 재어 맞췄다 — 그림 점 그대로다.</b> 예전 값은 <b>1.75배로 늘어난
/// 갈무리</b>에서 잰 것이라 창이 통째로 그만큼 부풀어 있었다(띠 높이 42 = 24 x 1.75).
/// 갈무리의 배경 무늬 마디가 가로 114 · 세로 91 이고 게임 무늬가 80x64 이므로 그 갈무리는
/// 1.425배였다. 그 배로 도로 나눈 값이 아래 것이다. 세로로 쌓이는 차례가 이렇다.
/// <code>
///   위 여백     6      본문 글    16
///   제목 띠    24      사이        8
///   사이        6      단추 줄    24
///                      아래 여백  16   →  모두 100
/// </code>
/// 가로는 좌우 6 을 두어 <b>제목 띠가 296</b>(마구리 32 + 가운데 33칸), 창이 308 이다.
/// 단추는 <b>64 x 24</b> 두 개를 16 띄워 가운데 놓는다.
///
/// 게임 상자에는 <b>테가 없다</b> — 양피지 바탕에서 곧바로 짙은 밤색으로 넘어간다.
/// </remarks>
public sealed class ConfirmDialog : Window
{
    private readonly GameUi.FocusGroup _focus = new();

    private ConfirmDialog(string text, string? title)
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
        var yes = _focus.Add("YES", () => { DialogResult = true; }, ButtonWidth);
        var no = _focus.Add("NO", () => { DialogResult = false; }, ButtonWidth);
        yes.Height = no.Height = ButtonHeight;
        yes.Margin = new Thickness(0, 0, ButtonGap / 2, 0);
        no.Margin = new Thickness(ButtonGap / 2, 0, 0, 0);
        buttons.Children.Add(yes);
        buttons.Children.Add(no);

        // 글이 길면 띠도 창도 그만큼 넓어진다 — 띠 폭은 8점 칸으로만 늘어나므로 칸에 맞춘다.
        var lines = Wrap(text);
        double widest = 0;
        foreach (string line in lines) widest = Math.Max(widest, GameUi.Font?.TextWidth(line) ?? 0);
        double barWidth = Math.Max(BarWidth,
                                   UiSprites.WidthFor(UiSprites.CellsFor(widest + TextInset * 2)));

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
        var body = new StackPanel { Margin = new Thickness(0, titled ? TitleGap : 0, 0, 0) };
        foreach (string line in lines)
            body.Children.Add(new GameUi.GameLabel(GameFont.WhiteColor, TextHeight)
            {
                Text = line,
                Bold = true,
                FallbackBrush = GameUi.Text,
                HorizontalAlignment = HorizontalAlignment.Center,
            });
        stack.Children.Add(body);
        stack.Children.Add(buttons);

        Content = new Border
        {
            Background = GameUi.Back,
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

    /// <summary>테 안쪽 여백.</summary>
    private const double SidePad = 6, TopPad = 6, BottomPad = 16;

    /// <summary>제목 띠의 크기. 높이는 게임 띠 높이 그대로다.</summary>
    private const double BarWidth = 296, BarHeight = UiSprites.BandHeight;

    /// <summary>제목 띠와 본문 사이, 본문과 단추 줄 사이.</summary>
    private const double TitleGap = 6, TextGap = 8;

    /// <summary>본문 한 줄의 높이와 좌우 여백.</summary>
    private const int TextHeight = GameUi.ItemTextHeight;
    private const double TextInset = 6;

    /// <summary>한 줄이 이보다 길면 끊는다. 게임도 긴 말은 두 줄로 낸다.</summary>
    private const double MaxTextWidth = 320;

    /// <summary>단추 크기와 사이. 폭은 마구리 둘에 가운데 넉 칸이다(16+8*4+16).</summary>
    private const double ButtonWidth = 64, ButtonHeight = UiSprites.BandHeight, ButtonGap = 16;

    /// <summary>
    /// 물어보고 YES 를 골랐으면 true. <paramref name="title"/> 을 주면 제목 띠를 얹는다.
    /// </summary>
    public static bool Ask(Window owner, string text, string? title = null) =>
        new ConfirmDialog(text, title) { Owner = owner }.ShowDialog() == true;
}
