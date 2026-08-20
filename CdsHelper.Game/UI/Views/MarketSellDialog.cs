using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Game.Engine.Market;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 시장 매각 창 — 지닌 것을 늘어놓고 하나를 골라 판다.
/// </summary>
/// <remarks>
/// 게임 차례를 그대로 따른다(매각 본체 <c>0x004B3C76</c>).
/// <code>
///   0  지닌 것이 없으면   "응? 도대체 무엇을 팔겠다는 건가?"  하고 끝난다
///   1  들어서면           "팔고 싶은 물건이 있으면 어디 보여주게!"
///   2  소지품 일람        줄을 고르면 "결정" 이 살아난다
///   3  값을 부른다        "으~음. 금화 %ld닢이란 말이군."   YES/NO
///   4  YES 면 팔린다
/// </code>
/// 사는 쪽과 달리 <b>값을 부르는 창이 곧 물음창</b>이다 — 알림창 하나를 더 띄우지 않고
/// 그 자리에서 YES/NO 를 받는다(<c>push 2</c> 로 단추 두 개를 달아 부른다).
///
/// 무엇이든 받아 준다. 그 도시가 파는 물건인지는 안 따진다.
/// </remarks>
public sealed class MarketSellDialog : Window
{
    /// <summary>고른 줄에 씌우는 남색. 구입 창과 같은 색이다.</summary>
    private static readonly Brush Picked = Freeze(Color.FromRgb(0x3A, 0x5A, 0x9A));

    private static SolidColorBrush Freeze(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private readonly Player _player;
    private readonly Market _market;
    private readonly int _cityId;

    private readonly List<(ItemTable.Record Item, Border Row)> _rows = [];
    private readonly GameUi.BandButton _decide;
    private int _at = -1;

    private MarketSellDialog(Player player, Market market, ItemTable items, int cityId)
    {
        _player = player;
        _market = market;
        _cityId = cityId;

        Title = "소지품 일람";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        var list = new StackPanel();
        foreach (int id in player.Items)
        {
            if (items.Find(id) is not { } item) continue;
            var row = Row(item);
            _rows.Add((item, row));
            list.Children.Add(row);
        }

        // 게임은 줄을 어두운 창 바탕이 아니라 밝은 칸 위에 얹는다.
        var page = new Border
        {
            Background = GameUi.PageFill,
            BorderBrush = GameUi.ItemEdge,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(6, 4, 6, 4),
            Child = new ScrollViewer
            {
                // 소지품이 늘면 창이 화면을 넘지 않게 여기서 자른다.
                MaxHeight = 420,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = list,
            },
        };

        _decide = new GameUi.BandButton("결정", Decide, 110) { On = false };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 12),
        };
        buttons.Children.Add(_decide);
        buttons.Children.Add(new GameUi.BandButton("중단", Close, 110));

        var title = GameUi.TitleBar("소지품 일람", Close);
        GameUi.EnableDrag(this, title);

        var stack = new StackPanel { MinWidth = 420 };
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

        KeyDown += OnKey;
    }

    /// <summary>줄 하나 — 이름, (갈래), 쳐 주는 값.</summary>
    private Border Row(ItemTable.Record item)
    {
        var row = new Border
        {
            Background = Brushes.Transparent,
            Padding = new Thickness(2, 3, 2, 3),
            Cursor = Cursors.Hand,
            Child = Line(item, picked: false),
        };
        row.MouseLeftButtonDown += (_, e) => e.Handled = true;
        row.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            Pick(_rows.FindIndex(r => ReferenceEquals(r.Row, row)));
        };
        return row;
    }

    /// <summary>줄 속 — 이름, (갈래), 값.</summary>
    private FrameworkElement Line(ItemTable.Record item, bool picked)
    {
        var line = new DockPanel { LastChildFill = true };

        var money = Label($"{_market.PaidFor(item, _cityId)}", new Thickness(12, 0, 10, 0), picked);
        DockPanel.SetDock(money, Dock.Right);
        line.Children.Add(money);

        var kind = Label($"({item.CategoryName})", new Thickness(12, 0, 0, 0), picked);
        DockPanel.SetDock(kind, Dock.Right);
        line.Children.Add(kind);

        line.Children.Add(Label(item.Name, new Thickness(10, 0, 0, 0), picked));
        return line;
    }

    /// <summary>
    /// 줄에 얹는 글씨. 게임 비트맵 글꼴로 찍는다.
    /// </summary>
    /// <remarks>
    /// 줄 칸은 양피지라 글씨가 검다. 고른 줄만 남색이 씌워지므로 그때는 흰빛으로 뒤집는다 —
    /// 색이 지을 때 정해지므로 고를 때마다 그 줄을 새로 짓는다(한 번에 바뀌는 줄은 둘뿐이다).
    /// </remarks>
    private static FrameworkElement Label(string text, Thickness margin, bool picked)
    {
        var label = new GameUi.GameLabel(picked ? GameFont.WhiteColor : GameFont.ButtonColor)
        {
            Margin = margin,
            Text = text,
        };
        // 글꼴을 못 읽으면 GameLabel 이 윈도 글꼴로 물러선다. 그때 색을 맞춰 준다.
        label.FallbackBrush = picked ? Brushes.White : Brushes.Black;
        return label;
    }

    private void Pick(int index)
    {
        if (index < 0 || index >= _rows.Count) return;
        _at = index;
        for (int i = 0; i < _rows.Count; i++)
        {
            bool on = i == index;
            _rows[i].Row.Background = on ? Picked : Brushes.Transparent;
            _rows[i].Row.Child = Line(_rows[i].Item, on);
        }
        _decide.On = true;
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Close();
                break;
            case Key.Up:
                Pick(_at <= 0 ? _rows.Count - 1 : _at - 1);
                e.Handled = true;
                break;
            case Key.Down:
                Pick(_at < 0 || _at >= _rows.Count - 1 ? 0 : _at + 1);
                e.Handled = true;
                break;
            case Key.Enter or Key.Space when _at >= 0:
                Decide();
                e.Handled = true;
                break;
        }
    }

    /// <summary>값을 부르고, YES 면 판다.</summary>
    private void Decide()
    {
        if (_at < 0 || _at >= _rows.Count) return;
        var item = _rows[_at].Item;

        int paid = _market.PaidFor(item, _cityId);
        if (!ConfirmDialog.Ask(this, $"으~음. 금화 {paid}닢이란 말이군.")) return;

        if (_market.Sell(_player, item, _cityId) != SellResult.Ok) return;

        // 판 줄을 목록에서 걷는다. 같은 것을 여럿 지녔으면 한 개만 빠진다.
        var (_, row) = _rows[_at];
        ((StackPanel)row.Parent).Children.Remove(row);
        _rows.RemoveAt(_at);
        _at = -1;
        _decide.On = false;

        // 다 팔았으면 더 볼 것이 없다.
        if (_rows.Count == 0) Close();
    }

    /// <summary>
    /// 매각 창을 연다. 지닌 것이 없으면 창 대신 게임 그대로의 한 마디만 낸다.
    /// </summary>
    public static void Show(Window owner, Player player, Market market, ItemTable items, int cityId)
    {
        if (player.Items.Count == 0)
        {
            GameDialog.Show(owner, "응? 도대체 무엇을 팔겠다는 건가?");
            return;
        }

        GameDialog.Show(owner, "팔고 싶은 물건이 있으면 어디 보여주게!");
        new MarketSellDialog(player, market, items, cityId) { Owner = owner }.ShowDialog();
    }
}
