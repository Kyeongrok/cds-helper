using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 부하편성 창 — 부관·항해사·측량사·통역 네 자리를 보여 주고 서로 바꾸게 한다.
/// </summary>
/// <remarks>
/// 여관과 술집의 "부하편성" 으로 연다. 게임 화면을 그대로 옮겼다.
/// <code>
///   ┌ 부하편성 ────────────┐
///   │ 부관  : 후안·데·에스칸데 │
///   │ 항해사 : 나코다·이스마엘  │
///   │ 측량사 : 샤비에르·데·야소 │   <- 한 줄을 누르면 남색으로 잡힌다
///   │ 통역  : 안토니오·피가페따 │
///   └──────────────────┘
///        [결정]   [중단]
/// </code>
/// <b>두 줄을 눌러 자리를 맞바꾼다.</b> 먼저 누른 줄이 잡히고, 다음 줄을 누르면 둘이
/// 바뀌면서 잡힘이 풀린다. 같은 줄을 다시 누르면 그냥 놓는다.
///
/// 자리 이름은 게임 EXE 의 표(<c>0x00571038</c>) 차례 그대로다.
///
/// 바꾼 것은 <b>결정을 눌러야</b> 들어간다. 중단하면 들어올 때 그대로 되돌린다 —
/// 게임도 두 단추를 그렇게 가른다.
/// </remarks>
public sealed class MateRosterDialog : Window
{
    /// <summary>잡힌 줄에 씌우는 남색. 다른 목록과 같은 색이다.</summary>
    private static readonly Brush Picked = Freeze(Color.FromRgb(0x3A, 0x5A, 0x9A));

    private static SolidColorBrush Freeze(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private readonly Player _player;

    /// <summary>들어올 때의 자리. 중단하면 이대로 되돌린다.</summary>
    private readonly string[] _before;

    private readonly Border[] _rows;
    private int _held = -1;

    private MateRosterDialog(Player player)
    {
        _player = player;
        _before = [.. player.Mates];
        _rows = new Border[Player.MaxMates];

        Title = "부하편성";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        var list = new StackPanel();
        for (int i = 0; i < _rows.Length; i++)
        {
            _rows[i] = Row(i);
            list.Children.Add(_rows[i]);
        }

        // 게임은 줄을 어두운 창 바탕이 아니라 밝은 칸 위에 얹는다.
        var page = new Border
        {
            Background = GameUi.PageFill,
            BorderBrush = GameUi.ItemEdge,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(6, 4, 6, 4),
            Child = list,
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 12),
        };
        buttons.Children.Add(new GameUi.BandButton("결정", Decide, 110));
        buttons.Children.Add(new GameUi.BandButton("중단", Cancel, 110));

        var title = GameUi.TitleBar("부하편성", Cancel);
        GameUi.EnableDrag(this, title);

        var stack = new StackPanel { MinWidth = 330 };
        stack.Children.Add(title);
        stack.Children.Add(page);
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
    }

    /// <summary>자리 한 줄.</summary>
    private Border Row(int slot)
    {
        var row = new Border
        {
            Background = Brushes.Transparent,
            Padding = new Thickness(2, 3, 2, 3),
            Cursor = Cursors.Hand,
            Child = Line(slot, held: false),
        };
        row.MouseLeftButtonDown += (_, e) => e.Handled = true;
        row.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            Touch(slot);
        };
        return row;
    }

    /// <summary>줄 속 — "부관 : 이름". 자리 이름은 너비를 맞춰 콜론이 세로로 선다.</summary>
    private FrameworkElement Line(int slot, bool held)
    {
        string who = _player.MateAt(slot);
        var line = new DockPanel { LastChildFill = true };

        var role = Label(Player.MateRoles[slot], held);
        role.Width = 64;
        role.HorizontalAlignment = HorizontalAlignment.Right;
        DockPanel.SetDock(role, Dock.Left);
        line.Children.Add(role);

        var colon = Label(":", held);
        colon.Margin = new Thickness(8, 0, 8, 0);
        DockPanel.SetDock(colon, Dock.Left);
        line.Children.Add(colon);

        // 빈 자리는 줄만 남긴다 — 게임도 이름 없는 자리를 비워 둔다.
        line.Children.Add(Label(who.Length > 0 ? who : "", held));
        return line;
    }

    /// <summary>줄에 얹는 글씨. 잡힌 줄은 남색 위라 흰빛으로 뒤집는다.</summary>
    private static GameUi.GameLabel Label(string text, bool held) =>
        new(held ? GameFont.WhiteColor : GameFont.ButtonColor)
        {
            Margin = new Thickness(6, 0, 6, 0),
            FallbackBrush = held ? Brushes.White : Brushes.Black,
            Text = text,
        };

    /// <summary>
    /// 줄을 누른다. 아무것도 안 잡혀 있으면 잡고, 잡혀 있으면 그 자리와 맞바꾼다.
    /// </summary>
    private void Touch(int slot)
    {
        if (_held < 0)
        {
            _held = slot;
            Paint();
            return;
        }

        if (_held != slot) _player.SwapMates(_held, slot);
        _held = -1;
        Paint();
    }

    /// <summary>줄을 다시 그린다. 잡힌 줄만 남색이다.</summary>
    private void Paint()
    {
        for (int i = 0; i < _rows.Length; i++)
        {
            bool held = i == _held;
            _rows[i].Background = held ? Picked : Brushes.Transparent;
            _rows[i].Child = Line(i, held);
        }
    }

    private void Decide() => Close();

    /// <summary>들어올 때 자리로 되돌리고 닫는다.</summary>
    private void Cancel()
    {
        for (int i = 0; i < _before.Length; i++) _player.SetMate(i, _before[i]);
        Close();
    }

    /// <summary>부하편성 창을 연다.</summary>
    public static void Show(Window owner, Player player) =>
        new MateRosterDialog(player) { Owner = owner }.ShowDialog();
}
