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
    public const int StageWidth = 384, StageHeight = 176;

    /// <summary>두 사람이 서는 자리와 내지를 때 나가는 거리(<c>0x004A794A</c> 의 <c>sub eax,0x1E</c>).</summary>
    private const double MyLeft = 8, FoeLeft = StageWidth - FighterSprites.Width - 8, Lunge = 30;

    /// <summary>한 판의 틱 수와 틱 하나의 길이.</summary>
    public const int Ticks = 17;
    private const int PoseTick = 10, HurtTick = 11, SayTick = 8;
    private static readonly TimeSpan TickTime = TimeSpan.FromMilliseconds(55);

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
        Background = new SolidColorBrush(Color.FromRgb(0x18, 0x14, 0x10));
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
        if (length <= 3) return tick < PoseTick ? 0 : tick < HurtTick ? 1 : 2;
        return Math.Min(length - 1, tick * length / Ticks);
    }

    private void Draw()
    {
        Put(_me, 0, _myMove, MyLeft, forward: true, _myLunge);
        Put(_foe, _foeSet, _foeMove, FoeLeft, forward: false, _foeLunge);
    }

    private void Put(Image image, int set, FighterSprites.Move move, double left,
                     bool forward, bool lunge)
    {
        int step = StepOf(move, _tick);
        var px = _art.TryGetBgra(set, FighterSprites.FrameOf(move, step));
        if (px == null) { image.Source = null; return; }

        var bmp = BitmapSource.Create(FighterSprites.Width, FighterSprites.Height, 96, 96,
                                      PixelFormats.Bgra32, null, px, FighterSprites.Width * 4);
        bmp.Freeze();
        image.Source = bmp;

        // 내지르는 두 장에서만 앞으로 나간다.
        double push = lunge && step > 0 ? Lunge : 0;
        SetLeft(image, forward ? left + push : left - push);
    }
}
