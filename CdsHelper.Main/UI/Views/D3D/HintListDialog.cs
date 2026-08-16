using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace CdsHelper.Main.UI.Views.D3D;

/// <summary>
/// 도시 커맨드 창의 "힌트 정보" — 지금까지 얻은 힌트를 늘어놓는 「취득 힌트 일람」.
/// </summary>
/// <remarks>
/// 게임처럼 고르는 시늉만 낸다 — 결정은 흐리고 중단으로 닫는다. 힌트는 책을 읽으면 는다
/// (볼트 <c>20.분석-도서관 책과 책등 색</c>).
/// </remarks>
public sealed class HintListDialog : Window
{
    private HintListDialog(IReadOnlyList<string> hints)
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        var list = new StackPanel();
        foreach (var name in hints)
            list.Children.Add(new TextBlock
            {
                Text = name,
                Foreground = Brushes.Black,
                FontWeight = FontWeights.Bold,
                FontSize = 15,
                Margin = new Thickness(10, 1, 6, 1),
            });

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 8),
        };
        buttons.Children.Add(GameUi.PushButton("결정", null));   // 고를 것이 아직 없다
        buttons.Children.Add(GameUi.PushButton("중단", Close));

        var title = GameUi.TitleBar("취득 힌트 일람", Close);
        GameUi.EnableDrag(this, title);

        var stack = new StackPanel();
        stack.Children.Add(title);
        stack.Children.Add(new Border
        {
            Background = GameUi.PageFill,
            BorderBrush = GameUi.ItemEdge,
            BorderThickness = new Thickness(2),
            Margin = new Thickness(4, 4, 4, 0),
            Padding = new Thickness(6, 4, 6, 4),
            Child = new ScrollViewer
            {
                Height = 300,
                Width = 280,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = list,
            },
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

        KeyDown += (_, e) => { if (e.Key is Key.Escape) Close(); };
        MouseRightButtonUp += (_, _) => Close();
    }

    /// <summary>얻은 힌트를 늘어놓는다. 하나도 없으면 그렇다고 알린다.</summary>
    public static void Show(Window owner, IReadOnlyList<string> hints)
    {
        if (hints.Count == 0)
        {
            NoticeDialog.Show(owner, "아직 얻은 힌트가 없다.");
            return;
        }
        new HintListDialog(hints) { Owner = owner }.ShowDialog();
    }
}
