using CdsHelper.Game.Engine.Models;
using CdsHelper.Support.Local.Models;
using CdsHelper.Game.Engine.Town;

namespace CdsHelper.Game.Engine.Menu;

/// <summary>
/// 시설 명령 창이 도시 화면에 시킬 수 있는 일들.
/// </summary>
/// <remarks>
/// 어느 줄이 무슨 일인지는 <see cref="TownWorks"/> 가 알고, <b>그 일마다 무엇을 하는지</b>
/// 는 <see cref="TownMenu"/> 가 안다. 실제로 창을 띄우고 돈을 세는 것은 도시 화면이라,
/// 그 사이를 이 문으로 잇는다.
///
/// <b>여기에는 창 물건이 오가지 않는다.</b> 오가는 것은 「할 수 있나」와 「해라」뿐이다 —
/// 그래야 차림표 쪽이 화면 물건을 안 알아도 된다.
///
/// 손이 안 달린 일은 <see cref="TownMenu.ActionFor"/> 가 null 을 내고, 그러면 그 줄이
/// 흐리게 나온다.
/// </remarks>
internal interface ITownScreen
{
    // ── 할 수 있나 ───────────────────────────────────────────────────────

    /// <summary>배가 한 척이라도 있는지. 출항·보급·선원편성이 이것을 본다.</summary>
    bool HasShips { get; }

    /// <summary>지닌 물건이 있는지. 자택 보관이 이것을 본다.</summary>
    bool HasItems { get; }

    /// <summary>시장에서 살 수 있는지 — 값 셈이 서 있어야 한다.</summary>
    bool CanBuyGoods { get; }

    /// <summary>시장에 팔 수 있는지 — 아이템 표까지 있어야 한다.</summary>
    bool CanSellGoods { get; }

    /// <summary>함대편성 창을 열 만한지 — 그 안의 네 줄 가운데 하나라도 살아 있어야 한다.</summary>
    bool CanFormFleet { get; }

    /// <summary>고칠 배가 있는지(<c>0x0044BD40</c>).</summary>
    bool CanRepairShip { get; }

    /// <summary>서가를 열 수 있는지 — 책 표를 읽었어야 한다.</summary>
    bool CanRead { get; }

    /// <summary>후손을 남길 수 있는지.</summary>
    bool CanLeaveHeir { get; }

    // ── 해라 ─────────────────────────────────────────────────────────────

    /// <summary>명령 창을 닫고 도시로 돌아간다.</summary>
    void CloseMenu();

    /// <summary>수련 — 가르치는 사람이 먼저 말을 걸고 그 다음이 배우기다.</summary>
    void Train(int buildingCode, uint teachMask);

    /// <summary>「기능」 — 저장 · 로드 · 게임 종료.</summary>
    void OpenSystemMenu();

    void Persuade(Patron patron);
    void Report(Patron patron);
    void BreakContract(Patron patron);

    /// <summary>출항. 나가도 좋은지는 항구가 따진다.</summary>
    void Sail();

    /// <summary>탐험을 떠난다. 나가도 좋은지는 성문이 따진다.</summary>
    void Explore(int buildingCode);

    /// <summary>발견한 건물의 해설 — 그림 한 장과 그 이야기.</summary>
    void ShowComment(int buildingCode);

    void OpenFleetForm();
    void OpenCrewForm();
    void ShowPortCityInfo();

    /// <summary>발표 — 다 알리고 나면 그 줄이 사라지므로 줄 목록을 다시 짓는다.</summary>
    void Announce();

    void Supply();
    void BuyShip();
    void SellShip();
    void RepairShip();
    void RefitShip();
    void BuyGoods();
    void SellGoods();

    /// <summary>여관 숙박.</summary>
    void Stay();

    void ShowMates();
    void LeaveHeir();
    void OpenRestMenu();
    void OpenSavingsMenu();
    void OpenStorage();
    void ShowEncyclopedia();
    void ReadBooks();

    /// <summary>그 줄이 술 줄인지 — 줄 이름이 곧 술 이름이다.</summary>
    /// <remarks>
    /// 술 줄은 일 표에 없다. 고장마다 파는 것이 달라 <see cref="TownWorks.LinesOf"/> 가
    /// 그때그때 앞에 붙이기 때문이다.
    /// </remarks>
    bool IsDrink(string item);

    /// <summary>술 한 잔 마신다.</summary>
    void Drink(string item);
}
