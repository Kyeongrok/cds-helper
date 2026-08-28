using System.Windows;
using CdsHelper.Game.Engine.Menu;
using CdsHelper.Game.Engine.Models;
using CdsHelper.Game.Engine.Town;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 항구 — 함대편성(기함·편입·삭제·파기) · 선원편성(모집·해고) · 발표.
/// </summary>
/// <remarks>
/// 값과 조건은 <see cref="CrewHire"/> · <see cref="Harbor"/> 가 알고, 여기서는 묻고
/// 알리는 차례만 맡는다. 배를 맡기고 찾는 것은 <b>이 마을</b>과 함께 적히므로 마을
/// 번호를 든다.
/// </remarks>
/// <param name="view">이 항구를 낸 도시 창. 물음창의 주인이다.</param>
/// <param name="game">이 판 — 주인공과 주사위가 여기서 온다.</param>
/// <param name="menu">항구 명령 창. 함대편성·선원편성 창을 그 위에 쌓는다.</param>
/// <param name="cityId">이 마을 번호. 배를 맡기고 찾을 때 쓴다.</param>
internal sealed class HarborMenu(Window view, Engine.Game game, GameMenuHost menu, int cityId)
{
    private readonly Window _view = view;
    private readonly Engine.Game _game = game;
    private readonly GameMenuHost _menu = menu;
    private readonly int _cityId = cityId;

    private Player _player => _game.Player;

    /// <summary>물음창을 얹을 창 — 명령 창이 떠 있으면 그 위다.</summary>
    private Window Owner => _menu.Window ?? _view;

    /// <summary>
    /// 함대편성 창. 게임처럼 제목 없이 줄만 쌓고, 마지막 줄만 회녹색 띠가 된다.
    /// "편성 종료" 를 누르면 항구 창으로 되돌아간다 — 창을 닫는 것이 아니라 담긴 것만 갈린다.
    /// </summary>
    public GameMenu FleetMenu() => new(
        [.. Facility.FleetMenu.Select(item => (item, FleetAction(item)))]);

    /// <summary>
    /// 함대편성 줄의 켜짐. 게임의 조건을 그대로 옮겼다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   기함 변경  0x0046A220  배가 두 척 이상
    ///   선박 편입  0x0046A240  함대가 여덟 척 미만이고 이 마을에 맡긴 배가 있다
    ///   선박 삭제  0x0046A270  배가 두 척 이상(이 마을이 더 맡을 수 있어야)
    ///   선박 파기  0x0046A2C0  배가 두 척 이상
    /// </code>
    /// 조건이 어긋난 줄은 흐리게 둔다.
    /// </remarks>
    private Action? FleetAction(string item) => item switch
    {
        "기함 변경" when _player.Ships.Count > 1 => ChangeFlagship,
        "선박 편입" when !_player.IsFleetFull
                      && _player.DockedAt(_cityId).Count > 0 => TakeShip,
        "선박 삭제" when _player.Ships.Count > 1
                      && _player.DockedAt(_cityId).Count < Player.MaxDocked => LeaveShip,
        "선박 파기" when _player.Ships.Count > 1 => ScrapShip,
        Facility.FleetExit => _menu.Pop,
        _ => null,
    };

    /// <summary>기함을 바꾼다. 게임의 <c>0x0046A2F0</c> 자리다.</summary>
    private void ChangeFlagship()
    {
        var owner = Owner;
        var ships = _player.Ships;

        int at = HintListDialog.Pick(owner,
            [.. ships.Select((h, i) => ShipyardMenu.ShipLine(h, i == _player.Flagship))],
            "기함 변경", "바꿀 배가 없습니다");
        if (at < 0) return;

        var name = ships[at].Name;
        if (!ConfirmDialog.Ask(owner, $"기함을 {name}호로 변경하겠습니다. 좋습니까?")) return;

        _player.SetFlagship(at);
    }

    /// <summary>맡겨 둔 배를 함대에 넣는다. 게임의 <c>0x0046A350</c> 자리다.</summary>
    private void TakeShip()
    {
        var owner = Owner;
        var docked = _player.DockedAt(_cityId);

        int at = HintListDialog.Pick(owner, [.. docked.Select(h => ShipyardMenu.ShipLine(h, false))],
                                     "편입선박 선택", "이 마을에 맡겨 둔 배가 없습니다");
        if (at < 0) return;

        if (!_player.Undock(_cityId, at))
            GameDialog.Show(owner, "이 이상 편입할 수 없습니다.");
    }

    /// <summary>함대의 배를 이 마을에 맡긴다. 게임의 <c>0x0046A400</c> 자리다.</summary>
    private void LeaveShip()
    {
        var owner = Owner;

        int at = HintListDialog.Pick(owner,
            [.. _player.Ships.Select((h, i) => ShipyardMenu.ShipLine(h, i == _player.Flagship))],
            "선박삭제", "삭제할 배가 없습니다");
        if (at < 0) return;

        if (!_player.Dock(at, _cityId))
            GameDialog.Show(owner, "이 이상 삭제할 수 없습니다.");
    }

    /// <summary>배를 없앤다. 게임의 <c>0x0046A490</c> 자리다 — 되돌릴 수 없어 한 번 묻는다.</summary>
    private void ScrapShip()
    {
        var owner = Owner;

        int at = HintListDialog.Pick(owner,
            [.. _player.Ships.Select((h, i) => ShipyardMenu.ShipLine(h, i == _player.Flagship))],
            "선박파기", "파기할 배가 없습니다");
        if (at < 0) return;

        var name = _player.Ships[at].Name;
        if (!ConfirmDialog.Ask(owner, $"{name}호를 파기하겠습니다. 좋습니까?")) return;

        if (!_player.Scrap(at))
            GameDialog.Show(owner, "이 이상 파기할 수 없습니다.");
    }

    /// <summary>
    /// 선원편성 창. 모집·해고 두 줄과 돌아가기다 — 게임의 <c>0x004774E0</c> 그대로다.
    /// </summary>
    /// <remarks>
    /// "선원해고" 는 태운 선원이 있어야 눌린다. 게임도 고르는 창을 지으며 그 줄의 켜짐을
    /// <c>0x0040E360() &gt; 0</c>(지금 선원 수)으로 정한다(<c>0x0047753E</c>).
    /// </remarks>
    public GameMenu CrewMenu() => new(
        [.. Facility.CrewMenu.Select(item => (item, CrewAction(item)))]);

    private Action? CrewAction(string item) => item switch
    {
        "선원모집" => HireCrew,
        "선원해고" when _player.Crew > 0 => FireCrew,
        Facility.CrewExit => _menu.Pop,
        _ => null,
    };

    /// <summary>선원 한 사람 값. 이름이 높을수록 싸다(<see cref="CrewHire"/>).</summary>
    private int CrewPrice => CrewHire.PriceFor(_player.Fame);

    /// <summary>
    /// 선원을 모집한다. 게임의 <c>0x00477330</c> 차례 그대로다.
    /// </summary>
    /// <remarks>
    /// 정원이 찼으면 아예 묻지 않고 물린다. 값을 못 치르면 다시 묻고, 다 태우고 나서도
    /// 최저 승원에 모자라면 한 번 더 권한다 — 게임도 그 자리에서 되돌아간다.
    /// </remarks>
    private void HireCrew()
    {
        var owner = Owner;

        while (true)
        {
            if (_player.Crew >= _player.MaxCrew)
            {
                GameDialog.Show(owner,
                    "선원수가 함대의 상한에 달하고 있습니다! 이 이상 고용해도 승선할 수 없습니다.");
                return;
            }

            int price = CrewPrice;
            GameDialog.Show(owner, $"몇 명 모집하겠습니까? 한 사람 당 금화 {price}닢 필요합니다.");

            int want = CountDialog.Ask(owner, "선원고용", "고용할 사람 수", "명",
                                       _player.MaxCrew - _player.Crew, 1, false,
                                       new CountDialog.Gauge("현재의 선원 수", _player.Crew),
                                       new CountDialog.Gauge("최저 선원 수", _player.MinCrew));
            if (want <= 0) return;

            if (price * want > _player.Gold)
            {
                GameDialog.Show(owner, "소지금이 모자랍니다.");
                continue;
            }

            _player.Pay(price * want);
            _player.AddCrew(want);

            // 아직 최저 승원에 모자라면 한 번 더 권한다.
            int lack = _player.MinCrew - _player.Crew;
            if (lack <= 0) return;
            if (!ConfirmDialog.Ask(owner,
                    $"앞으로 적어도 {lack}명은 필요합니다. 좀더 선원을 모집하겠습니까?"))
                return;
        }
    }

    /// <summary>
    /// 선원을 해고한다. 게임의 <c>0x00477460</c> 차례 그대로다 — 삯은 돌려주지 않는다.
    /// </summary>
    private void FireCrew()
    {
        var owner = Owner;

        GameDialog.Show(owner, "선원을 몇 명 해고시키겠습니까?");

        int want = CountDialog.Ask(owner, "선원해고", "해고할 사람 수", "명", _player.Crew,
                                   1, false,
                                   new CountDialog.Gauge("현재의 선원 수", _player.Crew),
                                   new CountDialog.Gauge("최저 승원 수", _player.MinCrew));
        if (want <= 0) return;

        // 최저 승원을 밑돌게 되면 한 번 물어본다.
        if (_player.Crew - want < _player.MinCrew
            && !ConfirmDialog.Ask(owner, "선원 수가 최저 승원 수를 밑돌고 있습니다. 괜찮습니까?"))
            return;

        _player.AddCrew(-want);
    }

    /// <summary>지금 항구에서 알릴 수 있는 발견물(<see cref="Harbor.Announceable"/>).</summary>
    public List<DiscoveryTable.Record> Announceable() =>
        Harbor.Announceable(_player, _game.Discoveries?.Table, _game.Hints);

    /// <summary>
    /// 발견물을 알린다. 게임의 <c>0x00476E10</c> → <c>0x0047EA80</c> 차례다.
    /// </summary>
    /// <remarks>
    /// 알리면 명성이 <b>보수 ÷ 70</b>(적어도 10)만큼 오른다(<c>0x0047E849</c> 가 보수를
    /// 0x46 으로 나누고 10 과 견준다). 게임은 그 자리에서 피로도도 풀고 규율을 100 으로
    /// 되돌리는데, 그 둘은 아직 우리 쪽에 없다.
    ///
    /// 하나 알리고 나면 목록으로 돌아온다 — 게임도 고른 것을 다 알릴 때까지 돈다.
    /// </remarks>
    public void Announce()
    {
        var owner = Owner;

        while (true)
        {
            var rows = Announceable();
            if (rows.Count == 0) return;

            int at = HintListDialog.Pick(owner, [.. rows.Select(r => r.Name)],
                                         "발표할 발견물 선택", "알릴 발견물이 없습니다");
            if (at < 0 || at >= rows.Count) return;

            var row = rows[at];
            if (!_player.Announce(row.Id)) continue;

            int fame = Harbor.FameFor(row);
            _player.Fame += fame;

            GameDialog.Show(owner, $"{row.Name}의 발견을 발표했다!");
            GameDialog.Show(owner, $"명성이 {fame} 올라갔다!");
        }
    }
}
