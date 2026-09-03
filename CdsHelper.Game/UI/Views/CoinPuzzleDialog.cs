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
/// 미니 게임 「코인 게임」(천칭 퍼즐) 화면.
/// </summary>
/// <remarks>
/// 게임의 <c>0x004531F0</c> 이고, 규칙은 <see cref="CoinPuzzle"/> 에 모아 두었다.
///
/// <b>그림은 게임 것 그대로다</b> — BALANCE.CDS 에서 뽑아 <c>asset/minigame</c> 에 둔다
/// (<c>tools/extract_minigame_art.py</c>). 자리 표가 EXE 에 셋으로 나뉘어 있다.
/// <code>
///   0x00549E10  파트 0 — 금화 32x32
///   0x00549E20  파트 1 — 대 176x16 · 나무 천칭 192x144 둘 · 금 천칭 208x168 셋
///   0x00549E3C  파트 2 — 단추 64x32 셋 · 접시 80x144 둘 · 받침 96x48 · 배경 448x384
/// </code>
/// 자리는 그리는 곳이 그대로 준다.
/// <code>
///   0x00451F91  배경 448x384 를 (8, 8) 에            ; 창이 464x400
///   0x00452599  금 천칭 208x168 을 (39, 49) 에
///   0x00452709  단추 64x32 를 (112, 240) 에
///   0x0045274A  다음 단추를 (192, 240) 에
/// </code>
/// 배경에 <b>오른쪽 흰 테 칸</b>과 <b>아래 검은 칸</b>이 비어 있다 — 금화를 늘어놓는
/// 데와 자취를 적는 데다.
/// </remarks>
internal sealed class CoinPuzzleDialog : InfoDialog
{
    private const int SceneWidth = 448, SceneHeight = 384;

    /// <summary>화면 점 기준 곱. <see cref="GameUi.PixelZoom"/> 이 배율로 나눈다.</summary>
    private const int Zoom = 2;

    /// <summary>금 천칭 자리(<c>0x00452599</c> 의 (39, 49)에서 테 8점을 뺀다).</summary>
    private const int ScaleX = 31, ScaleY = 41, ScaleW = 208, ScaleH = 168;

    /// <summary>
    /// 단추 자리. 아는 둘이 <c>0x00452709</c> 의 (112, 240) 과 <c>0x0045274A</c> 의
    /// (192, 240) 이고 — 테 8점을 빼면 104 · 184 다. 간격이 80 이니 첫 단추는 24 다.
    /// </summary>
    private const int ButtonY = 232, ButtonW = 64, ButtonH = 32;
    private static readonly int[] ButtonX = [24, 104, 184];

    /// <summary>금화를 늘어놓는 흰 테 칸. 배경에서 잰 것이다.</summary>
    private const int TrayX = 268, TrayY = 16, TrayStep = 34, TrayPer = 4;

    /// <summary>자취를 적는 아래 검은 칸.</summary>
    private const int LogX = 34, LogY = 296;

    private static readonly Brush OnLeft = Frozen(Color.FromRgb(0x4C, 0x8C, 0xC8));
    private static readonly Brush OnRight = Frozen(Color.FromRgb(0x6C, 0xC8, 0x6C));

    private readonly CoinPuzzle _game;
    private readonly Canvas _scene = new() { Width = SceneWidth, Height = SceneHeight };
    private readonly Image _scale = new() { Width = ScaleW, Height = ScaleH };
    private readonly Border[] _coin;
    private readonly StackPanel _log = new();
    private readonly GameUi.GameLabel _line = new(GameFont.WhiteColor) { Bold = true };

    private CoinPuzzleDialog(Random rng)
    {
        _game = new CoinPuzzle(rng);
        _coin = new Border[_game.Coins];

        Lay(Picture("coin-bg.png"), 0, 0, SceneWidth, SceneHeight);

        // 천칭. 기운 쪽에 따라 조각을 갈아 끼운다.
        RenderOptions.SetBitmapScalingMode(_scale, BitmapScalingMode.NearestNeighbor);
        _scale.IsHitTestVisible = false;
        Canvas.SetLeft(_scale, ScaleX);
        Canvas.SetTop(_scale, ScaleY);
        _scene.Children.Add(_scale);

        // 금화를 오른쪽 칸에 늘어놓는다. 왼쪽 단추로 왼접시, 오른쪽 단추로 오른접시.
        for (int i = 0; i < _game.Coins; i++)
        {
            int coin = i;
            var mark = new TextBlock
            {
                Text = $"{i + 1}",
                Foreground = Brushes.Black,
                FontWeight = FontWeights.Bold,
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
            };
            var box = new Border
            {
                Width = 32,
                Height = 32,
                Background = new ImageBrush(Picture("coin-gold-0.png")) { Stretch = Stretch.Fill },
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(2),
                Cursor = Cursors.Hand,
                Child = mark,
            };
            box.MouseLeftButtonDown += (_, e) => e.Handled = true;
            box.MouseLeftButtonUp += (_, e) => { e.Handled = true; Tap(coin, left: true); };
            box.MouseRightButtonUp += (_, e) => { e.Handled = true; Tap(coin, left: false); };

            Canvas.SetLeft(box, TrayX + i % TrayPer * TrayStep);
            Canvas.SetTop(box, TrayY + i / TrayPer * TrayStep);
            _scene.Children.Add(box);
            _coin[i] = box;
        }

        // 단추 셋 — WEIGH · CLEAR · DECIDE.
        Button(0, "coin-button-0.png", DoWeigh);
        Button(1, "coin-button-1.png", () => { _game.Clear(); Sync(); });
        Button(2, "coin-button-2.png", DoDecide);

        Canvas.SetLeft(_log, LogX);
        Canvas.SetTop(_log, LogY);
        _log.IsHitTestVisible = false;
        _scene.Children.Add(_log);

        // 알림줄은 판 위 왼쪽 꼭대기에 얹는다 — 밤색 판이 없어지면 붙일 데가 없다.
        _line.FallbackBrush = Brushes.White;
        _line.IsHitTestVisible = false;
        Canvas.SetLeft(_line, 14);
        Canvas.SetTop(_line, 10);
        Panel.SetZIndex(_line, 60);
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
        Content = GameUi.GoldFrame(_scene);
        GameUi.EnableDrag(this, _scene);

        // 오른쪽 단추는 <b>두 가지</b>를 한다 — 접시에 올린 금화를 내리고, 차림표를 편다.
        // 예전에는 내리기만 했다.
        MouseRightButtonUp += (_, e) =>
        {
            _game.Clear();
            Sync();
            GameUi.ContextMenu(this, PointToScreen(e.GetPosition(this)), Commands());
        };
        KeyDown += (_, e) => { if (e.Key is Key.Escape) { _game.Clear(); Sync(); } };

        Sync();
    }

    /// <summary>오른쪽 단추가 부르는 차림표. 예전 아래 단추 줄이 그대로 여기로 왔다.</summary>
    private IReadOnlyList<(string, Action?)> Commands() =>
    [
        ("게임 설명", Explain),
        ("포기한다", AskGiveUp),
        ("게임 복귀", () => { }),   // 차림표만 닫는다
    ];

    /// <summary>단추 하나. 그림은 게임 것을 그대로 쓴다.</summary>
    private void Button(int at, string art, Action run)
    {
        var image = new Image
        {
            Source = Picture(art),
            Width = ButtonW,
            Height = ButtonH,
            Cursor = Cursors.Hand,
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        Canvas.SetLeft(image, ButtonX[at]);
        Canvas.SetTop(image, ButtonY);
        image.MouseLeftButtonDown += (_, e) => e.Handled = true;
        image.MouseLeftButtonUp += (_, e) => { e.Handled = true; run(); };
        _scene.Children.Add(image);
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

    /// <summary>금화를 눌렀다 — 접시에 놓거나, 이미 접시에 있으면 두 접시를 비운다.</summary>
    private void Tap(int coin, bool left)
    {
        if (_game.Won != null) return;

        if (_game.PanOf(coin) != 0) { _game.Clear(); Sync(); return; }

        if (!_game.Put(coin, left))
        {
            NoticeDialog.Show(this, "접시 위에는 더 이상 금화를 실을 수 없습니다", "천칭 퍼즐");
            return;
        }
        Sync();
    }

    private void DoWeigh()
    {
        if (!_game.CanWeigh)
        {
            NoticeDialog.Show(this,
                "더 이상 천칭으로 금화의 무게를 달 수는 없습니다." + Environment.NewLine +
                "지금까지 얻은 결과를 분석해서 무게가 다른 금화를" + Environment.NewLine +
                "선택해 주십시오.", "천칭 퍼즐");
            return;
        }
        if (_game.Left.Count == 0 && _game.Right.Count == 0)
        {
            NoticeDialog.Show(this, "접시 위에는 아무 것도 없습니다", "천칭 퍼즐");
            return;
        }
        if (_game.Left.Count != _game.Right.Count)
        {
            NoticeDialog.Show(this, "양쪽 접시에 같은 수량의 금화가 놓여지지 않았습니다", "천칭 퍼즐");
            return;
        }

        _game.Weigh();
        Sync();
    }

    /// <summary>「가짜 금화 선택(DECIDE)」 — 어느 닢인지 고르게 하고 한 번 더 묻는다.</summary>
    private void DoDecide()
    {
        if (_game.Won != null) return;

        var names = Enumerable.Range(1, _game.Coins).Select(n => $"{n}번 금화").ToList();
        int pick = MapPointDialog.Ask(this, names, "가짜 금화 선택");
        if (pick < 0) return;

        if (!ConfirmDialog.Ask(this, "이 금화가 딴 것과 무게가 다르다고 단정해도 좋습니까?",
                               "천칭 퍼즐")) return;

        _game.Decide(pick);
        Close();
    }

    private void Explain() =>
        NoticeDialog.Show(this,
            "금 천칭에는 함정이 있습니다. 함정에 빠지지 않게 하기 위해서는 무게가 다른 " +
            "금화를 가려내고 천칭이 평형을 이루게 해야 합니다." + Environment.NewLine +
            Environment.NewLine +
            "나무 천칭을 3번까지 쓰고 무게가 다른 금화를 선택해 주십시오." +
            Environment.NewLine + Environment.NewLine +
            "금화를 왼쪽 단추로 누르면 왼쪽 접시에, 오른쪽 단추로 누르면 오른쪽 접시에 " +
            "놓입니다. 접시 하나에 여섯 닢까지 놓을 수 있고, 양쪽 수가 같아야 답니다." +
            Environment.NewLine +
            "가짜가 무거운지 가벼운지는 알려 주지 않습니다.", "게임 설명");

    private void AskGiveUp()
    {
        if (!ConfirmDialog.Ask(this, "천칭 퍼즐을 포기하겠습니까?", "포기한다")) return;
        _game.GiveUp();
        Close();
    }

    private void Sync()
    {
        _line.Text = $"  금화 {_game.Coins}닢   천칭 {_game.Weighed}/{CoinPuzzle.Weighings}회" +
                     $"   왼쪽 {_game.Left.Count} · 오른쪽 {_game.Right.Count}";

        // 마지막으로 단 결과대로 천칭을 기울인다.
        var tilt = _game.Log.Count == 0 ? CoinPuzzle.Tilt.Level : _game.Log[^1].Result;
        _scale.Source = Picture(tilt switch
        {
            CoinPuzzle.Tilt.Left => "coin-scale-1.png",
            CoinPuzzle.Tilt.Right => "coin-scale-2.png",
            _ => "coin-scale-0.png",
        });

        for (int i = 0; i < _game.Coins; i++)
        {
            int pan = _game.PanOf(i);
            _coin[i].BorderBrush = pan > 0 ? OnLeft : pan < 0 ? OnRight : Brushes.Transparent;
        }

        _log.Children.Clear();
        foreach (var (record, n) in _game.Log.Select((r, n) => (r, n + 1)))
        {
            string left = string.Join(" ", record.Left.Select(c => c + 1));
            string right = string.Join(" ", record.Right.Select(c => c + 1));
            string mark = record.Result switch
            {
                CoinPuzzle.Tilt.Left => "＞",
                CoinPuzzle.Tilt.Right => "＜",
                _ => "＝",
            };
            _log.Children.Add(new GameUi.GameLabel(GameFont.WhiteColor)
            {
                Text = $"{n}회  [{left}]  {mark}  [{right}]",
            });
        }
    }

    /// <summary>
    /// 놀이를 한 판 하고 <c>0x00450C2D</c> 이 하듯 결과를 알린다.
    /// </summary>
    /// <remarks>
    /// 삯 3000닢은 <b>놀이 속 천칭</b>에서만 나온다 — <c>0x00450C4C</c> 가
    /// <c>[0x154] != 0</c> 일 때만 <c>0x0047CBC0(0xBB8)</c> 을 부르는데, 그 값은
    /// 들어올 때 받은 인자이고 미니 게임은 0 을 준다(<c>0x0045FB54</c>).
    /// </remarks>
    public static void Play(Window owner, Random rng)
    {
        var dialog = new CoinPuzzleDialog(rng) { Owner = owner };
        dialog.ShowDialog();

        if (dialog._game.Won == true)
            NoticeDialog.Show(owner,
                "무게가 다른 금화를 잘 가려낸 것 같다. 천칭은 평형을 이루고" +
                Environment.NewLine + "보물 상자를 무사히 가질 수 있었다.", "게임 클리어");
        else
            NoticeDialog.Show(owner,
                "가려야 할 금화를 잘못 고른 것 같다. 천칭은 기울어지고 말았다.",
                "클리어 실패");
    }
}
