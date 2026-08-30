using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 힌트 하나를 펴 본 판 — 이름과 갈래, 그 이야기, 그리고 <b>부관의 평</b>.
/// </summary>
/// <remarks>
/// 「취득 힌트 일람」에서 한 줄을 고르고 결정을 누르면 뜬다. 글은 힌트 표의
/// <see cref="HintTable.Hint.Text"/> 다(힌트 줄 <c>+0x1C</c> 가 가리키는 글, 표
/// <c>0x00543FA0</c>) — 도서관에서 책을 읽을 때 펼친 책에 적히는 그 글이다.
///
/// 아래 한마디는 게임 표 <c>0x00560F38</c> 에서 온다. 한 줄이 <b>여덟 바이트</b>라
/// 앞이 부관이 있을 때, 뒤가 없을 때다(<c>0x0046EE92</c> 와 <c>0x0046EEBA</c>).
/// </remarks>
public sealed class HintDetailDialog : Window
{
    /// <summary>판의 색. 게임 갈무리에서 집은 회청색이다.</summary>
    private static readonly Brush PanelFill = Frozen(Color.FromRgb(0x6E, 0x82, 0xA6));
    private static readonly Brush PanelEdge = Frozen(Color.FromRgb(0x2C, 0x38, 0x50));

    /// <summary>판의 폭과 안쪽 여백, 글 한 줄의 높이.</summary>
    private const double PanelWidth = 296, PanelPad = 12, LineHeight = 20;

    /// <summary>글자 한 칸 — 한글 한 자가 두 칸이다.</summary>
    private const double CellWidth = 8;

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private HintDetailDialog(string head, string body, string comment)
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        var words = new StackPanel();
        words.Children.Add(Ink(head));
        words.Children.Add(new Border { Height = LineHeight });        // 한 줄 띄운다
        foreach (string line in Wrap(body, PanelWidth - PanelPad * 2))
            words.Children.Add(Ink(line));

        var panel = new Border
        {
            Width = PanelWidth,
            Background = PanelFill,
            BorderBrush = PanelEdge,
            BorderThickness = new Thickness(2),
            Padding = new Thickness(PanelPad),
            Child = words,
        };

        var ok = new GameUi.FocusGroup();
        var button = ok.Add("확인", Close, ButtonWidth);
        button.Height = UiSprites.BandHeight;

        var below = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        below.Children.Add(new GameUi.GameLabel(GameFont.WhiteColor, GameUi.ItemTextHeight)
        {
            Text = comment,
            Bold = true,
            FallbackBrush = GameUi.Text,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        below.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0),
            Children = { button },
        });

        var stack = new StackPanel();
        stack.Children.Add(panel);
        stack.Children.Add(below);

        Content = new Border
        {
            Background = GameUi.Back,
            BorderBrush = GameUi.Edge,
            BorderThickness = new Thickness(2),
            Padding = new Thickness(8, 8, 8, 12),
            Child = stack,
        };

        KeyDown += (_, e) =>
        {
            if (e.Key is Key.Escape) { Close(); return; }
            if (ok.HandleKey(e.Key)) e.Handled = true;
        };
        MouseRightButtonUp += (_, _) => Close();
        GameUi.EnableDrag(this, stack);
    }

    private const double ButtonWidth = 64;

    /// <summary>판 위에 글 한 줄 — 검은 벌이다.</summary>
    private static UIElement Ink(string line) =>
        new GameUi.GameLabel(GameFont.BlackColor, GameUi.ItemTextHeight)
        {
            Text = line,
            Bold = true,
            FallbackBrush = Brushes.Black,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

    /// <summary>판 너비에 맞춰 끊는다. 한글 한 자가 두 칸이다.</summary>
    private static List<string> Wrap(string text, double width)
    {
        var lines = new List<string>();
        var line = new StringBuilder();
        double used = 0;

        foreach (char c in text)
        {
            double w = c < 0x80 ? CellWidth : CellWidth * 2;
            if (used + w > width) { lines.Add(line.ToString()); line.Clear(); used = 0; }
            line.Append(c);
            used += w;
        }
        if (line.Length > 0) lines.Add(line.ToString());
        return lines;
    }

    /// <summary>
    /// 부관의 평(<c>0x00560F38</c>). 명성과 힌트 등급을 견주어 셋 가운데 하나다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   0046EE40  잣대 = 명성 / 2000
    ///   0046EE55  잣대 - 등급 == -1 이면  자리 = 1
    ///   0046EE7C  등급 &gt; 잣대 + 1 이면    자리 += 1
    ///   0046EE92  부관이 있으면 [자리][0], 없으면 [자리][1]
    /// </code>
    /// </remarks>
    public static string CommentOn(int grade, int fame, bool hasMate)
    {
        int mark = fame / FameStep;
        int at = mark - grade == -1 ? 1 : 0;
        if (grade > mark + 1) at++;
        at = Math.Clamp(at, 0, Comments.Length - 1);
        return Comments[at][hasMate ? 0 : 1];
    }

    /// <summary>명성을 재는 눈금(<c>0x0046EE67</c> 의 <c>0x7D0</c>).</summary>
    private const int FameStep = 2000;

    /// <summary>평 세 줄 — 앞이 부관이 있을 때, 뒤가 없을 때다.</summary>
    private static readonly string[][] Comments =
    [
        ["이거라면 발견할 수 있을 것 같군요. 빨리 찾으러 갑시다!", "발견할 수 있을 것 같습니다"],
        ["흥미있을 것 같군요. 스폰서를 찾읍시다!", "발견할 수 있을 것 같군요."],
        ["터무니 없는 이야기인 것 같군요. 찾기 힘들 것 같군요.", "찾을 수 있을 것 같지 않습니다."],
    ];

    /// <summary>힌트 하나를 펴 본다.</summary>
    public static void Show(Window owner, HintTable.Hint hint, string category,
                            int fame, bool hasMate)
    {
        string head = category.Length > 0 ? $"{hint.Name}({category})" : hint.Name;
        new HintDetailDialog(head, hint.Text, CommentOn(hint.Grade, fame, hasMate))
        {
            Owner = owner,
        }.ShowDialog();
    }
}
