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
/// </remarks>
internal sealed class TowerPuzzleDialog : InfoDialog
{
    private const int SceneWidth = 448, SceneHeight = 448;

    /// <summary>화면 점 기준 곱. <see cref="GameUi.PixelZoom"/> 이 배율로 나눈다.</summary>
    private const int Zoom = 2;

    private const int PlankW = 160, PlankH = 80;

    /// <summary>받침 셋의 가운데 x 와 판자가 얹히는 y. 배경에서 잰 것이다.</summary>
    private static readonly int[] PegX = [86, 226, 360];
    private static readonly int[] PegY = [352, 210, 352];

    /// <summary>판자 한 장이 쌓일 때마다 이만큼 올라간다.</summary>
    private const int Rise = 18;

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
    private readonly GameUi.GameLabel _line = new(GameFont.WhiteColor) { Bold = true };

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
            box.MouseLeftButtonDown += (_, e) => e.Handled = true;
            box.MouseLeftButtonUp += (_, e) => { e.Handled = true; Tap(here); };
            Canvas.SetLeft(box, PegX[peg] - PlankW / 2);
            Canvas.SetTop(box, PegY[peg] - 110);
            _scene.Children.Add(box);
            _spot[peg] = box;
        }

        RenderOptions.SetBitmapScalingMode(_held, BitmapScalingMode.NearestNeighbor);
        Panel.SetZIndex(_held, 90);
        _scene.Children.Add(_held);

        // 알림줄은 판 위 왼쪽 꼭대기에 얹는다 — 밤색 판이 없어지면 붙일 데가 없다.
        _line.FallbackBrush = Ring;
        _line.IsHitTestVisible = false;
        Canvas.SetLeft(_line, 14);
        Canvas.SetTop(_line, 10);
        Panel.SetZIndex(_line, 100);
        _scene.Children.Add(_line);

        _scene.Background = Brushes.Transparent;
        _scene.MouseLeftButtonDown += (_, e) => e.Handled = true;

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
        NoticeDialog.Show(this,
            "돌 판자를 셋째 기둥에 다 모으면 됩니다." + Environment.NewLine +
            "한 번에 맨 위 판자 하나만 옮길 수 있고, 저보다 작은 판자 위에는 놓지 " +
            "못합니다." + Environment.NewLine +
            "기둥을 눌러 집고, 다시 눌러 놓습니다. 오른쪽 단추로 도로 놓습니다.",
            "게임 설명");

    private void Sync()
    {
        _line.Text = $"  판자 {_game.Planks}장   {_game.Moves}수" +
                     (_game.Held > 0 ? $"   {_game.Held}번 판자를 들었다" : "");

        foreach (var image in _planks) _scene.Children.Remove(image);
        _planks.Clear();

        for (int peg = 0; peg < TowerPuzzle.Pegs; peg++)
        {
            var stack = _game.Stack(peg);
            for (int i = 0; i < stack.Count; i++) Plank(stack[i], PegX[peg], PegY[peg] - i * Rise);

            _spot[peg].BorderBrush = peg == _game.HeldFrom ? Ring : Brushes.Transparent;
        }

        // 들고 있는 판자는 그 기둥 위에 떠 있다.
        if (_game.Held > 0)
        {
            _held.Source = Picture($"tower-plank-{_game.Held - 1}.png");
            _held.Visibility = Visibility.Visible;
            Canvas.SetLeft(_held, PegX[_game.HeldFrom] - PlankW / 2);
            Canvas.SetTop(_held, PegY[_game.HeldFrom] - 110 - PlankH / 2);
        }
        else
        {
            _held.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>판자 한 장. 조각 번호는 <c>판자번호 - 1</c> 이다.</summary>
    private void Plank(int plank, int centre, int bottom)
    {
        var image = new Image
        {
            Source = Picture($"tower-plank-{plank - 1}.png"),
            Width = PlankW,
            Height = PlankH,
            IsHitTestVisible = false,
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        Panel.SetZIndex(image, 10 + plank);
        Canvas.SetLeft(image, centre - PlankW / 2);
        Canvas.SetTop(image, bottom - PlankH / 2);
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
