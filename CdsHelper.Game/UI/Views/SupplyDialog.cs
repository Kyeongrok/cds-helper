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
///   탑재품명   단중량/단가   현재량   보충량       가격
///   식량            5/  19   1151통  +  276 ↑↓   5244닢
///   물             10/  12   1151통  +  276 ↑↓   3312닢
///   자재            5/  31      0통  +    0 ↑↓      0닢
///   탄약           20/  31     17통  +    0 ↑↓      0닢
///
///                                        총계    8556닢
///   [최대] [10일분] [일지정] [전회분]     [결정] [돌아간다]
/// </code>
///
/// <b>이 화면은 밤색 판 하나다.</b> 목록 창들과 달리 양피지 칸이 없다 — <c>#311818</c> 바탕에
/// 밝은 글자와 단추가 바로 얹힌다. 테는 검은 선 <b>셋</b>이다. 게임 화면 왼쪽 테를 한 점씩
/// 찍으면 <c>(17,9,9) (46,22,22) (49,24,24) (46,22,22) (17,9,9) (11,5,5)</c> 처럼 짙은 선과
/// 바탕이 번갈아 나오는데, 남회색 정보 창과 짜임이 같다. 그래서
/// <see cref="GameUi.InfoFrame(UIElement, Brush, Brush)"/> 를 색만 바꿔 쓴다.
/// <b>제목 줄도 없다.</b>
///
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
    /// <summary>화면 바탕. 게임 화면에서 뽑았다.</summary>
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

    /// <summary>글이 놓이는 판의 크기. 품목과 총계 사이가 게임처럼 비도록 키를 못 박는다.</summary>
    private const double BoardWidth = 620, BoardHeight = 300;

    /// <summary>칸 폭 — 단중량/단가 · 현재량 · 보충량 · 가격.</summary>
    private const double UnitWidth = 160, HaveWidth = 100, AddWidth = 120, CostWidth = 110;

    /// <summary>↑↓ 한 번에 움직이는 통 수. Shift 를 누르면 열 배로 뛴다.</summary>
    private const int Step = 1, FastStep = 10;

    private readonly Player _player;
    private readonly int _rate;

    /// <summary>줄마다 지금 더 실으려는 통 수.</summary>
    private readonly int[] _add = new int[Supply.Count];

    private readonly GameUi.GameLabel[] _addLabels = new GameUi.GameLabel[Supply.Count];
    private readonly GameUi.GameLabel[] _costLabels = new GameUi.GameLabel[Supply.Count];
    private readonly GameUi.GameLabel _capacity = Label("");
    private readonly GameUi.GameLabel _weight = Label("");
    private readonly GameUi.GameLabel _gold = Label("");
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
        Background = Back;

        // 맨 윗줄 — 용량 · 중량 · 소지금.
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(Label("용량 "));
        head.Children.Add(_capacity);
        head.Children.Add(Label("    중량 "));
        head.Children.Add(_weight);
        head.Children.Add(Label("    소지금 "));
        head.Children.Add(_gold);

        var rows = new StackPanel();
        rows.Children.Add(head);
        rows.Children.Add(HeaderRow());
        for (int i = 0; i < Supply.Count; i++) rows.Children.Add(ItemRow(i));

        // 총계는 판 아래쪽에 붙는다 — 게임은 품목과 총계 사이를 통째로 비워 둔다.
        var totalRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        totalRow.Children.Add(Label("총계    "));
        totalRow.Children.Add(_total);

        var board = new DockPanel
        {
            Width = BoardWidth,
            Height = BoardHeight,
            Margin = new Thickness(14, 10, 14, 2),
            LastChildFill = false,
        };
        DockPanel.SetDock(rows, Dock.Top);
        board.Children.Add(rows);
        DockPanel.SetDock(totalRow, Dock.Bottom);
        board.Children.Add(totalRow);

        _decide = new GameButton("결정", Decide) { On = false };

        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(new GameButton("최대", Fill));
        left.Children.Add(new GameButton("10일분", TenDays));
        left.Children.Add(new GameButton("일지정"));
        left.Children.Add(new GameButton("전회분"));

        var right = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        right.Children.Add(_decide);
        right.Children.Add(new GameButton("돌아간다", Close));

        var buttons = new DockPanel { Margin = new Thickness(10, 0, 10, 10) };
        DockPanel.SetDock(left, Dock.Left);
        buttons.Children.Add(left);
        buttons.Children.Add(right);

        var page = new StackPanel();
        page.Children.Add(board);
        page.Children.Add(buttons);

        // 제목 줄이 없으므로(게임에도 없다) 판 아무 데나 잡아 옮긴다.
        var frame = GameUi.InfoFrame(page, Back, Line);
        GameUi.EnableDrag(this, frame);
        Content = frame;

        KeyDown += (_, e) => { if (e.Key is Key.Escape) Close(); };
        Paint();
    }

    // ── 줄 짓기 ──────────────────────────────────────────────────────────────

    private static UIElement HeaderRow() =>
        Row(Label("탑재품명"),
            Cell(Label("단중량/단가"), UnitWidth),
            Cell(Label("현재량"), HaveWidth),
            Cell(Label("보충량"), AddWidth),
            Cell(Label("가격"), CostWidth));

    private UIElement ItemRow(int index)
    {
        var supply = Supply.All[index];

        _addLabels[index] = Label("");
        _costLabels[index] = Label("");

        // 보충량 칸은 "+ 000 ↑↓" 한 벌이다.
        var spin = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        spin.Children.Add(Label("+"));
        spin.Children.Add(_addLabels[index]);
        spin.Children.Add(Arrow("↑", () => Bump(index, +1)));
        spin.Children.Add(Arrow("↓", () => Bump(index, -1)));

        return Row(Label(supply.Name),
                   Cell(Label($"{supply.UnitWeight,3}/{supply.PriceAt(_rate),4}"), UnitWidth),
                   Cell(Label($"{_player.SupplyOf(supply.Kind),5}통"), HaveWidth),
                   Cell(spin, AddWidth),
                   Cell(_costLabels[index], CostWidth));
    }

    /// <summary>
    /// 줄 하나. 첫 칸(품목 이름)은 왼쪽에 붙고 나머지는 <b>못 박은 폭</b>으로 오른쪽에 선다 —
    /// 그래야 줄마다 숫자가 세로로 맞는다.
    /// </summary>
    /// <remarks>
    /// 오른쪽 붙이기는 <b>먼저 넣은 것이 더 바깥</b>이므로 칸을 거꾸로 넣는다. 그래야 눈에
    /// 보이는 차례가 준 차례(단중량/단가 · 현재량 · 보충량 · 가격)와 같아진다.
    /// </remarks>
    private static UIElement Row(UIElement name, params UIElement[] cells)
    {
        var line = new DockPanel { Margin = new Thickness(0, 1, 0, 1), LastChildFill = false };
        DockPanel.SetDock(name, Dock.Left);
        line.Children.Add(name);
        for (int i = cells.Length - 1; i >= 0; i--)
        {
            DockPanel.SetDock(cells[i], Dock.Right);
            line.Children.Add(cells[i]);
        }
        return line;
    }

    /// <summary>오른쪽으로 밀어 붙인 한 칸.</summary>
    private static FrameworkElement Cell(UIElement inner, double width) => new Border
    {
        Width = width,
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
            Width = 15,
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
        // 누름은 삼킨다 — 판 끌기가 먼저 걸리면 마우스를 잡아 버려 뗌이 안 온다.
        box.MouseLeftButtonDown += (_, e) => e.Handled = true;
        box.MouseLeftButtonUp += (_, e) => { e.Handled = true; run(); };
        return box;
    }

    /// <summary>밤색 판 위에 얹는 밝은 글씨.</summary>
    private static GameUi.GameLabel Label(string text) =>
        new(GameFont.WhiteColor) { Text = text, FallbackBrush = Ink };

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
            _addLabels[i].Text = $"{_add[i],5}";
            _costLabels[i].Text = $"{Cost(i)}닢";
        }
        _capacity.Text = $"{Barrels}/{_player.Capacity}";
        _weight.Text = $"{Weight}/{_player.Tonnage}";
        _gold.Text = $"{_player.Gold}닢";
        _total.Text = $"{Total}닢";
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
