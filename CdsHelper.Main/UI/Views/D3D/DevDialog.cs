using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Support.Local.Models;
using CdsHelper.Support.Local.Settings;

namespace CdsHelper.Main.UI.Views.D3D;

/// <summary>
/// 개발용 창 — 소지금과 명성을 손으로 올리고 내린다.
/// </summary>
/// <remarks>
/// 놀이에는 없는 창이다. 명성이 오르는 길(발견물 발표)이나 돈이 도는 길(교역)을 아직
/// 흉내내지 않아서, 후원자 알현처럼 값이 있어야 볼 수 있는 것들을 시험하려면 손으로
/// 넣어 줄 데가 필요하다.
///
/// 늘리고 줄이는 단추는 정해진 폭으로 움직이고, 칸에 수를 적어 넣으면 그 값이 그대로 된다.
/// 값은 0 밑으로 안 내려간다.
/// </remarks>
public sealed class DevDialog : Window
{
    /// <summary>단추 한 번에 움직이는 폭.</summary>
    private const int GoldStep = 10000, FameStep = 500;

    private readonly Player _player;
    private readonly TextBox _gold = Field();
    private readonly TextBox _fame = Field();

    private DevDialog(Player player, Func<bool> coordsOn, Action<bool> setCoords)
    {
        _player = player;

        Title = "개발";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        var rows = new StackPanel { Margin = new Thickness(12, 10, 12, 4) };
        rows.Children.Add(Row("소지금", _gold, GoldStep, v => _player.SetGold(v)));
        rows.Children.Add(Row("명성", _fame, FameStep, v => _player.Fame = v));
        rows.Children.Add(EffectRow());

        // 좌표 겹쳐 보기 — 배가 선 자리를 WORLD.CDS 의 칸·파일 오프셋까지 지도 위에 띄운다.
        // 놀이에는 없는 것이라 이 창으로 옮겨 두었다.
        var coords = new CheckBox
        {
            Content = "좌표 겹쳐 보기",
            IsChecked = coordsOn(),
            Foreground = GameUi.Text,
            FontWeight = FontWeights.Bold,
            FontSize = 15,
            Margin = new Thickness(0, 10, 0, 4),
            VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = "배가 선 자리를 WORLD.CDS 의 칸·파일 오프셋까지 지도 위에 띄웁니다",
        };
        coords.Checked += (_, _) => setCoords(true);
        coords.Unchecked += (_, _) => setCoords(false);
        rows.Children.Add(coords);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 12),
        };
        buttons.Children.Add(GameUi.PushButton("닫기", Close, 96));

        var title = GameUi.TitleBar("개발", Close);
        GameUi.EnableDrag(this, title);

        var stack = new StackPanel();
        stack.Children.Add(title);
        stack.Children.Add(rows);
        stack.Children.Add(buttons);

        Content = new Border
        {
            Background = GameUi.Back,
            BorderBrush = GameUi.Edge,
            BorderThickness = new Thickness(2),
            Margin = new Thickness(4),
            Child = stack,
        };

        Sync();
        KeyDown += (_, e) => { if (e.Key is Key.Escape) Close(); };
    }

    /// <summary>줄 하나 — 이름, 적는 칸, 늘리고 줄이는 단추.</summary>
    private UIElement Row(string label, TextBox box, int step, Action<int> set)
    {
        var line = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 4, 0, 4),
        };

        line.Children.Add(new TextBlock
        {
            Text = label,
            Width = 64,
            Foreground = GameUi.Text,
            FontWeight = FontWeights.Bold,
            FontSize = 15,
            VerticalAlignment = VerticalAlignment.Center,
        });

        void Move(int by)
        {
            set(Math.Max(0, Current(box) + by));
            Sync();
        }

        line.Children.Add(GameUi.PushButton($"-{step:N0}", () => Move(-step), 96));
        line.Children.Add(box);
        line.Children.Add(GameUi.PushButton($"+{step:N0}", () => Move(+step), 96));

        // 적어 넣은 값은 칸을 떠날 때(또는 엔터) 그대로 들어간다.
        void Apply()
        {
            set(Math.Max(0, Current(box)));
            Sync();
        }
        box.LostFocus += (_, _) => Apply();
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Apply(); };

        return line;
    }

    /// <summary>도시 창이 열릴 때 줄 효과를 고르는 줄. 고른 값은 설정에 남는다.</summary>
    private UIElement EffectRow()
    {
        var line = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 8, 0, 4),
        };

        line.Children.Add(new TextBlock
        {
            Text = "도시 열림",
            Width = 64,
            Foreground = GameUi.Text,
            FontWeight = FontWeights.Bold,
            FontSize = 15,
            VerticalAlignment = VerticalAlignment.Center,
        });

        (CityOpenEffect Effect, string Label)[] choices =
        [
            (CityOpenEffect.None, "없음"),
            (CityOpenEffect.Expand, "펼침 (가운데서 커짐)"),
            (CityOpenEffect.Slide, "넘김 (미끄러져 들어옴)"),
            (CityOpenEffect.Fade, "페이드인"),
            (CityOpenEffect.Zoom, "확대/축소 (PPT식 — 커지며 나타나고 살짝 넘침)"),
        ];

        var box = new ComboBox
        {
            Width = 300,
            Margin = new Thickness(6, 0, 6, 0),
            Padding = new Thickness(6, 3, 6, 3),
            FontWeight = FontWeights.Bold,
            FontSize = 14,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        foreach (var (effect, label) in choices)
            box.Items.Add(new ComboBoxItem { Content = label, Tag = effect });

        box.SelectedIndex = Array.FindIndex(choices, c => c.Effect == AppSettings.CityOpenEffect);
        if (box.SelectedIndex < 0) box.SelectedIndex = 0;

        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedItem is ComboBoxItem { Tag: CityOpenEffect picked })
                AppSettings.CityOpenEffect = picked;
        };

        line.Children.Add(box);
        return line;
    }

    /// <summary>칸에 적힌 수. 수가 아니면 0.</summary>
    private static int Current(TextBox box) =>
        int.TryParse(box.Text.Replace(",", "").Trim(), out int v) ? v : 0;

    /// <summary>지금 값을 칸에 다시 적는다.</summary>
    private void Sync()
    {
        _gold.Text = _player.Gold.ToString("N0");
        _fame.Text = _player.Fame.ToString("N0");
    }

    private static TextBox Field() => new()
    {
        Width = 110,
        Margin = new Thickness(6, 0, 6, 0),
        Padding = new Thickness(4, 2, 4, 2),
        FontWeight = FontWeights.Bold,
        FontSize = 14,
        TextAlignment = TextAlignment.Right,
        Background = GameUi.ItemFill,
        Foreground = Brushes.Black,
        BorderBrush = GameUi.ItemEdge,
        BorderThickness = new Thickness(2),
        VerticalContentAlignment = VerticalAlignment.Center,
    };

    public static void Show(Window owner, Player player,
                            Func<bool> coordsOn, Action<bool> setCoords) =>
        new DevDialog(player, coordsOn, setCoords) { Owner = owner }.ShowDialog();
}
