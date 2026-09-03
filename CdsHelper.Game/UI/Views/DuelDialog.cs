using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
///   위 384x136  마당 — 그림 바탕에 두 사람이 선다(asset/duel, FighterSprites)
///   아래 384x112 눈금판 — 초상 둘 · 고른 손 둘 · 부위 막대 여섯
/// </code>
/// 눈금판 위의 자리는 <b>그림에 찍힌 자리표를 재어</b> 얻었다
/// (<see cref="DuelArt.Slots"/>) — 눈으로 맞춘 값이 아니다.
/// <b>왼쪽이 상대, 오른쪽이 나</b>다.
///
/// 손을 고를 때만 오른쪽 초상 자리 위에 <b>작은 명령 창</b>이 뜬다 — 게임도 그 자리다.
///
/// 상대가 하는 말은 게임 표(<c>0x005729E0</c> 부터 여섯씩 넉 줄)를 그대로 옮겼다.
/// </remarks>
public sealed class DuelDialog : Window
{
    /// <summary>고른 손 라벨과 부위 막대의 바탕 — 눈금판의 검은 홈이다.</summary>
    private static readonly Brush Slot = Frozen(Color.FromRgb(0x0A, 0x08, 0x08));

    /// <summary>남은 것 · 이번에 깎인 것.</summary>
    private static readonly Brush Left_ = Frozen(Color.FromRgb(0x4C, 0x8C, 0xC4));
    private static readonly Brush Hurt = Frozen(Color.FromRgb(0xC4, 0x30, 0x28));

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
    private readonly Border[] _mine = new Border[Duel.Lines];
    private readonly Border[] _theirs = new Border[Duel.Lines];
    private readonly Border[] _mineHurt = new Border[Duel.Lines];
    private readonly Border[] _theirsHurt = new Border[Duel.Lines];

    /// <summary>가운데 라벨 둘 — 이번에 고른 손.</summary>
    private readonly GameUi.GameLabel _myMove = MoveLabel();
    private readonly GameUi.GameLabel _foeMove = MoveLabel();

    /// <summary>지난 판의 부위 값 — 얼마나 깎였는지 빨강으로 내려고 들고 있는다.</summary>
    private readonly int[] _wasMine = new int[Duel.Lines];
    private readonly int[] _wasFoe = new int[Duel.Lines];

    /// <summary>명령 창이 앉는 자리 — 마당 한가운데다.</summary>
    private readonly Border _keyBox = new();

    /// <summary>명령 창이 마당 위에서 내려앉는 깊이(점).</summary>
    private const double KeyBoxTop = 28;

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

        // ── 위층: 마당 그림과 두 사람 ─────────────────────────────────────
        Put(canvas, Picture(board?.Path_(arena)), 0, 0, DuelArt.ArenaWidth, DuelArt.ArenaHeight);
        if (_stage != null) Put(canvas, _stage, 0, 0);

        // ── 아래층: 눈금판 ────────────────────────────────────────────────
        const int Top = DuelArt.ArenaHeight;
        // 눈금판은 마당 빛깔을 따라간다 — 초원이면 초원 것, 갑판이면 갑판 것.
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
            Put(canvas, Bar(out _theirs[i], out _theirsHurt[i]),
                DuelArt.Slots.FoeBarX, Top + DuelArt.Slots.BarY[i],
                DuelArt.Slots.BarW, DuelArt.Slots.BarH);
            Put(canvas, Bar(out _mine[i], out _mineHurt[i]),
                DuelArt.Slots.MyBarX, Top + DuelArt.Slots.BarY[i],
                DuelArt.Slots.BarW, DuelArt.Slots.BarH);
        }

        // 상대가 하는 말은 <b>눈금판 위의 흰 말풍선</b>이다 — 두 초상 사이를 채운다.
        Put(canvas, _bubble, BubbleX, Top + BubbleY, BubbleW, BubbleH);

        // 판 밑에는 아무것도 안 붙인다. 게임 판은 384x248 이 전부이고, 상대의 말은
        // 제목 「일기토」가 붙은 <b>제 창</b>으로 따로 난다. 어느 판인지(맞부딪힘·공격·
        // 방어)는 명령 창의 줄 이름이 그대로 일러 준다.
        // 손을 고를 때만 뜨는 작은 명령 창. 마당 <b>한가운데</b>에 뜬다(게임도 그 자리다).
        _keyBox.Background = GameUi.MenuBack;
        _keyBox.BorderBrush = GameUi.Edge;
        _keyBox.BorderThickness = new Thickness(1);
        _keyBox.Padding = new Thickness(3);
        _keyBox.Child = _keys;
        _keyBox.HorizontalAlignment = HorizontalAlignment.Center;
        _keyBox.VerticalAlignment = VerticalAlignment.Top;
        _keyBox.Margin = new Thickness(0, KeyBoxTop, 0, 0);

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
    /// 고른 손이 적히는 검은 홈. <b>게임 글꼴</b>로 찍는다.
    /// </summary>
    /// <remarks>
    /// 글꼴을 못 읽었을 때만 윈도 글꼴로 물러선다 — 검은 홈이라 그때 쓸 색을 흰빛으로
    /// 일러 준다(<see cref="GameUi.GameLabel.FallbackBrush"/>).
    /// </remarks>
    private static GameUi.GameLabel MoveLabel() => new(GameFont.WhiteColor)
    {
        Bold = true,
        FallbackBrush = Brushes.White,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
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

    /// <summary>그 판에 고른 손의 이름 — 맞부딪힘·공격이면 치는 줄, 방어면 막는 손이다.</summary>
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
    private static Border Bar(out Border left, out Border hurt)
    {
        // 남은 것은 왼쪽에서 자라고, 깎인 것은 <b>오른쪽 끝에서</b> 자란다.
        // 화면을 보면 파란 막대가 줄어든 만큼 그 오른쪽이 빨개진다.
        left = new Border { Background = Left_, HorizontalAlignment = HorizontalAlignment.Left };
        hurt = new Border { Background = Hurt, HorizontalAlignment = HorizontalAlignment.Right };

        var stack = new Grid();
        stack.Children.Add(left);
        stack.Children.Add(hurt);

        return new Border { Background = Slot, Child = stack };
    }

    /// <summary>막대와 라벨을 다시 그린다.</summary>
    private void Refresh()
    {
        double full = DuelArt.Slots.BarW;

        for (int i = 0; i < Duel.Lines; i++)
        {
            Paint(_mine[i], _mineHurt[i], _duel.MyParts[i], _wasMine[i], _duel.MyFull, full);
            Paint(_theirs[i], _theirsHurt[i], _duel.FoeParts[i], _wasFoe[i], _duel.FoeFull, full);
        }
    }

    /// <summary>
    /// 막대 한 칸을 칠한다 — 남은 것이 파랑, <b>이번 판에 깎인 만큼</b>이 빨강이다.
    /// </summary>
    /// <remarks>
    /// 파랑은 왼쪽에서 남은 만큼, 빨강은 오른쪽 끝에서 <b>이번에 잃은 만큼</b>이다.
    /// 둘 사이가 검게 남으면 그것은 <b>지난 판까지 잃은 것</b>이다.
    /// </remarks>
    private static void Paint(Border left, Border hurt, int now, int was, int full, double width)
    {
        int cap = Math.Max(1, full);
        left.Width = width * Math.Clamp(now, 0, cap) / cap;
        hurt.Width = width * Math.Clamp(was - now, 0, cap) / cap;
    }

    /// <summary>이번 판이 끝나면 부위 값을 갈무리한다 — 다음 판의 빨강 기준이다.</summary>
    private void Keep()
    {
        for (int i = 0; i < Duel.Lines; i++)
        {
            _wasMine[i] = _duel.MyParts[i];
            _wasFoe[i] = _duel.FoeParts[i];
        }
    }

    /// <summary>이번 판에 고를 손으로 단추를 다시 짓는다.</summary>
    private void Rebuild()
    {
        _keys.Children.Clear();
        var names = _duel.Choices();
        var focus = new GameUi.FocusGroup();

        // 필살은 아랫줄로 내린다 — 게임도 여섯 칸을 두 줄로 낸다.
        var top = new StackPanel { Orientation = Orientation.Horizontal };
        var bottom = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };

        for (int i = 0; i < names.Length; i++)
        {
            int pick = i;
            var key = focus.Add(names[i], () => Step(pick), 96);
            key.Height = UiSprites.BandHeight;
            key.Margin = new Thickness(0, 0, 4, 0);
            (i < Duel.Lines ? top : bottom).Children.Add(key);
        }

        _keys.Children.Add(top);
        if (bottom.Children.Count > 0) _keys.Children.Add(bottom);
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

        // 손을 고르고 나면 단추를 걷는다 — 그림이 도는 동안은 아무것도 못 누른다.
        _keys.Children.Clear();
        _keyBox.Visibility = Visibility.Collapsed;
        Speak("");                 // 새 판이 시작되면 앞 말은 걷는다
        _focus = null;

        // 두 사람이 고른 손을 가운데 홈에 적는다.
        _myMove.Text = MoveName(turn.Was, turn.MyMove);
        _foeMove.Text = MoveName(turn.Was, turn.FoeMove);

        var (mine, theirs, myLunge, foeLunge) = Moves(turn);
        _stage.Play(mine, theirs, myLunge, foeLunge,
                    onSay: null,
                    onHurt: Refresh,
                    onDone: () => Settle(turn));
    }

    /// <summary>이번 판에 두 사람이 지을 몸짓.</summary>
    private static (FighterSprites.Move Mine, FighterSprites.Move Theirs,
                    bool MyLunge, bool FoeLunge) Moves(in Duel.Turn turn)
    {
        static FighterSprites.Move Cut(int line) => (FighterSprites.Move)line;
        static FighterSprites.Move Guard(int g) => (FighterSprites.Move)(3 + g);

        return turn.Was switch
        {
            // 맞부딪힘 — 둘이 한꺼번에 내지른다.
            Duel.Phase.Clash => (Cut(turn.MyMove), Cut(turn.FoeMove), true, true),

            // 내가 친다 — 상대는 막는 몸짓이다.
            Duel.Phase.Attack => (turn.Finisher ? FighterSprites.Move.Finisher : Cut(turn.MyMove),
                                  Guard(turn.FoeMove), true, false),

            // 내가 막는다.
            _ => (Guard(turn.MyMove),
                  turn.Finisher ? FighterSprites.Move.Finisher : Cut(turn.FoeMove), false, true),
        };
    }

    /// <summary>판이 끝난 자리 — 말을 내고 다음 손을 묻는다.</summary>
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
        Rebuild();
    }

    // 「이번 판에 무엇이 오갔는지」를 한 줄로 적던 손은 걷었다. 게임은 그런 줄을 안
    // 낸다 — 오간 손은 눈금판 가운데 라벨 둘이, 맞고 안 맞고는 그림과 체력 막대가
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
    /// <param name="foeSet">상대 그림벌(1~8).</param>
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
