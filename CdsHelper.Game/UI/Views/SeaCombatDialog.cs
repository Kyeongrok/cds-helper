using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CdsHelper.Game.Engine.Sea;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 해전 판 — 원본 800x600 화면 그대로 띠·바다·배를 깔고, 이동을 찍고, 「Set」으로 한 턴을 굴린다.
/// </summary>
/// <remarks>
/// 셈은 <see cref="SeaBattle"/> 이 다 하고 여기서는 그리고 받기만 한다. 그림은 게임 것 그대로다
/// (<see cref="CombatArt"/>, 볼트 <c>61.분석-해전 그림</c>).
/// <code>
///   위 띠      bar-b-00  800x32   (0, 0)
///   아래 띠    bar-b-01  800x32   (0, 568) — 오른쪽 끝에 Set · Cancel 이 그려져 있다
///   바다 바탕  sea-00    800x600  (0x40, 0x20) − 스크롤                     ; 0x0043FFC7
///   칸·배      x = X*32 − 스크롤 + 0x38 ,  y = (Y+1)*32 − 스크롤 + (X 짝수 ? 16 : 0)   ; 0x0044006C
/// </code>
/// 판(23x17)이 다 들어가 스크롤은 0 이다. 좌우 기둥·나침반·E·A 글자 조각은 아직 안 뽑아
/// E 는 글자로 대신 찍고, 기둥 자리는 비워 둔다.
///
/// 이동 지시 중에는 원본처럼 <b>이동력 안의 칸을 육각 테로</b> 깔고(cell-00), 커서가 놓인
/// 후보 칸까지의 길을 <b>회색 칸</b>(cell-01)으로 칠한다.
/// </remarks>
public sealed class SeaCombatDialog : GameWindow
{
    /// <summary>해전이 어떻게 끝났는지.</summary>
    public enum Outcome
    {
        /// <summary>내 배가 모두 퇴각했거나 적이 모두 물러갔다.</summary>
        Escaped,

        /// <summary>항복했다(뒤처리는 다음 단계).</summary>
        Surrendered,

        /// <summary>적을 모두 물리쳤다(전리품·명성은 다음 단계).</summary>
        Won,

        /// <summary>내 배가 모두 가라앉았다(뒤처리는 다음 단계).</summary>
        Defeated,
    }

    /// <summary>포격 소리 파트 — 사운드 ID 0x29 발사 · 0x2A 명중 · 0x2B 빗나감 · 0x2E 격침(ID − 28).</summary>
    private const int FirePart = 0x29 - 28, HitPart = 0x2A - 28, MissPart = 0x2B - 28, SinkPart = 0x2E - 28;

    /// <summary>포격 연출의 한 장 참과 포탄이 날아가는 걸음 수(<c>0x004384E8</c>).</summary>
    private static readonly TimeSpan FxFrame = TimeSpan.FromMilliseconds(60);
    private const int BallSteps = 9;

    /// <summary>원본 화면 크기.</summary>
    private const int ScreenWidth = CombatArt.SeaWidth, ScreenHeight = CombatArt.SeaHeight;

    /// <summary>위아래 띠 높이.</summary>
    private const int BandHeight = 32;

    /// <summary>아래 띠에 그려진 Set · Cancel 자리(가로).</summary>
    private const double SetLeft = 672, CancelLeft = 736;

    /// <summary>아래 띠의 윗변(넓은 화면은 한 줄 위로 올라 560 이다)과 기둥 키.</summary>
    private const int BottomBandTop = 560, FrameHeight = 504;

    /// <summary>나침반 자리와 크기(112x112, 자리는 갈무리로 잼).</summary>
    private const int CompassX = 64, CompassY = 32, CompassSize = 112;

    /// <summary>나침반 위에 겹치는 풍향 조각.</summary>
    private readonly Image _compass = new();

    /// <summary>한 걸음 사이의 참.</summary>
    private static readonly TimeSpan StepSpan = TimeSpan.FromMilliseconds(220);

    private readonly SeaBattle _battle;
    private readonly CombatArt _art;
    private readonly Enemy _foe;
    private readonly uint[]? _face;
    private readonly SoundBank? _sfx;

    /// <summary>포격 연출(포탄·폭발·물기둥·피해 숫자)을 얹는 층.</summary>
    private readonly Canvas _fx = new() { IsHitTestVisible = false };

    private readonly Canvas _board = new() { Width = ScreenWidth, Height = ScreenHeight, ClipToBounds = true };
    private readonly Canvas _marks = new() { IsHitTestVisible = false };
    private readonly Canvas _path = new() { IsHitTestVisible = false };
    private readonly Dictionary<int, Image> _shipArt = [];

    private SeaBattle.Ship? _picked;
    private List<(List<SeaBattle.Move> Plan, int X, int Y, int Way)> _options = [];
    private bool _running;

    public Outcome Result { get; private set; } = Outcome.Surrendered;

    private SeaCombatDialog(SeaBattle battle, CombatArt art, in Enemy foe, uint[]? face, double zoom,
                            SoundBank? sfx)
    {
        _battle = battle;
        _art = art;
        _foe = foe;
        _face = face;
        _sfx = sfx;
        Panel.SetZIndex(_fx, 25);
        _board.Children.Add(_fx);

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = Brushes.Black;

        // 바다는 (0x40, 0x20) 에 깔린다 — 틀 아래로 들어간 만큼은 가려진다.
        Put(_board, art.Sea(), 0x40, 0x20, CombatArt.SeaWidth, CombatArt.SeaHeight, z: 0);

        // 틀 — 넓은 화면(800) 갈래의 파트 4 를 자른 것이다(볼트 61 의 5-2 절).
        //   위 띠 (0,0) 800x32 · 기둥 머리 (0,32)·(736,32) 64x32 · 기둥 (0,64)·(736,64) 64x504 · 아래 띠 (0,560)
        Put(_board, art.Path_("bar-b-00"), 0, 0, ScreenWidth, BandHeight, z: 30);
        Put(_board, art.Path_("pair-00"), 0, BandHeight, 64, 32, z: 30);
        Put(_board, art.Path_("pair-01"), ScreenWidth - 64, BandHeight, 64, 32, z: 30);
        Put(_board, art.Path_("frame-left"), 0, 64, 64, FrameHeight, z: 30);
        Put(_board, art.Path_("frame-right"), ScreenWidth - 64, 64, 64, FrameHeight, z: 30);
        Put(_board, art.Path_("bar-b-01"), 0, BottomBandTop, ScreenWidth, BandHeight, z: 30);

        // 나침반 — 장미(파트 19 첫 조각) 위에 풍향+1 번째 조각을 겹친다. 처음 자리는 갈무리로 잰 값이고,
        // 원본처럼 끌어다 옮길 수 있다(나침반은 제 창을 가진 딸 창이다 — 0x004337C0).
        var rose = new Canvas
        {
            Width = CompassSize,
            Height = CompassSize,
            Background = Brushes.Transparent,
            Cursor = Cursors.SizeAll,
        };
        Put(rose, art.Path_("compass-00"), 0, 0, CompassSize, CompassSize, z: 0);
        _compass.Width = _compass.Height = CompassSize;
        _compass.IsHitTestVisible = false;
        RenderOptions.SetBitmapScalingMode(_compass, GameUi.SpriteScaling);
        Panel.SetZIndex(_compass, 1);
        rose.Children.Add(_compass);
        Canvas.SetLeft(rose, CompassX);
        Canvas.SetTop(rose, CompassY);
        Panel.SetZIndex(rose, 20);
        _board.Children.Add(rose);
        DragCompass(rose);

        Panel.SetZIndex(_marks, 5);
        _board.Children.Add(_marks);
        Panel.SetZIndex(_path, 6);
        _board.Children.Add(_path);

        foreach (var ship in battle.Ships)
        {
            var image = new Image { Width = CombatArt.ShipSize, Height = CombatArt.ShipSize, IsHitTestVisible = false };
            RenderOptions.SetBitmapScalingMode(image, GameUi.SpriteScaling);
            Panel.SetZIndex(image, 10);
            _board.Children.Add(image);
            _shipArt[ship.Index] = image;
        }

        _board.Background = Brushes.Transparent;
        _board.MouseLeftButtonUp += Touch;
        _board.LayoutTransform = new ScaleTransform(zoom, zoom);

        // 판 아래에 말 줄은 없다 — 원본은 말을 모두 창으로 띄운다.
        Content = _board;

        // 배를 우클릭하면 「해전전황정보(선박)」 — 아군·적 모두. 빈 바다면 항복 차림표다.
        MouseRightButtonUp += (_, e) =>
        {
            if (_running) return;
            var (x, y) = CellAt(e.GetPosition(_board));
            if (x >= 0 && _battle.ShipAt(x, y) is { } ship)
            {
                SeaShipInfoDialog.Show(this, ship);
                return;
            }
            GameUi.ContextMenuAt(this, e.GetPosition(this),
                                 [("항복한다", Surrender), ("게임 복귀", () => { })]);
        };

        Loaded += (_, _) =>
        {
            Redraw();
            // 들머리 — 바람과 퇴각지점을 알리고 이동 지시를 재촉한다(0x0043C4E0).
            Say(_battle.WindNotice());
            Say(_battle.OrderPrompt());
        };
    }

    // ── 그리기 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 나침반을 끌어다 옮긴다 — 판 안(바다 쪽)에서만 움직이고, 누름은 판의 칸 누름으로 새지 않는다.
    /// </summary>
    private void DragCompass(Canvas rose)
    {
        Point grab = default;
        rose.MouseLeftButtonDown += (_, e) =>
        {
            grab = e.GetPosition(rose);
            rose.CaptureMouse();
            e.Handled = true;
        };
        rose.MouseMove += (_, e) =>
        {
            if (!rose.IsMouseCaptured) return;
            var at = e.GetPosition(_board);
            Canvas.SetLeft(rose, Math.Clamp(at.X - grab.X, 64, ScreenWidth - 64 - CompassSize));
            Canvas.SetTop(rose, Math.Clamp(at.Y - grab.Y, BandHeight, BottomBandTop - CompassSize));
        };
        rose.MouseLeftButtonUp += (_, e) =>
        {
            rose.ReleaseMouseCapture();
            e.Handled = true;
        };
    }

    /// <summary>칸 조각(48x32)의 왼쪽 위 — 원본 식 그대로다(<c>0x0044006C</c>).</summary>
    private static (double X, double Y) ScreenOf(int x, int y) =>
        (x * CombatArt.Cell + 0x38, (y + 1) * CombatArt.Cell + ((x & 1) == 0 ? 16 : 0));

    /// <summary>
    /// 열두 방향 그림에서 육각 방향 하나를 고른다 — 두 장마다 한 장이다.
    /// </summary>
    /// <remarks>
    /// 그림 열둘은 <b>0 이 위(돛 뒤가 보인다), 6 이 아래(흰 돛 앞이 보인다)</b>이고 시계 방향으로 돈다 —
    /// 육각 방향 0(위)~5 와 같은 차례라 두 장마다 한 장이다. 처음에 +6 을 먹여 180도 돌아가 있었고,
    /// 그 뒤 좌우만 뒤집어 위아래가 거꾸로 나왔다.
    /// </remarks>
    private static int FrameOf(int way) => ((way * 2) % CombatArt.Ways + CombatArt.Ways) % CombatArt.Ways;

    private void Redraw()
    {
        _marks.Children.Clear();

        // 나침반의 풍향 조각(compass-01~06 = 풍향 0~5).
        _compass.Source = Bitmap(_art.Path_($"compass-{_battle.Wind + 1:D2}"));

        // 퇴각 지대 — 원본 화면의 큰 고딕 E(mark-09, 32x32).
        foreach (var (x, y) in _battle.RetreatCells())
        {
            var (sx, sy) = ScreenOf(x, y);
            Put(_marks, _art.Path_("mark-09"), sx + 8, sy, 32, 32, z: 0);
        }

        // 고른 배의 이동력 안 칸을 육각 테로 깐다.
        if (_picked is { } picked)
        {
            for (int x = 0; x < SeaBattle.Cols; x++)
                for (int y = 0; y < SeaBattle.Rows; y++)
                {
                    if (!SeaBattle.OnBoard(x, y)) continue;
                    if (SeaBattle.Distance(picked.X, picked.Y, x, y) > picked.Power) continue;
                    Cell(_marks, x, y, lit: false);
                }

            // 찍어 둔 길은 회색 칸으로 칠한다 — 누른 칸까지의 길이다.
            if (picked.Plan.Count > 0
                && SeaBattle.Trace(picked.X, picked.Y, picked.Way, picked.Plan) is { Count: > 0 } trail)
            {
                foreach (var (px, py, _) in trail) Cell(_marks, px, py, lit: true);

                // 마지막 칸에는 그 칸에서 볼 방향의 화살표(mark-00~05 = 방향 0~5)를 얹는다.
                var (lx, ly, lway) = trail[^1];
                var (ax, ay) = ScreenOf(lx, ly);
                Put(_marks, _art.Path_($"mark-{lway:D2}"), ax + 8, ay, 32, 32, z: 0);
            }
        }

        foreach (var ship in _battle.Ships)
        {
            var image = _shipArt[ship.Index];
            image.Visibility = ship.CanAct ? Visibility.Visible : Visibility.Collapsed;
            if (!ship.CanAct) continue;

            image.Source = Bitmap(_art.Ship(ship.Art, FrameOf(ship.Way)));
            var (sx, sy) = ScreenOf(ship.X, ship.Y);

            // 내 배 발밑 칸 — 지시를 마쳤으면(부딪혀 못 움직이는 배 포함) 회색, 아직이면 빈 테다.
            if (ship.Mine) Cell(_marks, ship.X, ship.Y, lit: ship.Ordered || ship.Stuck);
            Canvas.SetLeft(image, sx);
            Canvas.SetTop(image, sy - 12);

            // 배 옆 글자 — 아군 파란 A(dot-01), 적 붉은 E(dot-02, 짐작).
            Put(_marks, _art.Path_(ship.Mine ? "dot-01" : "dot-02"), sx + (ship.Mine ? 4 : 36), sy + (ship.Mine ? 0 : 22), 8, 8, z: 0);
        }
    }

    private void Cell(Canvas layer, int x, int y, bool lit)
    {
        if (Bitmap(_art.CellArt(lit)) is not { } source) return;
        var (sx, sy) = ScreenOf(x, y);
        var image = new Image { Source = source, Width = 48, Height = 32 };
        RenderOptions.SetBitmapScalingMode(image, GameUi.SpriteScaling);
        Canvas.SetLeft(image, sx);
        Canvas.SetTop(image, sy);
        layer.Children.Add(image);
    }

    /// <summary>
    /// 말은 판 위에 「해전」 창으로 띄운다 — 부관(없으면 뱃사람) 얼굴이다. 적의 움직임까지 끝나면
    /// 「각 함대에 이동 지시를 내려 주십시오.」가 이 창으로 뜨며 내 턴이 열린다.
    /// </summary>
    private void Say(string text) => ConfirmDialog.Tell(this, text, BattleTitle, _face);

    // ── 받기 ──────────────────────────────────────────────────────────────

    /// <summary>화면 점에서 가장 가까운 칸.</summary>
    private static (int X, int Y) CellAt(Point at)
    {
        int bestX = -1, bestY = -1;
        double best = double.MaxValue;
        for (int x = 0; x < SeaBattle.Cols; x++)
            for (int y = 0; y < SeaBattle.Rows; y++)
            {
                if (!SeaBattle.OnBoard(x, y)) continue;
                var (sx, sy) = ScreenOf(x, y);
                double dx = at.X - (sx + 24), dy = at.Y - (sy + 16);
                double d = dx * dx + dy * dy;
                if (d < best) { best = d; bestX = x; bestY = y; }
            }
        return best > 40 * 40 ? (-1, -1) : (bestX, bestY);
    }

    private void Touch(object sender, MouseButtonEventArgs e)
    {
        if (_running) return;
        var at = e.GetPosition(_board);

        // 아래 띠의 Set · Cancel.
        if (at.Y >= BottomBandTop)
        {
            if (at.Y < BottomBandTop + BandHeight)
            {
                if (at.X >= CancelLeft) ClearOrders();
                else if (at.X >= SetLeft) Decide();
            }
            return;
        }

        var (x, y) = CellAt(at);
        if (x < 0) return;

        var here = _battle.ShipAt(x, y);

        if (here is { Mine: true })
        {
            // 퇴각 지대(E)에 선 배를 누르면 곧바로 「해전」 창에 「퇴각하겠습니까?」를 묻는다(0x0056B600).
            // 아니오면 여느 때처럼 고른다.
            if (!ReferenceEquals(here, _picked) && _battle.IsRetreatCell(here.X, here.Y)
                && ConfirmDialog.Ask(this, "퇴각하겠습니까?", BattleTitle))
            {
                _battle.Retreat(here);
                Unpick();
                if (_battle.AllMineGone) Finish();
                else AfterOrder();
                return;
            }

            if (ReferenceEquals(here, _picked))
            {
                _battle.Order(here, []);
                Unpick();
                ConfirmDialog.Tell(this, "이동하지 않습니다", BattleTitle);
                AfterOrder();
                return;
            }

            if (here.Stuck)
            {
                Say("충돌 영향으로 다음 지시를 받을 때까지 이동할 수 없습니다.");
                return;
            }

            _picked = here;
            _options = _battle.Options(here);
            Redraw();
            return;
        }

        if (_picked is not { } ship) return;

        var hit = _options.FirstOrDefault(o => o.X == x && o.Y == y);
        if (hit.Plan == null) return;

        // 누르면 그 칸까지의 길이 회색으로 칠해지고 「해전」 창이 결정을 알린다(0x0056B698·0x0056B660).
        _battle.Order(ship, hit.Plan);
        Redraw();
        ConfirmDialog.Tell(this,
            hit.Plan.All(m => m == SeaBattle.Move.Straight) ? "이동 전방 결정!" : "선회 방향 결정!",
            BattleTitle);
        // 확인을 누르면 벌집(테·길)은 걷히고, 그 배 발밑 칸이 회색으로 바뀌어 지시가 끝났음을 보인다.
        Unpick();
        AfterOrder();
    }

    /// <summary>해전 창 제목.</summary>
    private const string BattleTitle = "해전";

    /// <summary>
    /// 지시할 수 있는 내 배가 모두 지시를 마쳤으면 「이동 계획을 종료하겠습니까?」를 묻는다.
    /// </summary>
    private void AfterOrder()
    {
        if (_battle.Ships.Where(s => s.Mine && s.CanAct).All(s => s.Ordered || s.Stuck))
            Decide();
    }

    private void Unpick()
    {
        _picked = null;
        _options = [];
        _path.Children.Clear();
        Redraw();
    }

    private void ClearOrders()
    {
        if (_running) return;
        foreach (var ship in _battle.Ships.Where(s => s.Mine && s.CanAct)) ship.Plan.Clear();
        Say(_battle.OrderPrompt());
        Unpick();
    }

    /// <summary>「Set」 — 계획을 마치고 한 턴을 굴린다(<c>0x0043DDD0</c> → <c>0x0043CA60</c>).</summary>
    private void Decide()
    {
        if (_running) return;
        // 「해전」 창에 YES/NO — 얼굴 없이 묻는다(0x0056B5A0).
        if (!ConfirmDialog.Ask(this, "이동 계획을 종료하겠습니까?", BattleTitle)) return;

        _running = true;
        _picked = null;
        _options = [];
        _path.Children.Clear();
        int windBefore = _battle.Wind;
        try
        {
            _battle.EndPlanning();
            _battle.Execute(beat =>
            {
                Redraw();
                foreach (var (mover, hit) in beat.Crashes.Where(c => c.Mover.Mine || c.Hit.Mine))
                    Say(SeaBattle.CrashWord(mover, hit));

                // 「탄약이 떨어졌습니다! 공격할 수 없습니다!」 따위는 얼굴 없는 「해전」 창이다.
                foreach (string notice in beat.Notices)
                    ConfirmDialog.Tell(this, notice, BattleTitle);

                foreach (var volley in beat.Volleys)
                {
                    Animate(volley);
                    Redraw();
                }
                Wait(StepSpan);
            });
            Redraw();
        }
        finally
        {
            _running = false;
        }

        if (_battle.AllMineGone || _battle.AllEnemyGone) { Finish(); return; }

        if (_battle.Wind != windBefore) Say(_battle.WindNotice());
        Say(_battle.OrderPrompt());

        // 지난 턴에 부딪혀 이번 턴에 못 움직이는 배만 남았으면 그대로 다음 계획으로 넘어간다.
        if (_battle.Ships.Where(s => s.Mine && s.CanAct).All(s => s.Stuck))
            Say("충돌 영향으로 다음 지시를 받을 때까지 이동할 수 없습니다.");
    }

    private void Surrender()
    {
        if (_running) return;
        if (!ConfirmDialog.Ask(this, "항복하겠습니까?", face: _face)) return;
        Result = Outcome.Surrendered;
        Close();
    }

    /// <summary>판이 끝났다 — 다 빠져나갔거나 적이 다 물러갔다(<c>0x00435ABF</c> 벌).</summary>
    private void Finish()
    {
        if (_battle.AllEnemyGone)
        {
            // 적을 모두 물리쳤다 — 전리품·명성(0x004354C0)은 다음 단계에서 붙인다.
            Result = Outcome.Won;
            Close();
            return;
        }

        if (_battle.Ships.Any(s => s.Mine && s.State == SeaBattle.ShipState.Retreated))
        {
            Result = Outcome.Escaped;
            Say(_battle.EscapedWord(_foe.Name));
        }
        else
        {
            Result = Outcome.Defeated;
        }
        Close();
    }

    // ── 포격 연출 — 0x004384E8 ────────────────────────────────────────────

    /// <summary>
    /// 한 번의 포격을 그린다 — 발마다 포연·포탄, 맞으면 폭발, 빗나가면 물기둥, 끝에 피해 숫자, 가라앉으면 격침 소리.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   쏠 때    소리 0x29 · 포연 blast-15~17 · 포탄(dot 8x8)이 아홉 걸음에 과녁까지
    ///   맞으면   소리 0x2A · 폭발 blast-00~02 (큰 한 방이면 blast-06~08)
    ///   빗나가면 소리 0x2B · 과녁 곁 칸(rand 4) 에 물기둥 mark-06~08
    ///   피해 숫자 파트 20 의 24x24 — 일의 자리 +0x50 · 십 +0x38 · 백 +0x20, 다섯 박자
    ///   격침     소리 0x2E
    /// </code>
    /// </remarks>
    private void Animate(SeaBattle.Volley volley)
    {
        var (sx, sy) = ScreenOf(volley.Shooter.X, volley.Shooter.Y);
        var (tx, ty) = ScreenOf(volley.Target.X, volley.Target.Y);
        var rng = Random.Shared;

        foreach (var shot in volley.Shots)
        {
            _sfx?.Play(FirePart);

            // 포연 세 장과 포탄이 날아가는 걸음을 함께 흘린다.
            var ball = Sprite(_art.Path_("dot-00"), sx + 20, sy + 12, 8, 8);
            for (int step = 0; step <= BallSteps; step++)
            {
                if (step % 3 == 0 && step / 3 < 3)
                    Flash(_art.Path_($"blast-{15 + step / 3:D2}"), sx, sy - 8, 48, 48);
                if (ball != null)
                {
                    Canvas.SetLeft(ball, sx + 20 + (tx - sx) * step / (double)BallSteps);
                    Canvas.SetTop(ball, sy + 12 + (ty - sy) * step / (double)BallSteps);
                }
                Wait(TimeSpan.FromMilliseconds(25));
            }
            if (ball != null) _fx.Children.Remove(ball);

            if (shot.Hit)
            {
                _sfx?.Play(HitPart);
                int first = shot.Big ? 6 : 0;
                for (int f = 0; f < 3; f++)
                {
                    var blast = Sprite(_art.Path_($"blast-{first + f:D2}"), tx, ty - 8, 48, 48);
                    Wait(FxFrame);
                    if (blast != null) _fx.Children.Remove(blast);
                }
            }
            else
            {
                _sfx?.Play(MissPart);
                var (nx, ny) = SeaBattle.Step(volley.Target.X, volley.Target.Y, rng.Next(SeaBattle.Ways));
                var (wx, wy) = SeaBattle.OnBoard(nx, ny) ? ScreenOf(nx, ny) : (tx, ty);
                for (int f = 0; f < 3; f++)
                {
                    var splash = Sprite(_art.Path_($"mark-{6 + f:D2}"), wx + 8, wy - 4, 32, 32);
                    Wait(FxFrame);
                    if (splash != null) _fx.Children.Remove(splash);
                }
            }
        }

        Wait(FxFrame * 2);

        // 피해 숫자 — 맞은 발의 합. 십·백 자리가 0 이면 안 찍는다.
        int total = volley.Shots.Where(s => s.Hit).Sum(s => s.Damage);
        if (total > 0)
        {
            var digits = new List<Image>();
            int[] places = [total % 10, total / 10 % 10, total / 100 % 10];
            int[] offsets = [0x50, 0x38, 0x20];
            for (int p = 0; p < 3; p++)
            {
                if (p > 0 && total < Math.Pow(10, p)) break;
                if (Sprite(_art.Path_($"digit-{places[p]:D2}"), tx - 0x38 + offsets[p], ty - 24, 24, 24) is { } digit)
                    digits.Add(digit);
            }
            Wait(FxFrame * 5);
            foreach (var digit in digits) _fx.Children.Remove(digit);
        }

        if (volley.Sunk) _sfx?.Play(SinkPart);
    }

    /// <summary>연출 층에 그림 한 장을 올린다. 못 읽으면 null.</summary>
    private Image? Sprite(string? path, double x, double y, double w, double h)
    {
        if (Bitmap(path) is not { } source) return null;
        var image = new Image { Source = source, Width = w, Height = h };
        RenderOptions.SetBitmapScalingMode(image, GameUi.SpriteScaling);
        Canvas.SetLeft(image, x);
        Canvas.SetTop(image, y);
        _fx.Children.Add(image);
        return image;
    }

    /// <summary>그림 한 장을 한 참 띄웠다가 걷는다.</summary>
    private void Flash(string? path, double x, double y, double w, double h)
    {
        var image = Sprite(path, x, y, w, h);
        Wait(FxFrame);
        if (image != null) _fx.Children.Remove(image);
    }

    private static void Wait(TimeSpan span)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(span, DispatcherPriority.Normal,
                                        (_, _) => frame.Continue = false, Dispatcher.CurrentDispatcher);
        try { Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
    }

    private static readonly Dictionary<string, BitmapSource> Cache = [];

    private static BitmapSource? Bitmap(string? path)
    {
        if (path == null) return null;
        if (Cache.TryGetValue(path, out var hit)) return hit;
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.UriSource = new Uri(System.IO.Path.GetFullPath(path));
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.EndInit();
        bmp.Freeze();
        Cache[path] = bmp;
        return bmp;
    }

    private static void Put(Canvas board, string? path, double x, double y, double w, double h, int z)
    {
        if (Bitmap(path) is not { } source) return;
        var image = new Image { Source = source, Width = w, Height = h, IsHitTestVisible = false };
        RenderOptions.SetBitmapScalingMode(image, GameUi.SpriteScaling);
        Canvas.SetLeft(image, x);
        Canvas.SetTop(image, y);
        Panel.SetZIndex(image, z);
        board.Children.Add(image);
    }

    /// <summary>
    /// 판을 게임 창에 맞춰 키운다 — 원본 800x600 이 게임 창의 대부분을 채우게, 넘치지 않는 만큼.
    /// </summary>
    private static double ZoomFor(Window owner)
    {
        var stage = GameUi.RootOf(owner);
        double w = stage.ActualWidth > 0 ? stage.ActualWidth : SystemParameters.WorkArea.Width;
        double h = stage.ActualHeight > 0 ? stage.ActualHeight : SystemParameters.WorkArea.Height;
        double fit = Math.Min(w * 0.92 / ScreenWidth, (h * 0.92 - 40) / ScreenHeight);
        return Math.Max(1.0, Math.Floor(fit * 4) / 4);
    }

    // ── 열기 ──────────────────────────────────────────────────────────────

    /// <summary>
    /// 응전해 해전을 벌인다. 그림을 못 읽었으면 그렇다고 이르고 도망친 것으로 친다.
    /// </summary>
    /// <param name="face">말을 거는 얼굴 — 부관, 없으면 뱃사람.</param>
    /// <param name="seaWind">
    /// 함대 자리의 바다 바람(16방위, 세기). 원본은 이것으로 풍향·세기를 매긴다(<c>0x00441F1C</c>).
    /// 모르면 굴린다.
    /// </param>
    public static Outcome Fight(Window owner, Player player, in Enemy foe, Random rng, uint[]? face,
                                (int Dir, int Strength)? seaWind = null, SoundBank? sfx = null)
    {
        var art = CombatArt.Open();
        if (art == null)
        {
            NoticeDialog.Show(owner, $"해전 그림을 못 읽었다 — {CombatArt.LastError}");
            return Outcome.Escaped;
        }

        var battle = seaWind is { } w
            ? SeaBattle.FromSeaWind(rng, w.Dir, w.Strength)
            : new SeaBattle(rng, rng.Next(SeaBattle.Ways), rng.Next(3) + 1);

        // 제독 능력 벌(0x00441D97) — 포술 자리 · 무력 · 방어(넷째 능력+1). 부하 선장과 견주는 셈은 아직이다.
        battle.MineSide = new SeaBattle.Side(player.LevelOf(Skill.Names[3]),
                                             player.AbilityOf(Ability.Might),
                                             player.AbilityOf(Ability.Luck) + 1);
        // 적장 능력은 넷의 합(Sum)만 알아 고르게 나눠 쓴다.
        battle.EnemySide = new SeaBattle.Side(Math.Clamp(foe.Sum / 100, 0, 3), foe.Sum / 4, foe.Sum / 4);
        // 탄약 = 함대 보급품 탄약 x 10(볼트 85).
        battle.Ammo = player.SupplyOf(SupplyKind.Ammo) * 10;

        // 기함이 0번이다. 승원은 바다 커맨드 「편성」이 나눠 둔 배마다의 몫이고, 자리는 「대열」이 정한다.
        var ships = player.Ships;
        int flag = Math.Clamp(player.Flagship, 0, Math.Max(0, ships.Count - 1));
        var order = ships.Select((s, i) => (s, i)).OrderBy(p => p.i == flag ? 0 : 1).Take(SeaBattle.PerSide).ToList();
        var shares = player.CrewShares;
        for (int slot = 0; slot < order.Count; slot++)
        {
            var (ship, at) = order[slot];
            battle.Place(true, slot, ship.Name, ship.Speed, [.. ship.Sails],
                         art: Math.Clamp(ship.Hull.Skin, 0, 3),
                         hp: ship.Hp, crew: shares.ElementAtOrDefault(at), minCrew: ship.Crew,
                         gun: ship.Guns > 0 ? ship.Gun : -1, figurehead: ship.Figurehead,
                         formation: player.Formation,
                         hullName: ship.Hull.Name, cargo: ship.Capacity, guns: ship.Guns);
        }

        // 배가 한 척도 없으면(타이틀의 미니게임에서 여는 모의해전) 연습용 카라벨 한 척을 띄운다.
        if (order.Count == 0)
        {
            var practice = Hull.Cheapest;
            battle.Place(true, 0, practice.Name, practice.Speed,
                         [Support.Local.Models.Ship.Lateen, Support.Local.Models.Ship.Lateen, 0],
                         art: Math.Clamp(practice.Skin, 0, 3), hp: practice.Hp,
                         crew: practice.Crew + 20, minCrew: practice.Crew, gun: -1,
                         hullName: practice.Name, cargo: practice.Capacity);
        }

        // 적 배의 선체는 아직 모른다 — 두 돛대·추진력 50 짜리 배에 세이커포를 싣게 세운다.
        // 적의 대열은 굴린다(0x004421F6 의 rand(8)).
        int enemyFormation = rng.Next(SeaBattle.FormationCount);
        for (int slot = 0; slot < Math.Min(foe.Ships, SeaBattle.PerSide); slot++)
            battle.Place(false, slot, foe.Name, 50,
                         [Support.Local.Models.Ship.Square, Support.Local.Models.Ship.Lateen, 0],
                         art: 4 + Math.Min(3, slot), hp: 50, crew: 40, minCrew: 15, gun: 0,
                         hullName: "카락", cargo: 200, guns: 6,
                         formation: enemyFormation);

        var dialog = new SeaCombatDialog(battle, art, foe, face, ZoomFor(owner), sfx) { Owner = owner };
        dialog.ShowDialog();
        return dialog.Result;
    }

    /// <summary>해전 연습 — 개발용 창에서 연다. 붙는 무리는 조우 굴림으로 짓는다.</summary>
    public static void Play(Window owner, Player player, Random rng) =>
        Fight(owner, player, Encounter.Roll(rng), rng, null);
}
