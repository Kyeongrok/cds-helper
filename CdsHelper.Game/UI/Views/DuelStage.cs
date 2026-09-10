using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 일기토 판 — 두 사람이 마주 서서 칼을 겨루는 그림판.
/// </summary>
/// <remarks>
/// 게임 화면은 <c>0x004AA700</c> 이 짓는 <b>384x256</b> 칸이고 그 위층 384x136 이 여기다.
/// 배경 그림은 뒤에 깔리고(<see cref="DuelArt"/>) 이 판에는 사람 둘만 얹는다.
///
/// <b>몸짓은 여기서 셈하지 않는다.</b> 어느 눈금에 어느 장을 어디에 낼지는 죄다
/// <see cref="DuelMotions"/> 의 표에 적혀 있고, 이 판은 그것을 눈금에 맞춰 읽어 그리기만
/// 한다. 그래서 모션 메이커에서 고쳐 저장하면(<c>asset/duel/motion.json</c>) 다시 굽지
/// 않아도 놀이에 그대로 든다.
///
/// 한 판이 <b>서른세 눈금</b>이고(<c>0x00572A84</c>) 눈금 하나가 0.067초(1/15초)다.
/// <code>
///   눈금 0~7    여느 자세로 미끄러진다 — 한 눈금에 5점, 여덟 눈금에 40점
///   눈금 8      고른 손 이름이 뜬다             [0x00572A6C]
///   눈금 8·9    찌르는 첫 장
///   눈금 10     둘째 장
///   눈금 11     부위 체력이 깎이고 소리가 난다  [0x00572A74]
///   눈금 11~14  셋째 장 — 서른 점 더 내지른 채
///   눈금 15~32  여느 첫 장 하나로 선다
/// </code>
/// </remarks>
public sealed class DuelStage : Canvas
{
    /// <summary>판 크기. 게임 것과 같다(<c>0x004AA7BB</c> 의 <c>0x180</c> x <c>0x100</c>).</summary>
    public const int StageWidth = DuelArt.ArenaWidth, StageHeight = DuelArt.ArenaHeight;

    /// <summary>
    /// 두 사람이 <b>다 모여 서는</b> 자리.
    /// </summary>
    /// <remarks>
    /// <b>상대가 왼쪽, 내가 오른쪽</b>이다. 그림이 그렇게 그려져 있다 — 제독
    /// 스프라이트셋(0)은 <b>왼쪽을 보고</b> 상대 것은 <b>오른쪽을 본다</b>. 자리는
    /// 갈무리를 재어 잡았다.
    /// </remarks>
    private const double FoeStand = 60, MyStand = 173;

    /// <summary>자리 한계 — 상대는 40 아래로, 나는 200 위로 안 간다(<c>0x004A6EF0</c>).</summary>
    private const double WallNear = 40, WallFar = 200;

    /// <summary>한 판의 눈금 수 — <b>서른셋</b>이다(<c>0x00572A84</c>).</summary>
    public const int Ticks = 33;

    /// <summary>고른 손 이름이 뜨는 눈금과 부위 체력이 깎이는 눈금.</summary>
    private const int SayTick = 8, HurtTick = 11;

    /// <summary>
    /// 눈금 하나의 길이 — <b>67밀리초</b>(1/15초)다.
    /// </summary>
    /// <remarks>
    /// 갈무리(「주인공 중단공격」)의 <c>fcTL</c> 을 읽으면 열일곱 장이 죄다 0.067초다.
    /// 55밀리초로 두었던 것은 어림값이라 그만큼 빨랐다.
    /// </remarks>
    private static readonly TimeSpan TickTime = TimeSpan.FromSeconds(DuelMotions.Tick);

    private readonly FighterSprites _art;
    private readonly int _foeSet;
    private readonly Image _me = new();
    private readonly Image _foe = new();
    private readonly DispatcherTimer _timer = new();

    /// <summary>이 판에 두 사람이 짓는 몸짓.</summary>
    private DuelMotions.Motion? _myMotion, _foeMotion;

    /// <summary>지금 두 사람이 선 자리 — 판이 끝날 때마다 미끄러진 만큼 옮겨진다.</summary>
    private double _foeLeft = FoeStand, _myLeft = MyStand;

    /// <summary>다가오는 눈금. −1 이면 다 모여 판이 도는 중이다.</summary>
    private int _walkTick;

    private int _tick;
    private Action? _onSay, _onHurt, _onDone;

    public DuelStage(FighterSprites art, int foeSet)
    {
        _art = art;
        _foeSet = foeSet;
        Width = StageWidth;
        Height = StageHeight;
        // 바탕은 비운다 — 뒤에 깔린 배경 그림(asset/duel)이 그대로 비쳐야 한다.
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

    /// <summary>둘 다 기본 자세로 세운다. 아직 벽 쪽에 서 있다.</summary>
    public void Rest()
    {
        _myMotion = _foeMotion = DuelMotions.Find(DuelMotions.Idle);
        _walkTick = 0;
        _tick = 0;
        Draw();
    }

    /// <summary>
    /// 한 판을 돌린다. <paramref name="onSay"/> 는 여덟째 눈금, <paramref name="onHurt"/> 는
    /// 열한째 눈금, <paramref name="onDone"/> 은 끝난 뒤에 부른다.
    /// </summary>
    /// <param name="way">
    /// 이 판에 두 사람이 <b>함께</b> 미끄러질 쪽 — <c>−1</c> 내가 몰아붙임(앞으로) ·
    /// <c>+1</c> 내가 물러남 · <c>0</c> 맞부딪힘이라 제자리.
    /// </param>
    /// <remarks>
    /// 미끄러짐은 <b>몸짓 안에 들어 있다</b> — 공격 몸짓은 앞으로, 막는 몸짓은 뒤로
    /// 미끄러지는 자리를 제 표에 적어 두고 있다. 그래서 여기서는 맞부딪힘인지만 가리면 된다.
    /// </remarks>
    public void Play(FighterSprites.Move mine, FighterSprites.Move theirs, int way,
                     Action? onSay, Action? onHurt, Action onDone)
    {
        bool clash = way == 0;
        _myMotion = MotionFor(mine, clash);
        _foeMotion = MotionFor(theirs, clash);
        _onSay = onSay;
        _onHurt = onHurt;
        _onDone = onDone;
        _tick = 0;
        _walkTick = -1;
        Draw();
        _timer.Start();
    }

    /// <summary>
    /// 다가오기 — 둘이 <b>벽에서 가운데로</b> 걸어 나온다. 다 모이면 <paramref name="done"/>.
    /// </summary>
    /// <remarks>
    /// 단계 0 이 열여섯 눈금이고(<c>0x00572A68</c>) 그동안 한쪽이 여든 점씩 다가온다 —
    /// 한 눈금에 다섯 점이다(<c>0x004A7593</c>). 다 모이고 <b>나서야</b> 명령 창이 뜬다.
    /// </remarks>
    public void WalkIn(Action done)
    {
        var walk = DuelMotions.Find(DuelMotions.Walk);
        int ticks = walk == null ? 0 : (int)Math.Round(walk.Length / DuelMotions.Tick);

        _walkTick = 0;
        Draw();

        var clock = new DispatcherTimer { Interval = TickTime };
        clock.Tick += (_, _) =>
        {
            _walkTick++;
            Draw();
            if (_walkTick < ticks) return;

            clock.Stop();
            _walkTick = -1;                  // 다 왔으면 판 눈금으로 넘어간다
            _tick = 0;
            Draw();
            done();
        };
        clock.Start();
    }

    /// <summary>쓰러지는 모습으로 멈춘다.</summary>
    public void Fall(bool mine)
    {
        _timer.Stop();
        var fall = DuelMotions.Find(DuelMotions.Fall);
        if (mine) _myMotion = fall;
        else _foeMotion = fall;

        _walkTick = -1;
        _tick = Ticks;
        Draw();
    }

    /// <summary>그 몸짓을 적어 둔 표에서 찾는다.</summary>
    /// <param name="clash">맞부딪힘이면 참 — 찌르되 앞으로 미끄러지지 않는다.</param>
    private static DuelMotions.Motion? MotionFor(FighterSprites.Move move, bool clash) => move switch
    {
        FighterSprites.Move.HighThrust or
        FighterSprites.Move.MidThrust or
        FighterSprites.Move.LowThrust =>
            DuelMotions.Find(DuelMotions.ThrustKey((int)move, clash ? 0 : 1)),

        FighterSprites.Move.Jump or
        FighterSprites.Move.Dodge or
        FighterSprites.Move.Crouch =>
            DuelMotions.Find(DuelMotions.GuardKey((int)move - 3)),

        FighterSprites.Move.Victory => DuelMotions.Find(DuelMotions.Victory),
        FighterSprites.Move.Fall => DuelMotions.Find(DuelMotions.Fall),
        _ => DuelMotions.Find(DuelMotions.Idle),
    };

    private void Advance()
    {
        _tick++;
        if (_tick == SayTick) _onSay?.Invoke();
        if (_tick == HurtTick) _onHurt?.Invoke();
        Draw();
        if (_tick < Ticks) return;

        _timer.Stop();
        Settle();

        var done = _onDone;
        _onDone = _onSay = _onHurt = null;
        done?.Invoke();
    }

    /// <summary>
    /// 판이 끝나면 <b>미끄러져 간 만큼을 선 자리에 담는다</b>(<c>0x004A6D9A</c>).
    /// </summary>
    /// <remarks>
    /// 몸짓 끝자리의 점이 곧 이 판에 옮겨 간 거리다 — 공격이면 마흔, 막기면 −마흔,
    /// 맞부딪힘이면 0 이다. 두 사람이 <b>같은 쪽으로</b> 가므로 사이는 그대로다.
    /// 벽에 닿으면 아예 안 옮긴다.
    /// </remarks>
    private void Settle()
    {
        if (_myMotion is not { Steps.Length: > 0 } mine) return;
        if (_foeMotion is not { Steps.Length: > 0 } foe) return;

        double myPush = mine.Steps[^1].Push, foePush = foe.Steps[^1].Push;
        if (myPush == 0 && foePush == 0) return;

        double my = _myLeft - myPush;          // 나는 왼쪽이 앞이다
        double their = _foeLeft + foePush;     // 상대는 오른쪽이 앞이다
        if (their < WallNear || my > WallFar) return;

        _myLeft = my;
        _foeLeft = their;
    }

    private void Draw()
    {
        if (_walkTick >= 0)
        {
            var walk = DuelMotions.Find(DuelMotions.Walk);
            Put(_me, 0, walk, _walkTick, MyStand, forward: false);
            Put(_foe, _foeSet, walk, _walkTick, FoeStand, forward: true);
            return;
        }

        Put(_me, 0, _myMotion, _tick, _myLeft, forward: false);
        Put(_foe, _foeSet, _foeMotion, _tick, _foeLeft, forward: true);
    }

    /// <summary>그 몸짓의 그 눈금을 판에 건다. 앞은 상대 쪽이다.</summary>
    private void Put(Image image, int set, DuelMotions.Motion? motion, int tick,
                     double stand, bool forward)
    {
        if (motion == null) { image.Source = null; return; }

        var (frame, push) = motion.At(tick);
        var px = _art.TryGetBgra(set, frame);
        if (px == null) { image.Source = null; return; }

        var bmp = BitmapSource.Create(FighterSprites.Width, FighterSprites.Height, 96, 96,
                                      PixelFormats.Bgra32, null, px, FighterSprites.Width * 4);
        bmp.Freeze();
        image.Source = bmp;

        SetLeft(image, forward ? stand + push : stand - push);
    }
}
