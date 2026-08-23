using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 「어디로 들어 가시겠습니까?」 — 그 도시의 건물을 늘어놓고 하나를 고르게 한다.
/// </summary>
/// <remarks>
/// 도시 커맨드의 "맵 포인트에 들어간다"(<c>0x0053BE10</c>)가 여는 창이고, 제목은
/// <c>0x0053BF38</c> 이다. 줄은 <b>건물 이름</b>이다 — "베렌의 탑" · "리스본 왕립 도서관"
/// 처럼 그 도시만의 이름이 뜨고, 이름이 없는 건물은 종류로 낸다.
///
/// 줄이 열 몇 개라 오른쪽에 굴림대가 선다. 줄마다 게임 띠 단추를 그대로 쓰므로
/// 도시 그림에서 건물을 누르는 것과 같은 모습이 된다.
/// </remarks>
internal sealed class MapPointDialog : Window
{
    /// <summary>줄이 이보다 많으면 굴림대를 낸다.</summary>
    private const int RowsShown = 12;

    /// <summary>줄 하나의 높이(띠 단추 + 사이).</summary>
    private const double RowHeight = 30;

    private int _picked = -1;

    private MapPointDialog(IReadOnlyList<string> names, string title)
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        var rows = new StackPanel();
        for (int i = 0; i < names.Count; i++)
        {
            int pick = i;
            var button = new GameButton(names[i], () => { _picked = pick; Close(); },
                                        BandStyle.Button, RowWidth);
            button.Margin = new Thickness(0, 1, 0, 1);
            rows.Children.Add(button);
        }

        var bar = GameUi.TitleBar(title, Close);
        GameUi.EnableDrag(this, bar);

        var stack = new StackPanel();
        stack.Children.Add(bar);
        stack.Children.Add(new ScrollViewer
        {
            MaxHeight = RowsShown * RowHeight,
            VerticalScrollBarVisibility = names.Count > RowsShown
                ? ScrollBarVisibility.Visible : ScrollBarVisibility.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(3, 2, 3, 3),
            Content = rows,
        });

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

    /// <summary>줄 하나의 너비. 도시 이름이 붙은 긴 줄까지 들어가게 잡는다.</summary>
    private const double RowWidth = 340;

    /// <summary>
    /// 창을 띄우고 고른 줄 번호를 낸다. 물렀으면 -1.
    /// </summary>
    /// <param name="title">제목 줄. 미니 게임 고르기처럼 다른 데서도 쓴다.</param>
    public static int Ask(Window owner, IReadOnlyList<string> names,
                          string title = "어디로 들어 가시겠습니까?")
    {
        if (names.Count == 0) return -1;

        var dialog = new MapPointDialog(names, title) { Owner = owner };
        dialog.ShowDialog();
        return dialog._picked;
    }
}
