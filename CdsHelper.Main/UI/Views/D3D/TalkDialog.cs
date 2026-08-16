using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Support.Local.Helpers;

namespace CdsHelper.Main.UI.Views.D3D;

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
    private int _picked = -1;

    private TalkDialog(uint[]? face, string speaker, string text, string[] choices)
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        var line = new DockPanel { LastChildFill = true, Margin = new Thickness(14, 14, 18, 10) };

        if (face != null)
        {
            var picture = BitmapSource.Create(Portraits.Width, Portraits.Height, 96, 96,
                                              PixelFormats.Bgra32, null, face, Portraits.Width * 4);
            picture.Freeze();
            var image = new Image
            {
                Source = picture,
                // 도트 그림이라 정수배로만 늘린다. 그대로는 작아서 두 배로 건다.
                Width = Portraits.Width * 2,
                Height = Portraits.Height * 2,
                Stretch = Stretch.Fill,
                VerticalAlignment = VerticalAlignment.Top,
            };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);

            var framed = new Border
            {
                BorderBrush = GameUi.Edge,
                BorderThickness = new Thickness(2),
                Margin = new Thickness(0, 0, 14, 0),
                VerticalAlignment = VerticalAlignment.Top,
                Child = image,
            };
            DockPanel.SetDock(framed, Dock.Left);
            line.Children.Add(framed);
        }

        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
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
            Margin = new Thickness(0, 4, 0, 14),
        };
        if (choices.Length == 0)
        {
            buttons.Children.Add(GameUi.PushButton("확인", Close, 96));
        }
        else
        {
            for (int i = 0; i < choices.Length; i++)
            {
                int index = i;
                buttons.Children.Add(GameUi.PushButton(choices[i], () => { _picked = index; Close(); }, 110));
            }
        }

        var stack = new StackPanel();
        stack.Children.Add(line);
        stack.Children.Add(buttons);

        var root = new Border
        {
            Background = GameUi.Back,
            BorderBrush = GameUi.Edge,
            BorderThickness = new Thickness(2),
            Margin = new Thickness(4),
            Child = stack,
        };
        Content = root;
        GameUi.EnableDrag(this, root);
        GameUi.CarryOwnedWindows(this);

        KeyDown += (_, e) => { if (e.Key is Key.Escape) Close(); };
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
