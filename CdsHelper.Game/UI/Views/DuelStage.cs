using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CdsHelper.Game.Engine.Town;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 일기토 판 — 두 사람이 마주 서서 칼을 겨루는 그림판.
/// </summary>
/// <remarks>
/// 게임 화면은 <c>0x004AA700</c> 이 짓는 <b>384x256</b> 칸이다. 그 위에 배경
/// (<c>c:Landdata.cds</c>)을 깔고 사람 둘을 얹는데, 우리는 배경을 아직 안 읽어
/// 어두운 판만 깐다.
///
/// <b>한 판이 열일곱 틱</b>이다(<c>0x004A6E16</c> 이 <c>[0x00572A68]</c>=16 에서 끊는다).
/// 그림은 세 장이 이렇게 갈린다(<c>0x004A78CC</c>).
/// <code>
///   틱 0~9    첫 장          (겨눔)
///   틱 10     둘째 장        (내지름)  — 치는 쪽이 서른 점 앞으로 나간다
///   틱 11~16  셋째 장        (뻗음)
///   틱 8      두 사람이 고른 손 이름이 뜬다   [0x00572A6C]
///   틱 11     부위 체력이 깎이고 소리가 난다  [0x00572A74]
/// </code>
/// 필살과 쓰러짐만 여섯 장이라 그쪽은 틱을 여섯으로 나눠 돌린다.
/// </remarks>
public sealed class DuelStage : Canvas
{
    /// <summary>판 크기. 게임 것과 같다(<c>0x004AA7BB</c> 의 <c>0x180</c> x <c>0x100</c>).</summary>
    public const int StageWidth = DuelArt.ArenaWidth, StageHeight = DuelArt.ArenaHeight;

    /// <summary>
    /// 두 사람이 <b>다 모여 서는</b> 자리와 내지를 때 나가는 거리
    /// (<c>0x004A794A</c> 의 <c>sub eax,0x1E</c>).
    /// </summary>
    /// <remarks>
    /// <b>상대가 왼쪽, 내가 오른쪽</b>이다. 그림이 그렇게 그려져 있다 — 제독 벌(0)은
    /// <b>왼쪽을 보고</b> 상대 벌은 <b>오른쪽을 본다</b>. 예전에는 자리를 뒤바꿔 놓아
    /// 둘이 등을 지고 서 있었다.
    ///
    /// 자리는 갈무리(「일기토의 초기화」)를 재어 잡았다. 조각 안에서 몸통이 선 자리가
    /// 제독 벌은 106.5, 상대 벌은 46.0 이라 몸통 자리에서 그만큼 물리면 조각 왼끝이 나온다.
    /// </remarks>
    private const double FoeStand = 60, MyStand = 173, Lunge = 30;

    /// <summary>들머리에 <b>벽에 붙어</b> 서는 자리 — 다가설 만큼 바깥이다.</summary>
    private const double FoeStart = FoeStand - WalkWay, MyStart = MyStand + WalkWay;

    /// <summary>서로 다가서는 거리와 한 걸음, 걸음 수.</summary>
    private const double WalkWay = 70, WalkStep = 10;
    private const int WalkSteps = 7;

    /// <summary>
    /// 걸을 때의 눈금 — <b>발은 한 눈금마다, 자리는 세 눈금마다</b> 움직인다.
    /// </summary>
    /// <remarks>
    /// 갈무리는 <b>한 장이 0.067초</b>다(APNG 의 <c>fcTL</c> 을 읽었다 — 0.1초로 어림잡았던
    /// 것이 1.5배 느렸다). 「일기토 초기화 4컷」에서 다리가 장마다 바뀌는데 <b>첫 장과 넷째
    /// 장이 같으니</b> 세 장짜리 걸음을 <b>0.033초</b>마다 돌려야 0·2·1·0 으로 잡힌다.
    ///
    /// 자리는 열세 장짜리 갈무리에서 「두 장 걷고 한 장 쉬는」 결이라 <b>0.1초마다 10점</b>
    /// 이다 — 세 눈금에 한 번이다. 일곱 걸음이니 다 모이는 데 0.7초다.
    /// </remarks>
    private const int WalkTickMs = 33, WalkEvery = 3, WalkPoses = 3;

    /// <summary>걸을 때 보일 장. −1 이면 안 걷는 중이라 <see cref="StepOf"/> 가 정한다.</summary>
    private int _walkStep = -1;

    /// <summary>지금 두 사람이 선 자리. 들머리에는 벽에 붙어 있다.</summary>
    private double _foeLeft = FoeStart, _myLeft = MyStart;

    /// <summary>한 판의 틱 수와 틱 하나의 길이.</summary>
    public const int Ticks = 17;
    private const int PoseTick = 9, HurtTick = 11, SayTick = 8;

    /// <summary>
    /// 내지르고 나서 <b>여느 자세로 돌아오는</b> 눈금.
    /// </summary>
    /// <remarks>
    /// 갈무리(「주인공 중단공격」) 열일곱 장을 조각에 맞춰 보면 이렇다 — 8·9 눈금에 베는
    /// 자세 첫·둘째 장이 서고, 10~12 눈금에 셋째 장으로 <b>서른 점 내지른 채</b> 머물다가,
    /// 13 눈금부터 여느 자세로 돌아와 판이 끝날 때까지 그대로다. 끝까지 벤 자세로 두면
    /// 칼을 뻗은 채 굳어 다음 판이 어색하게 이어진다.
    /// </remarks>
    private const int RestTick = 13;

    /// <summary>
    /// 다가서기 시작하는 눈금과 그동안 나아가는 거리.
    /// </summary>
    /// <remarks>
    /// 한 판에 쓰이는 조각이 <b>셋이 아니라 여섯</b>이다. 갈무리를 조각에 맞춰 보면 3~7
    /// 눈금에 <b>여느 자세 세 장이 돌면서</b> 앞으로 나아가고, 그러고 나서 베는 장 셋이
    /// 붙는다 — 걸음을 따로 그려 두지 않고 여느 자세를 돌려 쓰는 것이다.
    /// <code>
    ///   눈금  0~2   여느 0        제자리
    ///         3~7   여느 1·2·0·1·2  279 → 244   ; 마흔 점쯤 다가선다
    ///         8·9   베기 0·1      다가선 자리
    ///        10~12  베기 2        + 서른 점 내지름
    ///        13~    여느          제자리로
    /// </code>
    /// <b>끝에 제자리로 물러나는 것</b>도 갈무리로 확인했다 — 한 판을 통째로 뜬 것
    /// (「일기토 전체」, 186장 39초)에서 가만 있을 때 자리가 <b>152 · 269 로 되풀이</b>된다.
    /// 판을 거듭해도 둘이 가까워지는 흐름이 없다.
    /// </remarks>
    private const int StepInTick = 3;
    private const double Approach = 40;
    /// <summary>
    /// 눈금 하나의 길이 — <b>67밀리초</b>(1/15초)다.
    /// </summary>
    /// <remarks>
    /// 갈무리(「주인공 중단공격」)의 <c>fcTL</c> 을 읽으면 열일곱 장이 죄다 0.067초이고
    /// 그 뒤에 1.15초를 멈춘다. 한 판이 <b>1.14초</b>라는 뜻이다 — 55밀리초로 두었던 것은
    /// 어림값이라 0.94초로 그만큼 빨랐다.
    /// </remarks>
    private static readonly TimeSpan TickTime = TimeSpan.FromMilliseconds(67);

    private readonly FighterSprites _art;
    private readonly int _foeSet;
    private readonly Image _me = new();
    private readonly Image _foe = new();
    private readonly DispatcherTimer _timer = new();

    private FighterSprites.Move _myMove = FighterSprites.Move.Idle;
    private FighterSprites.Move _foeMove = FighterSprites.Move.Idle;
    private bool _myLunge, _foeLunge;
    private int _tick;
    private Action? _onSay, _onHurt, _onDone;

    public DuelStage(FighterSprites art, int foeSet)
    {
        _art = art;
        _foeSet = foeSet;
        Width = StageWidth;
        Height = StageHeight;
        // 바탕은 비운다 — 뒤에 깔린 마당 그림(asset/duel)이 그대로 비쳐야 한다.
        Background = Brushes.Transparent;
        ClipToBounds = true;

        foreach (var image in new[] { _me, _foe })
        {
            image.Width = FighterSprites.Width;
            image.Height = FighterSprites.Height;
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
            RenderOptions.SetEdgeMode(image, EdgeMode.Aliased);
            SetTop(image, StageHeight - FighterSprites.Height);
            Children.Add(image);
        }

        _timer.Interval = TickTime;
        _timer.Tick += (_, _) => Advance();
        Rest();
    }

    /// <summary>둘 다 기본 자세로 세운다.</summary>
    public void Rest()
    {
        _myMove = _foeMove = FighterSprites.Move.Idle;
        _myLunge = _foeLunge = false;
        _tick = Ticks;
        Draw();
    }

    /// <summary>
    /// 한 판을 돌린다. <paramref name="onSay"/> 는 여덟째 틱, <paramref name="onHurt"/> 는
    /// 열한째 틱, <paramref name="onDone"/> 은 끝난 뒤에 부른다.
    /// </summary>
    public void Play(FighterSprites.Move mine, FighterSprites.Move theirs,
                     bool myLunge, bool foeLunge,
                     Action? onSay, Action? onHurt, Action onDone)
    {
        _myMove = mine;
        _foeMove = theirs;
        _myLunge = myLunge;
        _foeLunge = foeLunge;
        _onSay = onSay;
        _onHurt = onHurt;
        _onDone = onDone;
        _tick = 0;
        Draw();
        _timer.Start();
    }

    /// <summary>
    /// 들머리 — 둘이 <b>벽에서 가운데로</b> 걸어 나온다. 다 모이면 <paramref name="done"/>.
    /// </summary>
    /// <remarks>
    /// 갈무리(「일기토의 초기화」, 0.1초 간격 열세 장)를 재면 둘이 서로 <b>70점</b>씩
    /// 다가선다. 걸음이 10점씩이고 <b>두 장 걷고 한 장 쉬는</b> 결로 잡히는데, 이는 걸음이
    /// 0.1초보다 뜸하다는 뜻이라 <b>150밀리초마다 한 걸음</b>씩 일곱 걸음이다. 몸통 자리로
    /// 재면 이렇다(판 좌표).
    /// <code>
    ///   상대  36 → 106      내 편  350 → 280      사이  314 → 173
    /// </code>
    /// 다 모이고 <b>나서야</b> 명령 창이 뜬다 — 갈무리도 마지막 장에서 뜬다.
    ///
    /// 걷는 동안에는 <b>발이 돈다</b>(<see cref="_walkStep"/>). 한 자세로 두면 미끄러지듯
    /// 흘러가 유령처럼 보인다.
    /// </remarks>
    public void WalkIn(Action done)
    {
        int ticks = WalkSteps * WalkEvery;
        int at = 0;
        var clock = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(WalkTickMs) };
        clock.Tick += (_, _) =>
        {
            at++;
            _walkStep = at % WalkPoses;
            if (at % WalkEvery == 0)
            {
                _foeLeft += WalkStep;
                _myLeft -= WalkStep;
            }
            Draw();
            if (at < ticks) return;

            clock.Stop();
            _walkStep = -1;                      // 다 왔으면 여느 자세로 돌린다
            Draw();
            done();
        };
        clock.Start();
    }

    /// <summary>쓰러지는 모습으로 멈춘다.</summary>
    public void Fall(bool mine)
    {
        _timer.Stop();
        if (mine) _myMove = FighterSprites.Move.Fall;
        else _foeMove = FighterSprites.Move.Fall;
        _tick = Ticks;
        _myLunge = _foeLunge = false;
        Draw();
    }

    private void Advance()
    {
        _tick++;
        if (_tick == SayTick) _onSay?.Invoke();
        if (_tick == HurtTick) _onHurt?.Invoke();
        Draw();
        if (_tick < Ticks) return;

        _timer.Stop();
        var done = _onDone;
        _onDone = _onSay = _onHurt = null;
        done?.Invoke();
    }

    /// <summary>이번 틱에 보일 장. 세 장짜리는 게임 자리대로, 여섯 장짜리는 고르게 나눈다.</summary>
    private static int StepOf(FighterSprites.Move move, int tick)
    {
        int length = FighterSprites.Lengths[(int)move];
        if (length <= 3) return tick < PoseTick ? 0 : tick < PoseTick + 1 ? 1 : 2;
        return Math.Min(length - 1, tick * length / Ticks);
    }

    /// <summary>베는 자세를 보일 눈금인지 — 그 앞뒤는 여느 자세로 오간다.</summary>
    private bool Cutting => _tick >= PoseTick && _tick < RestTick;

    /// <summary>이 눈금에 앞으로 나가 있는 만큼 — 다가선 것에 내지른 것을 더한다.</summary>
    private double AdvanceAt(bool lunge)
    {
        if (_tick <= StepInTick) return 0;
        if (_tick < PoseTick) return Approach * (_tick - StepInTick) / (PoseTick - StepInTick);
        if (_tick < RestTick) return Approach + (lunge ? Lunge : 0);
        return Approach * Math.Max(0, Ticks - 1 - _tick) / (double)(Ticks - 1 - RestTick);
    }

    private void Draw()
    {
        // 베는 눈금이 아니면 여느 자세다 — 쓰러진 쪽만 그대로 둔다.
        var mine = !Cutting && _myMove != FighterSprites.Move.Fall
            ? FighterSprites.Move.Idle : _myMove;
        var theirs = !Cutting && _foeMove != FighterSprites.Move.Fall
            ? FighterSprites.Move.Idle : _foeMove;

        // 내지를 때 나아가는 쪽도 서로 반대다 — 나는 왼쪽으로, 상대는 오른쪽으로.
        Put(_me, 0, mine, _myLeft, forward: false, _myLunge);
        Put(_foe, _foeSet, theirs, _foeLeft, forward: true, _foeLunge);
    }

    private void Put(Image image, int set, FighterSprites.Move move, double left,
                     bool forward, bool lunge)
    {
        // 걷는 동안에는 발이 도는 장을 그대로 쓴다 — 안 그러면 미끄러지듯 흘러간다.
        // 다가서고 물러나는 눈금도 마찬가지로 여느 자세 세 장을 돌린다.
        int step = _walkStep >= 0 ? _walkStep
                 : !Cutting && move == FighterSprites.Move.Idle && _tick > StepInTick ? _tick % 3
                 : StepOf(move, _tick);
        step = Math.Min(step, FighterSprites.Lengths[(int)move] - 1);
        var px = _art.TryGetBgra(set, FighterSprites.FrameOf(move, step));
        if (px == null) { image.Source = null; return; }

        var bmp = BitmapSource.Create(FighterSprites.Width, FighterSprites.Height, 96, 96,
                                      PixelFormats.Bgra32, null, px, FighterSprites.Width * 4);
        bmp.Freeze();
        image.Source = bmp;

        double push = AdvanceAt(lunge);
        SetLeft(image, forward ? left + push : left - push);
    }
}
