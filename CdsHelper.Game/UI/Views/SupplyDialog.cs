using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 항구 보급 화면 — 식량·물·자재·탄약을 통 단위로 사서 싣는다.
/// </summary>
/// <remarks>
/// 게임 화면을 그대로 옮겼다.
/// <code>
///   용량 2871/2872   중량 21745/26375   소지금 3805119닢
///   탑재품명    단중량/단가   현재량   보충량      가격
///   식량           5/   19   1151통  +  276 ↑↓   5244닢
///   물            10/   12   1151통  +  276 ↑↓   3312닢
///   자재           5/   31      0통  +    0 ↑↓      0닢
///   탄약          20/   31     17통  +    0 ↑↓      0닢
///                                        총계   8556닢
///   [최대] [10일분] [일지정] [전회분]        [결정] [돌아간다]
/// </code>
/// 단추 글은 게임 것 그대로다(<c>0x00545650</c> 벌 — 결정·최대·10일분·일지정·전회분).
/// 품목 이름도 그렇다(<c>0x0055F248</c>~, 갈래 분기는 <c>0x004208A0</c>).
///
/// <b>날수는 선원수로 센다.</b> 게임 <c>0x00494010</c> 이
/// <c>날수 = min(식량통, 물통) * 10 / 총선원수</c> 이므로, 한 사람이 하루에 한 단위를 쓰고
/// 한 통이 열 단위다. 그래서 <b>10일분은 선원수만큼의 통</b>이다.
///
/// <b>지금 안 되는 것</b> — "일지정"(며칠분인지 손으로 적기)과 "전회분"(지난번과 같이)은
/// 자리만 두고 흐리게 낸다. 앞엣것은 수를 적어 넣는 창이 아직 없고(셈은
/// <see cref="FillDays"/> 로 이미 되어 있다), 뒤엣것은 지난번에 얼마를 실었는지 적어 두는
/// 자리가 없다.
///
/// 용량·중량은 함대가 실을 수 있는 양이다(<see cref="Player.Capacity"/> ·
/// <see cref="Player.Tonnage"/> — 배마다의 적재량·톤수를 더한 것). 게임은 여기에 실어 둔
/// 교역품까지 같이 세는데, 우리 쪽은 아직 보급품만 센다.
/// </remarks>
public sealed class SupplyDialog : Window
{
    /// <summary>↑↓ 한 번에 움직이는 통 수. 누르고 있으면 열 배로 뛴다(Shift).</summary>
    private const int Step = 1, FastStep = 10;

    private readonly Player _player;
    private readonly int _rate;

    /// <summary>줄마다 지금 더 실으려는 통 수.</summary>
    private readonly int[] _add = new int[Supply.Count];

    private readonly GameUi.GameLabel[] _addLabels = new GameUi.GameLabel[Supply.Count];
    private readonly GameUi.GameLabel[] _costLabels = new GameUi.GameLabel[Supply.Count];
    private readonly GameUi.GameLabel _head = Label("");
    private readonly GameUi.GameLabel _total = Label("");
    private readonly GameButton _decide;

    private SupplyDialog(Player player, int rate)
    {
        _player = player;
        _rate = rate;

        Title = "보급";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        var page = new StackPanel { Margin = new Thickness(10, 6, 10, 4) };
        page.Children.Add(_head);
        page.Children.Add(Header());
        for (int i = 0; i < Supply.Count; i++) page.Children.Add(Row(i));

        var totalLine = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
        };
        totalLine.Children.Add(Label("총계"));
        totalLine.Children.Add(_total);
        page.Children.Add(totalLine);

        // 게임은 줄을 어두운 창 바탕이 아니라 밝은 칸 위에 얹는다.
        var board = new Border
        {
            Background = GameUi.PageFill,
            BorderBrush = GameUi.ItemEdge,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(6, 4, 6, 4),
            Child = page,
        };

        _decide = new GameButton("결정", Decide, width: 110) { On = false };

        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(new GameButton("최대", Fill, width: 96));
        left.Children.Add(new GameButton("10일분", TenDays, width: 96));
        left.Children.Add(new GameButton("일지정", null, width: 96));
        left.Children.Add(new GameButton("전회분", null, width: 96));

        var right = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        right.Children.Add(_decide);
        right.Children.Add(new GameButton("돌아간다", Close, width: 110));

        var buttons = new DockPanel { Margin = new Thickness(6, 4, 6, 12) };
        DockPanel.SetDock(left, Dock.Left);
        buttons.Children.Add(left);
        buttons.Children.Add(right);

        var title = GameUi.TitleBar("보급", Close);
        GameUi.EnableDrag(this, title);

        var stack = new StackPanel { MinWidth = 560 };
        stack.Children.Add(title);
        stack.Children.Add(board);
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
        Paint();
    }

    // ── 줄 짓기 ──────────────────────────────────────────────────────────────

    private static UIElement Header() =>
        Line(Label("탑재품명"), Right(Label("단중량/단가"), 150),
             Right(Label("현재량"), 90), Right(Label("보충량"), 110), Right(Label("가격"), 90));

    private UIElement Row(int index)
    {
        var supply = Supply.All[index];

        _addLabels[index] = Label("");
        _costLabels[index] = Label("");

        var spin = new StackPanel { Orientation = Orientation.Horizontal };
        spin.Children.Add(Right(_addLabels[index], 56));
        spin.Children.Add(Arrow("↑", () => Bump(index, +1)));
        spin.Children.Add(Arrow("↓", () => Bump(index, -1)));

        return Line(Label(supply.Name),
                    Right(Label($"{supply.UnitWeight,5}/{supply.PriceAt(_rate),5}"), 150),
                    Right(Label($"{_player.SupplyOf(supply.Kind)}통"), 90),
                    Right(spin, 110),
                    Right(_costLabels[index], 90));
    }

    /// <summary>줄 하나를 칸으로 나눠 세운다.</summary>
    private static UIElement Line(params UIElement[] cells)
    {
        var line = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 2, 0, 2) };
        for (int i = 0; i < cells.Length; i++)
        {
            DockPanel.SetDock(cells[i], i == 0 ? Dock.Left : Dock.Right);
            line.Children.Add(cells[i]);
        }
        return line;
    }

    private static FrameworkElement Right(UIElement inner, double width) => new Border
    {
        Width = width,
        HorizontalAlignment = HorizontalAlignment.Right,
        Child = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { inner },
        },
    };

    /// <summary>↑·↓ 한 칸. 게임처럼 작은 네모 두 개다.</summary>
    private static UIElement Arrow(string mark, Action run)
    {
        var box = new Border
        {
            Width = 16,
            Background = GameUi.ItemFill,
            BorderBrush = GameUi.ItemEdge,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(1, 0, 0, 0),
            Cursor = Cursors.Hand,
            Child = new TextBlock
            {
                Text = mark,
                Foreground = Brushes.Black,
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center,
            },
        };
        box.MouseLeftButtonDown += (_, e) => e.Handled = true;
        box.MouseLeftButtonUp += (_, e) => { e.Handled = true; run(); };
        return box;
    }

    private static GameUi.GameLabel Label(string text) =>
        new(GameFont.ButtonColor) { Text = text, FallbackBrush = Brushes.Black };

    // ── 셈 ───────────────────────────────────────────────────────────────────

    /// <summary>더 실으려는 것까지 넣은 통 수.</summary>
    private int Barrels => _player.LoadedBarrels + _add.Sum();

    /// <summary>더 실으려는 것까지 넣은 무게.</summary>
    private int Weight => _player.LoadedWeight
                          + Supply.All.Sum(s => _add[(int)s.Kind] * s.UnitWeight);

    private int Cost(int index) => _add[index] * Supply.All[index].PriceAt(_rate);

    private int Total => Enumerable.Range(0, Supply.Count).Sum(Cost);

    /// <summary>한 통 더 실을 수 있는지 — 용량·중량·소지금을 다 본다.</summary>
    private bool CanAdd(int index)
    {
        var supply = Supply.All[index];
        return Barrels + 1 <= _player.Capacity
               && Weight + supply.UnitWeight <= _player.Tonnage
               && Total + supply.PriceAt(_rate) <= _player.Gold;
    }

    private void Bump(int index, int by)
    {
        int step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? FastStep : Step;
        for (int i = 0; i < step; i++)
        {
            if (by > 0 && !CanAdd(index)) break;
            if (by < 0 && _add[index] <= 0) break;
            _add[index] += by > 0 ? 1 : -1;
        }
        Paint();
    }

    /// <summary>실을 수 있는 데까지 채운다. 게임의 "최대" 다.</summary>
    private void Fill()
    {
        for (int i = 0; i < Supply.Count; i++)
            while (CanAdd(i)) _add[i]++;
        Paint();
    }

    /// <summary>
    /// 열흘 갈 만큼 채운다. <b>선원수만큼의 통</b>이다 — 한 사람이 하루에 한 단위를 쓰고
    /// 한 통이 열 단위이므로(<see cref="Supply.BarrelsForDays"/>) 열흘이면 딱 선원수다.
    /// </summary>
    private void TenDays() => FillDays(10);

    private void FillDays(int days)
    {
        int want = Supply.BarrelsForDays(days, _player.Crew);
        for (int i = 0; i < Supply.Count; i++)
        {
            // 식량과 물만 날수로 센다 — 자재·탄약은 날마다 닳는 것이 아니다.
            if (!Supply.All[i].IsDaily) continue;
            int need = want - _player.SupplyOf(Supply.All[i].Kind);
            while (_add[i] < need && CanAdd(i)) _add[i]++;
        }
        Paint();
    }

    private void Paint()
    {
        for (int i = 0; i < Supply.Count; i++)
        {
            _addLabels[i].Text = $"+{_add[i],5}";
            _costLabels[i].Text = $"{Cost(i)}닢";
        }
        _head.Text = $"용량 {Barrels,6}/{_player.Capacity,6}    "
                     + $"중량 {Weight,6}/{_player.Tonnage,6}    "
                     + $"소지금 {_player.Gold}닢";
        _total.Text = $"{Total,8}닢";
        _decide.On = Total > 0;
    }

    /// <summary>산 것을 싣고 값을 치른다.</summary>
    private void Decide()
    {
        int total = Total;
        if (total <= 0 || total > _player.Gold) return;

        for (int i = 0; i < Supply.Count; i++)
            if (_add[i] > 0) _player.AddSupply(Supply.All[i].Kind, _add[i]);
        _player.SetGold(_player.Gold - total);

        GameDialog.Show(this, "고맙네!");
        Close();
    }

    /// <summary>보급 화면을 연다. 배가 없으면 실을 데가 없다.</summary>
    public static void Show(Window owner, Player player, int rate = 100)
    {
        if (player.Ships.Count == 0)
        {
            GameDialog.Show(owner, "실을 배가 없지 않은가.");
            return;
        }
        new SupplyDialog(player, rate) { Owner = owner }.ShowDialog();
    }
}
