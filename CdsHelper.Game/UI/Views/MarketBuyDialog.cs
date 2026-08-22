using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CdsHelper.Game.Engine.Market;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 시장 구입 창 — 파는 것을 늘어놓고 하나를 고르게 한다.
/// </summary>
/// <remarks>
/// 게임 차례를 그대로 따른다.
/// <code>
///   1  구입 아이템 선택   줄을 고르면 "결정" 이 살아난다
///   2  아이템 창          그림·설명·효과            (ItemInfoDialog)
///   3  값 알림            "그렇다면 금화 %d닢 필요하네."
///   4  물음              "이 아이템을 구입하겠습니까?"  YES/NO
///   5  결과              돈이 되면 "고맙네!", 모자라면 "가난한 사람에게는 볼일 없네!"
/// </code>
/// 문구는 EXE 에서 그대로 옮겼다(<c>0x00544730</c> 벌). 값이 여럿일 때 "이것들의" 로 갈리는
/// 것까지 있지만 지금은 한 번에 하나만 고를 수 있어 "이" 쪽만 쓴다.
///
/// <b>돈 검사는 YES 를 고른 뒤다.</b> 목록에서도 값 알림에서도 막지 않는다 — 게임이
/// 그렇게 한다(구입 본체 <c>0x004B3AAD</c> 에서 소지금과 값을 견준다). 살 돈이 없는 줄도
/// 고를 수 있고 값도 알려 준다.
/// </remarks>
public sealed class MarketBuyDialog : Window
{
    /// <summary>줄 속 칸 — 이름 · (갈래) · 값. 오른쪽 것을 먼저 줘야 바깥에 선다.</summary>
    private static readonly GameListColumn[] Columns =
    [
        new(GameListDock.Right, new Thickness(12, 0, 10, 0)),   // 값
        new(GameListDock.Right, new Thickness(12, 0, 0, 0)),    // (갈래)
        new(GameListDock.Fill, new Thickness(10, 0, 0, 0)),     // 이름
    ];

    private readonly Player _player;
    private readonly Market _market;
    private readonly ItemDescriptions? _descriptions;
    private readonly ItemArt? _art;
    private readonly int _cityId;

    private readonly ItemTable.Record[] _stock;
    private readonly GameList _list;
    private readonly GameButton _decide;

    private MarketBuyDialog(Player player, Market market, int cityId,
                            ItemDescriptions? descriptions, ItemArt? art)
    {
        _player = player;
        _market = market;
        _cityId = cityId;
        _descriptions = descriptions;
        _art = art;

        Title = "구입 아이템 선택";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        _stock = [.. market.StockOf(cityId)];
        _list = new GameList(Columns, Cells, _stock.Length, "  지금 내놓은 물건이 없다.  ");

        _decide = new GameButton("결정", Decide, width: 110) { On = false };
        // 게임도 아무것도 안 고른 동안은 이 단추가 흐리다.
        _list.SelectionChanged += () => _decide.On = _list.Selected >= 0;

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 12),
        };
        buttons.Children.Add(_decide);
        buttons.Children.Add(new GameButton("중단", Close, width: 110));

        var title = GameUi.TitleBar("구입 아이템 선택", Close);
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
        var item = _stock[index];
        return [$"{_market.PriceOf(item, _cityId)}", $"({item.CategoryName})", item.Name];
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

    /// <summary>고른 것을 사는 데까지 끌고 간다 — 아이템 창 → 값 → 물음 → 결과.</summary>
    private void Decide()
    {
        int at = _list.Selected;
        if (at < 0 || at >= _stock.Length) return;
        var item = _stock[at];

        // 2. 무엇인지 보여 준다.
        ItemInfoDialog.Show(this, item, _descriptions?.Of(item.Id) ?? "", _art);

        // 3. 값을 알린다.
        int price = _market.PriceOf(item, _cityId);
        GameDialog.Show(this, $"그렇다면 금화 {price}닢 필요하네.");

        // 4. 물어본다. 게임은 여러 개일 때만 "이것들의" 로 갈린다 — 여기서는 늘 하나다.
        if (!ConfirmDialog.Ask(this, "이 아이템을 구입하겠습니까?")) return;

        // 5. 이제서야 돈을 본다.
        var result = _market.Buy(_player, item, _cityId);
        GameDialog.Show(this, result switch
        {
            BuyResult.Ok => "고맙네!",
            BuyResult.NotEnoughGold => "가난한 사람에게는 볼일 없네! 안 살 거면 돌아가게!",
            _ => "미안하네, 지금 물건이 떨어지고 없네.",
        });

        if (result == BuyResult.Ok) Close();
    }

    /// <summary>시장 구입 창을 연다.</summary>
    public static void Show(Window owner, Player player, Market market, int cityId,
                            ItemDescriptions? descriptions, ItemArt? art) =>
        new MarketBuyDialog(player, market, cityId, descriptions, art) { Owner = owner }.ShowDialog();
}
