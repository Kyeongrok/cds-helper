using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CdsHelper.Game.UI.Views;

/// <summary>게임 알림 창처럼 한 줄 알리고 확인만 받는 작은 창.</summary>
public sealed class NoticeDialog : Window
{
    private readonly GameUi.FocusGroup _focus = new();

    private NoticeDialog(string text, string? title)
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
            Margin = new Thickness(0, 0, 0, 18),
        };
        buttons.Children.Add(_focus.Add("확인", Close, 96));

        var stack = new StackPanel();
        // 게임 알림 상자에도 제목 띠가 붙는 것이 있다 — "대실패" · "성스러운 항아리" 처럼.
        if (!string.IsNullOrEmpty(title) && GameUi.TitleFrame(GameUi.Sprites, title!) is { } bar)
        {
            bar.Margin = new Thickness(0, 6, 0, 0);
            stack.Children.Add(bar);
        }
        stack.Children.Add(new TextBlock
        {
            Text = text,
            Foreground = GameUi.Text,
            FontWeight = FontWeights.Bold,
            FontSize = 16,
            Margin = new Thickness(28, 20, 28, 16),
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        stack.Children.Add(buttons);

        Content = new Border
        {
            Background = GameUi.Back,
            BorderBrush = GameUi.Edge,
            BorderThickness = new Thickness(2),
            Margin = new Thickness(4),
            Child = stack,
        };

        KeyDown += (_, e) => { if (e.Key is Key.Escape or Key.Enter or Key.Space) Close(); };
        MouseRightButtonUp += (_, _) => Close();
    }

    /// <param name="title">제목 띠에 얹을 글. 비우면 띠를 안 단다.</param>
    public static void Show(Window owner, string text, string? title = null) =>
        new NoticeDialog(text, title) { Owner = owner }.ShowDialog();
}
