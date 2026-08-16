using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CdsHelper.Main.UI.Views.D3D;

/// <summary>
/// 게임 대사 창처럼 한 마디 건네고 YES/NO 를 받는 창.
/// </summary>
/// <remarks>
/// 게임은 말하는 사람 얼굴을 왼쪽에 같이 띄우지만 초상화는 아직 안 꺼내 오므로 글만 낸다.
/// <see cref="PortDialog"/> 와 달리 문구를 밖에서 준다.
/// </remarks>
public sealed class ConfirmDialog : Window
{
    private ConfirmDialog(string text)
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
            Margin = new Thickness(0, 4, 0, 18),
        };
        buttons.Children.Add(GameUi.PushButton("YES", () => { DialogResult = true; }, 96));
        buttons.Children.Add(GameUi.PushButton("NO", () => { DialogResult = false; }, 96));

        var stack = new StackPanel { MaxWidth = 620 };
        stack.Children.Add(new TextBlock
        {
            Text = text,
            Foreground = GameUi.Text,
            FontWeight = FontWeights.Bold,
            FontSize = 16,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 26,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            Margin = new Thickness(28, 20, 28, 16),
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

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) DialogResult = false;
            if (e.Key == Key.Enter) DialogResult = true;
        };
        GameUi.EnableDrag(this, stack);
    }

    /// <summary>물어보고 YES 를 골랐으면 true.</summary>
    public static bool Ask(Window owner, string text) =>
        new ConfirmDialog(text) { Owner = owner }.ShowDialog() == true;
}
