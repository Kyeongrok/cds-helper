using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Game.Engine.Menu;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 줄 몇 개를 늘어놓고 하나를 고르게 하는 <b>모달</b> 창 — 게임 커맨드 창과 같은 모습이다.
/// </summary>
/// <remarks>
/// <see cref="GameMenu"/> 는 눌렀을 때 할 일을 줄마다 들고 있는 <b>안 멈추는</b> 창이라,
/// "골라 올 때까지 기다리는" 자리에는 못 쓴다. 첫 화면의 NEW GAME 처럼 <b>대답을 받아
/// 와야</b> 하는 곳에 이것을 쓴다.
/// <code>
///   NEW GAME
///     초심자용 주인공으로 시작한다(EASY)
///     새로운 주인공으로 시작한다(NORMAL)
///     취소
/// </code>
/// 마지막 줄은 <see cref="GameMenu"/> 가 알아서 회녹색 띠로 낸다 — 게임도 나가기 줄을
/// 그렇게 갈라 놓는다.
/// </remarks>
internal sealed class ChoiceDialog : Window
{
    private int _picked = -1;

    private ChoiceDialog(string title, IReadOnlyList<string> rows, string cancel)
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        // 네모진 창이라 비침이 필요 없다 — 레이어드 창은 겹칠 때마다 깜빡인다.
        Background = GameUi.Back;

        var items = new List<(string Text, Action? Run)>();
        for (int i = 0; i < rows.Count; i++)
        {
            int pick = i;
            items.Add((rows[i], () => { _picked = pick; Close(); }));
        }
        items.Add((cancel, Close));

        var menu = new GameMenu(title, null, [.. items]);
        var root = new Border { Background = GameUi.Back, Child = menu };
        GameUi.EnableDrag(this, root);
        Content = root;

        KeyDown += (_, e) => { if (e.Key is Key.Escape) Close(); };
        MouseRightButtonUp += (_, _) => Close();
    }

    /// <summary>
    /// 창을 띄우고 고른 줄 번호를 낸다. 물렀으면 -1.
    /// </summary>
    /// <param name="owner">주인 창.</param>
    /// <param name="title">제목 줄.</param>
    /// <param name="rows">고를 줄들.</param>
    /// <param name="cancel">마지막 나가기 줄의 글.</param>
    public static int Ask(Window owner, string title, IReadOnlyList<string> rows,
                          string cancel = "취소")
    {
        var dialog = new ChoiceDialog(title, rows, cancel) { Owner = owner };
        dialog.ShowDialog();
        return dialog._picked;
    }
}
