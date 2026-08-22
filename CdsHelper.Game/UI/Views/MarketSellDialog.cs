using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
    /// <summary>줄 속 칸 — 이름 · (갈래) · 쳐 주는 값. 구입 창과 같은 배치다.</summary>
    private static readonly GameListColumn[] Columns =
    [
        new(GameListDock.Right, new Thickness(12, 0, 10, 0)),   // 값
        new(GameListDock.Right, new Thickness(12, 0, 0, 0)),    // (갈래)
        new(GameListDock.Fill, new Thickness(10, 0, 0, 0)),     // 이름
    ];

    /// <summary>소지품이 늘면 창이 화면을 넘지 않게 여기서 자른다.</summary>
    private const double ListMaxHeight = 420;

    private readonly Player _player;
    private readonly Market _market;
    private readonly int _cityId;

    private readonly List<ItemTable.Record> _held = [];
    private readonly GameList _list;
    private readonly GameUi.BandButton _decide;

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

        foreach (int id in player.Items)
            if (items.Find(id) is { } item) _held.Add(item);

        _list = new GameList(Columns, Cells, _held.Count, maxHeight: ListMaxHeight);

        _decide = new GameUi.BandButton("결정", Decide, 110) { On = false };
        _list.SelectionChanged += () => _decide.On = _list.Selected >= 0;

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
        stack.Children.Add(_list);
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

    /// <summary>줄 하나의 칸 글자 — 값 · (갈래) · 이름. <see cref="Columns"/> 와 차례가 같다.</summary>
    private IReadOnlyList<string> Cells(int index)
    {
        var item = _held[index];
        return [$"{_market.PaidFor(item, _cityId)}", $"({item.CategoryName})", item.Name];
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (_list.HandleKey(e.Key)) { e.Handled = true; return; }

        switch (e.Key)
        {
            case Key.Escape:
                Close();
                break;
            case Key.Enter or Key.Space when _list.Selected >= 0:
                Decide();
                e.Handled = true;
                break;
        }
    }

    /// <summary>값을 부르고, YES 면 판다.</summary>
    private void Decide()
    {
        int at = _list.Selected;
        if (at < 0 || at >= _held.Count) return;
        var item = _held[at];

        int paid = _market.PaidFor(item, _cityId);
        if (!ConfirmDialog.Ask(this, $"으~음. 금화 {paid}닢이란 말이군.")) return;

        if (_market.Sell(_player, item, _cityId) != SellResult.Ok) return;

        // 판 줄을 목록에서 걷는다. 같은 것을 여럿 지녔으면 한 개만 빠진다.
        _held.RemoveAt(at);
        _list.Rebuild(_held.Count);

        // 다 팔았으면 더 볼 것이 없다.
        if (_held.Count == 0) Close();
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
