using System.Windows;

namespace CdsHelper.Maze;

/// <summary>
/// 미궁 64 퍼즐의 바깥 문. <b>이 덩이에서 밖으로 열린 것은 이것 하나뿐</b>이다.
/// </summary>
/// <remarks>
/// 화면(<see cref="MazePuzzleDialog"/>)은 CdsHelper.Game 의 밤색 판을 물려받는데 그
/// 판이 안쪽 것이라 화면도 안쪽으로 둘 수밖에 없다. 그래서 부르는 자리만 이렇게
/// 따로 낸다 — 띄우는 쪽(CdsHelper.Form)이
/// <c>ShipMapWindow.MazeGame = MazeGame.Play</c> 로 걸어 준다.
/// </remarks>
public static class MazeGame
{
    /// <summary>놀이를 한 판 하고 결과를 알린다.</summary>
    public static void Play(Window owner, Random rng) => MazePuzzleDialog.Play(owner, rng);
}
