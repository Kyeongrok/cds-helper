using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 지금까지 얻은 힌트를 늘어놓는 「취득 힌트 일람」. 그냥 보여 주기도 하고
/// (커맨드 창의 "힌트 정보"), 하나를 고르게 하기도 한다(왕궁의 "설득").
/// </summary>
/// <remarks>
/// 게임도 창 하나를 두 군데에서 쓴다. 고르는 쪽(<see cref="Pick"/>)은 EXE 의
/// <c>0x004769A0</c> 이 하는 일 그대로다 — 목록을 띄우고 고른 힌트 번호를 내며,
/// 중단하면 -1 이다. 힌트는 책을 읽으면 는다(볼트 <c>20.분석-도서관 책과 책등 색</c>).
/// </remarks>
public sealed class HintListDialog : Window
{
    /// <summary>목록 칸의 폭과 가장 높은 자리. 게임 갈무리에서 잰 값이다.</summary>
    private const double ListWidth = 420, ListMaxHeight = 300;

    /// <summary>아래 단추 둘의 폭과 사이.</summary>
    private const double ButtonWidth = 150, ButtonGap = 16;

    /// <summary>고른 줄. 아무것도 안 골랐으면 -1.</summary>
    private int _picked = -1;

    /// <summary>줄마다의 판. 고른 줄만 도드라지게 칠한다.</summary>
    private readonly List<Border> _rows = [];

    private readonly Border _decide;

    private HintListDialog(IReadOnlyList<string> hints, bool choosing, string caption)
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        var list = new StackPanel();
        for (int i = 0; i < hints.Count; i++)
        {
            int index = i;
            var row = new Border
            {
                Background = Brushes.Transparent,
                Padding = new Thickness(10, 1, 6, 1),
                Cursor = choosing ? Cursors.Hand : Cursors.Arrow,
                Child = new TextBlock
                {
                    Text = hints[i],
                    Foreground = Brushes.Black,
                    FontWeight = FontWeights.Bold,
                    FontSize = 15,
                },
            };
            if (choosing) row.MouseLeftButtonUp += (_, e) => { e.Handled = true; Select(index); };
            _rows.Add(row);
            list.Children.Add(row);
        }

        // 고르는 창이라도 아직 아무것도 안 골랐으면 결정은 흐리다 — 게임도 그렇다.
        _decide = GameUi.PushButton("결정", null);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 8),
        };
        _decide.Width = ButtonWidth;
        _decide.Margin = new Thickness(0, 0, ButtonGap / 2, 0);
        var stop = GameUi.PushButton("중단", Cancel, ButtonWidth);
        stop.Margin = new Thickness(ButtonGap / 2, 0, 0, 0);
        buttons.Children.Add(_decide);
        buttons.Children.Add(stop);

        var title = GameUi.TitleBar(caption, Cancel);
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
            // <b>가로로 넓고 세로는 줄 수를 따라간다.</b> 게임 창이 그렇다 — 두 줄이면
            // 두 줄만큼만 높고, 길어지면 그때 늘어나다 스무 줄쯤에서 멎고 굴러간다.
            // 예전에는 280x300 으로 박아 두어 줄이 몇 없어도 아래가 텅 비었다.
            Child = new ScrollViewer
            {
                Width = ListWidth,
                MaxHeight = ListMaxHeight,
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

        KeyDown += (_, e) => { if (e.Key is Key.Escape) Cancel(); };
        MouseRightButtonUp += (_, _) => Cancel();
    }

    /// <summary>한 줄을 고른다. 고르고 나야 결정이 살아난다.</summary>
    private void Select(int index)
    {
        _picked = index;
        for (int i = 0; i < _rows.Count; i++)
            _rows[i].Background = i == index ? GameUi.ItemFill : Brushes.Transparent;

        // 결정 단추를 살아 있는 것으로 갈아 끼운다(단추는 만들 때 손이 정해진다).
        if (_decide.Child is TextBlock label) label.Foreground = Brushes.Black;
        _decide.Cursor = Cursors.Hand;
        _decideReady = true;
    }

    private bool _decideReady;

    private void Cancel()
    {
        _picked = -1;
        Close();
    }

    /// <summary>줄을 늘어놓기만 한다. 하나도 없으면 그렇다고 알린다.</summary>
    /// <param name="caption">창 제목. 안 주면 「취득 힌트 일람」이다.</param>
    /// <param name="whenEmpty">줄이 하나도 없을 때 알릴 말.</param>
    public static void Show(Window owner, IReadOnlyList<string> hints,
                            string caption = "취득 힌트 일람",
                            string whenEmpty = "아직 얻은 힌트가 없다.")
    {
        if (hints.Count == 0)
        {
            NoticeDialog.Show(owner, whenEmpty);
            return;
        }
        new HintListDialog(hints, choosing: false, caption) { Owner = owner }.ShowDialog();
    }

    /// <summary>
    /// 한 줄을 고르게 한다. 고른 줄 번호를 내고, 중단하면 -1 이다.
    /// 고를 것이 없으면 <paramref name="whenEmpty"/> 로 알리고 -1 을 낸다.
    /// </summary>
    /// <remarks>
    /// 게임도 창 하나를 「취득 힌트 일람」과 「스폰서 일람」 두 군데에 쓴다
    /// (<c>0x004769A0</c> 와 <c>0x00476660</c> 이 같은 모양이다).
    /// </remarks>
    public static int Pick(Window owner, IReadOnlyList<string> items,
                           string caption = "취득 힌트 일람",
                           string whenEmpty = "설득 가능한 힌트가 없습니다")
    {
        if (items.Count == 0)
        {
            NoticeDialog.Show(owner, whenEmpty);
            return -1;
        }

        var dlg = new HintListDialog(items, choosing: true, caption) { Owner = owner };
        dlg._decide.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (dlg._decideReady) dlg.Close();
        };
        dlg.ShowDialog();
        return dlg._picked;
    }
}
