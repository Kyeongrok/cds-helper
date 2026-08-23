using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 게임 대사 창처럼 한 마디 건네고 YES/NO 를 받는 창.
/// </summary>
/// <remarks>
/// 게임은 말하는 사람 얼굴을 왼쪽에 같이 띄우지만 초상화는 아직 안 꺼내 오므로 글만 낸다.
/// <see cref="PortDialog"/> 와 달리 문구를 밖에서 준다.
///
/// <b>자리는 게임 화면을 재어 맞췄다.</b> 세로로 쌓이는 차례가 이렇다.
/// <code>
///   외곽 프레임  6      본문 글    25
///   위 여백     10      사이       17
///   제목 띠     42      단추 줄    42
///   사이        14      아래 여백  26
///                       외곽 프레임 6
/// </code>
/// 가로는 프레임 안쪽 좌우 10 을 두어 <b>제목 띠가 520</b>, 본문 글이 488 이다.
/// 단추는 <b>114 x 42</b> 두 개를 26 띄워 가운데 놓는다(묶음 254).
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
        };
        // 초점이 간 단추의 안쪽 테가 깜빡인다 — 게임이 지금 고른 것을 그렇게 알린다.
        var yes = _focus.Add("YES", () => { DialogResult = true; }, ButtonWidth);
        var no = _focus.Add("NO", () => { DialogResult = false; }, ButtonWidth);
        yes.Height = no.Height = ButtonHeight;
        yes.Margin = new Thickness(0, 0, ButtonGap / 2, 0);
        no.Margin = new Thickness(ButtonGap / 2, 0, 0, 0);
        buttons.Children.Add(yes);
        buttons.Children.Add(no);

        var stack = new StackPanel { Width = BarWidth };

        // 게임은 물음창에도 진홍 장식 띠로 제목을 얹는다("게임 로드" 따위).
        bool titled = !string.IsNullOrEmpty(title);
        if (titled && GameUi.TitleFrame(GameUi.Sprites, title!) is { } bar)
        {
            bar.Width = BarWidth;
            bar.Height = BarHeight;
            stack.Children.Add(bar);
        }

        stack.Children.Add(new TextBlock
        {
            Text = text,
            Foreground = GameUi.Text,
            FontWeight = FontWeights.Bold,
            FontSize = 17,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = TextHeight,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            Margin = new Thickness(TextInset, titled ? TitleGap : 0, TextInset, TextGap),
        });
        stack.Children.Add(buttons);

        Content = new Border
        {
            Background = GameUi.Back,
            BorderBrush = GameUi.Edge,
            BorderThickness = new Thickness(FrameEdge),
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

    // ── 게임 화면에서 잰 자리 ────────────────────────────────────────────────

    /// <summary>바깥 테의 굵기.</summary>
    private const double FrameEdge = 6;

    /// <summary>테 안쪽 여백.</summary>
    private const double SidePad = 10, TopPad = 10, BottomPad = 26;

    /// <summary>제목 띠의 크기.</summary>
    private const double BarWidth = 520, BarHeight = 42;

    /// <summary>제목 띠와 본문 사이, 본문과 단추 줄 사이.</summary>
    private const double TitleGap = 14, TextGap = 17;

    /// <summary>본문 한 줄의 높이와 좌우 여백(글 너비 488).</summary>
    private const double TextHeight = 25, TextInset = 16;

    /// <summary>단추 크기와 사이.</summary>
    private const double ButtonWidth = 114, ButtonHeight = 42, ButtonGap = 26;

    /// <summary>
    /// 물어보고 YES 를 골랐으면 true. <paramref name="title"/> 을 주면 제목 띠를 얹는다.
    /// </summary>
    public static bool Ask(Window owner, string text, string? title = null) =>
        new ConfirmDialog(text, title) { Owner = owner }.ShowDialog() == true;
}
