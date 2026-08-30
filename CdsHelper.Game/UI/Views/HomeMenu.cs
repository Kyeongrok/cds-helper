using System.Windows;
using CdsHelper.Game.Engine.Menu;
using CdsHelper.Game.Engine.Models;
using CdsHelper.Game.Engine.Town;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 자택 — 휴양과 저금.
/// </summary>
/// <remarks>
/// 달수와 지문은 <see cref="Home"/> 가 알고, 여기서는 묻고 알리는 차례만 맡는다.
/// 게임의 휴양 창이 <c>0x00460660</c>, 저금 창이 <c>0x004609C0</c> 이다.
///
/// 보관(<c>StorageDialog</c>)과 기능(<see cref="GameSystemMenu"/>)은 자택 줄이지만
/// 여기 없다 — 보관은 창 하나로 끝나고, 기능은 도시 일이 아니라 판 일이다.
/// </remarks>
/// <param name="view">이 자택을 낸 도시 창. 물음창의 주인이다.</param>
/// <param name="game">이 판 — 주인공과 주사위가 여기서 온다.</param>
/// <param name="menu">자택 명령 창. 휴양·저금 창을 그 위에 쌓는다.</param>
internal sealed class HomeMenu(Window view, Engine.Game game, GameMenuHost menu)
{
    private readonly Window _view = view;
    private readonly Engine.Game _game = game;
    private readonly GameMenuHost _menu = menu;

    private Player _player => _game.Player;
    private Random _random => _game.Random;

    /// <summary>물음창을 얹을 창 — 명령 창이 떠 있으면 그 위다.</summary>
    private Window Owner => _menu.Window ?? _view;

    // ── 후손을 남긴다 ────────────────────────────────────────────────────────

    /// <summary>아내가 있어야 눌린다 — 없으면 줄이 흐리다.</summary>
    public bool CanLeaveHeir => Home.CanLeaveHeir(_player);

    /// <summary>
    /// "후손을 남긴다" — 게임의 <c>0x00461330</c> 이다.
    /// </summary>
    /// <remarks>
    /// 차례가 이렇다.
    /// <code>
    ///   461363  아내가 없으면([0x005B61B0] == -1) 아무 일도 없다
    ///   46137c  아내 상태가 2 라야 한다
    ///   46139e  체력([0x005B60D8])이 100 이상이라야 한다
    ///   4613cc  rand(8) &lt; 2 라야 얻는다                      ← 네 번에 한 번
    ///   4613e3  0x004A6340(된 것인가) — MPEFFECT 2번(대포)
    ///   4613fc  됐으면 아이를 만든다
    ///   461401  0x00469850(5) — 닷새가 간다
    /// </code>
    /// 아내 상태와 체력 두 관문은 우리 쪽에 그 칸이 없어 안 옮겼다. 애니메이션이 대포인
    /// 것은 게임 그대로다.
    /// </remarks>
    public void LeaveHeir()
    {
        if (!CanLeaveHeir) return;

        bool born = Home.HeirBorn(_random);

        // 애니메이션은 도시 그림 위에서 돈다 — 명령 창이 아니라 그림이 든다.
        (_view as CityPicView)?.PlayHeir(born);

        // 닷새가 간다. 됐든 안 됐든 간다.
        _player.AdvanceDays(Home.HeirDays);

        if (!born)
        {
            GameDialog.Show(Owner, "이번에는 아이가 생기지 않았습니다.");
            return;
        }

        string name = HeirName();
        _player.AddHeir(name);
        GameDialog.Show(Owner, $"{_player.Spouse}님이 아이를 낳았습니다. 이름은 {name}입니다!");
    }

    /// <summary>
    /// 아이 이름. 게임은 이름 표에서 뽑는데 우리는 <b>주인공의 성</b>에 차례를 붙인다.
    /// </summary>
    private string HeirName()
    {
        string family = _player.Name.Length > 0 ? _player.Name : "이름 없는";
        return $"{family} {_player.Heirs.Count + 1}세";
    }

    /// <summary>
    /// 자택의 휴양 창 — 한 달 휴양 · 장기 휴양 · 취소. 게임의 <c>0x00460660</c> 그대로다.
    /// </summary>
    public GameMenu RestMenu() => new(
        [.. Facility.RestMenu.Select(item => (item, RestAction(item)))]);

    private Action? RestAction(string item) => item switch
    {
        "한 달 휴양" => RestOneMonth,
        "장기 휴양" => RestLong,
        Facility.RestExit => _menu.Pop,
        _ => null,
    };

    /// <summary>한 달 쉰다. 물어보고 예라야 쉰다.</summary>
    private void RestOneMonth()
    {
        if (ConfirmDialog.Ask(Owner, "한 달 동안 휴양하겠습니까?")) Rest(1);
    }

    /// <summary>몇 달이고 쉰다. 게임처럼 한 해까지만 고를 수 있다.</summary>
    private void RestLong()
    {
        var owner = Owner;

        GameDialog.Show(owner, "몇 개월 동안 휴양하겠습니까?");
        int months = CountDialog.Ask(owner, "휴양", "휴양할 달수", "개월", Home.MaxRestMonths);
        if (months > 0) Rest(months);
    }

    /// <summary>
    /// 그만큼 쉰다. 값은 안 든다 — 내 집이다.
    /// </summary>
    /// <remarks>
    /// 게임은 <c>0x004A2AD0(개월 x 30, 1)</c> 로 <b>날수</b>를 넘긴다 — 달력 달이 아니라
    /// 서른 날이다. 쉬고 나면 아내가 있으면 아내가, 없으면 지문이 셋 중 하나를 낸다
    /// (<c>0x004607FE</c> 의 <c>rand(3)</c>). 우리 쪽에는 아내가 없어 지문만 쓴다.
    ///
    /// 쉬면 <b>하루에 피로 -1, 사기 +3</b> 씩 돌아온다 — 게임은 그것을 날을 넘기는 자리
    /// (<c>0x004A2AD0</c>)에서 함께 하므로 <see cref="Player.AdvanceDays"/> 가 맡는다.
    /// 그래서 한 달만 쉬어도 폭풍 몇 번 분이 한꺼번에 풀린다.
    /// </remarks>
    private void Rest(int months)
    {
        _player.AdvanceDays(Home.RestDays(months));
        GameDialog.Show(Owner, Home.RestWord(_random));
    }

    /// <summary>
    /// 자택의 저금 창 — 저금한다 · 꺼낸다 · 중지한다. 게임의 <c>0x004609C0</c> 그대로다.
    /// </summary>
    /// <remarks>
    /// 제목이 <c>"저금 %8ld 닢"</c>(<c>0x005398C0</c>) 이라 지금 맡겨 둔 돈이 창 이름에 붙는다.
    /// 줄의 켜짐도 게임과 같다 — 저금은 소지금이, 꺼내기는 저금이 있어야 눌린다.
    /// </remarks>
    public GameMenu SavingsMenu() => new(
        $"저금 {_player.Savings,8} 닢", null,
        [.. Facility.SavingsMenu.Select(item => (item, SavingsAction(item)))]);

    private Action? SavingsAction(string item) => item switch
    {
        "저금한다" when _player.Gold > 0 => Deposit,
        "꺼낸다" when _player.Savings > 0 => Withdraw,
        Facility.SavingsExit => _menu.Pop,
        _ => null,
    };

    /// <summary>
    /// 저금한다. 소지금과 저금 칸이 남은 만큼만 맡길 수 있다.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x00460AC9</c> 그대로다 — 저금이 이미 백만 닢이면 "더 이상 저금할 수
    /// 없습니다"(<c>0x00539948</c>) 로 물리고, 아니면 <c>min(백만 - 저금, 소지금)</c> 까지 받는다.
    /// </remarks>
    private void Deposit()
    {
        var owner = Owner;

        int room = Player.MaxGold - _player.Savings;
        if (room <= 0)
        {
            GameDialog.Show(owner, "더 이상 저금할 수 없습니다");
            return;
        }

        int want = CountDialog.Ask(owner, "저금한다", "금  액", "닢",
                                   Math.Min(room, _player.Gold), MoneyStep, full: true,
                                   new CountDialog.Gauge("소지금", _player.Gold),
                                   new CountDialog.Gauge("저  금", _player.Savings));
        if (want <= 0) return;

        GameDialog.Show(owner, $"금화 {_player.Deposit(want)}닢을 저금하겠습니다");
    }

    /// <summary>
    /// 저금을 꺼낸다. 소지금도 백만 닢에서 막히므로 그만큼만 꺼낼 수 있다.
    /// </summary>
    /// <remarks>게임의 <c>0x00460B5F</c> 그대로다.</remarks>
    private void Withdraw()
    {
        var owner = Owner;

        int room = Player.MaxGold - _player.Gold;
        if (room <= 0)
        {
            GameDialog.Show(owner, "더 이상 꺼낼 수 없습니다");
            return;
        }

        int want = CountDialog.Ask(owner, "저금을 꺼낸다", "금  액", "닢",
                                   Math.Min(room, _player.Savings), MoneyStep, full: true,
                                   new CountDialog.Gauge("소지금", _player.Gold),
                                   new CountDialog.Gauge("저  금", _player.Savings));
        if (want <= 0) return;

        GameDialog.Show(owner, $"금화 {_player.Withdraw(want)}닢을 꺼내겠습니다");
    }

    /// <summary>돈을 ↑↓ 로 움직이는 단위. Shift 를 누르면 천 닢씩 뛴다.</summary>
    private const int MoneyStep = 100;
}
