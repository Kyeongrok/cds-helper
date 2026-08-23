using System.Windows;
using CdsHelper.Game.UI.Views;

namespace CdsHelper.Duel;

/// <summary>
/// 일기토의 바깥 문. <b>이 덩이에서 밖으로 열린 것은 이것 하나뿐</b>이다.
/// </summary>
/// <remarks>
/// 화면(<see cref="DuelDialog"/>)은 CdsHelper.Game 의 밤색 판을 물려받는데 그 판이
/// 안쪽 것이라 화면도 안쪽으로 둘 수밖에 없다. 그래서 부르는 자리만 이렇게 따로
/// 낸다 — 띄우는 쪽(CdsHelper.Form)이 <c>ShipMapWindow.DuelGame = DuelGame.Play</c>
/// 로 걸어 준다. <see cref="CdsHelper.Maze.MazeGame"/> 과 같은 꼴이다.
/// </remarks>
public static class DuelGame
{
    /// <summary>
    /// 상대의 세기 눈금. 값은 게임의 괴물·적장 벌(<c>0x00440DC1</c>)에서 따 왔다.
    /// </summary>
    private static readonly (string Name, int Body, int Might, int Sword)[] Foes =
    [
        ("해적 두목", 70, 70, 1),
        ("이슬람 제독", 80, 80, 2),
        ("토벌대장", 90, 90, 2),
        ("전설의 검객", 100, 100, 3),
    ];

    /// <summary>
    /// 한 판 한다.
    /// </summary>
    /// <remarks>
    /// 게임에서는 <b>해전에서 기함끼리 붙었을 때만</b> 열린다(<c>0x0043A347</c>).
    /// 아직 해전이 없으니 여기서는 상대를 골라 바로 붙는다.
    ///
    /// 내 값은 <c>0x004A87DE</c> 가 하듯 인물 레코드에서 그대로 가져온다 —
    /// 무력 <c>+0x28</c>, 검술 <c>+0x48</c> 에 1 을 더하는 자리까지 같다.
    /// </remarks>
    public static void Play(Window owner, Random rng)
    {
        var names = Foes.Select(foe => foe.Name).ToList();
        int pick = MapPointDialog.Ask(owner, names, "일기토");
        if (pick < 0) return;

        var (name, body, might, sword) = Foes[pick];

        // 아직 인물을 물리지 않았으니 웬만한 모험가 하나를 세운다.
        var mine = new Duel.Fighter("나", 100, 60, 2, 40, weapon: 34, armour: 25);
        var theirs = new Duel.Fighter(name, body, might, sword, might,
                                      weapon: 22 + pick * 10, armour: 15 + pick * 8);

        bool? won = DuelDialog.Play(owner, new Duel(mine, theirs, rng));
        if (won == null) return;

        NoticeDialog.Show(owner,
            won.Value
                ? "이겼다! 상대를 쓰러뜨렸다." + Environment.NewLine +
                  "「처형한다 · 놓아 준다 · 모두 뺏는다」 는 아직 안 옮겼다."
                : "졌다. 상대의 칼끝이 먼저 닿았다.",
            "일기토");
    }
}
