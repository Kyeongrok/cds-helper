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
///   0x00549E10  파트 0 — 금화 32x32 <b>스물여덟 장</b>
///                        0~12 번호 새긴 1~13 (밝은 벌) · 13~25 같은 열셋(어두운 벌)
///                        26~27 납작하게 누운 둘
///   0x00549E20  파트 1 — 기둥 64x160 · 대 176x16 · 나무 천칭 192x144 둘
///                        · 금 천칭 208x168 셋
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
/// 데와 자취를 적는 데다. <b>그 둘 말고는 아무것도 얹지 않는다</b> — 게임 화면에 없는
/// 것을 덧대면 그만큼 게임이 아니게 된다.
///
/// <b>천칭은 한 장이 아니라 조각을 겹쳐 세운다.</b> 기둥과 받침을 놓고, 평형이면 곧은
/// 대에 접시 둘을 걸고, 기울면 그 벌(나무 천칭 192x144)로 갈아 끼운다. 자리는 게임
/// 화면을 448x384 로 되돌려 조각마다 맞춰 찾은 것이다.
/// <code>
///   기둥 coin-post   ( 97, 44)  64x160      받침 coin-stand  ( 88, 29)  96x48
///   대   coin-beam   ( 51, 53) 176x16
///   접시 coin-pan-1  ( 27, 65)  80x144      coin-pan-0       (163, 65)  80x144
///   기움 coin-wood-0 ( 30, 42) 192x144      coin-wood-1      ( 30, 41)  192x144
/// </code>
/// <b>금 천칭은 안 쓴다</b> — 왼 접시에 얹힌 장식은 다 풀고 난 뒤에 나오는 것이다.
///
/// 금화는 <b>끌어다 접시에 놓을 수</b> 있고, 딸깍으로도 놓인다(왼쪽 단추가 왼접시,
/// 오른쪽 단추가 오른접시). 접시에 올린 금화는 쟁반에서 빠져 접시에 쌓이는데,
/// <b>쟁반의 빈자리는 그대로 둔다</b> — 남은 금화가 앞으로 당겨지지 않는다.
/// </remarks>
internal sealed class CoinPuzzleDialog : InfoDialog
{
    private const int SceneWidth = 448, SceneHeight = 384;

    /// <summary>화면 점 기준 곱. <see cref="GameUi.PixelZoom"/> 이 배율로 나눈다.</summary>
    private const int Zoom = 2;

    /// <summary>천칭 조각들이 놓이는 자리. 게임 화면에서 맞춰 찾은 것이다.</summary>
    private static readonly (int X, int Y) PostAt = (97, 44), StandAt = (88, 29),
                                           BeamAt = (51, 53),
                                           LeftPanAt = (27, 65), RightPanAt = (163, 65);

    /// <summary>기운 벌 둘 — 0 은 왼쪽이 내려간 것, 1 은 오른쪽이 내려간 것.</summary>
    private static readonly (int X, int Y)[] WoodAt = [(30, 42), (30, 41)];

    /// <summary>
    /// 단추 자리. 아는 둘이 <c>0x00452709</c> 의 (112, 240) 과 <c>0x0045274A</c> 의
    /// (192, 240) 이고 — 테 8점을 빼면 104 · 184 다. 간격이 80 이니 첫 단추는 24 다.
    /// </summary>
    private const int ButtonY = 232, ButtonW = 64, ButtonH = 32;
    private static readonly int[] ButtonX = [24, 104, 184];

    /// <summary>
    /// 금화를 늘어놓는 흰 테 칸. <b>배경 그림과 게임 화면을 재어 맞춘 것이다.</b>
    /// </summary>
    /// <remarks>
    /// <c>coin-bg.png</c> 의 흰 테가 가로 <c>271~272</c>·<c>430~431</c>, 세로
    /// <c>16~17</c>·<c>270~271</c> 이라 속이 <c>(273, 18)</c> 에서 157x252 다.
    /// 게임 화면에서 금화가 <b>한 줄에 셋</b>이고, 칸 속을 1 로 보면 금화가 0.199 ·
    /// 간격이 0.292 · 첫 칸이 왼쪽에서 0.062 · 위에서 0.057 이다. 그걸 157·252 에
    /// 옮기면 아래 값이 된다.
    /// </remarks>
    private const int TrayX = 283, TrayY = 32, TrayStep = 46, TrayPer = 3;

    /// <summary>자취를 적는 아래 검은 칸.</summary>
    private const int LogX = 34, LogY = 296;

    /// <summary>
    /// 두 접시의 가운데와 <b>금화가 얹히는 높이</b> — 천칭 그림에서 잰 것이다.
    /// </summary>
    /// <remarks>
    /// 접시 그림에서 <b>가장 넓은 줄</b>을 찾고 거기서 여섯 점을 올린 데가 금화 자리다 —
    /// 평평한 접시(<c>coin-pan-1</c>)가 <c>y 85</c> 에서 가장 넓고 금화가 <c>79</c> 에
    /// 놓이는 것을 기준으로 삼았다. 기운 벌도 같은 규칙으로 쟀다.
    /// </remarks>
    private static readonly (int X, int Y) LevelLeftPile = (43, 144), LevelRightPile = (179, 144);

    /// <summary>기운 벌에서 금화가 쌓이는 자리 — <c>[기움][0]</c> 왼쪽 · <c>[기움][1]</c> 오른쪽.</summary>
    private static readonly (int X, int Y)[][] WoodPile =
    [
        [(47, 164), (171, 130)],   // 왼쪽이 내려갔다
        [(46, 130), (172, 163)],   // 오른쪽이 내려갔다
    ];

    /// <summary>금화 한 닢을 더 얹을 때마다 올라가는 높이.</summary>
    private const int StackRise = 4;


    /// <summary>
    /// 금화를 끌어다 놓는 두 접시의 네모 — 금 천칭 그림 안에서 잰 자리다.
    /// </summary>
    /// <remarks>
    /// 기둥(<c>97~161</c>)을 비켜 좌우만 잡는다. 접시가 기울어도 그 언저리를 벗어나지
    /// 않으므로 줄까지 넉넉히 덮어 둔다.
    /// </remarks>
    private static readonly (int X, int Y, int W, int H) LeftPan = (20, 60, 76, 130);
    private static readonly (int X, int Y, int W, int H) RightPan = (156, 60, 76, 130);

    private readonly CoinPuzzle _game;
    private readonly Canvas _scene = new() { Width = SceneWidth, Height = SceneHeight };

    /// <summary>천칭에서 <b>기울기에 따라 갈아 끼우는</b> 조각들. 기둥·받침은 안 바뀐다.</summary>
    private readonly List<Image> _arm = [];
    private readonly Border[] _coin;
    private readonly StackPanel _log = new();

    /// <summary>접시에 쌓아 둔 납작 금화들. 다시 그릴 때마다 걷고 새로 놓는다.</summary>
    private readonly List<Image> _piled = [];

    private CoinPuzzleDialog(Random rng)
    {
        _game = new CoinPuzzle(rng);
        _coin = new Border[_game.Coins];

        Lay(Picture("coin-bg.png"), 0, 0, SceneWidth, SceneHeight);

        // 기둥과 받침은 늘 그 자리다. 대와 접시는 기울기마다 갈아 끼운다.
        Lay(Picture("coin-post.png"), PostAt.X, PostAt.Y, 64, 160);
        Lay(Picture("coin-stand.png"), StandAt.X, StandAt.Y, 96, 48);

        // 금화를 오른쪽 칸에 늘어놓는다. 왼쪽 단추로 왼접시, 오른쪽 단추로 오른접시.
        for (int i = 0; i < _game.Coins; i++)
        {
            int coin = i;
            var box = new Border
            {
                Width = 32,
                Height = 32,
                Background = new ImageBrush(Face(coin)) { Stretch = Stretch.Fill },
                Cursor = Cursors.Hand,
            };
            box.MouseLeftButtonDown += (_, e) => Grab(coin, e);
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

        // 끌고 다니는 동안 손끝에 붙어 다니는 금화.
        _ghost.IsHitTestVisible = false;
        _ghost.Visibility = Visibility.Collapsed;
        Panel.SetZIndex(_ghost, 80);
        _scene.Children.Add(_ghost);

        _scene.Background = Brushes.Transparent;
        _scene.MouseLeftButtonDown += (_, e) => e.Handled = true;

        // 집는 순간 판이 손을 잡으므로 뗌은 늘 판에 온다 — 놓는 데는 좌표로 짚는다
        // (부대배치 창과 같은 까닭이다).
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

        // 오른쪽 단추는 <b>두 가지</b>를 한다 — 접시에 올린 금화를 내리고, 차림표를 편다.
        // 예전에는 내리기만 했다.
        MouseRightButtonUp += (_, e) =>
        {
            _game.Clear();
            Sync();
            GameUi.ContextMenuAt(this, e.GetPosition(this), Commands());
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

    // ── 끌어다 놓기 ────────────────────────────────────────────────────────────

    private readonly Image _ghost = new() { Width = 32, Height = 32 };

    /// <summary>지금 끌고 있는 금화. 안 끌고 있으면 −1.</summary>
    private int _held = -1;

    /// <summary>집은 자리 — 여기서 얼마쯤 움직여야 「끌었다」로 친다.</summary>
    private Point _grabbed;
    private bool _dragging;

    /// <summary>끌었다고 치는 거리(판 점).</summary>
    private const double DragSlop = 4;

    private void Grab(int coin, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_game.Won != null) return;

        _held = coin;
        _dragging = false;
        _grabbed = e.GetPosition(_scene);
        _scene.CaptureMouse();
    }

    private void Drag(object sender, MouseEventArgs e)
    {
        if (_held < 0) return;

        var now = e.GetPosition(_scene);
        if (!_dragging)
        {
            if (Math.Abs(now.X - _grabbed.X) < DragSlop && Math.Abs(now.Y - _grabbed.Y) < DragSlop)
                return;

            _dragging = true;
            _ghost.Source = Face(_held);
            RenderOptions.SetBitmapScalingMode(_ghost, BitmapScalingMode.NearestNeighbor);
            _ghost.Visibility = Visibility.Visible;
        }
        Canvas.SetLeft(_ghost, now.X - 16);
        Canvas.SetTop(_ghost, now.Y - 16);
    }

    private void Land(object sender, MouseButtonEventArgs e)
    {
        if (_held < 0) return;
        e.Handled = true;

        int coin = _held;
        bool dragged = _dragging;
        var now = e.GetPosition(_scene);
        Release();

        // 끌지 않고 딸깍했으면 예전처럼 왼접시에 놓는다.
        if (!dragged) { Tap(coin, left: true); return; }

        if (In(now, LeftPan)) Tap(coin, left: true);
        else if (In(now, RightPan)) Tap(coin, left: false);
        else if (_game.PanOf(coin) != 0) { _game.Clear(); Sync(); }   // 접시 밖에 내려놓으면 내린다
    }

    private void Release()
    {
        _held = -1;
        _dragging = false;
        _ghost.Visibility = Visibility.Collapsed;
        _scene.ReleaseMouseCapture();
    }

    private static bool In(Point at, (int X, int Y, int W, int H) box) =>
        at.X >= box.X && at.X < box.X + box.W && at.Y >= box.Y && at.Y < box.Y + box.H;

    /// <summary>쟁반에 놓는 번호 새긴 금화.</summary>
    /// <remarks>
    /// 어두운 벌(<c>coin-face-dim-*</c>)도 뽑아 두었지만 여기서는 안 쓴다 — 접시에 올린
    /// 금화는 쟁반에서 빠지고 <b>납작하게 누운 벌</b>로 접시에 쌓인다.
    /// </remarks>
    private static BitmapImage? Face(int coin) => Picture($"coin-face-{coin}.png");

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
            NoticeDialog.Explain(this,
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
        NoticeDialog.Explain(this,
            "금 천칭에는 함정이 있습니다. 함정에 빠지지 않게 하기 위해서는 무게가 다른 " +
            "금화를 가려내고 천칭이 평형을 이루게 해야 합니다." + Environment.NewLine +
            Environment.NewLine +
            "나무 천칭을 3번까지 쓰고 무게가 다른 금화를 선택해 주십시오." +
            Environment.NewLine + Environment.NewLine +
            "금화를 접시로 끌어다 놓으면 그 접시에 실립니다. 왼쪽 단추로 누르면 왼쪽 " +
            "접시에, 오른쪽 단추로 누르면 오른쪽 접시에 놓입니다. 접시 하나에 여섯 " +
            "닢까지 놓을 수 있고, 양쪽 수가 같아야 답니다." +
            Environment.NewLine +
            "가짜가 무거운지 가벼운지는 알려 주지 않습니다.");

    private void AskGiveUp()
    {
        if (!ConfirmDialog.Ask(this, "천칭 퍼즐을 포기하겠습니까?", "포기한다")) return;
        _game.GiveUp();
        Close();
    }

    private void Sync()
    {
        // 마지막으로 단 결과대로 천칭을 기울인다.
        var tilt = _game.Log.Count == 0 ? CoinPuzzle.Tilt.Level : _game.Log[^1].Result;

        foreach (var image in _arm) _scene.Children.Remove(image);
        _arm.Clear();

        (int X, int Y) leftPile, rightPile;
        if (tilt == CoinPuzzle.Tilt.Level)
        {
            Arm("coin-beam.png", BeamAt, 176, 16);
            Arm("coin-pan-1.png", LeftPanAt, 80, 144);
            Arm("coin-pan-0.png", RightPanAt, 80, 144);
            (leftPile, rightPile) = (LevelLeftPile, LevelRightPile);
        }
        else
        {
            int down = tilt == CoinPuzzle.Tilt.Left ? 0 : 1;
            Arm($"coin-wood-{down}.png", WoodAt[down], 192, 144);
            (leftPile, rightPile) = (WoodPile[down][0], WoodPile[down][1]);
        }

        // 쟁반 — 접시에 올린 것만 숨긴다. <b>빈자리는 그대로 둔다</b> — 게임도 남은
        // 금화를 앞으로 당기지 않는다(1·2 를 올리면 3 이 첫 줄 오른쪽에 홀로 남는다).
        for (int i = 0; i < _game.Coins; i++)
            _coin[i].Visibility = _game.PanOf(i) != 0 ? Visibility.Collapsed : Visibility.Visible;

        // 접시 — 납작하게 누운 금화를 쌓는다.
        foreach (var image in _piled) _scene.Children.Remove(image);
        _piled.Clear();
        Pile(_game.Left.Count, leftPile);
        Pile(_game.Right.Count, rightPile);

        // 검은 칸에는 <b>늘 세 줄</b>이 서 있다 — 게임도 「1-」「2-」「3-」을 미리 그어
        // 두고 잰 차례대로 채운다. 아직 안 잰 첫 줄에는 지금 접시에 올린 것을 보인다.
        _log.Children.Clear();
        for (int n = 0; n < CoinPuzzle.Weighings; n++)
        {
            string body;
            if (n < _game.Log.Count)
            {
                var record = _game.Log[n];
                body = $"{Numbers(record.Left)}  {record.Result switch
                {
                    CoinPuzzle.Tilt.Left => "＞",
                    CoinPuzzle.Tilt.Right => "＜",
                    _ => "＝",
                }}  {Numbers(record.Right)}";
            }
            else if (n == _game.Log.Count)
            {
                body = $"{Numbers(_game.Left)}     {Numbers(_game.Right)}".TrimEnd();
            }
            else body = "";

            _log.Children.Add(new GameUi.GameLabel(GameFont.WhiteColor)
            {
                Text = $"{n + 1}-   {body}",
            });
        }
    }

    /// <summary>금화 번호를 늘어놓는다. 표에는 0부터지만 사람에게는 1부터다.</summary>
    private static string Numbers(IReadOnlyList<int> coins) =>
        string.Join(" ", coins.Select(c => c + 1));

    /// <summary>접시 하나에 금화 <paramref name="count"/> 닢을 쌓는다.</summary>
    private void Pile(int count, (int X, int Y) at)
    {
        for (int i = 0; i < count; i++)
        {
            var image = Piece(Picture($"coin-gold-{i % 2}.png"), at.X, at.Y - i * StackRise, 32, 32);
            Panel.SetZIndex(image, 40 + i);
            _piled.Add(image);
        }
    }

    /// <summary>기울기마다 갈아 끼우는 조각 하나.</summary>
    private void Arm(string art, (int X, int Y) at, int width, int height)
    {
        var image = Piece(Picture(art), at.X, at.Y, width, height);
        Panel.SetZIndex(image, 20);
        _arm.Add(image);
    }

    /// <summary>그림 한 조각을 판에 놓고 그 <see cref="Image"/> 를 낸다.</summary>
    private Image Piece(BitmapImage? art, int x, int y, int width, int height)
    {
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
        return image;
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
