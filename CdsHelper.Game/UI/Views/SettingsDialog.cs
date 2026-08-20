using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Settings;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 함대 창 설정. 지금은 배경음악을 켜고 끄는 것 하나다.
/// </summary>
/// <remarks>
/// 켜고 끈 것은 <see cref="AppSettings.BgmEnabled"/> 에 적혀 다음에 켤 때도 그대로다.
/// </remarks>
public sealed class SettingsDialog : Window
{
    private readonly BgmPlayer _bgm;

    /// <summary>켜고 끄는 칸이 앉는 자리. 뒤집을 때마다 칸을 통째로 갈아 끼운다.</summary>
    private readonly Border _row = new() { Padding = new Thickness(8, 8, 8, 4) };

    private SettingsDialog(BgmPlayer bgm)
    {
        _bgm = bgm;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        _row.Child = Toggle();

        var title = GameUi.TitleBar("설정", Close);
        GameUi.EnableDrag(this, title);

        var stack = new StackPanel();
        stack.Children.Add(title);
        stack.Children.Add(_row);
        stack.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 10),
            Children = { GameUi.PushButton("닫기", Close) },
        });

        Content = new Border
        {
            Background = GameUi.Back,
            BorderBrush = GameUi.Edge,
            BorderThickness = new Thickness(2),
            Margin = new Thickness(4),
            Child = stack,
        };

        KeyDown += (_, e) => { if (e.Key is Key.Escape or Key.Enter) Close(); };
        MouseRightButtonUp += (_, _) => Close();
    }

    private static string Label() => $"배경음악   {(AppSettings.BgmEnabled ? "켬" : "끔")}";

    private UIElement Toggle() => GameUi.MenuItem(Label(), Flip);

    /// <summary>켜고 끄기를 뒤집는다 — 곡은 그 자리에서 멈추거나 다시 돈다.</summary>
    /// <remarks>
    /// 글자만 갈아 끼우지 않고 칸을 <b>다시 짓는다</b>. 칸의 속 모양이 한 가지가 아니기
    /// 때문이다 — 게임 원본 조각을 읽었으면 띠 그림 위에 비트맵 글씨를 얹은 <c>Grid</c> 가
    /// 들어 있고, 못 읽었을 때만 <c>TextBlock</c> 이다. 속을 아는 척하고 형변환하면
    /// 원본 조각이 있는 자리에서 반드시 깨진다.
    /// </remarks>
    private void Flip()
    {
        AppSettings.BgmEnabled = !AppSettings.BgmEnabled;
        _bgm.Enabled = AppSettings.BgmEnabled;
        _row.Child = Toggle();
    }

    public static void Show(Window owner, BgmPlayer bgm) =>
        new SettingsDialog(bgm) { Owner = owner }.ShowDialog();
}
