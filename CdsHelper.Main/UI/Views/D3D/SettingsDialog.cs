using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Support.Local.Helpers;
using CdsHelper.Support.Local.Settings;

namespace CdsHelper.Main.UI.Views.D3D;

/// <summary>
/// 함대 창 설정. 지금은 배경음악을 켜고 끄는 것 하나다.
/// </summary>
/// <remarks>
/// 켜고 끈 것은 <see cref="AppSettings.BgmEnabled"/> 에 적혀 다음에 켤 때도 그대로다.
/// </remarks>
public sealed class SettingsDialog : Window
{
    private readonly BgmPlayer _bgm;
    private readonly Border _toggle;

    private SettingsDialog(BgmPlayer bgm)
    {
        _bgm = bgm;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        _toggle = GameUi.MenuItem(Label(), Flip);

        var title = GameUi.TitleBar("설정", Close);
        GameUi.EnableDrag(this, title);

        var stack = new StackPanel();
        stack.Children.Add(title);
        stack.Children.Add(new Border
        {
            Padding = new Thickness(8, 8, 8, 4),
            Child = _toggle,
        });
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

    /// <summary>켜고 끄기를 뒤집는다 — 곡은 그 자리에서 멈추거나 다시 돈다.</summary>
    private void Flip()
    {
        AppSettings.BgmEnabled = !AppSettings.BgmEnabled;
        _bgm.Enabled = AppSettings.BgmEnabled;
        ((TextBlock)_toggle.Child).Text = Label();
    }

    public static void Show(Window owner, BgmPlayer bgm) =>
        new SettingsDialog(bgm) { Owner = owner }.ShowDialog();
}
