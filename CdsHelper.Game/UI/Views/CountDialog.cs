using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 수를 하나 고르게 하는 작은 창 — 고를 줄 하나와 눈금 줄 몇을 얹는다.
/// </summary>
/// <remarks>
/// 게임의 <c>0x00454AA0</c> 을 옮겼다. 인자 차례가 그대로 화면 모양이다.
/// <code>
///   0x00454AA0(제목, 1, 라벨, 0, 단위, 0, 최대, 라벨2, 값2, 라벨3, 값3, 0)
///
///   ┌ 선원고용 ──────────────┐
///   │  고용할 사람 수    12 ↑↓ 명 │
///   │  현재의 선원 수    15       │
///   │  최저 선원 수      15       │
///   │        [결정]  [중단]       │
///   └────────────────────────┘
/// </code>
/// 선원 모집·해고가 이 창을 제목과 줄만 갈아 쓴다(<c>0x004773CF</c> · <c>0x004774A4</c>).
///
/// ↑↓ 와 Shift 로 열씩 뛰는 것은 보급 화면(<see cref="SupplyDialog"/>)과 같은 결이다 —
/// 게임 원본은 자릿수를 눌러 넣는 꼴이지만 그쪽은 아직 흉내내지 않는다.
/// </remarks>
public sealed class CountDialog : Window
{
    /// <summary>화면 바탕. 보급·계약 화면과 같은 밤색 판이다.</summary>
    private static readonly Brush Back = Frozen(Color.FromRgb(0x31, 0x18, 0x18));

    /// <summary>테를 두르는 짙은 선.</summary>
    private static readonly Brush Line = Frozen(Color.FromRgb(0x11, 0x09, 0x09));

    /// <summary>글꼴 조각을 못 읽었을 때 물러설 글씨색.</summary>
    private static readonly Brush Ink = Frozen(Color.FromRgb(0xCB, 0xC5, 0xC5));

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    /// <summary>↑↓ 한 번에 움직이는 수. Shift 를 누르면 열 배로 뛴다.</summary>
    private const int Step = 1, FastStep = 10;

    /// <summary>값이 놓이는 칸의 폭. 줄마다 수가 세로로 맞게 못 박는다.</summary>
    private const double ValueWidth = 90;

    /// <summary>눈금 줄 하나 — 이름과 값.</summary>
    /// <param name="Name">줄 이름("현재의 선원 수").</param>
    /// <param name="Value">그 값.</param>
    public readonly record struct Gauge(string Name, int Value);

    private readonly int _max;
    private readonly GameUi.GameLabel _count;
    private readonly GameButton _decide;

    /// <summary>고른 수. 중단하면 0 이다.</summary>
    private int _picked;

    private int _at;

    private CountDialog(string caption, string label, string unit, int max, Gauge[] lines)
    {
        _max = max;

        Title = caption;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = Back;

        _count = Label("");

        var pick = new StackPanel { Orientation = Orientation.Horizontal };
        pick.Children.Add(Label(label));
        pick.Children.Add(Cell(_count));
        pick.Children.Add(Arrow("↑", () => Bump(+1)));
        pick.Children.Add(Arrow("↓", () => Bump(-1)));
        pick.Children.Add(Label(" " + unit));

        var rows = new StackPanel { Margin = new Thickness(16, 12, 16, 4) };
        rows.Children.Add(pick);
        foreach (var line in lines)
        {
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 2, 0, 0),
            };
            // 눈금 줄에는 단위를 안 붙인다 — 게임도 단위는 고르는 줄에만 준다.
            row.Children.Add(Label(line.Name));
            row.Children.Add(Cell(Label($"{line.Value}")));
            rows.Children.Add(row);
        }

        _decide = new GameButton("결정", Decide) { On = false };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 10),
        };
        buttons.Children.Add(_decide);
        buttons.Children.Add(new GameButton("중단", Close));

        var page = new StackPanel();
        page.Children.Add(GameUi.TitleBar(caption, Close));
        page.Children.Add(rows);
        page.Children.Add(buttons);

        var frame = GameUi.InfoFrame(page, Back, Line);
        GameUi.EnableDrag(this, frame);
        Content = frame;

        KeyDown += OnKey;
        MouseRightButtonUp += (_, _) => Close();
        Paint();
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape: Close(); break;
            case Key.Up: Bump(+1); e.Handled = true; break;
            case Key.Down: Bump(-1); e.Handled = true; break;
            case Key.Enter or Key.Space when _at > 0: Decide(); e.Handled = true; break;
        }
    }

    private void Bump(int by)
    {
        int step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? FastStep : Step;
        _at = Math.Clamp(_at + by * step, 0, _max);
        Paint();
    }

    private void Paint()
    {
        _count.Text = $"{_at}";
        _decide.On = _at > 0;
    }

    private void Decide()
    {
        if (_at <= 0) return;
        _picked = _at;
        Close();
    }

    /// <summary>밤색 판 위에 얹는 밝은 글씨.</summary>
    private static GameUi.GameLabel Label(string text) => new(GameFont.WhiteColor)
    {
        Text = text,
        FallbackBrush = Ink,
        HorizontalAlignment = HorizontalAlignment.Left,
    };

    /// <summary>값을 오른쪽으로 밀어 붙인 한 칸. 줄마다 수가 세로로 맞는다.</summary>
    private static FrameworkElement Cell(UIElement inner) => new Border
    {
        Width = ValueWidth,
        Child = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { inner },
        },
    };

    /// <summary>↑·↓ 한 칸. 보급 화면 것과 같다.</summary>
    private static UIElement Arrow(string mark, Action run)
    {
        var box = new Border
        {
            Width = 15,
            Background = GameUi.ItemFill,
            BorderBrush = GameUi.ItemEdge,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(2, 0, 0, 0),
            Cursor = Cursors.Hand,
            Child = new TextBlock
            {
                Text = mark,
                Foreground = Brushes.Black,
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center,
            },
        };
        // 누름은 삼킨다 — 판 끌기가 먼저 걸리면 마우스를 잡아 버려 뗌이 안 온다.
        box.MouseLeftButtonDown += (_, e) => e.Handled = true;
        box.MouseLeftButtonUp += (_, e) => { e.Handled = true; run(); };
        return box;
    }

    /// <summary>
    /// 수를 고르게 한다. 고른 수를 내고, 중단하거나 0 이면 0 이다.
    /// </summary>
    /// <param name="caption">창 제목("선원고용").</param>
    /// <param name="label">고르는 줄 이름("고용할 사람 수").</param>
    /// <param name="unit">단위("명").</param>
    /// <param name="max">고를 수 있는 가장 큰 수.</param>
    /// <param name="lines">밑에 붙는 눈금 줄들.</param>
    public static int Ask(Window owner, string caption, string label, string unit,
                          int max, params Gauge[] lines)
    {
        if (max <= 0) return 0;

        var dialog = new CountDialog(caption, label, unit, max, lines) { Owner = owner };
        dialog.ShowDialog();
        return dialog._picked;
    }
}
