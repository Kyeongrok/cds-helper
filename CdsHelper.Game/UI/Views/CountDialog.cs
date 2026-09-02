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
///   ┌──── 선원고용 ────┐
///   │ 고용할 사람 수 [ 0][계산기]명 │
///   │ 현재의 선원 수          0명 │
///   │ 최저 선원 수           12명 │
///   │   [  결정  ]  [  중단  ]   │
///   └──────────────────────────┘
/// </code>
/// 선원 모집·해고가 이 창을 제목과 줄만 갈아 쓴다(<c>0x004773CF</c> · <c>0x004774A4</c>).
///
/// 화면에서 본 대로 맞춘 것 넷이다.
/// <list type="bullet">
///   <item><b>↑↓ 가 없다.</b> 값은 칸 옆 계산기로만 넣는다.</item>
///   <item><b>눈금 줄에도 단위가 붙는다</b> — "0명" · "12명" 이지 "0" · "12" 가 아니다.</item>
///   <item>제목 띠에 <b>닫기(X)가 없다</b>. 나가는 길은 "중단" 이다.</item>
///   <item>결정·중단이 <b>같은 폭</b>으로 나란히 선다.</item>
/// </list>
/// 키보드 ↑↓ 는 안 보이는 채로 남겨 둔다 — 화면 모양을 건드리지 않는 덤이다.
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

    /// <summary>Shift 를 누르면 한 번에 이만큼 배로 뛴다.</summary>
    private const int Fast = 10;

    /// <summary>값이 놓이는 칸의 폭. 줄마다 수가 세로로 맞게 못 박는다.</summary>
    private const double ValueWidth = 90;

    /// <summary>눈금 줄 하나 — 이름과 값.</summary>
    /// <param name="Name">줄 이름("현재의 선원 수").</param>
    /// <param name="Value">그 값.</param>
    public readonly record struct Gauge(string Name, int Value);

    private readonly int _max;
    private readonly int _step;
    private readonly GameUi.GameLabel _count;
    private readonly GameButton _decide;

    /// <summary>고른 수. 중단하면 0 이다.</summary>
    private int _picked;

    private int _at;

    private CountDialog(string caption, string label, string unit, int max, int step,
                        bool full, Gauge[] lines)
    {
        _max = max;
        _step = step;

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
        // 값은 계산기로만 넣는다 — 게임도 칸 옆에 계산기 하나만 달아 두었다.
        pick.Children.Add(Pad());
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
            // 눈금 줄에도 단위가 붙는다 — 화면은 "0명" · "12명" 이다.
            row.Children.Add(Label(line.Name));
            row.Children.Add(Cell(Label($"{line.Value}{unit}")));
            rows.Children.Add(row);
        }

        _decide = new GameButton("결정", Decide) { On = false, MinWidth = ButtonWidth };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 12),
        };
        // "최대" 는 계산기 판 안에도 MAX 로 있다. 돈처럼 자릿수가 큰 창에서만 밖에 낸다.
        if (full)
            buttons.Children.Add(new GameButton("최대", () => { _at = _max; Paint(); })
                                 { MinWidth = ButtonWidth });
        buttons.Children.Add(_decide);
        buttons.Children.Add(new GameButton("중단", Close) { MinWidth = ButtonWidth });

        var page = new StackPanel();
        // 닫기(X)는 안 단다 — 화면의 이 창에는 없다. 나가는 길은 "중단" 이다.
        page.Children.Add(GameUi.TitleBar(caption, null));
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
        int step = _step * (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? Fast : 1);
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

    /// <summary>
    /// 칸 옆의 작은 계산기 단추. 누르면 숫자판이 떠서 값을 곧장 찍어 넣는다.
    /// </summary>
    /// <remarks>
    /// 판은 <see cref="NumberPadDialog"/> 다 — 신규 캐릭터 창의 연령·생일 칸이 여는 것과
    /// 같은 판이라 AC·DEL·MAX·MIN 이 그대로 있다. MAX 는 여기서 고를 수 있는 가장 큰 수,
    /// MIN 은 0 이다.
    /// </remarks>
    private UIElement Pad()
    {
        var box = GameUi.CalcButton(Type, PadSize);
        box.Margin = new Thickness(4, 0, 0, 0);
        return box;

        void Type()
        {
            if (NumberPadDialog.Ask(this, _at, 0, _max) is not { } typed) return;
            _at = Math.Clamp(typed, 0, _max);
            Paint();
        }
    }

    /// <summary>계산기 단추 한 칸의 크기.</summary>
    private const double PadSize = 15;

    /// <summary>결정·중단 한 단추의 가장 좁은 폭. 화면에서 재어 맞췄다.</summary>
    private const double ButtonWidth = 92;

    /// <summary>
    /// 수를 고르게 한다. 고른 수를 내고, 중단하거나 0 이면 0 이다.
    /// </summary>
    /// <param name="caption">창 제목("선원고용").</param>
    /// <param name="label">고르는 줄 이름("고용할 사람 수").</param>
    /// <param name="unit">단위("명").</param>
    /// <param name="max">고를 수 있는 가장 큰 수.</param>
    /// <param name="step">↑↓ 한 번에 움직이는 수. Shift 를 누르면 그 열 배로 뛴다.</param>
    /// <param name="full">참이면 "최대" 단추를 단다 — 돈처럼 자릿수가 큰 것에 쓴다.</param>
    /// <param name="lines">밑에 붙는 눈금 줄들.</param>
    public static int Ask(Window owner, string caption, string label, string unit,
                          int max, int step = 1, bool full = false, params Gauge[] lines)
    {
        if (max <= 0) return 0;

        var dialog = new CountDialog(caption, label, unit, max, step, full, lines) { Owner = owner };
        dialog.ShowDialog();
        return dialog._picked;
    }
}
