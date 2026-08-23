using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Game.UI.Views;

namespace CdsHelper.Maze;

/// <summary>
/// 미니 게임 「미궁 64 퍼즐」 화면.
/// </summary>
/// <remarks>
/// 게임의 <c>0x0042C8A0</c> 이다. 규칙과 판정은 <see cref="MazePuzzle"/> 에 모아 두었다.
///
/// <b>그림은 게임 것 그대로다</b> — MAZE.CDS 에서 뽑아 <c>asset/minigame</c> 에 둔다
/// (<c>tools/extract_minigame_art.py</c>). 바탕에 <b>네 층의 돌바닥이 비스듬히</b>
/// 그려져 있고, 밟은 칸에 바닥 조각을 얹는 식이다 — 1인칭이 아니다.
/// <code>
///   0x0042BCEA  배경 352x432 를 (8, 8) 에 찍는다
///   0x0042BE87  y0 = 0x1F(31), 층마다 0x6A(106) 씩
///   0x0042BE99  x  = 0x64(100), 줄마다 -0x16(22), 칸마다 +0x34(52)
///   0x0042BECE  밟은 칸에 80x24 조각
///   0x0042BF93  상자는 (x + 0x7B, y + 0x13) — 곧 칸에서 (+23, -12)
///   0x0042C0A0  방향 화살표 여섯 벌 — 32x24
/// </code>
/// <b>화살표는 갈 수 있는 칸 위에 뜬다.</b> 여섯 벌의 자리 상수를 풀어 보면 다 같은
/// 꼴이다 — <b>가는 칸에서 (+25, -2)</b>. 짚은 방향만 금빛 조각으로 갈린다.
/// <code>
///   방향 1 위층  y = 층*106 + 줄*22 - 0x4D   x = 0x7D + 칸*52 - 줄*22
///   방향 2 아래  y = …            + 0x87    x = 0x7D + …
///   방향 3 줄-1  y = …            + 0x07    x = 0x93 + …
///   방향 4 줄+1  y = …            + 0x33    x = 0x67 + …
///   방향 5 칸-1  y = …            + 0x1D    x = 0x49 + …
///   방향 6 칸+1  y = …            + 0x1D    x = 0xB1 + …
/// </code>
/// 그래서 칸 자리가 이렇게 난다.
/// <code>
///   x = 100 + 칸 * 52 - 줄 * 22
///   y =  31 + 층 * 106 + 줄 * 22
/// </code>
/// </remarks>
internal sealed class MazePuzzleDialog : InfoDialog
{
    /// <summary>게임 그림의 크기. 자리 값이 다 이 눈금이다.</summary>
    private const int SceneWidth = 352, SceneHeight = 432;

    /// <summary>
    /// 그림을 <b>화면 점</b> 기준으로 몇 배로 놓을지. 1 이면 원본 크기다.
    /// </summary>
    /// <remarks>
    /// 모니터 배율(DPI)이 얼마든 <b>그림 점 하나가 화면 점 하나</b>가 되게
    /// <see cref="GameUi.PixelZoom"/> 이 나눠 준다.
    /// </remarks>
    private const int Zoom = 1;

    /// <summary>바닥 칸 조각과 그 자리 셈(<c>0x0042BE87</c> 벌).</summary>
    private const int FloorW = 80, FloorH = 24;
    private const int OriginX = 100, OriginY = 31;
    private const int ColStep = 52, RowStep = 22, LayerStep = 106;

    /// <summary>상자·문은 칸에서 이만큼 옮겨 얹는다(<c>0x0042BF93</c>).</summary>
    private const int ItemDx = 23, ItemDy = -12, ItemSize = 32;

    /// <summary>탐험가는 40x40 이고 칸 가운데 선다.</summary>
    private const int HeroSize = 40, HeroDx = 20, HeroDy = -22;

    /// <summary>화살표는 <b>가는 칸</b>에서 이만큼 옮겨 얹는다(<c>0x0042C0A0</c> 벌).</summary>
    private const int ArrowW = 32, ArrowH = 24, ArrowDx = 25, ArrowDy = -2;

    private static readonly Brush Ring = Frozen(Colors.White);

    private readonly MazePuzzle _game;
    private readonly Canvas _scene = new() { Width = SceneWidth, Height = SceneHeight };
    private readonly Image[] _floor = new Image[MazePuzzle.Rooms];
    private readonly Image[] _item = new Image[MazePuzzle.Rooms];
    private readonly Image[] _arrow = new Image[MazePuzzle.Ways.Length];
    private readonly Image _hero = new()
    {
        Width = HeroSize,
        Height = HeroSize,
        IsHitTestVisible = false,
    };
    private readonly GameUi.GameLabel _line = new(GameFont.WhiteColor) { Bold = true };
    private readonly GameButton _undo;
    private readonly GameButton _open;

    /// <summary>지금 짚은 방향. 게임의 <c>[0x2FC]</c> 다.</summary>
    private int _point = -1;

    private MazePuzzleDialog(Random rng)
    {
        _game = new MazePuzzle(rng);
        _undo = new GameButton("ＵＮＤＯ", AskUndo);
        _open = new GameButton("보물 상자를 연다", OpenChest);

        if (Picture("maze-bg.png") is { } back)
        {
            var image = new Image { Source = back, Width = SceneWidth, Height = SceneHeight };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
            _scene.Children.Add(image);
        }

        for (int room = 0; room < MazePuzzle.Rooms; room++) Cell(room);
        for (int way = 0; way < MazePuzzle.Ways.Length; way++) Arrow(way);

        RenderOptions.SetBitmapScalingMode(_hero, BitmapScalingMode.NearestNeighbor);
        _hero.Source = Picture("maze-hero.png");
        Panel.SetZIndex(_hero, 50);
        _scene.Children.Add(_hero);

        // 빈 데도 누름을 받아야 한다. 누름을 여기서 먹어야 판에 걸린 창 끌기가 안 물고 간다.
        _scene.Background = Brushes.Transparent;
        _scene.MouseLeftButtonDown += (_, e) => e.Handled = true;
        _scene.MouseLeftButtonUp += SceneUp;
        // 모니터 배율을 물어 나눠 준다 — 그림 점 하나가 화면 점 하나가 되게.
        double zoom = GameUi.PixelZoom(this, Zoom);
        _scene.LayoutTransform = new ScaleTransform(zoom, zoom);

        // 셈은 판 밖에 적는다 — 판 안은 게임 그림이 다 쓴다.
        _line.FallbackBrush = Ring;
        var rows = new StackPanel();
        rows.Children.Add(_line);
        rows.Children.Add(Gap(4));
        rows.Children.Add(_scene);

        Build("미궁 64 퍼즐", rows, SceneWidth * zoom + 30, SceneHeight * zoom + 130,
              _open, _undo,
              new GameButton("게임 설명", Explain),
              new GameButton("포기한다", AskGiveUp));

        KeyDown += OnKey;
        Sync();
    }

    /// <summary>그 칸의 왼쪽 위 자리.</summary>
    private static Point Spot(int room)
    {
        int col = room % MazePuzzle.Side;
        int row = room / MazePuzzle.Side % MazePuzzle.Side;
        int layer = room / (MazePuzzle.Side * MazePuzzle.Side);
        return new Point(OriginX + col * ColStep - row * RowStep,
                         OriginY + layer * LayerStep + row * RowStep);
    }

    /// <summary>칸 하나 — 밟은 바닥, 갈 수 있음을 알리는 테, 상자나 문.</summary>
    private void Cell(int room)
    {
        var at = Spot(room);

        var floor = new Image
        {
            Width = FloorW,
            Height = FloorH,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };
        RenderOptions.SetBitmapScalingMode(floor, BitmapScalingMode.NearestNeighbor);
        Canvas.SetLeft(floor, at.X);
        Canvas.SetTop(floor, at.Y);
        _scene.Children.Add(floor);
        _floor[room] = floor;

        var item = new Image
        {
            Width = ItemSize,
            Height = ItemSize,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };
        RenderOptions.SetBitmapScalingMode(item, BitmapScalingMode.NearestNeighbor);
        Canvas.SetLeft(item, at.X + ItemDx);
        Canvas.SetTop(item, at.Y + ItemDy);
        Panel.SetZIndex(item, 20);
        _scene.Children.Add(item);
        _item[room] = item;
    }

    /// <summary>방향 화살표 하나. 갈 수 있을 때만 뜨고, 누르면 그리로 간다.</summary>
    private void Arrow(int way)
    {
        var image = new Image
        {
            Width = ArrowW,
            Height = ArrowH,
            Visibility = Visibility.Collapsed,
            Cursor = Cursors.Hand,
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        Panel.SetZIndex(image, 60);

        image.MouseLeftButtonDown += (_, e) => e.Handled = true;
        image.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            int next = MazePuzzle.Neighbour(_game.Here, MazePuzzle.Ways[way].Step);
            if (next >= 0) Step(next);
        };

        // 짚으면 금빛 조각으로 갈린다 — 게임의 [0x2FC] 자리다.
        image.MouseEnter += (_, _) => { _point = way; Sync(); };
        image.MouseLeave += (_, _) => { if (_point == way) { _point = -1; Sync(); } };

        _scene.Children.Add(image);
        _arrow[way] = image;
    }

    /// <summary>뽑아 둔 그림 한 장. 없으면 null.</summary>
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

    /// <summary>누른 자리에서 가장 가까운 칸. 너무 멀면 -1.</summary>
    private static int RoomAt(Point at)
    {
        int best = -1;
        double near = double.MaxValue;
        for (int room = 0; room < MazePuzzle.Rooms; room++)
        {
            var spot = Spot(room);
            double dx = at.X - (spot.X + FloorW / 2.0);
            double dy = at.Y - (spot.Y + FloorH / 2.0);
            // 칸이 마름모라 세로를 늘려 재야 이웃 층으로 안 샌다.
            double far = dx * dx + dy * dy * 4;
            if (far < near) { near = far; best = room; }
        }
        return near <= 40 * 40 ? best : -1;
    }

    private void SceneUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        int room = RoomAt(e.GetPosition(_scene));
        if (room >= 0) Step(room);
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space) { AskUndo(); return; }

        int step = e.Key switch
        {
            Key.Right => +1,
            Key.Left => -1,
            Key.Down => +MazePuzzle.Side,
            Key.Up => -MazePuzzle.Side,
            Key.PageDown => +MazePuzzle.Side * MazePuzzle.Side,
            Key.PageUp => -MazePuzzle.Side * MazePuzzle.Side,
            _ => 0,
        };
        if (step == 0) return;

        int next = MazePuzzle.Neighbour(_game.Here, step);
        if (next >= 0) Step(next);
        e.Handled = true;
    }

    private void Step(int room)
    {
        if (_game.Over != MazePuzzle.Result.Playing) return;
        if (!_game.Walk(room)) return;

        Sync();

        int number = _game.ChestAt(room);
        if (number != 0 && !_game.ChestOpen(number)
            && ConfirmDialog.Ask(this, "보물 상자가 있습니다. 열겠습니까?", "보물 상자 발견"))
        {
            OpenChest();
            if (_game.Over != MazePuzzle.Result.Playing) return;
        }

        if (room != _game.Exit) return;

        int was = _game.Restarted;
        var result = _game.Arrive();
        Sync();

        if (result == MazePuzzle.Result.Playing && _game.Restarted > was)
        {
            NoticeDialog.Show(this, "방을 전부 돌지 않았기 때문에" + Environment.NewLine +
                                    "입구로 되돌아 오고 말았다!", "처음부터 다시");
            return;
        }
        if (result != MazePuzzle.Result.Playing) Close();
    }

    private void OpenChest()
    {
        int number = _game.OpenChest();
        if (number == 0) return;

        if (_game.Over == MazePuzzle.Result.Trapped)
        {
            Sync();
            Close();
            return;
        }

        NoticeDialog.Show(this,
            number == MazePuzzle.Chests
                ? "보물을 손에 넣었다!"
                : $"보물 상자 {number + 1}의 열쇠를 손에 넣었다!",
            number == MazePuzzle.Chests ? "보물 발견" : "열쇠 발견");
        Sync();
    }

    private void AskUndo()
    {
        if (_game.Undone >= MazePuzzle.MaxUndo)
        {
            NoticeDialog.Show(this,
                "[U N D O (취소) ] 는 3회까지입니다. 더 이상 사용할 수 없습니다.", "횟수 오버");
            return;
        }
        if (!_game.CanUndo) return;
        if (!ConfirmDialog.Ask(this, "한발 앞의 상태로 돌아가겠습니까?", "앞으로 돌아간다")) return;

        _game.Undo();
        Sync();
    }

    private void Explain() =>
        NoticeDialog.Show(this,
            "바닥을 전부 한번씩만 통과해, 출구로 나가 주십시오." + Environment.NewLine +
            "나아갈 방향의 방을 눌러 이동합니다." + Environment.NewLine +
            "키보드는 ↑↓←→ 와 PageUp·PageDown 으로 움직이고," + Environment.NewLine +
            "[U N D O(취소)]는 스페이스키를 누릅니다." + Environment.NewLine +
            "[U N D O(취소)]를 사용할 수 있는 것은 3회까지입니다." + Environment.NewLine +
            Environment.NewLine +
            "보물 상자는 숫자가 적은 순서로 밖에 열지 못합니다만 열지 않아도 밖으로 나갈 " +
            "수 있습니다. 바닥을 전부 통과하지 않고 출구로 가면 처음으로 돌아갑니다. " +
            "이것도 3회까지입니다.", "게임 설명");

    private void AskGiveUp()
    {
        if (!ConfirmDialog.Ask(this, "미궁으로부터의 탈출을 포기하겠습니까?", "포기한다")) return;
        _game.GiveUp();
        Close();
    }

    private void Sync()
    {
        _line.Text = $"  밟은 방 {_game.Walked}/{MazePuzzle.Rooms}" +
                     $"   상자 {_game.Opened}/{MazePuzzle.Chests}" +
                     $"   취소 {_game.Undone}/{MazePuzzle.MaxUndo}" +
                     $"   다시 {_game.Restarted}/{MazePuzzle.MaxRestart}";

        var floor = Picture("maze-floor.png");

        for (int room = 0; room < MazePuzzle.Rooms; room++)
        {
            bool walked = _game.StepAt(room) != 0;
            _floor[room].Source = floor;
            _floor[room].Visibility = walked ? Visibility.Visible : Visibility.Collapsed;

            int chest = _game.ChestAt(room);
            var art = room == _game.Exit ? Picture("maze-door.png")
                    : chest == 0 ? null
                    : Picture(_game.ChestOpen(chest)
                              ? $"maze-chest-open-{chest - 1}.png"
                              : $"maze-chest-{chest - 1}.png");

            _item[room].Source = art;
            _item[room].Visibility = art == null ? Visibility.Collapsed : Visibility.Visible;
        }

        // 갈 수 있는 방향에만 화살표를 세운다. <b>가는 칸</b> 위에 뜬다.
        for (int way = 0; way < MazePuzzle.Ways.Length; way++)
        {
            int next = _game.Over == MazePuzzle.Result.Playing
                     ? MazePuzzle.Neighbour(_game.Here, MazePuzzle.Ways[way].Step) : -1;
            bool open = next >= 0 && _game.StepAt(next) == 0;

            _arrow[way].Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            if (!open) continue;

            _arrow[way].Source = Picture($"maze-arrow-{way * 2 + (_point == way ? 1 : 0)}.png");
            var to = Spot(next);
            Canvas.SetLeft(_arrow[way], to.X + ArrowDx);
            Canvas.SetTop(_arrow[way], to.Y + ArrowDy);
        }

        var at = Spot(_game.Here);
        Canvas.SetLeft(_hero, at.X + HeroDx);
        Canvas.SetTop(_hero, at.Y + HeroDy);

        _undo.On = _game.CanUndo;
        _open.On = _game.ChestAt(_game.Here) != 0
                   && !_game.ChestOpen(_game.ChestAt(_game.Here));
    }

    /// <summary>놀이를 한 판 하고 <c>0x0042C8A0</c> 이 하듯 결과를 알린다.</summary>
    public static void Play(Window owner, Random rng)
    {
        var dialog = new MazePuzzleDialog(rng) { Owner = owner };
        dialog.ShowDialog();

        switch (dialog._game.Over)
        {
            case MazePuzzle.Result.Trapped:
                NoticeDialog.Show(owner,
                    "순서를 지키지 않았으므로 보물 상자에 장치된 덫이 작동!" +
                    Environment.NewLine + "순식간에 목숨을 잃고 말았다!", "게임 오버");
                break;

            case MazePuzzle.Result.Failed:
                NoticeDialog.Show(owner,
                    "이번엔 되돌아 오지 않았지만 문이 닫히고 말았다. 이제 탈출은 불가능하다.",
                    "클리어 실패");
                NoticeDialog.Show(owner, "게임 오버입니다. 다음 번엔 노력합시다.", "게임 오버");
                break;

            case MazePuzzle.Result.GaveUp:
                NoticeDialog.Show(owner, "포기하자, 바닥이 차츰 웅웅거리기 시작했다!", "게임 오버");
                NoticeDialog.Show(owner, "게임 오버입니다. 다음 번엔 노력합시다.", "게임 오버");
                break;

            case MazePuzzle.Result.Cleared:
                NoticeDialog.Show(owner, "축하하네! 드디어 자네는 미궁을 돌파했네!", "게임 클리어");
                break;

            case MazePuzzle.Result.Perfect:
                NoticeDialog.Show(owner,
                    "축하하네! 자네는 실수하지 않고 미궁을 돌파해 보물을 손에 넣었네!",
                    "게임 클리어");
                break;
        }
    }
}
