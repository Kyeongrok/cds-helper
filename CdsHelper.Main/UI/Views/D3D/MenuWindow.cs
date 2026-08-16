using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace CdsHelper.Main.UI.Views.D3D;

/// <summary>
/// 명령 창 하나를 담아 제 창(HWND)으로 띄운다. 도시 그림 창 옆에 붙여 놓으려고 쓴다 —
/// 그림 안에 그리면 그림이 작을 때 창을 꽉 채워 버린다(게임도 그림 옆에 따로 띄운다).
/// </summary>
public sealed class MenuWindow : Window
{
    private MenuWindow(UIElement content)
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        ShowInTaskbar = false;
        Background = Brushes.Transparent;
        AllowsTransparency = true;

        var root = new Border { Background = GameUi.Back, Child = content };
        Content = root;
        GameUi.EnableDrag(this, root);

        KeyDown += (_, e) => { if (e.Key is Key.Escape) Close(); };
        MouseRightButtonUp += (_, _) => Close();
    }

    /// <summary>
    /// 주인 창의 오른쪽에 붙여 띄운다. 화면 밖으로 나가면 왼쪽으로 접어 넣는다.
    /// </summary>
    public static MenuWindow ShowBeside(Window owner, UIElement content)
    {
        var window = new MenuWindow(content) { Owner = owner };
        window.Show();      // 크기를 알아야 자리를 잡는다

        double left = owner.Left + owner.Width + 6;
        if (left + window.ActualWidth > SystemParameters.VirtualScreenWidth)
            left = Math.Max(0, owner.Left - window.ActualWidth - 6);

        window.Left = left;
        window.Top = Math.Max(0, owner.Top + (owner.Height - window.ActualHeight) / 2);
        return window;
    }
}
