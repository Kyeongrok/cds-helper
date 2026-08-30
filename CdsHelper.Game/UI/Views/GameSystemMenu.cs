using System.Windows;
using CdsHelper.Game.Engine.Menu;
using CdsHelper.Game.Engine.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 자택·여관의 "기능" 줄에서 뻗는 차림표 — 저장 · 로드 · 게임 종료 · 게임 재개.
/// </summary>
/// <remarks>
/// 도시 일이 아니라 <b>판 일</b>이다. 시설 차림표에서 뻗어 나올 뿐이라 도시 창 안에 들어
/// 있었는데, 하는 일은 판을 적고 첫 화면으로 돌아가는 것뿐이라 여기로 갈라 두었다.
///
/// 게임도 이 넷이 한 덩이다 — 저장 <c>0x004A2800</c> · 로드 <c>0x004A2830</c> ·
/// 게임 종료 <c>0x004A2860</c> 이 나란히 놓여 있고 고르는 자리가 <c>0x004A292B</c> 다.
/// </remarks>
internal static class GameSystemMenu
{
    /// <summary>기능 차림표를 짓는다.</summary>
    /// <param name="view">차림표를 낸 시설 창. 물음창의 주인이고, 첫 화면은 이 창의 주인이 낸다.</param>
    /// <param name="game">적을 판.</param>
    /// <param name="menu">차림표를 든 자리. 고르고 나면 접는다.</param>
    public static GameMenu Build(Window view, Engine.Game game, GameMenuHost menu) => new(
        [.. Facility.SystemMenu.Select(item => (item, ActionOf(item, view, game, menu)))]);

    private static Action? ActionOf(string item, Window view, Engine.Game game,
                                   GameMenuHost menu) => item switch
    {
        "저장" => () => Save(view, game, menu),
        "로드" => () => Load(view, menu),
        "게임 종료" => () => Quit(view, menu),
        "게임 재개" => menu.Close,
        _ => null,
    };

    /// <summary>
    /// 지금 판(소지금·날짜·있는 도시·배운 기술)을 적는다. 게임처럼 <b>겹쳐 쓸지 먼저 묻고</b>
    /// 다 적은 뒤에 겹쳐 썼다고 알린다 — 세이브 자리가 하나뿐이라 적는 일은 늘 겹쳐 쓰기다.
    /// 게임 폴더가 아니라 우리 자리에 쓴다 — <see cref="Engine.GameSave"/> 참고.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x004A2800</c> 그대로다 — 물음(<c>0x00568CB8</c>) · 쓰기 · 알림(<c>0x00568CE0</c>).
    /// 물음에 YES 가 아니면 아무것도 쓰지 않고 그냥 돌아간다.
    /// </remarks>
    public static void Save(Window view, Engine.Game game, GameMenuHost menu)
    {
        var owner = menu.Window ?? view;
        if (!ConfirmDialog.Ask(owner, "데이터를 겹쳐 쓰겠습니다. 좋습니까?")) return;

        string error = game.Save();
        ConfirmDialog.Tell(owner, error.Length == 0 ? "데이터를 겹쳐 썼습니다"
                                                    : $"기록하지 못했다 — {error}");
    }

    /// <summary>
    /// 적어 둔 판을 도로 불러온다. 게임처럼 <b>한 번 묻고</b> 곧바로 불러온다.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x004A2830</c> 이다 — 물음이 <c>0x00568CF8</c>("데이터를 불러 오겠습니다.
    /// 좋습니까?")다. YES 가 아니면 아무것도 안 한다.
    ///
    /// <b>지금 판은 사라진다</b> — 적어 두지 않은 것은 되돌릴 길이 없다. 게임도 그렇다.
    /// </remarks>
    public static void Load(Window view, GameMenuHost menu)
    {
        if (view.Owner is not ShipMapWindow map) { menu.Close(); return; }
        if (!ConfirmDialog.Ask(menu.Window ?? view, "데이터를 불러 오겠습니다. 좋습니까?")) return;

        menu.Close();
        map.LoadGame();
    }

    /// <summary>
    /// 놀이를 그만두고 첫 화면으로 돌아간다. 게임도 창을 닫지 않고 첫 화면으로만 되돌아간다.
    /// </summary>
    /// <remarks>
    /// 되돌리는 일은 함대 창이 맡는다 — 시설 창은 그 창이 거느린 것이라 곧 닫힌다.
    /// 물어보고 나서 하는 것은 되돌릴 수 없기 때문이다(적어 두지 않은 것은 사라진다).
    ///
    /// 문구는 게임 것 그대로다(<c>0x00568D20</c>). 게임은 <b>아직 저장하지 않은 판</b>이면
    /// 여기서 한 번 더 묻는데(<c>0x00568D38</c> · 상태비트 <c>0x005A4D18 &amp; 0x80</c>)
    /// 우리는 그 비트를 아직 안 들고 있어 그 갈래가 없다.
    /// </remarks>
    public static void Quit(Window view, GameMenuHost menu)
    {
        if (view.Owner is not ShipMapWindow map) { menu.Close(); return; }
        if (!ConfirmDialog.Ask(view, "게임을 종료하겠습니까?")) return;

        menu.Close();
        map.ReturnToTitle();
    }
}
