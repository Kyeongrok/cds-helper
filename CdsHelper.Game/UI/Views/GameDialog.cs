using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 게임 창 한 벌을 갖춘 밑바탕 창. 이대로도 "한 마디 알리고 확인 받기" 로 쓰고,
/// 물려받아 본문과 단추를 늘려 쓸 수도 있다.
/// </summary>
/// <remarks>
/// 창을 지을 때마다 되풀이하던 것들 — 테 두 겹, 제목 띠, 끌어 옮기기, 단추 초점 깜빡임,
/// ESC 로 닫기 — 을 여기 한 번만 적어 두었다. 물려받는 쪽은 <see cref="AddLine"/> 와
/// <see cref="AddButton"/> 만 부르면 된다.
/// <code>
///   ┌ 테 ────────────────────┐
///   │  제목 띠 (있을 때만)    │   GameUi.TitleFrame
///   │  본문                   │   AddLine · Add
///   │  단추 줄                │   AddButton
///   └────────────────────────┘
/// </code>
/// 본문과 단추 줄을 따로 담아 둔 까닭은 차례 때문이다. 한 칸에 몰아 두면 창을 지은 뒤에
/// 본문을 더할 때 이미 놓인 단추 <b>밑으로</b> 붙는다.
///
/// 단추는 <see cref="GameUi.FocusGroup"/> 에 묶여, 방향키로 옮기고 엔터로 고른다 —
/// 지금 어느 것이 골라져 있는지는 안쪽 테가 깜빡여 알린다(게임이 그렇게 알린다).
/// </remarks>
public class GameDialog : Window
{
    private readonly GameUi.FocusGroup _focus = new();

    /// <summary>글·칸이 쌓이는 자리.</summary>
    private readonly StackPanel _body = new();

    /// <summary>맨 아래 단추 줄.</summary>
    private readonly StackPanel _buttons = new()
    {
        Orientation = Orientation.Horizontal,
        HorizontalAlignment = HorizontalAlignment.Center,
        Margin = new Thickness(0, 4, 0, 18),
    };

    /// <param name="title">제목 띠에 얹을 글. 안 주면 띠를 두지 않는다.</param>
    protected GameDialog(string? title = null)
    {
        Title = title ?? "";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        // 620 은 게임 물음창 폭을 재어 맞춘 값이다. 글이 길면 여기서 접힌다.
        var stack = new StackPanel { MaxWidth = 620 };

        if (!string.IsNullOrEmpty(title))
        {
            // 원본 조각을 못 읽었으면 띠 없이 글만 낸다 — 민색 상자를 대신 두면 게임 것과 안 맞는다.
            var bar = GameUi.TitleFrame(GameUi.Sprites, title);
            if (bar != null) stack.Children.Add(bar);
        }

        stack.Children.Add(_body);
        stack.Children.Add(_buttons);

        Content = new Border
        {
            Background = GameUi.Back,
            BorderBrush = GameUi.Edge,
            BorderThickness = new Thickness(2),
            Margin = new Thickness(4),
            Child = stack,
        };

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(); return; }
            if (_focus.HandleKey(e.Key)) e.Handled = true;
        };
        GameUi.EnableDrag(this, stack);
    }

    /// <summary>본문에 무엇이든 하나 얹는다.</summary>
    protected void Add(UIElement element) => _body.Children.Add(element);

    /// <summary>
    /// 본문에 글 한 덩이를 얹는다. 길면 접힌다.
    /// </summary>
    /// <remarks>
    /// <c>AddText</c> 라 하지 않은 것은 <see cref="ContentControl.AddText"/> 와 이름이
    /// 겹치기 때문이다. 숨기면(<c>new</c>) 부르는 쪽이 어느 것인지 헷갈린다.
    /// </remarks>
    protected TextBlock AddLine(string text)
    {
        var block = new TextBlock
        {
            Text = text,
            Foreground = GameUi.Text,
            FontWeight = FontWeights.Bold,
            FontSize = 16,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 26,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            Margin = new Thickness(28, 20, 28, 16),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        Add(block);
        return block;
    }

    /// <summary>
    /// 아래 단추 줄에 단추 하나를 단다. 먼저 단 것에 초점이 가 있다.
    /// </summary>
    protected Border AddButton(string text, Action run, double width = 96)
    {
        var button = _focus.Add(text, run, width);
        _buttons.Children.Add(button);
        return button;
    }

    /// <summary>
    /// 한 마디 알리고 확인만 받는다. 늘리지 않고 이대로 쓸 때의 길이다.
    /// </summary>
    public static void Show(Window owner, string text, string? title = null)
    {
        var dialog = new GameDialog(title) { Owner = owner };
        dialog.AddLine(text);
        dialog.AddButton("확인", dialog.Close);
        dialog.ShowDialog();
    }
}
