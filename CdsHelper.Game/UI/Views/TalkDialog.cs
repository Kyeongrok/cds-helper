using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 인물이 말하는 창 — 왼쪽에 얼굴, 오른쪽에 대사. 게임의 인물 대사 창(<c>0x004692E0</c>)과
/// 같은 모양이다.
/// </summary>
/// <remarks>
/// 얼굴은 MALE.CDS · FEMALE.CDS 에서 온다(<see cref="Portraits"/>, 80x96 도트 그림).
/// 얼굴을 못 구하면 대사만 낸다 — 그림이 없다고 말까지 막을 일은 아니다.
///
/// 물어보는 창(승낙/교섭 따위)으로도 쓸 수 있다. <paramref name="choices"/> 를 주면
/// 그 단추들이 서고, 고른 자리를 낸다.
/// </remarks>
public sealed class TalkDialog : Window
{
    /// <summary>
    /// 게임 대사 창을 재어 맞춘 자리들(그림 점). 얼굴은 <b>1배</b>로 놓는다 —
    /// 두 배로 걸면 얼굴만 커져 글자와 단추가 딸려 보인다.
    /// </summary>
    /// <remarks>
    /// 화면(1.775배로 늘어난 갈무리)에서 잰 값이다. 얼굴 142x169 는 80x96 의 1.775배이고,
    /// 창 높이 278 은 <c>14 + 96 + 8 + 24 + 14</c> 의 1.775배로 딱 떨어진다.
    /// 글자는 얼굴과 <b>윗선</b>이 맞는다 — 가운데로 맞추지 않는다.
    /// </remarks>
    private const double Pad = 14, FaceGap = 20, ButtonGap = 8;

    /// <summary>확인 단추 폭. 게임 것은 마구리 둘에 가운데 넉 칸이다(16+8*4+16).</summary>
    private const double OkWidth = 64;

    private int _picked = -1;
    private readonly GameUi.FocusGroup _focus = new();

    private TalkDialog(uint[]? face, string speaker, string text, string[] choices)
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        var line = new DockPanel { LastChildFill = true, Margin = new Thickness(Pad, Pad, Pad, 0) };

        if (face != null)
        {
            var picture = BitmapSource.Create(Portraits.Width, Portraits.Height, 96, 96,
                                              PixelFormats.Bgra32, null, face, Portraits.Width * 4);
            picture.Freeze();
            // 테를 두르지 않는다 — 게임은 얼굴을 창 바탕에 그대로 얹는다.
            var image = new Image
            {
                Source = picture,
                Width = Portraits.Width,
                Height = Portraits.Height,
                Stretch = Stretch.Fill,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 0, FaceGap, 0),
            };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);

            DockPanel.SetDock(image, Dock.Left);
            line.Children.Add(image);
        }

        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
        if (speaker.Length > 0)
            words.Children.Add(new TextBlock
            {
                Text = speaker,
                Foreground = GameUi.Edge,
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                Margin = new Thickness(0, 0, 0, 6),
            });
        words.Children.Add(new TextBlock
        {
            Text = text,
            Foreground = GameUi.Text,
            FontWeight = FontWeights.Bold,
            FontSize = 16,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 420,
        });
        line.Children.Add(words);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, ButtonGap, 0, Pad),
        };
        if (choices.Length == 0)
        {
            buttons.Children.Add(_focus.Add("확인", Close, OkWidth));
        }
        else
        {
            for (int i = 0; i < choices.Length; i++)
            {
                int index = i;
                buttons.Children.Add(_focus.Add(choices[i], () => { _picked = index; Close(); }, 110));
            }
        }

        var stack = new StackPanel();
        stack.Children.Add(line);
        stack.Children.Add(buttons);

        // 게임 창은 밝은 선 한 점만 두른다 — 두 점으로 두르면 그 선이 먼저 눈에 든다.
        var root = new Border
        {
            Background = GameUi.Back,
            BorderBrush = GameUi.Edge,
            BorderThickness = new Thickness(1),
            Child = stack,
        };
        Content = root;
        GameUi.EnableDrag(this, root);
        GameUi.CarryOwnedWindows(this);

        KeyDown += (_, e) =>
        {
            if (e.Key is Key.Escape) { Close(); return; }
            if (_focus.HandleKey(e.Key)) e.Handled = true;
        };
        MouseRightButtonUp += (_, _) => Close();
    }

    /// <summary>얼굴을 띄우고 한마디 한다. 확인만 받는다.</summary>
    public static void Say(Window owner, uint[]? face, string speaker, string text) =>
        new TalkDialog(face, speaker, text, []) { Owner = owner }.ShowDialog();

    /// <summary>
    /// 얼굴을 띄우고 물어본다. 고른 자리를 내고, 그냥 닫으면 -1 이다.
    /// </summary>
    public static int Ask(Window owner, uint[]? face, string speaker, string text,
                          params string[] choices)
    {
        var dlg = new TalkDialog(face, speaker, text, choices) { Owner = owner };
        dlg.ShowDialog();
        return dlg._picked;
    }
}
