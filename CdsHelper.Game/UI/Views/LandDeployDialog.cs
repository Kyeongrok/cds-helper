using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Game.Engine.Land;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 육상전 <b>부대배치</b> 화면 — 여섯 자리에 부대를 놓고 「결정」을 누른다.
/// </summary>
/// <remarks>
/// 게임의 <c>0x0049E000</c>~<c>0x004A0500</c> 이다(볼트 <c>65.분석-육상전</c> 4절).
/// <code>
///   0x004A1200  부대 수 = min(6, max(1, 총인원 / 15))
///   0x004A04F0  총인원 = 선원 + 1
///   0x004A04B0  저절로 나누기 — 총인원 ÷ 부대수, 나머지는 마지막 칸에 몬다
///   0x004473B0  「결정」 검사 — 제독 부대가 없으면 물린다
/// </code>
///
/// <b>아직 못 짚은 것 둘.</b> 첫째, 배치판 그림(LANDDATA 파트 6)의 짜임을 못 풀어
/// 돌 판을 <see cref="Slab"/> 로 그린다. 둘째, <b>플레이어가 낼 수 있는 병종의 범위</b>가
/// 아직이라(볼트 11절) 제독 한 부대에 나머지는 경보병으로 둔다 — 부대 수와 나눔은
/// 게임 셈 그대로다.
/// </remarks>
internal sealed class LandDeployDialog : Window
{
    /// <summary>자리 여섯. 이름은 게임 표 <c>0x00559448</c> 차례다.</summary>
    private static readonly string[] SlotNames =
    [
        "전열 왼측", "전열 중앙", "전열 우측",
        "후열 왼측", "후열 중앙", "후열 우측",
    ];

    /// <summary>자리 수와 한 줄에 놓는 수.</summary>
    private const int SlotCount = 6, SlotsPerRow = 3;

    /// <summary>부대 하나에 드는 최소 인원 — <c>0x004A1200</c> 의 15 다.</summary>
    private const int PerUnit = 15;

    /// <summary>제독 병종 번호.</summary>
    private const int Admiral = 2;

    /// <summary>세워 둔 대신 병종 — 낼 수 있는 병종을 아직 못 짚었다.</summary>
    private const int Footman = 5;

    private readonly Engine.Game _game;
    private readonly LandArt? _art;
    private readonly int _culture;

    /// <summary>팔레트 한 벌 — 아직 안 놓은 부대들.</summary>
    private readonly List<Troop> _pool = [];

    /// <summary>자리 여섯에 놓인 것. 비었으면 null.</summary>
    private readonly Troop?[] _placed = new Troop?[SlotCount];

    private readonly Border[] _slots = new Border[SlotCount];
    private readonly StackPanel _palette = new() { Orientation = Orientation.Vertical };
    private readonly TextBlock _status = new()
    {
        Margin = new Thickness(10, 6, 10, 0),
        Foreground = GameUi.Text,
        TextWrapping = TextWrapping.Wrap,
    };

    private Troop? _picked;

    /// <summary>부대 하나 — 병종과 사람 수.</summary>
    private sealed class Troop
    {
        public int Kind { get; init; }
        public int Men { get; set; }
        public string Name =>
            Kind >= 0 && Kind < LandUnits.Names.Length ? LandUnits.Names[Kind] : $"병종 {Kind}";
    }

    private LandDeployDialog(Engine.Game game, string cityName, int culture)
    {
        _game = game;
        _culture = culture;
        _art = game.Directory.Length > 0 ? LandArt.Open(game.Directory) : null;

        Title = $"{cityName} — 부대배치";
        Width = 900;
        Height = 560;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = GameUi.Back;

        Split();
        Content = Build();
        Redraw();
    }

    /// <summary>창을 띄운다. 「결정」을 눌렀으면 참.</summary>
    public static bool Show(Window? owner, Engine.Game game, string cityName, int culture)
    {
        var window = new LandDeployDialog(game, cityName, culture);
        if (owner != null) window.Owner = owner;
        return window.ShowDialog() == true;
    }

    // ── 부대 나누기 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 부대 수와 사람 수를 게임 셈대로 나눈다.
    /// </summary>
    /// <remarks>
    /// 총인원은 <b>선원 + 1</b>(제독 자신)이고, 부대 수는 <c>min(6, max(1, 총인원/15))</c> 다.
    /// 여섯 부대를 다 쓰려면 아흔 명이 있어야 한다. 나머지는 마지막 칸에 몬다.
    /// </remarks>
    private void Split()
    {
        int men = _game.Player.Crew + 1;
        int units = Math.Clamp(men / PerUnit, 1, SlotCount);
        int each = men / units;

        for (int i = 0; i < units; i++)
            _pool.Add(new Troop
            {
                // 첫 부대가 제독이다 — 「결정」이 이것을 찾는다(0x004473B0).
                Kind = i == 0 ? Admiral : Footman,
                Men = i == units - 1 ? men - each * (units - 1) : each,
            });
    }

    // ── 화면 ───────────────────────────────────────────────────────────────────

    private UIElement Build()
    {
        var board = new UniformGrid
        {
            Rows = SlotCount / SlotsPerRow,
            Columns = SlotsPerRow,
            Margin = new Thickness(12),
        };
        for (int i = 0; i < SlotCount; i++)
        {
            int slot = i;
            _slots[i] = Slab();
            _slots[i].MouseLeftButtonUp += (_, _) => Place(slot);
            board.Children.Add(_slots[i]);
        }

        var back = GameUi.PushButton("전회", () => { DialogResult = false; Close(); }, 72);
        var decide = GameUi.PushButton("결정", Decide, 72);
        decide.Margin = new Thickness(6, 0, 0, 0);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 12, 12),
            Children = { back, decide },
        };

        var right = new DockPanel { Width = 300 };
        DockPanel.SetDock(buttons, Dock.Bottom);
        right.Children.Add(buttons);
        right.Children.Add(new ScrollViewer
        {
            Content = _palette,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(4, 12, 12, 0),
        });

        var body = new DockPanel();
        DockPanel.SetDock(right, Dock.Right);
        body.Children.Add(right);
        body.Children.Add(board);

        var page = new DockPanel();
        DockPanel.SetDock(_status, Dock.Bottom);
        page.Children.Add(_status);
        page.Children.Add(body);
        return page;
    }

    /// <summary>
    /// 자리 하나 — 게임의 돌 판이다.
    /// </summary>
    /// <remarks>
    /// LANDDATA 파트 6 의 짜임을 못 풀어 그림 대신 그린다. 풀리면 이 자리에 얹으면 된다.
    /// </remarks>
    private static Border Slab() => new()
    {
        Margin = new Thickness(6),
        BorderThickness = new Thickness(3),
        BorderBrush = new SolidColorBrush(Color.FromRgb(0x6B, 0x5E, 0x55)),
        Background = new LinearGradientBrush(
            Color.FromRgb(0xA8, 0x9C, 0x94), Color.FromRgb(0x7D, 0x71, 0x69), 90),
        Cursor = System.Windows.Input.Cursors.Hand,
    };

    private void Redraw()
    {
        for (int i = 0; i < SlotCount; i++) _slots[i].Child = Face(_placed[i], SlotNames[i]);

        _palette.Children.Clear();
        foreach (var troop in _pool) _palette.Children.Add(Tile(troop));

        int left = _pool.Count;
        _status.Text = _picked is { } pick
            ? $"「{pick.Name} x{pick.Men}」 을 놓을 자리를 고르세요.  (남은 부대 {left})"
            : left > 0
                ? $"오른쪽에서 부대를 고르고 자리를 누르세요.  (남은 부대 {left})"
                : "다 놓았습니다. 「결정」을 누르세요.";
    }

    /// <summary>자리에 놓인 부대의 얼굴. 비었으면 자리 이름만 흐리게 낸다.</summary>
    private UIElement Face(Troop? troop, string name)
    {
        if (troop == null)
            return new TextBlock
            {
                Text = name,
                Foreground = new SolidColorBrush(Color.FromArgb(0x66, 0x30, 0x28, 0x22)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };

        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        if (Picture(troop.Kind) is { } art) stack.Children.Add(art);
        stack.Children.Add(new TextBlock
        {
            Text = $"{troop.Name}  x{troop.Men}",
            Foreground = Brushes.Black,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        return stack;
    }

    /// <summary>팔레트 한 칸.</summary>
    private UIElement Tile(Troop troop)
    {
        var stack = new StackPanel { Orientation = Orientation.Horizontal };
        if (Picture(troop.Kind) is { } art) stack.Children.Add(art);
        stack.Children.Add(new TextBlock
        {
            Text = $"{troop.Name}  x{troop.Men}",
            Foreground = GameUi.Text,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0),
        });

        var box = new Border
        {
            Child = stack,
            Margin = new Thickness(0, 0, 0, 6),
            Padding = new Thickness(6),
            BorderThickness = new Thickness(2),
            BorderBrush = ReferenceEquals(troop, _picked) ? Brushes.Gold : GameUi.Edge,
            Background = GameUi.MenuBack,
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        box.MouseLeftButtonUp += (_, _) => { _picked = troop; Redraw(); };
        return box;
    }

    /// <summary>그 병종의 첫 몸짓. 게임 폴더를 모르면 null 이다.</summary>
    private Image? Picture(int kind)
    {
        if (_art == null) return null;

        var bgra = _art.TryGetUnit(kind, friend: true, _culture, frame: 0, out int w, out int h);
        if (bgra == null) return null;

        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, bgra, w * 4);
        bmp.Freeze();

        var image = new Image { Source = bmp, Width = w, Height = h, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        return image;
    }

    // ── 놓고 걷기 ──────────────────────────────────────────────────────────────

    /// <summary>고른 부대를 그 자리에 놓는다. 이미 놓인 자리를 누르면 걷는다.</summary>
    private void Place(int slot)
    {
        // 이미 놓인 자리를 누르면 걷어 팔레트로 돌려보낸다.
        if (_placed[slot] is { } there)
        {
            _pool.Add(there);
            _placed[slot] = null;
            _picked = there;
            Redraw();
            return;
        }
        if (_picked is not { } pick) return;

        _placed[slot] = pick;
        _pool.Remove(pick);
        _picked = _pool.FirstOrDefault();
        Redraw();
    }

    /// <summary>
    /// 「결정」 — 제독 부대가 판에 없으면 물린다(<c>0x004473B0</c>).
    /// </summary>
    private void Decide()
    {
        if (!_placed.Any(t => t is { Kind: Admiral }))
        {
            NoticeDialog.Show(this, "제독의 부대를 배치해 주십시오", "");
            return;
        }
        DialogResult = true;
        Close();
    }
}
