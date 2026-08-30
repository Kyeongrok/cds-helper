using System.Windows;
using CdsHelper.Game.Engine.Menu;
using CdsHelper.Game.Engine.Models;
using CdsHelper.Game.Engine.Sea;
using CdsHelper.Game.Engine.Town;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 조선소 — 매각 · 수리 · 개조(마스트 · 돛 · 포탑 · 대포 · 선명).
/// </summary>
/// <remarks>
/// 값과 조건은 <see cref="Shipyard"/> 가 알고, 여기서는 <b>묻고 알리는 차례</b>만 맡는다.
/// 게임도 조선소 화면 하나가 이 셋을 다 거느린다(<c>0x0044B820</c> 매각 ·
/// <c>0x0044B9C0</c> 수리 · <c>0x00496960</c> 개조).
///
/// 도시 그림 창이 들고 있던 것을 그대로 옮겼다 — 배를 손보는 일은 도시가 아니라
/// 조선소가 한다.
/// </remarks>
/// <param name="view">이 조선소를 낸 도시 창. 물음창의 주인이다.</param>
/// <param name="game">이 판 — 주인공과 주사위가 여기서 온다.</param>
/// <param name="menu">조선소 명령 창. 줄의 흐림을 다시 잡을 때 쓴다.</param>
/// <param name="cityId">이 마을 번호. 맡겨 둔 배를 찾을 때 쓴다.</param>
/// <param name="rate">이 마을 시세(%). 매각·수리 값에 먹인다.</param>
internal sealed class ShipyardMenu(Window view, Engine.Game game, GameMenuHost menu,
                                   int cityId, int culture, int rate)
{
    /// <summary>조선소의 건물 코드. 화자표에서 목수를 찾을 때 쓴다.</summary>
    private const int BuildingCode = 6;

    private readonly Window _view = view;
    private readonly Engine.Game _game = game;
    private readonly GameMenuHost _menu = menu;
    private readonly int _cityId = cityId;
    private readonly int _culture = culture;
    private readonly int _rate = rate;

    private Player _player => _game.Player;
    private Random _random => _game.Random;

    /// <summary>물음창을 얹을 창 — 명령 창이 떠 있으면 그 위다.</summary>
    private Window Owner => _menu.Window ?? _view;

    /// <summary>
    /// 들어설 때 목수가 건네는 한마디. 게임의 <c>0x0044B4A0</c> 자리다 — 문구가
    /// <c>0x00530F38</c> 이고, 얼굴은 이 마을 문화권이 정한다(리스본은 402, 이슬람권은
    /// 315 다).
    /// </summary>
    public void Greet() =>
        ConfirmDialog.Tell(_view, "형씨, 바다에 나갈 거면 좋은 배를 사요.",
                           face: _game.SpeakerFace(BuildingCode, _culture));

    /// <summary>고칠 배가 있는지. 없으면 게임처럼 "수리" 줄이 흐리다.</summary>
    public bool CanRepair => RepairTargets().Count > 0;

    /// <summary>
    /// 조선소에 배를 판다. 게임의 <c>0x0044B820</c> 자리다.
    /// </summary>
    /// <remarks>
    /// 값은 산 값의 <b>6할</b>에 도시 시세를 먹인 것이다(<see cref="Hull.SellPrice"/> ·
    /// <c>0x00423A30</c> → <c>0x00429DC0</c>). 배가 한 척뿐이면 줄 자체가 흐리고
    /// (<c>0x0044B863</c> 의 <c>cmp esi,1 / jle</c>), <b>기함은 못 판다</b>
    /// (<c>0x00531188</c> "기함을 처분하는 일은 불가능합니다!").
    ///
    /// 차례는 이렇다.
    /// <code>
    ///   0044B863  배가 한 척뿐이면 "기함을 처분하는 일은 불가능합니다!"
    ///             (없으면 "이 이상 배를 처분하는 일은 불가능합니다.")
    ///   0044B889  [배+0x64] 가 선 배는 값이 0 이다 — 기함이라 못 판다
    ///   0044B8B9  "어느 배를 팔 건가? 봐 주겠네."
    ///   0044B8DA  0x00423750 — 「매각선박의 선택」. 고른 것을 비트마스크로 낸다
    ///   0044B91C  "%ld닢입니다. 좋습니까?"  · YES 면 고른 배를 다 처분하고 값을 받는다
    /// </code>
    /// <b>여러 척을 한꺼번에 판다.</b> 그래서 목록 아래에 "견적합계" 가 붙는다.
    /// </remarks>
    public void SellShip()
    {
        var owner = Owner;

        // 배가 기함뿐이면 그 자리에서 물린다 — 목록도 안 뜬다.
        if (_player.Ships.Count <= 1)
        {
            ConfirmDialog.Tell(owner, _player.Ships.Count == 1
                ? "기함을 처분하는 일은 불가능합니다!"
                : "이 이상 배를 처분하는 일은 불가능합니다.");
            return;
        }

        ConfirmDialog.Tell(owner, "어느 배를 팔 건가? 봐 주겠네.",
                           face: _game.SpeakerFace(BuildingCode, _culture));

        // 기함은 값이 0 이라 줄이 흐리고 안 골라진다 — 게임도 그렇게 낸다.
        var rows = _player.Ships.Select((s, i) => new ShipSellDialog.Row(
            i, s.Name, s.Hull.Name,
            s.Figurehead >= 0 ? NameOf(s.Figurehead) : "---",
            i == _player.Flagship ? 0 : Shipyard.SellPrice(s, _rate))).ToList();

        var picked = ShipSellDialog.Ask(owner, rows);
        if (picked.Count == 0) return;

        int paid = picked.Sum(at => Shipyard.SellPrice(_player.Ships[at], _rate));
        if (!ConfirmDialog.Ask(owner, $"{paid}닢입니다. 좋습니까?")) return;

        // 뒤에서부터 뺀다 — 앞을 먼저 빼면 뒤 자리가 하나씩 밀린다.
        foreach (int at in picked.OrderByDescending(i => i)) _player.Scrap(at);
        _player.Earn(paid);

        // 한 척만 남았으면 "매각" 줄이 그 자리에서 꺼져야 한다.
        _menu.Refresh();
    }

    /// <summary>
    /// 배를 고친다. 게임의 <c>0x0044B9C0</c> 자리다.
    /// </summary>
    /// <remarks>
    /// 고칠 배는 <b>함대와 이 마을에 맡겨 둔 배</b>를 다 훑어 모은다(<c>0x0044BC50</c>).
    /// 값은 이렇다.
    /// <code>
    ///   0x0044BBF0  손상 = (최대내구 - 지금내구) + (최대돛 - 지금돛)   ; 음수는 0
    ///   0x0044BAA1  값 = (rand(4) + 26) * 손상                        ; 26~29 곱
    ///   0x0044BABD  값 = 값 x 도시 시세 / 100                          ; 적어도 1
    /// </code>
    /// 우리 선체 표에는 돛 값이 없어 <b>내구만</b> 센다.
    ///
    /// 게임 화면은 여러 척을 한꺼번에 골라 값을 합치는데 여기서는 한 척씩 고친다.
    /// </remarks>
    /// <summary>
    /// 이 마을에서 고칠 수 있는 배 — 함대 먼저, 그 뒤가 이 마을이 맡은 배다.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x0044BC50(도시, 0)</c> 이다. 이 목록이 비면 조선소 차림표의 <b>"수리" 줄이
    /// 꺼진다</b>(<c>0x0044BD40</c> 이 <c>0x0044BC50 &gt; 0</c> 을 본다) — 그래서 평소에는
    /// "수리가 필요한 배는 없네!" 를 볼 일이 없다.
    /// </remarks>
    private List<(Ship Ship, bool Docked)> RepairTargets() =>
        Shipyard.RepairTargets(_player, _cityId);

    public void RepairShip()
    {
        var owner = Owner;
        var hurt = RepairTargets();
        if (hurt.Count == 0)
        {
            GameDialog.Show(owner, "수리가 필요한 배는 없네!");
            return;
        }

        int CostOf(Ship s) => Shipyard.RepairCost(s, _rate, _random);

        int at = HintListDialog.Pick(owner,
            [.. hurt.Select(h => $"{(h.Docked ? "맡김 " : "     ")}{h.Ship.Name}  " +
                                 $"내구 {h.Ship.Hp,3}/{h.Ship.MaxHp,-3}")],
            "수리선박 선택", "수리가 필요한 배는 없네!");
        if (at < 0 || at >= hurt.Count) return;

        var ship = hurt[at].Ship;
        int cost = CostOf(ship);
        if (!ConfirmDialog.Ask(owner, $"수리하는데 금화 {cost}닢 필요하네. 좋나?")) return;

        if (!_player.Pay(cost))
        {
            GameDialog.Show(owner, "소지금이 모자랍니다!");
            return;
        }
        ship.Repair();

        // 마지막 상한 배를 고쳤으면 "수리" 줄이 그 자리에서 꺼져야 한다.
        _menu.Refresh();
    }

    // ── 개조 ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// 조선소 개조 — 배를 고르고, 그 배의 개조 창을 연다.
    /// </summary>
    /// <remarks>
    /// 게임(<c>0x00496960</c>)은 배를 고른 뒤 <b>그 마을에서 손댈 수 있는 배인지</b>부터
    /// 본다 — 도시의 문화권(<c>0x004A1820</c>)이 0~2 나 10 이면 다우선(선체 7)을 못 고치고,
    /// 그 밖의 문화권에서는 <b>다우선만</b> 고친다. 못 고치면
    /// "이 배 형은 내가 어떻게 할 수 없다."(<c>0x00532338</c>) 를 내고 도로 고르게 한다.
    /// 우리 선체 다섯에는 다우선이 없어 그 갈래가 안 생긴다 — 그래서 안 옮겼다.
    ///
    /// 배를 고르면 열한 줄짜리 개조 창이 뜨고(<c>0x004966E0</c>), 한 줄을 마치면 게임은
    /// <b>그 줄만 꺼</b>(<c>0x0049690A</c>) 같은 배를 계속 손보게 둔다. 우리도 그렇게 한다 —
    /// 더 못 늘리는 줄은 저절로 흐려진다.
    /// </remarks>
    public void RefitShip()
    {
        var owner = Owner;
        if (_player.Ships.Count == 0) { GameDialog.Show(owner, "배가 없습니다"); return; }

        int at = HintListDialog.Pick(owner,
            [.. _player.Ships.Select((s, i) => ShipLine(s, i == _player.Flagship))],
            "개조선박의 선택", "배가 없습니다");
        if (at < 0 || at >= _player.Ships.Count) return;

        _menu.Push(() => RefitMenu(_player.Ships[at]));
    }

    /// <summary>배 한 척의 개조 창. 줄은 게임 열한 줄 그대로고, 할 수 없는 줄은 흐리다.</summary>
    private GameMenu RefitMenu(Ship ship) => new(
        [.. Facility.RefitMenu.Select(item => (item, RefitAction(ship, item)))]);

    private Action? RefitAction(Ship ship, string item) => item switch
    {
        Facility.RefitCapacity when ship.CanGrowCapacity => () => DoRefit(ship, item),
        Facility.RefitTonnage when ship.CanGrowTonnage => () => DoRefit(ship, item),
        Facility.RefitReinforce when ship.CanReinforce => () => DoRefit(ship, item),
        Facility.RefitMast when ship.CanAddMast => () => AddMast(ship),
        Facility.RefitSailKind when ship.CanChangeSail && ship.Masts > 0 => () => SwapSail(ship),
        Facility.RefitSail when ship.CanAddSail => () => AddSail(ship),
        Facility.RefitTurrets => () => ChangeTurrets(ship),
        Facility.RefitCannon => () => BuyCannon(ship),
        Facility.RefitFigurehead => () => Carve(ship),
        Facility.RefitRename => () => RenameShip(ship),
        Facility.RefitExit => _menu.Pop,
        _ => null,
    };

    /// <summary>
    /// 조선소가 갖춰 둔 선수상 — 어디나 둘, 문화권마다 한둘이 더 있다.
    /// </summary>
    private IReadOnlyList<int> Stock() => Figureheads.StockFor(_culture);

    /// <summary>지금 지니고 있는 선수상들 — 소지품에서 갈래 6 을 골라낸 것이다.</summary>
    /// <remarks>게임의 <c>0x00495BA0</c> 이 소지품을 훑어 같은 목록을 짓는다.</remarks>
    private List<int> Carried() =>
        [.. _player.Items.Select(Figureheads.FromItem).Where(Figureheads.Known).Distinct()];

    /// <summary>
    /// 선수상 — 조선소에서 사거나, 지니고 있는 것을 뱃머리에 단다.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x00495D10</c> 이 목록을 짓는다 — <b>조선소 재고</b>(<c>0x00429DF0</c>)와
    /// <b>내 소지품</b>(<c>0x00495BA0</c>)을 이어 붙인 것이다. 값은 어디서 온 것이냐에 따라
    /// 다르다.
    /// <code>
    ///   재고     아이템 구입값 x 시세 / 100        "금화 %1d닢이네."            0x00531DC0
    ///   소지품   0x0056E280[등급]                  "선두상을 단 값으로 …"       0x00531DD0
    ///   531bc0   "지금 붙어있는 선수상은 놓아 가고 가는가?"   이미 달았을 때
    ///   531cc0   "…저주받아 풀 수가 없네."                   저주는 못 뗀다
    ///   531d40   "이! 이 선수상은... 정말 이것을 달아도 좋단 말이지?"  저주받은 것을 달 때
    ///   531e08   "돈이 모자라는 것 같군."
    /// </code>
    /// 달고 있던 것은 <b>놓고 간다</b> — 게임은 그 매각값을 도로 얹어 준다
    /// (<c>0x00495CB6</c> 이 더하고 <c>0x00495CD3</c> 이 새 값을 뺀다).
    /// </remarks>
    private void Carve(Ship ship)
    {
        var owner = Owner;

        // 저주받은 것을 달고 있으면 갈아 낼 수가 없다.
        if (Figureheads.Cursed(ship.Figurehead))
        {
            GameDialog.Show(owner,
                $"안됐지만, 자네가 지금 달고 있는 선수상은 저주받아 풀 수가 없네. {NameOf(ship.Figurehead)}");
            return;
        }

        // 재고가 앞, 지닌 것이 뒤다 — 게임도 그 차례로 잇는다.
        var stock = Stock();
        var carried = Carried().Where(i => !stock.Contains(i)).ToList();
        var offer = new List<int>(stock);
        offer.AddRange(carried);
        if (offer.Count == 0) return;

        int at = HintListDialog.Pick(owner,
            [.. offer.Select((i, k) => $"{NameOf(i),-12}{CostOf(i, k < stock.Count),7}닢")],
            "선수상 선택", "달 수 있는 선수상이 없습니다");
        if (at < 0) return;

        int pick = offer[at];
        bool buying = at < stock.Count;
        int cost = CostOf(pick, buying);

        if (ship.Figurehead >= 0
            && !ConfirmDialog.Ask(owner, "지금 붙어있는 선수상은 놓아 가고 가는가?")) return;

        if (Figureheads.Cursed(pick)
            && !ConfirmDialog.Ask(owner, "이! 이 선수상은... 이보게, 정말 이것을 달아도 좋단 말이지?"))
            return;

        GameDialog.Show(owner, buying
            ? $"금화 {cost}닢이네."
            : $"선수상을 단 값으로 금화 {cost}닢 받겠네.");
        if (!_player.CanAfford(cost)) { GameDialog.Show(owner, "돈이 모자라는 것 같군."); return; }

        // 놓고 가는 것은 팔아 준다. 지닌 것을 달았으면 소지품에서 던다.
        int back = ship.Figurehead >= 0 ? SellBack(ship.Figurehead) : 0;
        _player.Pay(cost);
        if (back > 0) _player.Earn(back);
        if (!buying) _player.Drop(Figureheads.ToItem(pick));
        ship.Carve(pick);

        GameDialog.Show(owner, $"{NameOf(pick)}을 달았네. 좋은 항해가 되기를!");
        _menu.Pop();
        _menu.Push(() => RefitMenu(ship));
    }

    /// <summary>
    /// 다는 값. 조선소에서 사면 구입값에 시세를 먹이고, 지닌 것을 달면 등급이 삯을 정한다.
    /// </summary>
    private int CostOf(int index, bool buying) => buying
        ? Math.Max(1, (_game.Items?.Find(Figureheads.ToItem(index))?.BuyList ?? 0) * _rate / 100)
        : Figureheads.PriceOf(index);

    /// <summary>놓고 가는 선수상을 팔아 주는 값 — 매각값에 시세를 먹인다.</summary>
    private int SellBack(int index) =>
        Math.Max(0, (_game.Items?.Find(Figureheads.ToItem(index))?.SellList ?? 0) * _rate / 100);

    /// <summary>선수상 이름 — 아이템 표에서 낸다(213 송골매상 …).</summary>
    private string NameOf(int index) =>
        _game.Items?.Find(Figureheads.ToItem(index))?.Name ?? $"선수상 {index}";

    /// <summary>
    /// 마스트 추가 — 돛대를 하나 더 세운다.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x00494BD0</c> 이다.
    /// <code>
    /// 494a50  코구·다우는 못 늘리고, 카라벨은 둘까지, 그 밖은 셋까지
    /// 494c2c  값 = 선체 구입값 / 5
    /// 494c7b  "적재용량이 조금 주는데 괜찮나?"
    /// 494c9a  카라벨·대형카라벨은 "마스트에는 삼각돛을 달겠네." — 고를 것 없이 삼각돛이다
    /// 494cc5  그 밖은 "마스트에 달 돛의 종류를 정해 주게." → 삼각 · 사각 · 그만둔다
    /// 494b52  적재용량 -= 25 · 필요승원 += 2
    /// </code>
    /// </remarks>
    private void AddMast(Ship ship)
    {
        var owner = Owner;
        int cost = Shipyard.MastCost(ship);

        GameDialog.Show(owner, $"금화 {cost}닢이 드네.");
        if (!_player.CanAfford(cost)) { GameDialog.Show(owner, "돈이 모자라는 것 같군."); return; }
        if (!ConfirmDialog.Ask(owner, "적재용량이 조금 주는데 괜찮나?")) return;

        int sail;
        if (!ship.CanChangeSail)
        {
            if (!ConfirmDialog.Ask(owner, "마스트에는 삼각돛을 달겠네.")) return;
            sail = Ship.Lateen;
        }
        else
        {
            GameDialog.Show(owner, "마스트에 달 돛의 종류를 정해 주게.");
            int at = HintListDialog.Pick(owner, [Ship.SailNames[Ship.Lateen], Ship.SailNames[Ship.Square]],
                                         "돛 종류", "");
            if (at < 0) return;
            sail = at == 0 ? Ship.Lateen : Ship.Square;
            if (!ConfirmDialog.Ask(owner, sail == Ship.Lateen
                    ? "이것은 역풍에 뛰어나네. 이 돛을 달겠네?"
                    : "이것은 순풍에 뛰어나네. 이 돛을 달겠네?")) return;
        }

        var was = ship.Snapshot();
        _player.Pay(cost);

        int mast = ship.AddMast(sail);
        if (mast < 0) return;

        string where = Ship.MastNames[mast], what = Ship.SailNames[sail];
        NoticeDialog.Show(owner, $"{where}에 {what}{GameUi.Josa(what, "을", "를")} 달았습니다");
        ShowRefit(owner, Refit.Between(was, ship.Snapshot()), ship);
    }

    /// <summary>
    /// 돛종류 변경 — 마스트 하나의 돛을 삼각↔사각으로 바꾼다.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x00494F10</c> 이다. 값은 <b>선체 구입값 / 20</b>(<c>0x004950CB</c>).
    /// <code>
    ///   0x005316A8  "어느 마스트의 돛을 바꿀건가?"
    ///   0x005316D8  "삼각돛을 순풍에 뛰어난 사각돛으로 바꿀 건가?"
    ///   0x00531708  "사각돛을 역풍에 뛰어난 삼각돛으로 바꿀 건가?"
    ///   0x00531738  "금화 %ld닢이 드는데, 좋나?"
    ///   0x00531660  "%s%s %s%s 변경했습니다"
    /// </code>
    /// </remarks>
    private void SwapSail(Ship ship)
    {
        var owner = Owner;
        var standing = new List<int>();
        for (int i = 0; i < Ship.MastSlots; i++)
            if (ship.Sails[i] != Ship.NoSail) standing.Add(i);
        if (standing.Count == 0) return;

        GameDialog.Show(owner, "어느 마스트의 돛을 바꿀건가?");
        int pick = HintListDialog.Pick(owner,
            [.. standing.Select(i => $"{GameUi.Pad(Ship.MastNames[i], 14)}{Ship.SailNames[ship.Sails[i]]}")],
            "돛종류 변경", "");
        if (pick < 0 || pick >= standing.Count) return;

        int mast = standing[pick];
        bool lateen = ship.Sails[mast] == Ship.Lateen;
        if (!ConfirmDialog.Ask(owner, lateen
                ? "삼각돛을 순풍에 뛰어난 사각돛으로 바꿀 건가?"
                : "사각돛을 역풍에 뛰어난 삼각돛으로 바꿀 건가?")) return;

        int cost = Shipyard.SailCost(ship);
        if (!ConfirmDialog.Ask(owner, $"금화 {cost}닢이 드는데, 좋나?")) return;
        if (!_player.Pay(cost)) { GameDialog.Show(owner, "돈이 모자라는 것 같군."); return; }
        if (!ship.SwapSail(mast)) return;

        string where = Ship.MastNames[mast], what = Ship.SailNames[ship.Sails[mast]];
        NoticeDialog.Show(owner,
            $"{where}{GameUi.Josa(where, "을", "를")} {what}{GameUi.Josa(what, "으로", "로")} 변경했습니다");
        _menu.Refresh();
    }

    /// <summary>
    /// 돛 추가 — 추진력을 올리고 그만큼 배가 여려진다.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x00495320</c> 이다. 값은 <b>선체 구입값 / 20</b>.
    /// <code>
    ///   0x005317D8  "이 이상 돛을 단다면 마스트가 부러지네."
    ///   0x00531800  "금화 %ld닢이 드네."
    ///   0x00531818  "마스트에 부담이 되어 배가 조그마한 충격에도 약해지지만, 괜찮겠나?"
    /// </code>
    /// </remarks>
    private void AddSail(Ship ship)
    {
        var owner = Owner;
        int cost = Shipyard.SailCost(ship);

        GameDialog.Show(owner, $"금화 {cost}닢이 드네.");
        if (!_player.CanAfford(cost)) { GameDialog.Show(owner, "돈이 모자라는 것 같군."); return; }
        if (!ConfirmDialog.Ask(owner,
                "마스트에 부담이 되어 배가 조그마한 충격에도 약해지지만, 괜찮겠나?")) return;

        _player.Pay(cost);
        ShowRefit(owner, ship.AddSail(), ship);
    }

    /// <summary>
    /// 포탑수변경 — 대포를 걸 자리를 늘리거나 줄인다.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x00496190</c> 이다.
    /// <code>
    /// 4961d6  상한 = min(선체 표 +0x30, 지금 포탑 + 적재용량)
    /// 496213  0x00454AA0("포탑수 결정", "포탑수", "문", 상한, "최대포탑수", "현재의 포탑수")
    /// 49621d  그대로면 "자네와 장난칠 여유없네."          0x00531F10
    /// 496234  값 = (새 - 지금) x 5 x 5 x 8 = 200 x 늘린 수
    /// 49624a  "금화 %ld닢 받겠네."                        0x00531F28
    /// 49625c  줄일 때는 "뗄 거라면 돈은 필요없네."         0x00531F40
    /// 4960d4  넘치는 대포는 "가격의 30프로로 사 주겠네."   0x00531F80
    /// </code>
    /// </remarks>
    private void ChangeTurrets(Ship ship)
    {
        var owner = Owner;
        GameDialog.Show(owner, "포탑은 몇 개로 할건가?");

        int want = CountDialog.Ask(owner, "포탑수 결정", "포탑수", "문", ship.MaxTurrets, 1, true,
            new CountDialog.Gauge("최대포탑수", ship.MaxTurrets),
            new CountDialog.Gauge("현재의 포탑수", ship.Turrets));
        if (want < 0) return;
        if (want == ship.Turrets) { GameDialog.Show(owner, "자네와 장난칠 여유없네."); return; }

        int cost = Math.Max(0, want - ship.Turrets) * Cannon.TurretPrice;
        GameDialog.Show(owner, cost > 0 ? $"금화 {cost}닢 받겠네." : "뗄 거라면 돈은 필요없네.");
        if (!_player.CanAfford(cost)) { GameDialog.Show(owner, "돈이 모자라네."); return; }
        if (!ConfirmDialog.Ask(owner, "괜찮겠나?")) return;

        var was = ship.Snapshot();
        var gun = Cannon.Of(ship.Gun);
        _player.Pay(cost);

        int spilled = ship.SetTurrets(want);
        if (spilled > 0 && gun != null)
        {
            GameDialog.Show(owner, "지금 싣고 있는 것은 가격의 30프로로 사 주겠네.");
            int back = gun.Price * spilled * Cannon.BuyBackPercent / 100;
            _player.Earn(back);
            GameDialog.Show(owner, $"금화 {back}닢을 벌었습니다.");
        }

        ShowRefit(owner, Refit.Between(was, ship.Snapshot()), ship);
    }

    /// <summary>
    /// 대포구입 — 포탑에 걸 대포를 골라 싣는다.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x004963E0</c> 이다.
    /// <code>
    /// 49643e  포탑이 0 이면 "포탑이 없으면 대포는 실을 수 없네."   0x005320E8
    /// 496473  "어느 대포를 실을 건가?"                            0x00532110
    /// 4964ff  남는 무게 = 적재중량 - 실은무게 + 지금 대포 무게      ; 다 내렸다 치고 잰다
    /// 496520  실을 수 있는 문수 = min(포탑수, 남는무게 / 대포중량)
    /// 496532  같은 대포를 이미 다 실었으면 "이 대포는 더 이상 실을 수 없네."  0x00532128
    /// 49654b  단가보다 돈이 적으면 "돈이 모자라는군."               0x00532178
    /// 4965b7  "실을 수 있을 만큼 싣겠네."(예/아니오) → 아니면 "얼마나 싣겠나?"
    /// 4965f8  "%s%s %d문 실으면 금화 %d닢이네. 좋은가?"            0x005321F0
    /// </code>
    /// 갈래를 바꿔 실으면 실려 있던 것은 <b>30프로</b>로 되사 준다.
    /// <b>어느 마을에서나 넷 다 판다</b> — 게임은 마을마다 파는 것을 가리는데
    /// (<c>0x00443FD0</c>) 그 표는 아직 안 읽었다.
    /// </remarks>
    private void BuyCannon(Ship ship)
    {
        var owner = Owner;
        if (ship.Turrets <= 0) { GameDialog.Show(owner, "포탑이 없으면 대포는 실을 수 없네."); return; }

        GameDialog.Show(owner, "어느 대포를 실을 건가?");
        int at = HintListDialog.Pick(owner,
            [.. Cannon.All.Select(c => $"{GameUi.Pad(c.Name, 12)}{c.Price,6}닢{c.Weight,5}")],
            "대포 선택", "대포가 없네.");
        if (at < 0 || at >= Cannon.Count) return;

        var gun = Cannon.All[at];
        // 이 배의 대포를 다 내렸다 치고 함대에 남는 무게 — 게임도 그렇게 잰다.
        int free = _player.Tonnage - _player.LoadedWeight + ship.GunWeight;
        int room = ship.RoomFor(at, free);
        if (at == ship.Gun) room -= ship.Guns;

        if (room <= 0)
        {
            GameDialog.Show(owner, at == ship.Gun ? "이 대포는 더 이상 실을 수 없네."
                                                  : "이 대포는 무거워서 실을 수 없네.");
            return;
        }
        if (!_player.CanAfford(gun.Price)) { GameDialog.Show(owner, "돈이 모자라는군."); return; }

        GameDialog.Show(owner, gun.Word);
        room = Math.Min(room, _player.Gold / gun.Price);

        int want = ConfirmDialog.Ask(owner, "실을 수 있을 만큼 싣겠네.")
            ? room
            : CountDialog.Ask(owner, "얼마나 싣겠나?", "대포수", "문", room, 1, true,
                new CountDialog.Gauge("최대대포수", ship.Turrets),
                new CountDialog.Gauge("현재의 포수", ship.Guns));
        if (want <= 0) return;

        int cost = gun.Price * want;
        string who = gun.Name;
        if (!ConfirmDialog.Ask(owner,
                $"{who}{GameUi.Josa(who, "을", "를")} {want}문 실으면 금화 {cost}닢이네. 좋은가?"))
            return;
        if (!_player.Pay(cost)) { GameDialog.Show(owner, "돈이 모자라네."); return; }

        var was = ship.Snapshot();

        // 갈래가 갈리면 실려 있던 것은 30프로로 되사 준다.
        if (at != ship.Gun && Cannon.Of(ship.Gun) is { } old && ship.Guns > 0)
        {
            GameDialog.Show(owner, "지금 싣고 있는 것은 가격의 30프로로 사 주겠네.");
            int back = old.Price * ship.Guns * Cannon.BuyBackPercent / 100;
            _player.Earn(back);
            GameDialog.Show(owner, $"금화 {back}닢을 벌었습니다.");
            ship.Load(at, want);
        }
        else
        {
            ship.Load(at, ship.Guns + want);
        }

        ShowRefit(owner, Refit.Between(was, ship.Snapshot()), ship);
    }

    /// <summary>개조 결과 상자를 띄우고 개조 창을 다시 짓는다.</summary>
    private void ShowRefit(Window owner, Refit change, Ship ship)
    {
        if (change.Any)
            NoticeDialog.Show(owner, string.Join(Environment.NewLine,
                change.Lines.Select(l => $"{GameUi.Pad(l.Name, 12)}{l.Before,4} → {l.After,4}")));

        _menu.Pop();
        _menu.Push(() => RefitMenu(ship));
    }

    /// <summary>
    /// 선명변경 — 배 이름을 바꾼다. 값은 안 든다.
    /// </summary>
    /// <remarks>
    /// 게임은 <c>0x00495B90</c> → <c>0x00423BE0</c> 으로 <b>선명입력</b> 창을 띄운다.
    /// 미리 갖춰 둔 이름 스물하나가 먼저 뜨고(포인터 표 <c>0x0053C178</c>), 오른쪽 위 작은
    /// 단추를 누르면 글자판이 떠서 하나씩 찍어 지을 수 있다 —
    /// <see cref="ShipNameDialog"/> · <see cref="TextInputDialog"/> 가 그 둘이다.
    ///
    /// 게임은 배를 <b>살 때도</b> 같은 창으로 이름을 받는데 우리 조선소 구입은 아직 안 묻는다 —
    /// 그때는 안 쓴 이름을 하나 집어 준다(<c>Player.SuggestShipName</c>).
    /// </remarks>
    private void RenameShip(Ship ship)
    {
        var owner = Owner;
        GameDialog.Show(owner, "배의 이름을 정해 주십시오");

        // 그대로 결정했으면 고칠 게 없다 — 창은 그 둘을 가려 주지 않는다.
        if (ShipNameDialog.Ask(owner, ship.Name) is not { } name || name == ship.Name) return;
        if (!ship.Rename(name)) return;

        NoticeDialog.Show(owner, $"{ship.Name}호로 바꾸었다");
        _menu.Refresh();
    }

    /// <summary>
    /// 개조 한 줄을 치른다 — 값을 알리고, 물어보고, 고치고, 바뀐 값을 보여 준다.
    /// </summary>
    /// <remarks>
    /// 차례와 문구는 게임 것 그대로다(<c>0x004955D0</c> 벌).
    /// <code>
    ///   0x00531938  "금화 %ld닢이 드네."
    ///   0x005319A8  "돈이 모자라는 것 같군."
    ///   0x00531950  "용량과 함께 적재용량도 조금 올라가지만, 스피드와 내구력이 조금 떨어지네. 괜찮겠나?"
    ///   0x00531920  "이 이상은 무리로군."
    /// </code>
    /// 게임처럼 <b>돈 검사를 물어보기 앞</b>에 한다 — 시장 구입과는 차례가 반대다.
    /// </remarks>
    private void DoRefit(Ship ship, string item)
    {
        var owner = Owner;
        int cost = Shipyard.RefitCost(ship);

        GameDialog.Show(owner, $"금화 {cost}닢이 드네.");
        if (!_player.CanAfford(cost)) { GameDialog.Show(owner, "돈이 모자라는 것 같군."); return; }
        if (!ConfirmDialog.Ask(owner, Shipyard.RefitWarning(item))) return;

        _player.Pay(cost);
        var change = item switch
        {
            Facility.RefitTonnage => ship.GrowTonnage(),
            Facility.RefitReinforce => ship.Reinforce(),
            _ => ship.GrowCapacity(),
        };

        // 게임이 개조 뒤에 띄우는 "%-12s%4d → %4d" 상자. 배가 바뀌었으니 줄의 흐림도 다시 잡는다.
        ShowRefit(owner, change, ship);
    }

    /// <summary>
    /// 배 한 척을 줄로 적는다 — 이름과 내구·추진·적재를 붙인다. 상했으면 내구를 "지금/최대"로 낸다.
    /// </summary>
    internal static string ShipLine(Ship ship, bool flag)
    {
        string hp = ship.NeedsRepair ? $"{ship.Hp,3}/{ship.MaxHp,-3}" : $"{ship.MaxHp,3}    ";
        return $"{(flag ? "★" : "  ")}{ship.Name}  내구{hp} 추진{ship.Speed,3} 적재{ship.Capacity,4}";
    }
}
