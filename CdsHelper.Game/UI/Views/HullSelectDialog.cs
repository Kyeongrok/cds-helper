using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Support.Local.Models;

using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 조선소 → 구입 에서 뜨는 "선체종류 선택" 창. 한 줄을 고르고 결정을 누르면 그 배를 산다 —
/// 값은 <see cref="Player"/> 의 소지금에서 빠지고 배는 그 사람의 함대에 붙는다.
/// </summary>
/// <remarks>
/// 살 수 없는 줄은 흐려 두고 고를 수도 없게 한다. 소지금이 모자라거나 배가 이미
/// <see cref="Player.MaxShips"/> 척이면 그렇다 — 까닭은 창 아래에 적는다.
/// </remarks>
public sealed class HullSelectDialog : Window
{
    /// <summary>
    /// 표 머리글. <b>값 칸은 없다</b> — 게임 표도 대포수에서 끝난다.
    /// </summary>
    /// <remarks>값은 "결정" 을 누른 뒤 "…은(는) %d닢일세. 사겠나?" 로 묻는다.</remarks>
    private static readonly string[] Headers =
        ["선체명", "내구력", "추진력", "적재용량", "적재중량", "필요승인", "대포수"];

    /// <summary>칸 폭. 이름만 넓고 숫자는 글자에 맞춰 좁다.</summary>
    private static readonly double[] Widths = [108, 58, 58, 64, 68, 64, 58];

    /// <summary>
    /// 머리글 띠와 줄 띠의 색. 게임 갈무리에서 그대로 뽑았다 — 머리글이 한 톤 짙다.
    /// </summary>
    private static readonly Brush HeadFill = Frozen(Color.FromRgb(0xDE, 0xC6, 0xAD));
    private static readonly Brush RowFill = Frozen(Color.FromRgb(0xFF, 0xEF, 0xD6));

    /// <summary>고른 줄의 띠. 머리글보다 한 톤 더 짙어 눈에 든다.</summary>
    private static readonly Brush PickFill = Frozen(Color.FromRgb(0xC4, 0xA8, 0x8C));

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    /// <summary>표가 이보다 길어지면 굴린다.</summary>
    private const double TableMaxHeight = 420;

    private readonly Player _player;
    private readonly GameButton _decide;
    private readonly Dictionary<Hull, Border> _rows = [];

    private Hull? _picked;

    /// <summary>이 창에서 산 배. 안 샀으면 null.</summary>
    public Hull? Bought { get; private set; }

    private HullSelectDialog(Player player)
    {
        _player = player;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        _decide = new GameButton("결정", Decide);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 8),
        };
        buttons.Children.Add(_decide);
        buttons.Children.Add(new GameButton("중단", Close));

        var title = GameUi.TitleBar("선체종류 선택", Close);
        GameUi.EnableDrag(this, title);   // 제목 줄을 잡아 옮긴다

        var stack = new StackPanel();
        stack.Children.Add(title);
        // 표는 밑판 없이 띠만 늘어놓는다 — 게임도 머리글과 줄이 곧 띠고, 그 둘레는
        // 창 바탕(짙은 밤색)이 그대로 보인다.
        stack.Children.Add(new Border
        {
            Margin = new Thickness(8, 4, 8, 0),
            // 배를 등록해 넣으면 줄이 얼마든 늘 수 있다 — 화면 밖으로 자라지 않게 굴린다.
            Child = new ScrollViewer
            {
                Content = BuildTable(),
                MaxHeight = TableMaxHeight,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            },
        });
        stack.Children.Add(buttons);

        Content = GameUi.DialogEdge(stack);

        SetDecide(enabled: false);   // 줄을 고르기 전에는 흐리다
        KeyDown += (_, e) => { if (e.Key is Key.Escape) Close(); };
        MouseRightButtonUp += (_, _) => Close();
    }

    private StackPanel BuildTable()
    {
        var table = new StackPanel();
        table.Children.Add(Row(Headers, header: true));

        foreach (var hull in Hull.All)
        {
            string[] cells =
            [
                hull.Name, $"{hull.Hp}", $"{hull.Speed}", $"{hull.Capacity}",
                $"{hull.Tonnage}", $"{hull.Crew}", $"{hull.Guns}",
            ];
            bool canBuy = _player.CanBuy(hull) == PurchaseResult.Ok;
            var row = Row(cells, header: false, dim: !canBuy);
            if (canBuy)
            {
                row.Cursor = Cursors.Hand;
                row.MouseLeftButtonUp += (_, e) => { e.Handled = true; Pick(hull); };
            }
            _rows[hull] = row;
            table.Children.Add(row);
        }
        return table;
    }

    /// <summary>표 한 줄. 이름은 가운데, 숫자는 오른쪽으로 붙인다 — 게임 표와 같은 모양이다.</summary>
    private static Border Row(string[] cells, bool header, bool dim = false)
    {
        var grid = new Grid();
        for (int c = 0; c < Widths.Length; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Widths[c]) });

        for (int c = 0; c < cells.Length; c++)
        {
            // 줄 글씨도 <b>게임 글꼴</b>이다. 살 수 없는 배는 흐리게 내는데, 글꼴은 색이
            // 색표 색인이라 회색 자리가 없다 — 칸 자체의 투명도로 흐리게 한다.
            var tb = new GameUi.GameLabel(GameFont.BlackColor)
            {
                Text = cells[c],
                Bold = true,
                FallbackBrush = dim ? Brushes.Gray : Brushes.Black,
                Opacity = dim ? 0.45 : 1.0,
                Margin = new Thickness(3, 1, 3, 1),
                HorizontalAlignment = header || c == 0
                    ? HorizontalAlignment.Center
                    : HorizontalAlignment.Right,
            };
            Grid.SetColumn(tb, c);
            grid.Children.Add(tb);
        }

        return new Border { Background = header ? HeadFill : RowFill, Child = grid };
    }

    /// <summary>줄을 고른다. 고른 줄만 짙게 뒤집어 두고 결정을 살린다.</summary>
    private void Pick(Hull hull)
    {
        _picked = hull;
        foreach (var (h, row) in _rows)
        {
            bool on = h == hull;
            row.Background = on ? PickFill : RowFill;
        }
        SetDecide(enabled: true);
    }

    /// <summary>결정 단추를 살리거나 흐린다. 줄을 고르기 전에는 눌러도 아무 일이 없다.</summary>
    private void SetDecide(bool enabled) => _decide.On = enabled;

    /// <summary>
    /// 결정 — 살 건지 묻고, 사겠다면 이름을 짓게 한 뒤에 산다.
    /// </summary>
    /// <remarks>
    /// 차례가 셋이다.
    /// <list type="number">
    ///   <item>못 사는 까닭이 있으면 여기서 끝낸다 — 물어 놓고 "소지금이 모자랍니다" 를 내면 헛걸음이다.</item>
    ///   <item>"사겠나?" 를 묻는다. 아니오면 아무 일도 없다.</item>
    ///   <item>「선명입력」(<see cref="ShipNameDialog"/>)을 낸다. <b>여기엔 중단이 없다</b> —
    ///         살지 말지는 앞에서 이미 정했으므로, 이름을 지어야 넘어간다.</item>
    /// </list>
    /// 돈은 이름까지 정해진 뒤에 뺀다.
    /// </remarks>
    private void Decide()
    {
        if (_picked is not { } hull) return;

        switch (_player.CanBuy(hull))
        {
            case PurchaseResult.NotEnoughGold:
                NoticeDialog.Show(this, "소지금이 모자랍니다");
                return;
            case PurchaseResult.FleetFull:
                NoticeDialog.Show(this, $"배는 {Player.MaxShips}척까지만 가질 수 있습니다");
                return;
        }

        if (!ConfirmDialog.Ask(this, $"「{hull.Name}」은(는) {hull.Price}닢일세. 사겠나?")) return;

        string name = ShipNameDialog.Ask(this, _player.SuggestShipName(), mustName: true)!;

        if (_player.Buy(hull, name) != PurchaseResult.Ok) return;

        Bought = hull;
        NoticeDialog.Show(this, $"「{name}」을(를) 샀습니다 · {hull.Name} · {hull.Price}닢");
        Close();
    }

    /// <summary>선체 표를 띄운다. 배를 샀으면 그 선체를 낸다.</summary>
    public static Hull? Show(Window owner, Player player)
    {
        var dlg = new HullSelectDialog(player) { Owner = owner };
        dlg.ShowDialog();
        return dlg.Bought;
    }
}
