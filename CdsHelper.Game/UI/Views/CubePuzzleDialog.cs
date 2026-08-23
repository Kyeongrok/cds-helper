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
/// 미니 게임 「화살표 입방체 퍼즐」 화면.
/// </summary>
/// <remarks>
/// 게임의 <c>0x0049B3C0</c> 이고, 규칙은 <see cref="CubePuzzle"/> 에 모아 두었다.
///
/// 이 놀이는 <b>제 그림 파일이 없다</b> — <c>0x0049B422</c> 가 <c>0x00455DE0</c> 을
/// 부르니 <b>MGGRAPH.CDS</b> 를 함께 쓴다. 조각 번호는 그 표(<c>0x00549E98</c>)의
/// 것이다.
/// <code>
///   조각 0~5   64x48   돌리는 화살표 여섯
///   조각 6·7   64x48   좌대에 선 모험자와 입방체
///   조각 8     64x48   빈 좌대
///   조각 9     64x48   좌대 위 흰 화살표
///   조각 10    64x48   금괴
///   조각 11   512x352  배경
/// </code>
/// </remarks>
internal sealed class CubePuzzleDialog : InfoDialog
{
    private const int SceneWidth = 512, SceneHeight = 352;

    /// <summary>화면 점 기준 곱. <see cref="GameUi.PixelZoom"/> 이 배율로 나눈다.</summary>
    private const int Zoom = 2;

    private const int TileW = 64, TileH = 48;

    /// <summary>판을 그리는 자리. 배경의 오른쪽 빈 데에 맞춘다.</summary>
    private const int BoardX = 168, BoardY = 40, StepX = 60, StepY = 52;

    private static readonly Brush Ring = Frozen(Colors.White);
    private static readonly Brush Way = Frozen(Color.FromRgb(0x6C, 0xC8, 0x6C));

    private readonly CubePuzzle _game;
    private readonly Canvas _scene = new() { Width = SceneWidth, Height = SceneHeight };
    private readonly Border[,] _cell = new Border[CubePuzzle.Side, CubePuzzle.Side];
    private readonly Image _stand = new() { Width = TileW, Height = TileH, IsHitTestVisible = false };
    private readonly GameUi.GameLabel _line = new(GameFont.WhiteColor) { Bold = true };
    private readonly GameUi.GameLabel _arrow = new(GameFont.WhiteColor) { Bold = true };
    private readonly GameButton _spin;

    private CubePuzzleDialog(Random rng)
    {
        _game = new CubePuzzle(rng);
        _spin = new GameButton("수평으로 돌린다", DoSpin);

        Lay(Picture("cube-bg.png"), 0, 0, SceneWidth, SceneHeight);

        for (int y = 0; y < CubePuzzle.Side; y++)
        for (int x = 0; x < CubePuzzle.Side; x++)
        {
            var box = new Border
            {
                Width = 52,
                Height = 44,
                Background = new ImageBrush(Picture("cube-stand.png")) { Stretch = Stretch.Uniform },
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(2),
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(box, BoardX + x * StepX);
            Canvas.SetTop(box, BoardY + y * StepY);
            _scene.Children.Add(box);
            _cell[x, y] = box;
        }

        RenderOptions.SetBitmapScalingMode(_stand, BitmapScalingMode.NearestNeighbor);
        _stand.Source = Picture("cube-hero.png");
        Panel.SetZIndex(_stand, 50);
        _scene.Children.Add(_stand);

        // 왼쪽 검은 칸에 지금 위에 온 화살표를 적는다.
        _arrow.FallbackBrush = Ring;
        Canvas.SetLeft(_arrow, 24);
        Canvas.SetTop(_arrow, 40);
        _scene.Children.Add(_arrow);

        _scene.Background = Brushes.Transparent;
        _scene.MouseLeftButtonDown += (_, e) => e.Handled = true;

        double zoom = GameUi.PixelZoom(this, Zoom);
        _scene.LayoutTransform = new ScaleTransform(zoom, zoom);

        var rows = new StackPanel();
        rows.Children.Add(_line);
        rows.Children.Add(Gap(4));
        rows.Children.Add(_scene);

        Build("화살표 입방체 퍼즐", rows, SceneWidth * zoom + 30, SceneHeight * zoom + 140,
              new GameButton("↑ 넘어뜨린다", () => Roll(0)),
              new GameButton("↓", () => Roll(2)),
              new GameButton("←", () => Roll(3)),
              new GameButton("→", () => Roll(1)),
              _spin,
              new GameButton("게임 설명", Explain));

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Up) Roll(0);
            else if (e.Key == Key.Right) Roll(1);
            else if (e.Key == Key.Down) Roll(2);
            else if (e.Key == Key.Left) Roll(3);
            else if (e.Key == Key.Space) DoSpin();
        };

        Sync();
    }

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

    private void Roll(int way)
    {
        if (_game.Over != null) return;

        _game.Roll(way);
        Sync();
        if (_game.Over != null) Close();
    }

    private void DoSpin()
    {
        if (!_game.Spin())
        {
            if (_game.JustSpun)
                NoticeDialog.Show(this, "2번 계속해서 수평으로 회전할 수 없다.", "게임 설명");
            return;
        }
        Sync();
    }

    private void Explain() =>
        NoticeDialog.Show(this,
            "성공조건 [자기가 타고 있는 좌대를 움직여서 출구로 이동한다]" +
            Environment.NewLine + Environment.NewLine +
            "입방체를 지면에 수직으로 90도 회전시키면 그 때 위의 면에 나온 화살표가 " +
            "가리키는 방향으로 좌대가 하나만 움직인다." + Environment.NewLine +
            "지면에 수평방향으로 회전시켜도 대좌는 움직이지 않는다. 이 수평 회전은 " +
            "한번에 90도이지만, 연달아 돌릴 수는 없다." + Environment.NewLine +
            "입방체가 서로 면하는 면은 대칭이 되어 있으며 반대편의 면이 그대로 비치는 " +
            "것 처럼 되어 있다.", "게임 설명");

    private void Sync()
    {
        _line.Text = $"  {_game.Moves}수" + (_game.JustSpun ? "   방금 수평으로 돌렸다" : "");
        _arrow.Text = $"위 면: {CubePuzzle.Ways[_game.Arrow].Name}";

        for (int y = 0; y < CubePuzzle.Side; y++)
        for (int x = 0; x < CubePuzzle.Side; x++)
            _cell[x, y].BorderBrush = x == _game.ExitX && y == _game.ExitY
                                      ? Way : Brushes.Transparent;

        int px = Math.Clamp(_game.X, 0, CubePuzzle.Side - 1);
        int py = Math.Clamp(_game.Y, 0, CubePuzzle.Side - 1);
        Canvas.SetLeft(_stand, BoardX + px * StepX - 6);
        Canvas.SetTop(_stand, BoardY + py * StepY - 8);

        _spin.On = _game.Over == null && !_game.JustSpun;
    }

    /// <summary>
    /// 한 판 한다. 지면 <c>0x0049B3C0</c> 이 하듯 <b>한 번 더</b> 준다.
    /// </summary>
    public static void Play(Window owner, Player player, Random rng)
    {
        for (int go = 0; go < 2; go++)
        {
            var dialog = new CubePuzzleDialog(rng) { Owner = owner };
            dialog.ShowDialog();

            if (dialog._game.Over == true)
            {
                NoticeDialog.Show(owner,
                    $"금화로 따지면 {CubePuzzle.Prize} 닢에 상당되는 금괴를 손에 넣었다!",
                    "금괴 취득");
                player.Earn(CubePuzzle.Prize);
                return;
            }
            if (dialog._game.Over == null) return;      // 그만뒀다

            if (go == 0)
                NoticeDialog.Show(owner,
                    "아니! 밑바닥으로 떨어진 줄 알았는데 실은 그 아래층이 존재했다!" +
                    Environment.NewLine +
                    "자, 모험자여! 이것이 마지막 찬스다! 입방체를 잘 조작하여 이 상황을 " +
                    "타파하라!", "다시 한번 도전할 수 있습니다!");
        }
    }
}
