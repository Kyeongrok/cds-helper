using System.Windows;
using System.Windows.Controls;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Game.UI.Views;

namespace CdsHelper.Game.Engine.Menu;

/// <summary>메뉴 줄 하나.</summary>
/// <param name="Text">줄에 적히는 글.</param>
/// <param name="Run">누르면 할 일. null 이면 흐리고 안 눌린다 — 아직 손이 안 달린 줄이 그렇다.</param>
/// <param name="Style">
/// 띠 무늬. 안 주면 자리가 정한다 — <b>마지막 줄만</b> 회녹색이고 나머지는 베이지다.
/// </param>
internal sealed record GameMenuRow(string Text, Action? Run = null, BandStyle? Style = null);

/// <summary>
/// 게임 명령 창 하나 — 제목 줄과 단추들을 세로로 쌓고 상자로 두른다.
/// </summary>
/// <remarks>
/// 창은 <b>단추 목록</b>이다(<see cref="GameButton"/>). 제목 줄도 무늬가 다른 단추일 뿐이라
/// 특별한 물건이 아니다 — 다만 닫기(X)를 다는 갈래만 <see cref="GameUi.TitleBar"/> 를 쓴다.
/// <code>
///   ┌ 커맨드 ──────┐   Title  진홍
///   │ 출항         │   Button 베이지
///   │ 보급         │
///   │ 취소         │   Alt    회녹색 — 마지막 줄
///   └─────────────┘
/// </code>
///
/// <b>목록(<see cref="GameList"/>)과는 다른 물건이다.</b> 메뉴는 띠 그림으로 짓고 누르면
/// 곧바로 일이 일어난다. 목록은 양피지에 글자만 얹고 고르기만 하며, 하는 일은 창 아래
/// "결정" 이 맡는다.
///
/// 창을 띄우고 겹치고 되돌아가는 것은 <see cref="GameMenuHost"/> 가 맡는다.
/// </remarks>
internal sealed class GameMenu : Border
{
    /// <summary>창을 두르는 테. 게임은 밝은 선 <b>한 점</b>에 짙은 여백 여섯 점이다.</summary>
    private const double BoxEdge = 1, BoxPad = 6;

    /// <summary>창을 자연 폭보다 이만큼 넓게 잡는다.</summary>
    private const double BoxWiden = 1.1;

    /// <param name="title">제목 줄에 적을 글. 비우면 제목 줄 자체가 없다(기능 창이 그렇다).</param>
    /// <param name="rows">줄들.</param>
    /// <param name="onClose">제목 줄 오른쪽에 닫기(X)를 단다. null 이면 안 단다.</param>
    public GameMenu(string title, IReadOnlyList<GameMenuRow> rows, Action? onClose = null)
    {
        var stack = new StackPanel();

        if (title.Length > 0) stack.Children.Add(TitleRow(title, onClose));

        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            // 게임은 창의 마지막 줄(나가기·취소)만 회녹색으로 낸다 — 도서관의 "도서관을
            // 나온다", 도시정보의 "취소" 가 그것이다.
            var style = row.Style
                        ?? (i == rows.Count - 1 ? BandStyle.Alt : BandStyle.Button);
            // 메뉴는 줄을 붙여 쌓으므로 단추끼리 벌리는 여백을 덮는다.
            stack.Children.Add(new GameButton(row.Text, row.Run, style) { Margin = default });
        }

        Background = GameUi.MenuBack;
        BorderBrush = GameUi.Edge;
        BorderThickness = new Thickness(BoxEdge);
        Padding = new Thickness(BoxPad);
        Child = stack;

        // 자연 폭보다 한 뼘 넓게 잡는다. 딱 맞게 두면 긴 줄("마을로 돌아간다")의 끝 글자가
        // 띠를 넘어 잘린다 — 게임 글꼴이 한 글자 16점이라 조금만 길어도 자리가 모자란다.
        Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        MinWidth = Math.Ceiling(DesiredSize.Width * BoxWiden);
    }

    /// <summary>글자와 손을 짝지어 주는 짧은 길. 줄이 많은 창을 적을 때 쓴다.</summary>
    public GameMenu(string title, Action? onClose, params (string Text, Action? Run)[] rows)
        : this(title, [.. rows.Select(r => new GameMenuRow(r.Text, r.Run))], onClose) { }

    /// <summary>제목 없이 줄만 쌓은 창. 기능 창처럼 제목이 없는 것에 쓴다.</summary>
    public GameMenu(params (string Text, Action? Run)[] rows) : this("", null, rows) { }

    /// <summary>
    /// 제목 줄. 닫기(X)를 달 때만 <see cref="GameUi.TitleBar"/> 를 쓰고, 아니면 띠 하나다.
    /// </summary>
    /// <remarks>
    /// 제목도 <see cref="BandStyle.Title"/> 무늬의 단추다. 다만 원본 조각을 못 읽었을 때
    /// 물러서는 모습이 단추와 다르다 — 단추는 베이지 상자로, 제목은 <b>어두운 상자에 밝은
    /// 글씨</b>로 물러선다. 그래서 그 갈래만 손으로 짓는다.
    /// </remarks>
    private static UIElement TitleRow(string title, Action? onClose)
    {
        if (onClose != null) return GameUi.TitleBar(title, onClose);

        return GameUi.TitleFrame(GameUi.Sprites, title) ?? new Border
        {
            Background = GameUi.MenuBack,
            BorderBrush = GameUi.Edge,
            BorderThickness = new Thickness(2),
            Padding = new Thickness(18, 2, 18, 2),
            Child = new TextBlock
            {
                Text = title,
                Foreground = GameUi.Text,
                FontWeight = FontWeights.Bold,
                FontSize = 15,
                HorizontalAlignment = HorizontalAlignment.Center,
            },
        };
    }
}
