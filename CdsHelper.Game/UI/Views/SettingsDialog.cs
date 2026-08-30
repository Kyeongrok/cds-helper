using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Settings;
using CdsHelper.Game.Local.Settings;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 함대 창 설정. 배경음악과 효과음을 따로 켜고 끈다.
/// </summary>
/// <remarks>
/// 켜고 끈 것은 <see cref="GameSettings.BgmEnabled"/> 에 적혀 다음에 켤 때도 그대로다.
/// </remarks>
public sealed class SettingsDialog : Window
{
    private readonly BgmPlayer _bgm;

    /// <summary>켜고 끄는 칸이 앉는 자리. 뒤집을 때마다 칸을 통째로 갈아 끼운다.</summary>
    private readonly Border _bgmRow = new() { Padding = new Thickness(8, 8, 8, 2) };
    private readonly Border _sfxRow = new() { Padding = new Thickness(8, 2, 8, 2) };
    private readonly Border _bgmVolRow = new() { Padding = new Thickness(8, 2, 8, 2) };
    private readonly Border _sfxVolRow = new() { Padding = new Thickness(8, 2, 8, 4) };

    private SettingsDialog(BgmPlayer bgm)
    {
        _bgm = bgm;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        _bgmRow.Child = BgmToggle();
        _sfxRow.Child = SfxToggle();
        _bgmVolRow.Child = VolumeRow("배경음악", GameSettings.BgmVolume, StepBgm);
        _sfxVolRow.Child = VolumeRow("효과음  ", GameSettings.SfxVolume, StepSfx);

        var title = GameUi.TitleBar("설정", Close);
        GameUi.EnableDrag(this, title);

        var stack = new StackPanel();
        stack.Children.Add(title);
        stack.Children.Add(_bgmRow);
        stack.Children.Add(_bgmVolRow);
        stack.Children.Add(_sfxRow);
        stack.Children.Add(_sfxVolRow);
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

    /// <summary>줄 글자. 두 줄의 폭이 어긋나지 않게 이름을 같은 길이로 맞춰 둔다.</summary>
    private static string Label(string name, bool on) => $"{name}   {(on ? "켬" : "끔")}";

    private UIElement BgmToggle() =>
        new GameButton(Label("배경음악", GameSettings.BgmEnabled), FlipBgm);

    private UIElement SfxToggle() =>
        new GameButton(Label("효과음  ", GameSettings.SfxEnabled), FlipSfx);

    /// <summary>켜고 끄기를 뒤집는다 — 곡은 그 자리에서 멈추거나 다시 돈다.</summary>
    /// <remarks>
    /// 글자만 갈아 끼우지 않고 칸을 <b>다시 짓는다</b>. 칸의 속 모양이 한 가지가 아니기
    /// 때문이다 — 게임 원본 조각을 읽었으면 띠 그림 위에 비트맵 글씨를 얹은 <c>Grid</c> 가
    /// 들어 있고, 못 읽었을 때만 <c>TextBlock</c> 이다. 속을 아는 척하고 형변환하면
    /// 원본 조각이 있는 자리에서 반드시 깨진다.
    /// </remarks>
    private void FlipBgm()
    {
        GameSettings.BgmEnabled = !GameSettings.BgmEnabled;
        _bgm.Enabled = GameSettings.BgmEnabled;
        _bgmRow.Child = BgmToggle();
    }

    /// <summary>
    /// 효과음을 켜고 끈다. 배경음악과 따로 논다 — 곡은 두고 소리만 끄고 싶을 때가 있다.
    /// 실제로 가르는 자리는 <see cref="SoundBank.Play"/> 다.
    /// </summary>
    private void FlipSfx()
    {
        GameSettings.SfxEnabled = !GameSettings.SfxEnabled;
        _sfxRow.Child = SfxToggle();
    }

    /// <summary>소리 크기 한 줄 — <c>◀ 100 ▶</c> 로 열씩 오르내린다.</summary>
    private static UIElement VolumeRow(string name, int volume, Action<int> step)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new GameButton("◀", () => step(-GameSettings.VolumeStep),
                                        BandStyle.Button, StepWidth));
        row.Children.Add(new GameButton($"{name} {volume,3}", null, BandStyle.Button, NumberWidth));
        row.Children.Add(new GameButton("▶", () => step(GameSettings.VolumeStep),
                                        BandStyle.Button, StepWidth));
        return row;
    }

    /// <summary>소리 크기 줄의 칸 폭.</summary>
    private const double StepWidth = 32, NumberWidth = 120;

    private void StepBgm(int by)
    {
        GameSettings.BgmVolume += by;
        _bgm.Volume = GameSettings.BgmVolume / (double)GameSettings.MaxVolume;
        _bgmVolRow.Child = VolumeRow("배경음악", GameSettings.BgmVolume, StepBgm);
    }

    private void StepSfx(int by)
    {
        GameSettings.SfxVolume += by;
        _sfxVolRow.Child = VolumeRow("효과음  ", GameSettings.SfxVolume, StepSfx);
    }

    public static void Show(Window owner, BgmPlayer bgm) =>
        new SettingsDialog(bgm) { Owner = owner }.ShowDialog();
}
