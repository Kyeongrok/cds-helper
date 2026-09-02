using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Game.Engine.Menu;
using CdsHelper.Game.Local.Helpers;

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

    private ChoiceDialog(string title, IReadOnlyList<string> rows, string? cancel)
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        // 네모진 창이라 비침이 필요 없다 — 레이어드 창은 겹칠 때마다 깜빡인다.
        Background = GameUi.Back;

        var items = new List<GameMenuRow>();
        for (int i = 0; i < rows.Count; i++)
        {
            int pick = i;
            // 나가기 줄이 없는 창은 <b>마지막 줄도 단추</b>다 — 안 그러면 GameMenu 가
            // 끝 줄을 회녹색 나가기 띠로 낸다. 스폰서의 승낙/교섭이 그런 창이다.
            items.Add(new GameMenuRow(rows[i], () => { _picked = pick; Close(); },
                                      cancel == null ? BandStyle.Button : null));
        }
        if (cancel != null) items.Add(new GameMenuRow(cancel, Close));

        var menu = new GameMenu(title, items);
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

    /// <summary>
    /// 나가기 줄 없이 <b>줄이 모두 같은 단추</b>인 고르기 창. 물렀으면 -1.
    /// </summary>
    /// <remarks>
    /// 스폰서의 계약 제안이 이 모양이다 — 제목 띠에 「기간N년 금화 N닢」이 서고 그 아래
    /// 승낙한다·교섭한다 두 줄이 같은 무늬로 놓인다. 게임은 이 창을
    /// <c>0x00469A70</c> 으로 낸다(<c>0x004AF22A</c> 가 줄 수 2 를 넘긴다).
    /// </remarks>
    public static int Pick(Window owner, string title, IReadOnlyList<string> rows)
    {
        var dialog = new ChoiceDialog(title, rows, null) { Owner = owner };
        dialog.ShowDialog();
        return dialog._picked;
    }
}
