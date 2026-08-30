using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 명령 창 하나를 담아 제 창(HWND)으로 띄운다. 도시 그림 창 옆에 붙여 놓으려고 쓴다 —
/// 그림 안에 그리면 그림이 작을 때 창을 꽉 채워 버린다(게임도 그림 옆에 따로 띄운다).
/// </summary>
public sealed class MenuWindow : Window
{
    private readonly Border _root;

    /// <summary>
    /// 담고 있는 것을 갈아 끼운다. 시설 명령 창에서 "기능" 처럼 한 창 안에서 줄이 바뀔 때 쓴다 —
    /// 창을 새로 띄우면 자리가 튀어 보인다.
    /// </summary>
    public void SetContent(UIElement content) => _root.Child = content;

    /// <summary>
    /// 줄이 바뀌어 창 크기가 달라졌을 때 다시 한가운데로 민다.
    /// </summary>
    /// <remarks>
    /// 게임은 창을 낼 때마다 <c>원점 + 크기/2</c> 를 다시 잰다(<c>0x00469E80</c>).
    /// 우리는 창을 그대로 두고 알맹이만 갈아 끼우므로, 열한 줄짜리 자택 차림표에서
    /// 네 줄짜리 <b>기능</b> 으로 들어가면 <b>왼쪽 위에 그대로 걸려</b> 있었다.
    /// </remarks>
    /// <summary>정해 준 자리로 옮긴다. 화면 밖으로는 안 나간다.</summary>
    public void MoveTo(Point at)
    {
        UpdateLayout();
        Left = Math.Max(0, Math.Min(at.X, SystemParameters.VirtualScreenWidth - ActualWidth));
        Top = Math.Max(0, Math.Min(at.Y, SystemParameters.VirtualScreenHeight - ActualHeight));
    }

    public void Recenter()
    {
        if (Owner is not { } owner) return;
        UpdateLayout();
        Left = Math.Max(0, owner.Left + (owner.Width - ActualWidth) / 2);
        Top = Math.Max(0, owner.Top + (owner.Height - ActualHeight) / 2);
    }

    private MenuWindow(UIElement content)
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        ShowInTaskbar = false;
        // <b>레이어드 창으로 두지 않는다.</b> AllowsTransparency 를 켜면 창이 소프트웨어로
        // 합성되어, 겹쳐 있는 창들 사이에서 활성 창이 오갈 때마다 통째로 다시 그려진다 —
        // 대화 창을 여닫을 때 화면이 깜빡이던 것이 그것이다. 이 창은 네모지고 속이 꽉
        // 차 있어 비침이 필요 없다.
        Background = GameUi.Back;

        _root = new Border { Background = GameUi.Back, Child = content };
        Content = _root;
        GameUi.EnableDrag(this, _root);
        GameUi.CarryOwnedWindows(this);   // 이 창에서 연 창(힌트 일람 따위)도 같이 옮긴다

        KeyDown += (_, e) => { if (e.Key is Key.Escape) Close(); };
        MouseRightButtonUp += (_, _) => Close();

        // <b>닫기 전에 주인 창을 먼저 띄운다.</b> 창을 부수고 나서 초점을 정하게 두면
        // 윈도가 다음 창을 z 차례에서 고르는데, 우리 창들은 테 없는 데다 작업표시줄에도
        // 안 나와서 <b>다른 앱으로 새어 나간다</b> — 도서관에서 나올 때 편집기나 터미널이
        // 잠깐 앞으로 나왔다 들어오던 것이 그것이다. 부수기 전에 주인을 띄워 두면
        // 고를 것이 이미 정해져 있어 샐 일이 없다.
        Closing += (_, _) => Owner?.Activate();

        // 그래도 새면 붙들어 온다(위 한 줄로 안 잡히는 자리가 남아 있을 수 있다).
        FocusWatch.KeepInApp(this);

        // 초점이 어디로 가는지 보려고 둔 진단(FocusWatch). 다 잡고 나면 지운다.
        Closed += (_, _) => FocusWatch.After("명령창 닫힘");
        Deactivated += (_, _) => FocusWatch.After("명령창 초점 잃음");
    }

    /// <summary>
    /// 주인 창 <b>한가운데</b>에 띄운다. 게임이 시설 명령 창을 내는 자리다.
    /// </summary>
    /// <remarks>
    /// 게임은 누른 건물과 상관없이 늘 그리는 영역 한가운데에 낸다 — 메뉴 객체의 자리가
    /// 음수(<c>POINT(-1,-1)</c> = 안 정함)면 <c>원점 + 크기/2</c> 로 잡고, 한 번 잡으면
    /// 그 자리를 계속 쓴다(<c>0x00469E80</c>, 볼트 <c>15.분석-시설 화면 엔진</c>).
    ///
    /// 예전에는 주인 창 오른쪽에 붙여 냈는데 게임에는 없는 모습이었다.
    /// </remarks>
    public static MenuWindow ShowCentered(Window owner, UIElement content)
    {
        var window = new MenuWindow(content) { Owner = owner };
        window.Show();      // 크기를 알아야 자리를 잡는다

        window.Left = Math.Max(0, owner.Left + (owner.Width - window.ActualWidth) / 2);
        window.Top = Math.Max(0, owner.Top + (owner.Height - window.ActualHeight) / 2);
        return window;
    }

    /// <summary>
    /// 정해 준 자리(화면 좌표, WPF 단위)에 왼쪽 위 모서리를 맞춰 띄운다. 상단 띠에서 부르는
    /// 도시정보 창처럼 누른 자리 밑에 붙여야 하는 것에 쓴다. 화면 밖으로는 안 나가게 민다.
    /// </summary>
    public static MenuWindow ShowAt(Window owner, UIElement content, Point at)
    {
        var window = new MenuWindow(content) { Owner = owner };
        window.Show();      // 크기를 알아야 화면 안으로 밀어 넣을 수 있다

        window.Left = Math.Max(0, Math.Min(at.X, SystemParameters.VirtualScreenWidth - window.ActualWidth));
        window.Top = Math.Max(0, Math.Min(at.Y, SystemParameters.VirtualScreenHeight - window.ActualHeight));
        return window;
    }
}
