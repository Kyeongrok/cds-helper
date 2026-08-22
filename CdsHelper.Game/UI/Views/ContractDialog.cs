using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 계약 정보 창 — 지금 맺고 있는 계약을 보여 준다. 도시 커맨드의 "계약 정보" 로 연다.
/// </summary>
/// <remarks>
/// 게임 화면을 그대로 옮겼다. 제목은 <b>힌트 이름</b>이다.
/// <code>
///   신세계해협                                    [X]
///      스폰서  헨리 7세
///        마을  런던
///      계약금    124300닢
///        선금     62150닢    계약 기한
///        미불     62150닢       나머지 2년
///      발견물
///
///      증거품
///                                            [취소]
/// </code>
/// 줄 글은 게임 서식 그대로다(<c>0x0055A3A0</c> 벌) — 앞의 빈칸까지 그대로 두면 게임
/// 글꼴(한글 16점 · 빈칸 8점)에서 "스폰서"와 "  마을", "계약금"과 "  선금" 의 오른쪽 끝이
/// 저절로 맞는다.
///
/// <code>
///   0x0055A3A0  "스폰서  %s"          0x0055A3B0  "  마을  %s"
///   0x0055A3C0  "계약금  %8ld닢"      0x0055A3D0  "  선금  %8ld닢    계약 기한"
///   0x0055A3F0  "  미불  %8ld닢      "  0x0055A408 "나머지" / "%2d년" / "%2d개월"
///   0x0055A420  "기한이 지났습니다"    0x0055A438  "발견물"   0x0055A440  "증거품"
/// </code>
///
/// <b>선금도 미불도 계약금의 절반이다</b> — 그리는 자리(<c>0x0047F38C</c> · <c>0x0047F3C8</c>)가
/// 둘 다 <c>계약금 / 2</c> 를 낸다. 남은 기한은 날수를 365 로 나눠 햇수를, 나머지를 30 으로
/// 나눠 달수를 내며, 햇수가 있으면 달수는 0 일 때 안 적는다(<c>0x0047F444</c>).
///
/// <b>발견물</b> 은 이 계약을 맺은 뒤에 발견한 것이고, <b>증거품</b> 은 그것들이 준 물건 중
/// 아직 지니고 있는 것이다. 게임은 후원자에게 보고할 때 이 둘을 내민다.
///
/// 계약이 없으면 이 창을 열지 않고 게임처럼 한 줄로 물린다 — "계약을 맺지 않았습니다"
/// (<c>0x00533228</c>, 부르는 곳 <c>0x00426018</c>).
/// </remarks>
public sealed class ContractDialog : Window
{
    /// <summary>화면 바탕. 보급 화면과 같은 밤색 판이다.</summary>
    private static readonly Brush Back = Frozen(Color.FromRgb(0x31, 0x18, 0x18));

    /// <summary>테를 두르는 짙은 선.</summary>
    private static readonly Brush Line = Frozen(Color.FromRgb(0x11, 0x09, 0x09));

    /// <summary>글꼴 조각을 못 읽었을 때 물러설 글씨색.</summary>
    private static readonly Brush Ink = Frozen(Color.FromRgb(0xCB, 0xC5, 0xC5));

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    /// <summary>글이 놓이는 판의 크기. 발견물·증거품 칸이 게임처럼 넉넉히 비도록 못 박는다.</summary>
    private const double BoardWidth = 560, BoardHeight = 420;

    /// <summary>발견물·증거품 칸에 비워 두는 높이. 게임도 이만큼씩 띄운다.</summary>
    private const double ListHeight = 96;

    private ContractDialog(Contract contract, DateTime today, string title,
                           IReadOnlyList<string> found, IReadOnlyList<string> evidence)
    {
        Title = "계약 정보";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = Back;

        var rows = new StackPanel();
        rows.Children.Add(Label($"   스폰서  {contract.Sponsor}"));
        if (contract.City.Length > 0) rows.Children.Add(Label($"     마을  {contract.City}"));

        rows.Children.Add(Gap());
        rows.Children.Add(Label($"   계약금  {contract.Amount,8}닢"));
        rows.Children.Add(Label($"     선금  {contract.Advance,8}닢    계약 기한"));
        rows.Children.Add(Label($"     미불  {contract.Unpaid,8}닢      {Deadline(contract, today)}"));

        rows.Children.Add(Gap());
        rows.Children.Add(Label("   발견물"));
        rows.Children.Add(List(found, ListHeight));
        rows.Children.Add(Label("   증거품"));
        rows.Children.Add(List(evidence, ListHeight));

        var head = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 6) };
        var close = CloseBox();
        DockPanel.SetDock(close, Dock.Right);
        head.Children.Add(close);
        head.Children.Add(Label(title));

        var board = new DockPanel
        {
            Width = BoardWidth,
            Height = BoardHeight,
            Margin = new Thickness(14, 10, 14, 2),
            LastChildFill = false,
        };
        DockPanel.SetDock(head, Dock.Top);
        board.Children.Add(head);
        DockPanel.SetDock(rows, Dock.Top);
        board.Children.Add(rows);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(10, 0, 10, 10),
        };
        buttons.Children.Add(new GameButton("취소", Close));

        var page = new StackPanel();
        page.Children.Add(board);
        page.Children.Add(buttons);

        var frame = GameUi.InfoFrame(page, Back, Line);
        GameUi.EnableDrag(this, frame);
        Content = frame;

        KeyDown += (_, e) => { if (e.Key is Key.Escape or Key.Enter) Close(); };
        MouseRightButtonUp += (_, _) => Close();
    }

    /// <summary>
    /// 기한 칸의 글. 지났으면 그렇다고 적고, 아니면 "나머지 2년" · "나머지 5개월" 이다.
    /// </summary>
    /// <remarks>
    /// 햇수가 남았으면 달수는 0 일 때 안 적는다 — "나머지 2년" 이지 "나머지 2년 0개월" 이
    /// 아니다. 햇수가 0 이면 달수만 적는다.
    /// </remarks>
    private static string Deadline(Contract contract, DateTime today)
    {
        if (contract.DaysLeft(today) <= 0) return "기한이 지났습니다";

        var (years, months) = contract.Remaining(today);
        string text = "나머지";
        if (years > 0) text += $" {years,2}년";
        if (years == 0 || months > 0) text += $" {months,2}개월";
        return text;
    }

    /// <summary>제목 줄 오른쪽 끝의 닫기(X). 게임 창들도 그 자리에 있다.</summary>
    private FrameworkElement CloseBox()
    {
        var box = new Border
        {
            Background = GameUi.ItemFill,
            BorderBrush = GameUi.ItemEdge,
            BorderThickness = new Thickness(2),
            Padding = new Thickness(5, 0, 5, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
            ToolTip = "닫기",
            Child = new TextBlock
            {
                Text = "✕",
                Foreground = Brushes.Black,
                FontWeight = FontWeights.Bold,
                FontSize = 13,
            },
        };
        // 누름은 삼킨다 — 판 끌기가 먼저 걸리면 마우스를 잡아 버려 뗌이 안 온다.
        box.MouseLeftButtonDown += (_, e) => e.Handled = true;
        box.MouseLeftButtonUp += (_, e) => { e.Handled = true; Close(); };
        return box;
    }

    /// <summary>줄 사이를 띄우는 빈 칸. 게임도 묶음 사이를 한 줄만큼 띄운다.</summary>
    private static UIElement Gap() => new Border { Height = 10 };

    /// <summary>이름을 죽 늘어놓는 칸. 비어 있으면 자리만 비워 둔다(게임도 그렇다).</summary>
    private static UIElement List(IReadOnlyList<string> names, double height)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 2, 0, 0) };
        foreach (var name in names) stack.Children.Add(Label($"      {name}"));

        return new Border
        {
            Height = height,
            Child = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = stack,
            },
        };
    }

    /// <summary>
    /// 밤색 판 위에 얹는 밝은 글씨. 줄이 세로로 쌓이므로 <b>왼쪽에 붙여</b> 둔다 — 그냥 두면
    /// 칸이 가로로 늘어나 글자가 가운데로 간다.
    /// </summary>
    private static GameUi.GameLabel Label(string text) => new(GameFont.WhiteColor)
    {
        Text = text,
        FallbackBrush = Ink,
        HorizontalAlignment = HorizontalAlignment.Left,
    };

    /// <summary>
    /// 계약 정보 창을 연다. 계약이 없으면 게임처럼 한 줄로 물린다.
    /// </summary>
    /// <param name="hintName">제목에 쓸 힌트 이름.</param>
    /// <param name="found">이 계약을 맺은 뒤 발견한 것의 이름.</param>
    /// <param name="evidence">그 발견물이 준 물건 중 아직 지닌 것의 이름.</param>
    public static void Show(Window owner, Contract? contract, DateTime today,
                            string hintName,
                            IReadOnlyList<string> found, IReadOnlyList<string> evidence)
    {
        if (contract == null)
        {
            NoticeDialog.Show(owner, "계약을 맺지 않았습니다");
            return;
        }

        new ContractDialog(contract, today, hintName, found, evidence) { Owner = owner }
            .ShowDialog();
    }
}
