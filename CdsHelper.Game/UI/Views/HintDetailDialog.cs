using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 힌트 하나를 펴 본 <b>파란 판</b> — 이름과 갈래, 그리고 그 이야기.
/// </summary>
/// <remarks>
/// 「취득 힌트 일람」에서 한 줄을 고르고 결정을 누르면 뜬다. 글은 힌트 표의
/// <see cref="HintTable.Hint.Text"/> 다(힌트 줄 <c>+0x1C</c> 가 가리키는 글, 표
/// <c>0x00543FA0</c>) — 도서관에서 책을 읽을 때 펼친 책에 적히는 그 글이다.
///
/// <b>부관의 평은 이 판에 안 붙는다.</b> 게임은 판 하나와 <b>말 창 하나</b>를 따로 띄운다 —
/// 파란 판은 화면 위쪽에 뜨고, "발견할 수 있을 것 같군요." 는 여느 대사처럼 아래쪽 말 창에
/// 뜬다. 그 말 창의 확인을 누르면 둘 다 닫힌다. 예전에는 한 창에 붙여 두었는데 그러면
/// 판이 세로로 길어지고 글도 잘렸다.
///
/// 평 글은 게임 표 <c>0x00560F38</c> 에서 온다. 한 줄이 <b>여덟 바이트</b>라 앞이 부관이
/// 있을 때, 뒤가 없을 때다(<c>0x0046EE92</c> 와 <c>0x0046EEBA</c>).
/// </remarks>
public sealed class HintDetailDialog : Window
{
    /// <summary>판의 색. 게임 갈무리에서 집은 회청색이다.</summary>
    private static readonly Brush PanelFill = Frozen(Color.FromRgb(0x6E, 0x82, 0xA6));
    private static readonly Brush PanelEdge = Frozen(Color.FromRgb(0x2C, 0x38, 0x50));

    /// <summary>
    /// 판 속에 글이 놓이는 폭. 게임 판은 한 줄에 한글 열아홉 자쯤 든다.
    /// </summary>
    private const double TextWidth = 19 * 16;

    /// <summary>판 안쪽 여백과 한 줄 높이.</summary>
    private const double PanelPad = 14, LineHeight = 20;

    /// <summary>글꼴을 못 읽었을 때 눈대중으로 쓸 글자 폭(한글은 두 배).</summary>
    private const double GuessCell = 8;

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private HintDetailDialog(string head, string body)
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        ShowInTaskbar = false;
        Background = Brushes.Transparent;

        var words = new StackPanel();
        words.Children.Add(Ink(head));
        words.Children.Add(new Border { Height = LineHeight });        // 한 줄 띄운다
        foreach (string line in Wrap(body, TextWidth))
            words.Children.Add(Ink(line));

        // 게임 판은 테가 두 겹이다 — 짙은 선 안에 한 칸 띄우고 다시 짙은 선.
        Content = new Border
        {
            BorderBrush = PanelEdge,
            BorderThickness = new Thickness(2),
            Background = PanelFill,
            Padding = new Thickness(2),
            Child = new Border
            {
                BorderBrush = PanelEdge,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(PanelPad),
                Child = new StackPanel { Width = TextWidth, Children = { words } },
            },
        };

        KeyDown += (_, e) => { if (e.Key is Key.Escape) Close(); };
    }

    /// <summary>판 위에 글 한 줄 — 검은 벌이다.</summary>
    private static UIElement Ink(string line) =>
        new GameUi.GameLabel(GameFont.BlackColor, GameUi.ItemTextHeight)
        {
            Text = line,
            Bold = true,
            FallbackBrush = Brushes.Black,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

    /// <summary>
    /// 판 너비에 맞춰 끊는다. 자 너비는 <b>게임 글꼴에 직접 물어본다</b> —
    /// 한글 16점 · ASCII 8점으로 어림하던 것이 실제와 어긋나 글이 오른쪽으로 삐져나갔다.
    /// </summary>
    private static List<string> Wrap(string text, double width)
    {
        var font = GameUi.Font;
        var lines = new List<string>();
        var line = new StringBuilder();
        double used = 0;

        foreach (char c in text)
        {
            double w = font?.TextWidth(c.ToString()) ?? (c < 0x80 ? GuessCell : GuessCell * 2);
            if (used + w > width && line.Length > 0)
            {
                lines.Add(line.ToString());
                line.Clear();
                used = 0;
            }
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

    /// <summary>판이 주인 창 위쪽에서 얼마나 내려앉는지.</summary>
    private const double PanelTop = 40;

    /// <summary>
    /// 힌트 하나를 펴 본다 — 파란 판을 띄우고, 부관의 평은 <b>따로</b> 말 창으로 낸다.
    /// </summary>
    public static void Show(Window owner, HintTable.Hint hint, string category,
                            int fame, bool hasMate)
    {
        string head = category.Length > 0 ? $"{hint.Name}({category})" : hint.Name;
        var panel = new HintDetailDialog(head, hint.Text) { Owner = owner };

        // 판은 화면 위쪽에 세운다. 말 창은 여느 대사처럼 가운데에 뜨므로 겹치지 않는다.
        panel.SourceInitialized += (_, _) =>
        {
            panel.Left = owner.Left + (owner.ActualWidth - panel.ActualWidth) / 2;
            panel.Top = owner.Top + PanelTop;
        };
        panel.Show();

        try
        {
            ConfirmDialog.Tell(owner, CommentOn(hint.Grade, fame, hasMate));
        }
        finally
        {
            panel.Close();
        }
    }
}
