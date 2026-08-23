using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 미니 게임 「낚시 게임」 화면.
/// </summary>
/// <remarks>
/// 게임의 <c>0x0047BDD0</c> 이고, 규칙은 <see cref="FishingGame"/> 에 모아 두었다.
///
/// <b>그림은 게임 것 그대로다</b> — FISHING.CDS 에서 뽑아 <c>asset/minigame</c> 에 둔다
/// (<c>tools/extract_minigame_art.py</c>). 자리 표는 EXE 의 <c>0x00569194</c> 이고
/// 배경은 <c>0x0047ADF2</c> 가 <b>336x392</b> 를 통째로 찍는다.
///
/// 격자 자리는 <b>그림에서 재어</b> 썼다 — 물빛 줄이 세로로 <c>x = 48 + 칸 * 40</c>
/// 일곱, 가로로 <c>y = 63 + 줄 * 40</c> 일곱이라 그 사이가 여섯 줄이다. 칸 사이가
/// 마흔인 것은 <c>0x0047A8F4</c> 의 <c>40 * 칸</c> 과 맞는다.
///
/// <b>바다 것들은 처음부터 다 보인다.</b> 어디에 오징어와 낙지가 있는지 보고 피해
/// 가는 놀이라 감추면 안 된다. 대어도 바닥에 보인다.
/// </remarks>
internal sealed class FishingGameDialog : InfoDialog
{
    private const int SceneWidth = 336, SceneHeight = 392;

    /// <summary>정수배로만 늘린다.</summary>
    private const int Zoom = 2;

    /// <summary>물빛 줄 자리. 그림에서 잰 것이다.</summary>
    private const int GridX = 48, GridY = 63, Step = 40;

    private const int BeastSize = 32, HookSize = 16, FishW = 32, FishH = 16;

    private readonly FishingGame _game;
    private readonly Canvas _scene = new() { Width = SceneWidth, Height = SceneHeight };
    private readonly Image _hook = new() { Width = HookSize, Height = HookSize };
    private readonly Image _boat = new() { Width = BeastSize, Height = BeastSize };
    private readonly Image[] _arrow = new Image[2];
    private readonly GameUi.GameLabel _line = Label("");

    private FishingGameDialog(Random rng)
    {
        _game = new FishingGame(rng);

        Lay(Picture("fish-bg.png"), 0, 0, SceneWidth, SceneHeight);

        // 바다 것들. 오징어와 낙지는 칸 가운데에 선다.
        for (int at = 0; at < FishingGame.Cells; at++)
        {
            int what = _game.CellAt(at);
            if (what < FishingGame.Squid) continue;

            var art = Picture(what == FishingGame.Squid ? "fish-big-1.png" : "fish-big-2.png");
            Lay(art, CellX(at % FishingGame.Columns) - BeastSize / 2,
                CellY(at / FishingGame.Columns) - BeastSize / 2, BeastSize, BeastSize);
        }

        // 대어는 바닥에, 제 칸에 눕는다.
        Lay(Picture("fish-small-0.png"), CellX(_game.BigOneColumn) - FishW / 2,
            GridY + FishingGame.Rows * Step + 14, FishW, FishH);

        // 배와 바늘.
        Ready(_boat, Picture("fish-big-0.png"));
        Canvas.SetLeft(_boat, CellX(_game.DropColumn) - BeastSize / 2);
        Canvas.SetTop(_boat, 32);   // 배는 물낯(y = 63) 위에 뜬다
        Panel.SetZIndex(_boat, 40);
        _scene.Children.Add(_boat);

        Ready(_hook, Picture("fish-hook.png"));
        Panel.SetZIndex(_hook, 50);
        _scene.Children.Add(_hook);

        // 왼쪽·오른쪽 화살표. 게임도 오른쪽 위에 나란히 둔다.
        for (int i = 0; i < 2; i++)
        {
            int way = i == 0 ? -1 : +1;
            var image = new Image { Width = FishW, Height = FishH, Cursor = Cursors.Hand };
            Ready(image, Picture($"fish-arrow-{i}.png"));
            Canvas.SetLeft(image, 224 + i * FishW);
            Canvas.SetTop(image, 14);
            image.MouseLeftButtonDown += (_, e) => e.Handled = true;
            image.MouseLeftButtonUp += (_, e) => { e.Handled = true; Steer(way); };
            _scene.Children.Add(image);
            _arrow[i] = image;
        }

        _scene.Background = Brushes.Transparent;
        _scene.MouseLeftButtonDown += (_, e) => e.Handled = true;
        _scene.LayoutTransform = new ScaleTransform(Zoom, Zoom);

        var rows = new StackPanel();
        rows.Children.Add(_line);
        rows.Children.Add(Gap(4));
        rows.Children.Add(_scene);

        Build("낚시 게임", rows, SceneWidth * Zoom + 30, SceneHeight * Zoom + 130,
              new GameButton("←", () => Steer(-1)),
              new GameButton("내린다", Fall),
              new GameButton("→", () => Steer(+1)),
              new GameButton("게임 설명", Explain));

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Left) Steer(-1);
            else if (e.Key == Key.Right) Steer(+1);
            else if (e.Key is Key.Down or Key.Enter or Key.Space) Fall();
        };

        Sync();
    }

    /// <summary>그 칸의 가운데 x — 물빛 세로 줄 자리다.</summary>
    private static double CellX(int column) => GridX + column * Step;

    /// <summary>그 줄의 가운데 y — 가로 줄 사이다.</summary>
    private static double CellY(int row) => GridY + row * Step + Step / 2.0;

    private static void Ready(Image image, BitmapSource? art)
    {
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        image.Source = art;
    }

    /// <summary>그림 한 장을 그 자리에 깐다.</summary>
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

    private void Steer(int way)
    {
        _game.Steer(way);
        Sync();
    }

    private void Fall()
    {
        if (_game.Got != FishingGame.Catch.None) return;

        _game.Fall();
        Sync();
        if (_game.Got != FishingGame.Catch.None) Close();
    }

    private void Explain() =>
        NoticeDialog.Show(this,
            "바다에서 바늘을 떨어뜨려서 바닥에 있는 대어를 낚는 게임입니다. " +
            "낚시바늘은 줄을 따라 내려갑니다." + Environment.NewLine +
            "내려가는 도중에 화살표를 클릭하든지 ←→버튼을 누르면 교차하는 데에서 " +
            "낚시바늘을 옆으로 이동할 수 있습니다만, 다음에 교차하는 데에서는 반드시 " +
            "밑으로 내려갑니다.", "게임 설명");

    private void Sync()
    {
        string way = _game.Lean > 0 ? "오른쪽으로" : _game.Lean < 0 ? "왼쪽으로" : "곧장 아래로";
        _line.Text = $"  {_game.Row + 1}/{FishingGame.Rows}줄   다음 걸음: {way}";

        // 바늘은 줄 위에 걸린다. 아직 안 내려왔으면 배 밑이다.
        Canvas.SetLeft(_hook, CellX(_game.Column) - HookSize / 2.0);
        Canvas.SetTop(_hook, _game.Row < 0 ? 60 : CellY(_game.Row) - HookSize / 2.0);
    }

    /// <summary>
    /// 한 판 한다. 결과 글은 <c>0x0047AD31</c> 의 뜀표 그대로다.
    /// </summary>
    public static void Play(Window owner, Random rng)
    {
        var dialog = new FishingGameDialog(rng) { Owner = owner };
        dialog.ShowDialog();

        switch (dialog._game.Got)
        {
            case FishingGame.Catch.SquidCaught:
                NoticeDialog.Show(owner, "왓! 오징어가 얼굴에 먹물을 토했다!", "오징어를 낚았다");
                break;

            case FishingGame.Catch.OctopusCaught:
                NoticeDialog.Show(owner,
                    "악마의 물고기다! 너무 징그러워서" + Environment.NewLine +
                    "갑판에 내동댕이쳤다.", "낙지를 낚았다");
                break;

            case FishingGame.Catch.SmallFry:
            case FishingGame.Catch.SmallFryToo:
                NoticeDialog.Show(owner,
                    "재수없게 잡어를 낚았군." + Environment.NewLine +
                    "주방장에게 갖다 줄까···", "잡어를 낚았다");
                break;

            case FishingGame.Catch.BigOne:
                NoticeDialog.Show(owner, "잘 됐다! 바다 깊숙히 있는 고기를 낚았다!",
                                  "대어을 낚았다");
                break;

            case FishingGame.Catch.Seabed:
                NoticeDialog.Show(owner,
                    "아무리 당겨도 끌어올릴 수 없다." + Environment.NewLine +
                    "[지구를 낚았다]고 해야하나.", "바닥에 걸렸다");
                break;
        }
    }
}
