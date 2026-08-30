using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Support.Local.Models;
using CdsHelper.Support.Local.Settings;
using CdsHelper.Game.Local.Settings;

namespace CdsHelper.Game.UI.Views;

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

    /// <summary>개발 창이 만지는 것들. 늘어나서 묶어 두었다.</summary>
    public sealed class Options
    {
        /// <summary>좌표 겹쳐 보기.</summary>
        public Func<bool> CoordsOn { get; init; } = () => false;
        public Action<bool> SetCoords { get; init; } = _ => { };

        /// <summary>지도 위의 까만 조작 줄(체크상자·안내 글).</summary>
        public Func<bool> ToolBarOn { get; init; } = () => false;
        public Action<bool> SetToolBar { get; init; } = _ => { };

        /// <summary>게임 폴더. 화면 조각을 뽑을 때 쓴다.</summary>
        public string GameDirectory { get; init; } = "";
    }

    private readonly string _gameDirectory;

    private DevDialog(Player player, Options options)
    {
        _player = player;
        _gameDirectory = options.GameDirectory;

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
        rows.Children.Add(HurtRow());
        rows.Children.Add(SpouseRow());

        // 좌표 겹쳐 보기 — 배가 선 자리를 WORLD.CDS 의 칸·파일 오프셋까지 지도 위에 띄운다.
        // 놀이에는 없는 것이라 이 창으로 옮겨 두었다.
        rows.Children.Add(Toggle("좌표 겹쳐 보기", options.CoordsOn(), options.SetCoords,
            "배가 선 자리를 WORLD.CDS 의 칸·파일 오프셋까지 지도 위에 띄웁니다"));

        rows.Children.Add(Toggle("조작 줄 보기", options.ToolBarOn(), options.SetToolBar,
            "지도 위의 까만 줄 — 커서로 몰기·화면 따라가기 같은 개발용 단추들입니다"));

        // 게임 창 단추의 좌우 여백. 띠 마구리(양 끝 조각)가 앉을 자리다 — 크게 잡으면
        // 글자에서 멀어지고 작게 잡으면 글자가 마구리 위로 올라앉는다.
        rows.Children.Add(Tune("단추 여백", GameSettings.BandPad,
            GameSettings.MinBandPad, GameSettings.MaxBandPad, v => GameSettings.BandPad = v,
            $"게임 창 단추의 좌우 여백(점). 띠 마구리는 실제로 16점입니다. 기본값 {GameSettings.DefaultBandPad}."
            + " 바꾼 값은 다음에 여는 창부터 듭니다."));

        // 화면 조각을 PNG 로 뽑아 asset/ui 에 넣는다 — 손으로 다듬으려면 그림 파일이 있어야 한다.
        var dump = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 10, 0, 4),
        };
        dump.Children.Add(new TextBlock
        {
            Text = "화면 조각",
            Width = 64,
            Foreground = GameUi.Text,
            FontWeight = FontWeights.Bold,
            FontSize = 15,
            VerticalAlignment = VerticalAlignment.Center,
        });
        dump.Children.Add(GameUi.PushButton("MISC.CDS 뽑기", () =>
        {
            string result = _gameDirectory.Length == 0
                ? "게임 폴더를 아직 모릅니다"
                : UiSpriteDump.Run(_gameDirectory);
            NoticeDialog.Show(this, result);
        }, 180));
        rows.Children.Add(dump);

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

    /// <summary>
    /// 아내를 붙였다 뗀다 — 자택 "후손을 남긴다" 줄이 아내가 있어야 눌린다.
    /// </summary>
    /// <remarks>
    /// 놀이에는 없는 줄이다. 게임에서 아내를 맞는 길(여관·술집 사건)을 아직 안 옮겨서,
    /// 그 줄을 눌러 보려면 여기서 붙여 주는 수밖에 없다.
    /// </remarks>
    private UIElement SpouseRow()
    {
        var line = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 4, 0, 4),
        };

        var shown = new TextBlock
        {
            Width = 120,
            Foreground = GameUi.Text,
            FontSize = 15,
            VerticalAlignment = VerticalAlignment.Center,
        };
        void Paint() => shown.Text = _player.Spouse.Length > 0 ? _player.Spouse : "— 없음";
        Paint();

        line.Children.Add(new TextBlock
        {
            Text = "아내",
            Width = 64,
            Foreground = GameUi.Text,
            FontWeight = FontWeights.Bold,
            FontSize = 15,
            VerticalAlignment = VerticalAlignment.Center,
        });
        line.Children.Add(GameUi.PushButton("맞는다", () =>
        {
            _player.Marry("카타리나");
            Paint();
        }, 96));
        line.Children.Add(GameUi.PushButton("없앤다", () => { _player.Marry(""); Paint(); }, 96));
        line.Children.Add(shown);
        return line;
    }

    /// <summary>
    /// 배를 조금 상하게 하는 줄. 조선소 수리를 시험하려고 둔다 — 놀이 안에서 배를 상하게 하는
    /// 것은 폭풍뿐이라, 위도 띠까지 배를 몰지 않고도 손상을 만들 수 있게 남겨 둔다.
    /// </summary>
    private UIElement HurtRow()
    {
        var line = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 4, 0, 4),
        };

        line.Children.Add(new TextBlock
        {
            Text = "배 손상",
            Width = 64,
            Foreground = GameUi.Text,
            FontWeight = FontWeights.Bold,
            FontSize = 15,
            VerticalAlignment = VerticalAlignment.Center,
        });

        line.Children.Add(GameUi.PushButton("모두 -5", () =>
        {
            foreach (var ship in _player.Ships) ship.Hurt(5);
        }, 96));
        line.Children.Add(GameUi.PushButton("모두 고침", () =>
        {
            foreach (var ship in _player.Ships) ship.Repair();
            _player.SetFatigue(0);
            _player.SetMorale(Player.MaxMorale);
        }, 96));
        line.Children.Add(GameUi.PushButton("항해 20일", () =>
        {
            _player.SetDaysAtSea(20);
        }, 96));
        line.Children.Add(GameUi.PushButton("피로 90", () =>
        {
            _player.SetFatigue(90);
        }, 96));
        return line;
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

    /// <summary>
    /// 수를 하나 맞추는 줄. 단추로 한 칸씩 움직이고 칸에 적어 넣어도 된다.
    /// <see cref="Row"/> 와 달리 놀이 값이 아니라 <b>설정</b>을 만지므로 따로 둔다.
    /// </summary>
    private static UIElement Tune(string label, int value, int min, int max,
                                  Action<int> set, string tip)
    {
        var box = Field();
        box.Width = 60;
        box.Text = value.ToString();

        var line = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 8, 0, 2),
            ToolTip = tip,
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

        void Put(int v)
        {
            int clamped = Math.Clamp(v, min, max);
            set(clamped);
            box.Text = clamped.ToString();
        }

        line.Children.Add(GameUi.PushButton("-1", () => Put(Current(box) - 1), 48));
        line.Children.Add(box);
        line.Children.Add(GameUi.PushButton("+1", () => Put(Current(box) + 1), 48));

        box.LostFocus += (_, _) => Put(Current(box));
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Put(Current(box)); };
        return line;
    }

    /// <summary>켜고 끄는 줄 하나.</summary>
    private static CheckBox Toggle(string label, bool on, Action<bool> set, string tip)
    {
        var box = new CheckBox
        {
            Content = label,
            IsChecked = on,
            Foreground = GameUi.Text,
            FontWeight = FontWeights.Bold,
            FontSize = 15,
            Margin = new Thickness(0, 8, 0, 2),
            VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = tip,
        };
        box.Checked += (_, _) => set(true);
        box.Unchecked += (_, _) => set(false);
        return box;
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

        box.SelectedIndex = Array.FindIndex(choices, c => c.Effect == GameSettings.CityOpenEffect);
        if (box.SelectedIndex < 0) box.SelectedIndex = 0;

        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedItem is ComboBoxItem { Tag: CityOpenEffect picked })
                GameSettings.CityOpenEffect = picked;
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

    public static void Show(Window owner, Player player, Options options) =>
        new DevDialog(player, options) { Owner = owner }.ShowDialog();
}
