namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 한 마디 알리고 확인만 받는 창 — <see cref="ConfirmDialog"/> 를 부르는 <b>이름 하나</b>다.
/// </summary>
/// <remarks>
/// 예전에는 제 창을 따로 지었다. 그래서 같은 말이 이 창과 물음창에서 <b>다른 폭 · 다른
/// 글꼴</b>로 떴다 — 이쪽은 WPF 글자에 여백을 손으로 박았고, 물음창은 게임 셈
/// (칸수 = max(30, 가장 긴 줄) · 너비 = 칸수 x 8 + 32)으로 게임 글꼴을 찍는다.
///
/// 게임은 알림과 물음이 <b>한 함수</b>다(<c>0x00469060</c> — 첫 인자가 0 이면 확인,
/// 2 면 YES/NO). 우리도 한 벌만 두고, 이 이름은 부르는 자리 여든 남짓을 그대로 두려고
/// 남겨 둔 껍데기다.
/// </remarks>
public static class NoticeDialog
{
    /// <param name="owner">알림을 얹을 창.</param>
    /// <param name="text">할 말.</param>
    /// <param name="title">제목 띠에 얹을 글. 비우면 띠를 안 단다.</param>
    public static void Show(System.Windows.Window owner, string text, string? title = null) =>
        ConfirmDialog.Tell(owner, text, title);
}
