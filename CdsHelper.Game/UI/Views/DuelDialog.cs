using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Windows.Media.Imaging;
using CdsHelper.Game.Engine;
using CdsHelper.Game.Engine.Town;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 일기토 판 — 부위 셋을 두고 칼을 겨룬다.
/// </summary>
/// <remarks>
/// 셈은 <see cref="Duel"/> 이 다 하고 이 창은 보여 주기만 한다. 판은 게임과 같은
/// <b>384x248 두 층</b>이다(<c>0x004AA7BB</c> 의 <c>0x180 x 0x100</c>).
/// <code>
///   위 384x136  배경 — 그림 바탕에 두 사람이 선다(asset/duel, FighterSprites)
///   아래 384x112 눈금판 — 초상 둘 · 고른 명령 둘 · 부위 막대 여섯
/// </code>
/// 눈금판 위의 자리는 <b>그림에 찍힌 자리표를 재어</b> 얻었다
/// (<see cref="DuelArt.Slots"/>) — 눈으로 맞춘 값이 아니다.
/// <b>왼쪽이 상대, 오른쪽이 나</b>다.
///
/// 명령을 고를 때만 오른쪽 초상 자리 위에 <b>작은 명령 창</b>이 뜬다 — 게임도 그 자리다.
///
/// 상대가 하는 말은 게임 표(<c>0x005729E0</c> 부터 여섯씩 넉 줄)를 그대로 옮겼다.
/// </remarks>
public sealed class DuelDialog : GameWindow
{
    /// <summary>고른 명령 라벨과 부위 막대의 바탕 — 눈금판의 검은 홈이다.</summary>
    private static readonly Brush Slot = Frozen(Color.FromRgb(0x0A, 0x08, 0x08));

    /// <summary>
    /// 부위 막대를 칠하는 붓 셋 — <b>게임 조각을 그대로</b> 깐다.
    /// </summary>
    /// <remarks>
    /// 게임은 1점 폭 x 8점 높이 조각을 72번까지 찍어 막대를 그린다. 그 조각들이
    /// 눈금판 파트(<c>FIGHTER.CDS</c> 32)의 <b>앞 16바이트</b>에 들어 있어
    /// <c>asset/duel/duel-bar-*.png</c> 로 뽑아 두었다(<c>tools/extract_duel_art.py</c>).
    /// <code>
    ///   duel-bar-full   가득 찬 자리(파랑)   — 눈금판 그림에서 오려 낸 것
    ///   duel-bar-hurt   맞은 자리(빨강)      — 자리 0   (0x004A7279)
    ///   duel-bar-empty  빈 칸(나뭇결)        — 자리 8   (0x004A71D0)
    /// </code>
    /// 손으로 고른 빛깔 하나로 칠하던 것과 달리 여덟 줄의 결(검정 · 어둠 · 밝음 · 중간 …)이
    /// 그대로 살아난다.
    /// </remarks>
    private static readonly Brush Left_ = Tile("duel-bar-full", Color.FromRgb(0x4C, 0x8C, 0xC4));
    private static readonly Brush Hurt = Tile("duel-bar-hurt", Color.FromRgb(0xC4, 0x30, 0x28));
    private static readonly Brush Empty_ = Tile("duel-bar-empty", Color.FromRgb(0x0A, 0x08, 0x08));

    /// <summary>
    /// 1점 폭 조각을 가로로 이어 까는 붓. 그림이 없으면 그 빛깔로 물러선다.
    /// </summary>
    private static Brush Tile(string name, Color fallback)
    {
        string? path = DuelArt.Open()?.Path_(name);
        if (path == null) return Frozen(fallback);

        var brush = new ImageBrush(new BitmapImage(new Uri(path, UriKind.RelativeOrAbsolute)))
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 1, DuelArt.Slots.BarH),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.Fill,
        };
        RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.NearestNeighbor);
        brush.Freeze();
        return brush;
    }

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    /// <summary>상대가 하는 말 넉 줄 — 게임 표 <c>0x005729E0</c>·<c>F8</c>·<c>0x00572A10</c>·<c>28</c>.</summary>
    private static readonly string[][] Taunts =
    [
        // 내가 맞았을 때
        [
            "찔렀다!",
            "빈틈투성이로군.\n한눈 팔고 있으면\n저세상행이지.",
            "헤헤.\n내가 한수 위로군!",
            "그게\n방어하는 건가.",
            "좀 하는 녀석인 줄\n알았더니...\n뜻밖이군.",
            "슬슬 본 실력을\n내 보시지.\n시시하군.",
        ],
        // 내 공격이 막혔을 때
        [
            "미지근한데.\n그만두겠는가?",
            "안됐군.\n이길 것 같지도 않군.",
            "오~옳지, 아깝군.\n조금 더다.",
            "얏!\n피했다.",
            "그 정도 솜씨로...\n아직이야!",
            "너 같은 녀석에게\n당할 것 같았느냐!",
        ],
        // 상대의 공격을 내가 막았을 때
        [
            "피했나.\n제법이군!",
            "앗!\n실패했다.",
            "이런 바보같은...",
            "이것을 피하리라고는\n곤란하게 됐군.",
            "실패했다!",
            "아니!\n제법이군, 자네.",
        ],
        // 상대가 맞았을 때
        [
            "자, 지금부터네.",
            "제법이야.\n할 마음이 생기는군.",
            "안됐네만\n이 댓가는\n비싸네.",
            "아직이다.\n아직 끝나지 않았다.\n승부는 지금부터다!",
            "으윽!\n방심한 것 같군.",
            "우오오옷!\n제법이군, 자네.",
        ],
    ];

    private readonly Duel _duel;
    private readonly GameRandom _dice;
    private readonly uint[]? _face;
    private readonly DuelStage? _stage;

    private readonly StackPanel _keys = new();

    /// <summary>부위 막대 여섯 — 남은 것과 이번에 깎인 것을 겹쳐 그린다.</summary>
    /// <summary>
    /// 부위 막대 한 칸 — 조각 넷을 겹쳐 짓는다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   남은 것        파랑        [0 .. 지금]
    ///   이번에 깎인 것 나무색      [지금 .. 앞판]   ← 맞는 순간 잠깐 스치는 빛깔
    ///   그 위로        빨강        같은 자리로 다섯 눈금에 걸쳐 차오른다
    ///   지난 판까지    빨강        [앞판 .. 끝]
    /// </code>
    /// 곧 <b>잃은 만큼은 빨강으로 남는다</b>. 나무색은 맞는 그 순간에만 보인다 —
    /// 나무색으로 남겨 두었던 것은 틀렸다.
    /// </remarks>
    private sealed class BarView
    {
        public Border Keep = null!;    // 남은 것(파랑)
        public Border Fresh = null!;   // 이번에 깎인 자리(나무색)
        public Border Flash = null!;   // 그 위로 차오르는 빨강
        public Border Lost = null!;    // 지난 판까지 깎인 것(빨강)
    }

    private readonly BarView[] _mine = new BarView[Duel.Lines];
    private readonly BarView[] _theirs = new BarView[Duel.Lines];

    /// <summary>가운데 라벨 둘 — 이번에 고른 명령.</summary>
    private readonly GameUi.GameLabel _myMove = MoveLabel();
    private readonly GameUi.GameLabel _foeMove = MoveLabel();

    /// <summary>지난 판의 부위 값 — 얼마나 깎였는지 빨강으로 내려고 들고 있는다.</summary>
    private readonly int[] _wasMine = new int[Duel.Lines];
    private readonly int[] _wasFoe = new int[Duel.Lines];

    /// <summary>명령 창이 앉는 자리 — 판 오른쪽 아래다.</summary>
    private readonly Border _keyBox = new();

    /// <summary>
    /// 명령 창이 앉는 자리 — 판 <b>오른쪽 아래</b>, 눈금판 위에 걸친다.
    /// </summary>
    /// <remarks>
    /// 배경 한가운데에 두었더니 싸우는 두 사람을 가렸다. 게임 화면을 재어 보면 창이
    /// 눈금판 위쪽에 걸쳐 오른쪽으로 붙어 있다 — 내 초상 자리를 덮는 대신 마당을
    /// 비워 두는 것이다.
    ///
    /// <b>아래를 붙박고 위로 자라게 둔다.</b> 위를 붙박으면 필살이 붙어 여섯 줄이 되는
    /// 공격 판에서 창이 판 밑으로 잘려 나간다.
    /// </remarks>
    private const double KeyBoxRight = 20, KeyBoxBottom = 25;

    /// <summary>
    /// 말풍선 자리 — 두 초상 사이다. 화면에서 재어 맞췄다.
    /// </summary>
    private const double BubbleX = 120, BubbleY = 12, BubbleW = 180, BubbleH = 76;

    /// <summary>상대가 하는 말이 적히는 흰 말풍선. 할 말이 없으면 안 보인다.</summary>
    private readonly StackPanel _bubbleText = new()
    {
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(10, 4, 8, 4),
    };

    private readonly Border _bubble = new()
    {
        Background = System.Windows.Media.Brushes.White,
        BorderBrush = System.Windows.Media.Brushes.Black,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(10),
        Visibility = Visibility.Collapsed,
    };

    private DuelDialog(Duel duel, GameRandom dice, uint[]? face, uint[]? myFace,
                       FighterSprites? art, int foeSet, DuelArt? board, string arena)
    {
        _duel = duel;
        _dice = dice;
        _face = face;
        // 몸짓 표를 <b>판을 열 때마다</b> 다시 읽는다 — 모션 메이커에서 고쳐 저장한 것이
        // 놀이를 다시 띄우지 않아도 다음 일기토부터 들게 하려는 것이다.
        DuelMotions.Forget();
        if (art != null) _stage = new DuelStage(art, foeSet);

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        var canvas = new Canvas
        {
            Width = DuelArt.BoardWidth,
            Height = DuelArt.BoardHeight,
            Background = GameUi.Back,
        };

        // ── 위층: 배경 그림과 두 사람 ─────────────────────────────────────
        Put(canvas, Picture(board?.Path_(arena)), 0, 0, DuelArt.ArenaWidth, DuelArt.ArenaHeight);
        if (_stage != null) Put(canvas, _stage, 0, 0);

        // ── 아래층: 눈금판 ────────────────────────────────────────────────
        const int Top = DuelArt.ArenaHeight;
        // 눈금판은 배경 빛깔을 따라간다 — 초원이면 초원 것, 갑판이면 갑판 것.
        Put(canvas, Picture(board?.Path_(DuelArt.PanelFor(arena))), 0, Top,
            DuelArt.PanelWidth, DuelArt.PanelHeight);

        // 왼쪽이 상대, 오른쪽이 나다.
        Put(canvas, Portrait(face), DuelArt.Slots.FoePortraitX, Top + DuelArt.Slots.PortraitY);
        Put(canvas, Portrait(myFace), DuelArt.Slots.MyPortraitX, Top + DuelArt.Slots.PortraitY);

        Put(canvas, Framed(_foeMove), DuelArt.Slots.FoeMoveX, Top + DuelArt.Slots.MoveY,
            DuelArt.Slots.MoveW, DuelArt.Slots.MoveH);
        Put(canvas, Framed(_myMove), DuelArt.Slots.MyMoveX, Top + DuelArt.Slots.MoveY,
            DuelArt.Slots.MoveW, DuelArt.Slots.MoveH);

        for (int i = 0; i < Duel.Lines; i++)
        {
            Put(canvas, Bar(out _theirs[i], mirror: true),
                DuelArt.Slots.FoeBarX, Top + DuelArt.Slots.BarY[i],
                DuelArt.Slots.BarW, DuelArt.Slots.BarH);
            Put(canvas, Bar(out _mine[i], mirror: false),
                DuelArt.Slots.MyBarX, Top + DuelArt.Slots.BarY[i],
                DuelArt.Slots.BarW, DuelArt.Slots.BarH);
        }

        // 상대가 하는 말은 <b>눈금판 위의 흰 말풍선</b>이다 — 두 초상 사이를 채운다.
        Put(canvas, _bubble, BubbleX, Top + BubbleY, BubbleW, BubbleH);

        // 판 밑에는 아무것도 안 붙인다. 게임 판은 384x248 이 전부이고, 상대의 말은
        // 제목 「일기토」가 붙은 <b>제 창</b>으로 따로 난다. 어느 판인지(맞부딪힘·공격·
        // 방어)는 명령 창의 줄 이름이 그대로 일러 준다.
        // 명령을 고를 때만 뜨는 작은 명령 창. <b>오른쪽 아래</b>에 뜬다 — 게임도 그 자리다.
        _keyBox.Background = GameUi.MenuBack;
        _keyBox.BorderBrush = GameUi.Edge;
        _keyBox.BorderThickness = new Thickness(1);
        _keyBox.Padding = new Thickness(3);
        _keyBox.Child = _keys;
        _keyBox.HorizontalAlignment = HorizontalAlignment.Right;
        _keyBox.VerticalAlignment = VerticalAlignment.Bottom;
        _keyBox.Margin = new Thickness(0, 0, KeyBoxRight, KeyBoxBottom);

        // 판 위에 겹쳐 놓아야 판 밖으로 삐져나가지 않는다 — 예전에는 자리를 못 박아
        // 오른쪽으로 벗어났다.
        var page = new Grid { Width = DuelArt.BoardWidth, Height = DuelArt.BoardHeight };
        page.Children.Add(canvas);
        page.Children.Add(_keyBox);

        Content = new Border
        {
            BorderBrush = GameUi.Edge,
            BorderThickness = new Thickness(1),
            Child = page,
        };

        GameUi.EnableDrag(this, page);
        KeyDown += (_, e) => { if (e.Key == Key.Escape) e.Handled = true; };   // 판은 물러날 수 없다

        for (int i = 0; i < Duel.Lines; i++)
        {
            _wasMine[i] = _duel.MyParts[i];
            _wasFoe[i] = _duel.FoeParts[i];
        }

        Refresh();
        Rebuild();

        // 다가오기 — 둘이 벽에서 가운데로 걸어 나온 <b>뒤에야</b> 명령 창이 뜬다
        // (갈무리 「일기토의 초기화」). 그림이 없는 판은 걸을 것도 없다.
        if (_stage is { } stage)
        {
            _keyBox.Visibility = Visibility.Hidden;
            bool walked = false;
            Loaded += (_, _) =>
            {
                if (walked) return;
                walked = true;
                stage.WalkIn(() => _keyBox.Visibility = Visibility.Visible);
            };
        }
    }

    /// <summary>칸 하나를 판 위 그 자리에 앉힌다.</summary>
    private static void Put(Canvas canvas, UIElement? what, double x, double y,
                            double w = 0, double h = 0)
    {
        if (what == null) return;

        Canvas.SetLeft(what, x);
        Canvas.SetTop(what, y);
        if (w > 0 && what is FrameworkElement box) { box.Width = w; box.Height = h; }
        canvas.Children.Add(what);
    }

    /// <summary>뽑아 둔 그림 한 장. 파일이 없으면 null 이고 그 자리는 빈 채로 둔다.</summary>
    private static Image? Picture(string? path)
    {
        if (path == null) return null;

        var image = new Image { Source = new BitmapImage(new Uri(path, UriKind.RelativeOrAbsolute)) };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(image, EdgeMode.Aliased);
        return image;
    }

    /// <summary>초상 한 장을 자리 가운데에 앉힌다. 얼굴이 없으면 자리를 비운다.</summary>
    private static UIElement? Portrait(uint[]? face)
    {
        if (face == null) return null;

        var bmp = BitmapSource.Create(Portraits.Width, Portraits.Height, 96, 96,
                                      PixelFormats.Bgra32, null, face, Portraits.Width * 4);
        bmp.Freeze();

        var image = new Image
        {
            Source = bmp,
            Width = Portraits.Width,
            Height = Portraits.Height,
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(image, EdgeMode.Aliased);

        // 자리(84x96)가 초상(80x96)보다 조금 넓다 — 가운데로 민다.
        return new Border
        {
            Width = DuelArt.Slots.PortraitW,
            Height = DuelArt.Slots.PortraitH,
            Child = image,
        };
    }

    /// <summary>
    /// 고른 명령이 적히는 검은 홈. <b>게임 글꼴</b>로 찍는다.
    /// </summary>
    /// <remarks>
    /// 글꼴을 못 읽었을 때만 윈도 글꼴로 물러선다 — 검은 홈이라 그때 쓸 색을 흰빛으로
    /// 일러 준다(<see cref="GameUi.GameLabel.FallbackBrush"/>).
    /// </remarks>
    private static GameUi.GameLabel MoveLabel() => new(GameFont.WhiteColor)
    {
        // <b>굵게 찍지 않는다.</b> 굵게 하면 한 점 겹쳐 찍느라 획이 두꺼워지고 오른쪽
        // 아래로 그림자가 진 것처럼 보인다. 원본의 고른 명령 글씨는 그림자가 없다.
        Bold = false,
        FallbackBrush = Brushes.White,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>상대 쪽에서 본 판 갈래 — 내가 치면 상대는 막고, 내가 막으면 상대가 친다.</summary>
    private static Duel.Phase Flip(Duel.Phase was) => was switch
    {
        Duel.Phase.Attack => Duel.Phase.Guard,
        Duel.Phase.Guard => Duel.Phase.Attack,
        _ => was,                                   // 맞부딪힘은 둘 다 친다
    };

    private static Border Framed(UIElement inner) => new() { Background = Slot, Child = inner };

    /// <summary>
    /// 말풍선에 상대의 말을 적는다. 빈 글이면 풍선을 걷는다.
    /// </summary>
    /// <remarks>
    /// 게임은 이 말을 <b>판 위 흰 말풍선</b>으로 낸다 — 제목 붙은 딴 창이 아니다.
    /// 글꼴도 판과 같은 게임 글꼴이고 바탕이 희어 검은 글씨다.
    /// </remarks>
    private void Speak(string text)
    {
        _bubbleText.Children.Clear();

        if (text.Length == 0)
        {
            _bubble.Visibility = Visibility.Collapsed;
            return;
        }

        foreach (string line in GameUi.Wrap(text, BubbleW - 20))
            _bubbleText.Children.Add(new GameUi.GameLabel(GameFont.BlackColor)
            {
                Text = line,
                Bold = true,
                FallbackBrush = System.Windows.Media.Brushes.Black,
                HorizontalAlignment = HorizontalAlignment.Left,
            });

        _bubble.Child = _bubbleText;
        _bubble.Visibility = Visibility.Visible;
    }

    /// <summary>그 판에 고른 명령의 이름 — 맞부딪힘·공격이면 치는 줄, 방어면 막는 명령이다.</summary>
    private static string MoveName(Duel.Phase was, int move)
    {
        if (move < 0) return "";
        return was == Duel.Phase.Guard
            ? (move < Duel.Guards.Length ? Duel.Guards[move] : "")
            : (move < Duel.Attacks.Length ? Duel.Attacks[move]
                                          : (move - Duel.Lines < Duel.Finishers.Length
                                             ? Duel.Finishers[move - Duel.Lines] : ""));
    }

    /// <summary>
    /// 부위 막대 한 칸 — 검은 홈에 남은 것(파랑)과 이번에 깎인 것(빨강)을 겹친다.
    /// </summary>
    private static Border Bar(out BarView view, bool mirror)
    {
        // <b>두 쪽이 서로 거울이다.</b> 아군 막대는 왼쪽에 파랑이 붙어 오른쪽에서 빨개지고,
        // 적군 막대는 오른쪽에 파랑이 붙어 <b>왼쪽에서</b> 빨개진다. 게임도 그렇다 —
        // 적군 쪽 셈만 0x004A7202 에서 <c>neg</c> 로 뒤집어 72 에서 빼며 센다.
        var side = mirror ? HorizontalAlignment.Right : HorizontalAlignment.Left;

        view = new BarView
        {
            Keep = new Border { Background = Left_, HorizontalAlignment = side },
            Fresh = new Border { Background = Empty_, HorizontalAlignment = side },
            Lost = new Border { Background = Hurt, HorizontalAlignment = side },
        };
        // 빨강은 <b>조각 쪽에서 반대로</b> 차오른다 — 아군은 오른쪽에서 파랑 쪽으로,
        // 적군은 왼쪽에서 파랑 쪽으로 들어온다.
        view.Flash = new Border
        {
            Background = Hurt,
            HorizontalAlignment = mirror ? HorizontalAlignment.Left : HorizontalAlignment.Right,
        };
        view.Fresh.Child = view.Flash;

        var stack = new Grid();
        stack.Children.Add(view.Lost);      // 아래에 깔고
        stack.Children.Add(view.Fresh);     //   그 위에 이번 것
        stack.Children.Add(view.Keep);      //   맨 위에 남은 것

        return new Border { Background = Slot, Child = stack };
    }

    /// <summary>
    /// 깎인 자리가 빨갛게 <b>차는 데 걸리는 눈금</b>.
    /// </summary>
    /// <remarks>
    /// 화면을 보면 맞은 자리가 대뜸 빨개지지 않고 <b>다섯 눈금에 걸쳐</b> 왼쪽에서
    /// 빨갛게 차 온다. 한 눈금이 0.067초이니 0.34초다.
    /// </remarks>
    private const int HurtSteps = 5;

    /// <summary>막대와 라벨을 다시 그린다.</summary>
    /// <param name="flash">맞은 눈금이면 참 — 빨강이 다섯 눈금에 걸쳐 찬다.</param>
    private void Refresh(bool flash = false)
    {
        double full = DuelArt.Slots.BarW;

        for (int i = 0; i < Duel.Lines; i++)
        {
            Paint(_mine[i], _duel.MyParts[i], _wasMine[i], _duel.MyFull, full, flash, mirror: false);
            Paint(_theirs[i], _duel.FoeParts[i], _wasFoe[i], _duel.FoeFull, full, flash, mirror: true);
        }
    }

    /// <summary>
    /// 막대 한 칸을 칠한다 — 남은 것이 파랑, <b>이번 판에 깎인 만큼</b>이 빨강이다.
    /// </summary>
    /// <remarks>
    /// 파랑은 왼쪽에서 남은 만큼, 빨강은 오른쪽 끝에서 <b>이번에 잃은 만큼</b>이다.
    /// 둘 사이가 검게 남으면 그것은 <b>지난 판까지 잃은 것</b>이다.
    /// </remarks>
    private static void Paint(BarView view, int now, int was, int full,
                             double width, bool flash, bool mirror)
    {
        int cap = Math.Max(1, full);
        double keep = width * Math.Clamp(now, 0, cap) / cap;
        double fresh = width * Math.Clamp(was - now, 0, cap) / cap;
        double lost = width * Math.Clamp(cap - was, 0, cap) / cap;

        view.Keep.Width = keep;
        view.Fresh.Width = fresh;
        view.Lost.Width = lost;

        // 자리는 파랑 <b>바로 옆</b>이다. 적군 쪽은 거울이라 오른쪽에서 물려 놓는다.
        Place(view.Fresh, keep, mirror);
        Place(view.Lost, keep + fresh, mirror);

        if (!flash || fresh <= 0)
        {
            // 짓시늉을 걷어야 폭을 다시 박을 수 있다 — 걸린 채로는 값이 안 든다.
            view.Flash.BeginAnimation(FrameworkElement.WidthProperty, null);
            view.Flash.Width = fresh;          // 다 지나간 자리는 <b>빨강</b>으로 남는다
            return;
        }

        // 맞는 순간에만 나무색이 스친다 — 그 위로 빨강이 다섯 눈금에 걸쳐 차오른다.
        var fill = new DoubleAnimationUsingKeyFrames();
        for (int step = 1; step <= HurtSteps; step++)
            fill.KeyFrames.Add(new DiscreteDoubleKeyFrame(
                fresh * step / HurtSteps,
                KeyTime.FromTimeSpan(TimeSpan.FromSeconds(DuelMotions.Tick * step))));

        view.Flash.BeginAnimation(FrameworkElement.WidthProperty, fill);
    }

    /// <summary>막대 조각을 그만큼 물려 놓는다 — 거울인 쪽은 오른쪽에서 잰다.</summary>
    private static void Place(Border what, double at, bool mirror) =>
        what.Margin = mirror ? new Thickness(0, 0, at, 0) : new Thickness(at, 0, 0, 0);

    /// <summary>이번 판이 끝나면 부위 값을 갈무리한다 — 다음 판의 빨강 기준이다.</summary>
    private void Keep()
    {
        for (int i = 0; i < Duel.Lines; i++)
        {
            _wasMine[i] = _duel.MyParts[i];
            _wasFoe[i] = _duel.FoeParts[i];
        }
    }

    /// <summary>
    /// 명령 단추의 폭 — 명령 이름 가운데 가장 긴 것에 맞춘다.
    /// </summary>
    /// <remarks>
    /// 명령은 죄다 넉 자 안쪽이다 — 공격과 필살이 「상단공격」·「상단필살」로 넉 자,
    /// 막기가 「뛴다」 두 자에서 「웅크린다」 넉 자다. 가장 긴 것으로 한 번 재어 붙박아
    /// 두면 어느 판에서나 창 폭이 같다.
    /// </remarks>
    private static double KeyWidth =>
        Duel.Attacks.Concat(Duel.Finishers).Concat(Duel.Guards).Max(GameUi.BandWidthFor);

    /// <summary>이번 판에 고를 손으로 단추를 다시 짓는다.</summary>
    private void Rebuild()
    {
        _keys.Children.Clear();
        var names = _duel.Choices();
        var focus = new GameUi.FocusGroup();

        // 게임은 명령을 <b>세로로 쌓아</b> 낸다 — 갈무리의 상단·중단·하단 공격이 한 줄씩이다.
        // 폭은 <b>명령 이름 가운데 가장 긴 것</b>에 맞춰 붙박는다 — 96 으로 박아 두었더니
        // 좌우가 휑했다.
        double width = KeyWidth;

        var column = new StackPanel();
        for (int i = 0; i < names.Length; i++)
        {
            int pick = i;
            var key = focus.Add(names[i], () => Step(pick), width);
            key.Height = UiSprites.BandHeight;
            key.Margin = new Thickness(0, 0, 0, 2);
            column.Children.Add(key);
        }

        _keys.Children.Add(column);
        _keyBox.Visibility = Visibility.Visible;

        KeyDown -= OnKey;
        _focus = focus;
        KeyDown += OnKey;
    }

    private GameUi.FocusGroup? _focus;

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (_focus != null && _focus.HandleKey(e.Key)) e.Handled = true;
    }

    /// <summary>한 판을 치른다. 그림이 있으면 다 돌고 나서 말을 낸다.</summary>
    private void Step(int pick)
    {
        var turn = _duel.Play(pick);

        if (_stage == null) { Settle(turn); return; }

        // 명령을 고르고 나면 단추를 걷는다 — 그림이 도는 동안은 아무것도 못 누른다.
        _keys.Children.Clear();
        _keyBox.Visibility = Visibility.Collapsed;
        Speak("");                 // 새 판이 시작되면 앞 말은 걷는다
        _focus = null;

        // 두 사람이 고른 명령을 가운데 홈에 적는다.
        // 갈래는 <b>내 쪽에서 본 것</b>이라 상대는 뒤집어 읽어야 한다 — 내가 치는 판이면
        // 상대는 막는 것이고, 내가 막는 판이면 상대가 친다. 안 뒤집었더니 상대가 웅크렸는데
        // 「하단공격」이라고 떴다.
        _myMove.Text = MoveName(turn.Was, turn.MyMove);
        _foeMove.Text = MoveName(Flip(turn.Was), turn.FoeMove);

        // 판 갈래대로 두 사람이 통째로 마흔 점 옮겨 간다(0x004A6EE5) — 내가 몰아붙이면
        // 상대 쪽으로, 막기만 하면 내 쪽으로다. 맞부딪힘은 제자리다. 맞았는지는 안 본다.
        // 이 판이 시작할 때 옮겨 가므로 <b>공격이면 다가서며 찌르고 방어면 물러나면서</b>
        // 뛴다 — 앞 판에서 옮겨 두면 뛰는 판에 앞으로 나가는 꼴이 된다.
        var (mine, theirs) = Moves(turn);
        int way = turn.Was switch
        {
            Duel.Phase.Attack => -1,
            Duel.Phase.Guard => +1,
            _ => 0,
        };
        // <b>꼬리를 걷는다.</b> 한 판이 서른세 눈금이지만 볼 것은 그 앞쪽에서 끝난다 —
        // 찌르기는 눈금 15 에, 빨강은 눈금 16 에 다 찬다. 남은 눈금은 여느 자세로 서
        // 있기만 하므로 기다릴 까닭이 없다.
        int ticks = turn.Blow == Duel.Blow.Blocked ? DuelStage.ShortTicks : DuelStage.HitTicks;

        _stage.Play(mine, theirs, way, ticks,
                    onSay: null,
                    onHurt: () => Refresh(flash: true),
                    onDone: () => Settle(turn));
    }

    /// <summary>
    /// 이번 판에 두 사람이 지을 몸짓.
    /// </summary>
    /// <remarks>
    /// 앞으로 나가고 뒤로 물러나는 것은 <b>몸짓 표가 갖고 있다</b>(<see cref="DuelMotions"/>) —
    /// 여기서는 누가 찌르고 누가 막는지만 고른다.
    /// </remarks>
    private static (FighterSprites.Move Mine, FighterSprites.Move Theirs) Moves(in Duel.Turn turn)
    {
        static FighterSprites.Move Thrust(int line) => (FighterSprites.Move)line;
        static FighterSprites.Move Guard(int g) => (FighterSprites.Move)(3 + g);

        return turn.Was switch
        {
            // 맞부딪힘 — 둘이 한꺼번에 내지른다.
            Duel.Phase.Clash => (Thrust(turn.MyMove), Thrust(turn.FoeMove)),

            // 내가 친다 — 상대는 막는 몸짓이다. 필살도 <b>여느 찌르는 그림</b>을 쓴다.
            // 스프라이트셋 6 은 이겼을 때의 몸짓이라 여기에 걸 것이 아니다(FighterSprites.Move.Victory).
            Duel.Phase.Attack => (Thrust(turn.MyMove), Guard(turn.FoeMove)),

            // 내가 막는다.
            _ => (Guard(turn.MyMove), Thrust(turn.FoeMove)),
        };
    }

    /// <summary>판이 끝난 자리 — 말을 내고 다음 명령을 묻는다.</summary>
    private void Settle(in Duel.Turn turn)
    {
        Refresh();
        Keep();          // 빨강은 이번 판 것만 — 다음 판 기준을 여기서 갈무리한다

        // 상대의 말은 판 위 흰 말풍선으로 난다 — 게임도 그 자리다.
        Speak(Taunt(turn));

        if (_duel.Over)
        {
            _stage?.Fall(mine: _duel.Won != true);
            _keys.Children.Clear();
            var focus = new GameUi.FocusGroup();
            var ok = focus.Add("확인", () => { DialogResult = _duel.Won; }, 96);
            ok.Height = UiSprites.BandHeight;
            _keys.Children.Add(ok);
            _focus = focus;
            _keyBox.Visibility = Visibility.Visible;
            return;
        }

        _stage?.Rest();

        // 말풍선과 명령 창은 <b>같이 서 있지 않는다</b> — 말이 잠깐 떴다 사라지고 나서야
        // 명령 창이 뜬다. 할 말이 없는 판(맞부딪힘)은 곧바로 낸다.
        if (Taunt(turn).Length == 0) { Rebuild(); return; }

        _keyBox.Visibility = Visibility.Collapsed;
        var wait = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromSeconds(DuelMotions.Tick * TauntTicks),
        };
        wait.Tick += (_, _) =>
        {
            wait.Stop();
            Speak("");
            Rebuild();
        };
        wait.Start();
    }

    /// <summary>상대의 말이 떠 있는 눈금 — 이만큼 지나면 걷고 명령 창을 낸다.</summary>
    private const int TauntTicks = 15;

    // 「이번 판에 무엇이 오갔는지」를 한 줄로 적던 줄은 걷었다. 게임은 그런 줄을 안
    // 낸다 — 오간 명령은 눈금판 가운데 라벨 둘이, 맞고 안 맞고는 그림과 체력 막대가
    // 일러 준다. 말풍선에는 상대의 <b>비아냥</b>만 뜬다.

    /// <summary>상대가 하는 말. 어느 줄에서 고를지는 게임과 같다(<c>0x004A6E77</c>).</summary>
    private string Taunt(in Duel.Turn turn)
    {
        if (turn.Was == Duel.Phase.Clash) return "";
        int group = turn.Blow switch
        {
            Duel.Blow.MeHit or Duel.Blow.MeGrazed => 0,
            Duel.Blow.Blocked => turn.Was == Duel.Phase.Attack ? 1 : 2,
            _ => 3,
        };
        var row = Taunts[group];
        return row[_dice.Next(row.Length)];
    }

    /// <summary>판을 연다. 이겼으면 true.</summary>
    /// <param name="art">싸움 그림. 없으면 막대와 글로만 낸다.</param>
    /// <param name="foeSet">상대 스프라이트셋(1~8).</param>
    public static bool Show(Window owner, Duel duel, GameRandom dice, uint[]? face,
                            FighterSprites? art = null, int foeSet = 1,
                            uint[]? myFace = null, string arena = DuelArt.Field)
    {
        var window = new DuelDialog(duel, dice, face, myFace, art, foeSet,
                                    DuelArt.Open(), arena) { Owner = owner };
        window.ShowDialog();
        return duel.Won == true;
    }
}
