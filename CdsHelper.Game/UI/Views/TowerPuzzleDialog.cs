using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 미니 게임 「발라몬의 탑 퍼즐」 화면.
/// </summary>
/// <remarks>
/// 게임의 <c>0x00431740</c> 이고, 규칙은 <see cref="TowerPuzzle"/> 에 모아 두었다.
///
/// <b>그림은 게임 것 그대로다</b> — TOWER.CDS 에서 뽑아 <c>asset/minigame</c> 에 둔다.
/// 자리 표는 EXE 의 <c>0x00547400</c> 이고 <c>[200704, 303104, 405504]</c> 다.
/// <code>
///   0       448x448   배경 — 돌 받침 셋
///   200704  160x80 x8 돌 판자. 0x00431077 이 크기를, 0x00431067 이
///           «자리 + (판자번호 - 1) * 12800» 으로 몇째 벌인지 준다
/// </code>
/// 받침 자리는 배경에서 재어 썼다 — 위 하나, 아래 둘이 세모꼴로 놓인다.
/// <b>그림 말고는 아무것도 얹지 않는다</b> — 게임 화면에 없는 것을 덧대면 그만큼
/// 게임이 아니게 된다. 들고 있는 판자는 떠 있는 그림 자체가 알려 준다.
///
/// 판자는 <b>끌어다 옮길 수</b> 있고, 딸깍 두 번(집을 기둥 · 놓을 기둥)으로도 옮긴다.
/// 규칙은 <see cref="TowerPuzzle.Tap"/> 하나라 둘이 같은 길로 든다 — 끌기는 「집기
/// 딸깍」과 「놓기 딸깍」을 한 몸짓으로 묶은 것뿐이다.
/// </remarks>
internal sealed class TowerPuzzleDialog : InfoDialog
{
    private const int SceneWidth = 448, SceneHeight = 448;

    /// <summary>화면 점 기준 곱. <see cref="GameUi.PixelZoom"/> 이 배율로 나눈다.</summary>
    private const int Zoom = 2;

    private const int PlankW = 160, PlankH = 80;

    /// <summary>
    /// 받침 셋의 가운데 x 와 판자가 얹히는 y. 배경에서 잰 것이다.
    /// </summary>
    /// <remarks>
    /// <b>차례가 뜻을 가진다.</b> 다 모아야 하는 데가 <see cref="TowerPuzzle.Goal"/> —
    /// 곧 <b>셋째</b> 기둥이고(<c>0x004305EE</c> 가 <c>[esi+0x144]</c> 를 판자 수와 견준다),
    /// 그 자리는 배경에서 <b>가운데 위</b>에 홀로 놓인 받침이다. 쌓아 올리는 것이 이 놀이의
    /// 「탑」이니 눈에도 그렇게 보여야 한다. 아래 둘이 앞의 두 자리다.
    /// </remarks>
    private static readonly int[] PegX = [86, 360, 226];
    private static readonly int[] PegY = [352, 352, 210];

    /// <summary>
    /// 판자마다의 <b>잉크 위·아래</b>. 160x80 칸 안에서 그림이 실제로 그려진 자리다.
    /// </summary>
    /// <remarks>
    /// 판자가 커질수록 두껍다 — 가장 작은 것이 서른 점, 가장 큰 것이 예순 점이다.
    /// 칸 기준으로 일정하게 올리면(예전 <c>Rise = 18</c>) 큰 판자가 작은 것을 거의 다
    /// 삼켜 <b>단이 안 보이고 원뿔처럼</b> 된다. 그래서 <b>잉크를 기준으로</b> 쌓는다 —
    /// 아래 판자의 윗면에 위 판자의 밑을 얹는다.
    /// </remarks>
    private static readonly (int Top, int Bottom)[] PlankInk =
    [
        (20, 49), (22, 53), (19, 54), (18, 57), (16, 59), (12, 61), (10, 63), (8, 67),
    ];

    /// <summary>
    /// 위 판자가 아래 판자 윗면에 파묻히는 깊이.
    /// </summary>
    /// <remarks>
    /// 작을수록 단이 또렷해지고 클수록 원뿔이 된다. <b>스물</b>이 단이 보이면서도
    /// 여덟 장을 다 쌓았을 때 판을 안 넘는 값이다 — 가운데 위 받침(<c>y 210</c>)에
    /// 여덟 장을 쌓으면 꼭대기가 <c>y 12</c> 로 아슬아슬하게 든다. 열넷이면 −30 으로
    /// 판 밖으로 나간다.
    /// </remarks>
    private const int Sink = 20;

    private static readonly Brush Ring = Frozen(Colors.White);

    private readonly TowerPuzzle _game;
    private readonly Canvas _scene = new() { Width = SceneWidth, Height = SceneHeight };
    private readonly Border[] _spot = new Border[TowerPuzzle.Pegs];
    private readonly List<Image> _planks = [];
    private readonly Image _held = new()
    {
        Width = PlankW,
        Height = PlankH,
        Visibility = Visibility.Collapsed,
        IsHitTestVisible = false,
    };

    private TowerPuzzleDialog(int planks, Random rng)
    {
        _game = new TowerPuzzle(planks, rng);

        Lay(Picture("tower-bg.png"), 0, 0, SceneWidth, SceneHeight);

        // 기둥마다 누르는 칸. 받침을 넉넉히 덮는다.
        for (int peg = 0; peg < TowerPuzzle.Pegs; peg++)
        {
            int here = peg;
            var box = new Border
            {
                Width = PlankW,
                Height = 150,
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(2),
                Cursor = Cursors.Hand,
            };
            box.MouseLeftButtonDown += (_, e) => Grab(here, e);
            Canvas.SetLeft(box, PegX[peg] - PlankW / 2);
            Canvas.SetTop(box, PegY[peg] - 110);
            _scene.Children.Add(box);
            _spot[peg] = box;
        }

        RenderOptions.SetBitmapScalingMode(_held, BitmapScalingMode.NearestNeighbor);
        Panel.SetZIndex(_held, 90);
        _scene.Children.Add(_held);

        _scene.Background = Brushes.Transparent;
        _scene.MouseLeftButtonDown += (_, e) => e.Handled = true;

        // 집는 순간 판이 손을 잡으므로 뗌은 늘 판에 온다 — 놓는 기둥은 좌표로 짚는다.
        _scene.MouseMove += Drag;
        _scene.MouseLeftButtonUp += Land;

        double zoom = GameUi.PixelZoom(this, Zoom);
        _scene.LayoutTransform = new ScaleTransform(zoom, zoom);

        // 게임은 미니 게임에 밤색 판도 제목도 아래 단추 줄도 안 두른다 — 그림에 금빛
        // 테만 두르고, 할 일은 오른쪽 단추 차림표가 맡는다(성배 퍼즐·미궁 64 와 같다).
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;
        Content = GameUi.GoldFrame(_scene, Close);
        GameUi.EnableDrag(this, _scene);

        // 오른쪽 단추는 <b>두 가지</b>를 한다 — 들고 있던 판자를 도로 놓고, 차림표를 편다.
        MouseRightButtonUp += (_, e) =>
        {
            _game.PutBack();
            Sync();
            GameUi.ContextMenuAt(this, e.GetPosition(this), Commands());
        };
        KeyDown += (_, e) => { if (e.Key is Key.Escape) { _game.PutBack(); Sync(); } };

        Sync();
    }

    /// <summary>오른쪽 단추가 부르는 차림표. 예전 아래 단추 줄이 그대로 여기로 왔다.</summary>
    private IReadOnlyList<(string, Action?)> Commands() =>
    [
        ("게임 설명", Explain),
        ("포기한다", Close),
        ("게임 복귀", () => { }),   // 차림표만 닫는다
    ];

    private void Lay(BitmapSource? art, double x, double y, double width, double height)
    {
        if (art == null) return;

        var image = new Image
        {
            Source = art,
            Width = width,
            Height = height,
            IsHitTestVisible = false,
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        Canvas.SetLeft(image, x);
        Canvas.SetTop(image, y);
        _scene.Children.Add(image);
    }

    private static BitmapImage? Picture(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "asset", "minigame", name);
        if (!File.Exists(path)) return null;

        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.UriSource = new Uri(path);
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    // ── 끌어다 놓기 ────────────────────────────────────────────────────────────

    /// <summary>누른 기둥과 자리. 안 누르고 있으면 −1.</summary>
    private int _from = -1;
    private Point _grabbed;
    private bool _dragging;

    /// <summary>끌었다고 치는 거리(판 점).</summary>
    private const double DragSlop = 5;

    /// <summary>
    /// 기둥을 눌렀다 — <b>빈손이면 여기서 집는다</b>.
    /// </summary>
    /// <remarks>
    /// 이미 판자를 들고 있으면 여기서는 아무것도 안 한다. 놓는 것은 <see cref="Land"/>
    /// 라야 끌어서 놓는 것과 딸깍으로 놓는 것이 한 길로 든다.
    /// </remarks>
    private void Grab(int peg, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_game.Won) return;

        _from = peg;
        _dragging = false;
        _grabbed = e.GetPosition(_scene);
        _scene.CaptureMouse();

        if (_game.Held <= 0) Tap(peg);      // 빈손 — 집는다
    }

    private void Drag(object sender, MouseEventArgs e)
    {
        if (_from < 0 || _game.Held <= 0) return;

        var now = e.GetPosition(_scene);
        if (!_dragging)
        {
            if (Math.Abs(now.X - _grabbed.X) < DragSlop && Math.Abs(now.Y - _grabbed.Y) < DragSlop)
                return;
            _dragging = true;
        }

        // 끄는 동안은 들고 있는 판자가 손끝을 따라온다.
        Canvas.SetLeft(_held, now.X - PlankW / 2.0);
        Canvas.SetTop(_held, now.Y - PlankH / 2.0);
    }

    private void Land(object sender, MouseButtonEventArgs e)
    {
        if (_from < 0) return;
        e.Handled = true;

        int from = _from;
        bool dragged = _dragging;
        var now = e.GetPosition(_scene);
        _from = -1;
        _dragging = false;
        _scene.ReleaseMouseCapture();

        if (_game.Held <= 0) return;

        // 끌지 않고 딸깍만 했으면 집은 채로 둔다 — 다음 딸깍이 놓을 기둥이다.
        if (!dragged) { if (PegAt(now) is int to && to != from) Tap(to); Sync(); return; }

        // 기둥 밖에 놓으면 없던 일이다 — 도로 제자리에 얹는다.
        if (PegAt(now) is int drop) Tap(drop);
        else _game.PutBack();
        Sync();
    }

    /// <summary>그 자리에 놓인 기둥 번호. 어느 기둥도 아니면 null.</summary>
    private int? PegAt(Point at)
    {
        for (int peg = 0; peg < TowerPuzzle.Pegs; peg++)
        {
            double x = PegX[peg] - PlankW / 2.0, y = PegY[peg] - 110;
            if (at.X >= x && at.X < x + PlankW && at.Y >= y && at.Y < y + 150) return peg;
        }
        return null;
    }

    private void Tap(int peg)
    {
        if (_game.Won) return;

        if (!_game.Tap(peg))
        {
            if (_game.Held > 0)
                NoticeDialog.Show(this, "저보다 작은 판자 위에는 놓을 수 없습니다",
                                  "발라몬의 탑");
            return;
        }
        Sync();

        if (_game.Won)
        {
            NoticeDialog.Show(this,
                $"판자 {_game.Planks}장을 {_game.Moves}수에 다 모았다!", "발라몬의 탑");
            Close();
        }
    }

    private void Explain() =>
        NoticeDialog.Explain(this,
            "돌 판자를 셋째 기둥에 다 모으면 됩니다." + Environment.NewLine +
            "한 번에 맨 위 판자 하나만 옮길 수 있고, 저보다 작은 판자 위에는 놓지 " +
            "못합니다." + Environment.NewLine +
            "기둥을 눌러 집고, 다시 눌러 놓습니다. 오른쪽 단추로 도로 놓습니다.");

    private void Sync()
    {
        foreach (var image in _planks) _scene.Children.Remove(image);
        _planks.Clear();

        for (int peg = 0; peg < TowerPuzzle.Pegs; peg++)
        {
            var stack = _game.Stack(peg);
            // 아래에서부터 잉크를 맞대어 쌓는다.
            int foot = PegY[peg];
            for (int i = 0; i < stack.Count; i++)
            {
                int plank = stack[i];
                Plank(plank, i, PegX[peg], foot);
                var (top, bottom) = InkOf(plank);
                foot -= bottom - top - Sink;      // 다음 판자는 이 판자 윗면에 앉는다
            }

            _spot[peg].BorderBrush = peg == _game.HeldFrom ? Ring : Brushes.Transparent;
        }

        // 들고 있는 판자는 그 기둥 위에 떠 있다.
        if (_game.Held > 0)
        {
            _held.Source = Picture($"tower-plank-{_game.Held - 1}.png");
            _held.Visibility = Visibility.Visible;
            Canvas.SetLeft(_held, PegX[_game.HeldFrom] - PlankW / 2);
            Canvas.SetTop(_held, PegY[_game.HeldFrom] - 110 - InkOf(_game.Held).Bottom);
        }
        else
        {
            _held.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>판자 한 장. 조각 번호는 <c>판자번호 - 1</c> 이다.</summary>
    /// <summary>그 판자의 잉크 자리. 표 밖이면 가운데쯤으로 친다.</summary>
    private static (int Top, int Bottom) InkOf(int plank)
    {
        int at = plank - 1;
        return at >= 0 && at < PlankInk.Length ? PlankInk[at] : (20, 60);
    }

    /// <param name="level">아래에서 몇째로 쌓였는지. 앞뒤를 이것으로 가른다.</param>
    /// <param name="bottom">판자 <b>잉크의 밑</b>이 놓일 자리.</param>
    private void Plank(int plank, int level, int centre, int bottom)
    {
        var image = new Image
        {
            Source = Picture($"tower-plank-{plank - 1}.png"),
            Width = PlankW,
            Height = PlankH,
            IsHitTestVisible = false,
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        // 앞뒤는 <b>쌓인 차례</b>로 정한다 — 판자 번호로 정하면 큰 것이 늘 앞이라
        // 위에 얹은 작은 판자가 뒤로 숨는다.
        Panel.SetZIndex(image, 10 + level);
        Canvas.SetLeft(image, centre - PlankW / 2);
        Canvas.SetTop(image, bottom - InkOf(plank).Bottom);
        _scene.Children.Add(image);
        _planks.Add(image);
    }

    /// <summary>
    /// 판자를 몇 장 쓸지 묻고 한 판 한다.
    /// </summary>
    /// <remarks>
    /// 게임도 <c>0x0045FB79</c> 에서 «판자를 몇 장 사용하겠습니까?»(<c>0x00571E90</c>)
    /// 를 먼저 묻는다 — <c>0x00481FE0(4, 4, 8, 1, 1)</c> 이라 넷에서 여덟까지다.
    ///
    /// <b>그 <c>0x00481FE0</c> 이 계산기다</b> — 나이·생일을 받는 것과 같은 물건이라
    /// (<see cref="NumberPadDialog"/>) 넷째·다섯째 인자가 <c>MIN·MAX</c> 단추가 넣는
    /// 값이다. 예전에는 «4장 · 5장 …» 을 늘어놓은 목록으로 물었는데, 게임은 목록을 안
    /// 낸다.
    /// </remarks>
    public static void Play(Window owner, Random rng)
    {
        int? planks = NumberPadDialog.Ask(owner, TowerPuzzle.LeastPlanks,
                                          TowerPuzzle.LeastPlanks, TowerPuzzle.MostPlanks,
                                          "판자를 몇 장 사용하겠습니까?");
        if (planks == null) return;

        new TowerPuzzleDialog(planks.Value, rng) { Owner = owner }.ShowDialog();
    }
}
