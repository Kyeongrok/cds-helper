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
/// 셈은 <see cref="Duel"/> 이 다 하고 이 창은 보여 주기만 한다. 게임 화면은
/// 그림 두 사람이 서로 치고받는 그림판인데(<c>SCombat.cds</c>), 우리는 그 조각을
/// 아직 안 읽어 <b>부위 체력 막대와 상대의 말</b>로만 낸다.
///
/// 상대가 하는 말은 게임 표(<c>0x005729E0</c> 부터 여섯씩 넉 줄)를 그대로 옮겼다.
/// </remarks>
public sealed class DuelDialog : Window
{
    private const double Width_ = 452, BarWidth = 150, BarHeight = 11;
    private const double Pad = 10;

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
    private readonly TextBlock _says = new();
    private readonly TextBlock _phase = new();
    private readonly Border[] _mine = new Border[Duel.Lines];
    private readonly Border[] _theirs = new Border[Duel.Lines];
    private readonly TextBlock[] _mineText = new TextBlock[Duel.Lines];
    private readonly TextBlock[] _theirsText = new TextBlock[Duel.Lines];

    private DuelDialog(Duel duel, GameRandom dice, uint[]? face,
                       FighterSprites? art, int foeSet)
    {
        _duel = duel;
        _dice = dice;
        _face = face;
        if (art != null) _stage = new DuelStage(art, foeSet);

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;
        Width = Width_;

        var body = new StackPanel { Margin = new Thickness(Pad) };

        if (_stage != null)
        {
            body.Children.Add(new Border
            {
                BorderBrush = GameUi.Edge,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 8),
                HorizontalAlignment = HorizontalAlignment.Center,
                Child = _stage,
            });
        }

        var sides = new Grid();
        sides.ColumnDefinitions.Add(new ColumnDefinition());
        sides.ColumnDefinitions.Add(new ColumnDefinition());
        var left = Column(duel.Me.Name, _mine, _mineText);
        var right = Column(duel.Foe.Name, _theirs, _theirsText);
        Grid.SetColumn(right, 1);
        sides.Children.Add(left);
        sides.Children.Add(right);
        body.Children.Add(sides);

        if (face != null)
        {
            var bmp = BitmapSource.Create(Portraits.Width,
                                          Portraits.Height, 96, 96,
                                          PixelFormats.Bgra32, null, face,
                                          Portraits.Width * 4);
            bmp.Freeze();
            var image = new Image
            {
                Source = bmp,
                Width = Portraits.Width,
                Height = Portraits.Height,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 6, 0, 0),
            };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
            body.Children.Add(image);
        }

        _says.Foreground = GameUi.Text;
        _says.FontSize = 13;
        _says.TextWrapping = TextWrapping.Wrap;
        _says.MinHeight = 52;
        _says.Margin = new Thickness(0, 8, 0, 4);
        body.Children.Add(_says);

        _phase.Foreground = GameUi.Edge;
        _phase.FontWeight = FontWeights.Bold;
        _phase.Margin = new Thickness(0, 0, 0, 4);
        body.Children.Add(_phase);

        body.Children.Add(_keys);

        Content = new Border
        {
            BorderBrush = GameUi.Edge,
            BorderThickness = new Thickness(1),
            Child = body,
        };

        GameUi.EnableDrag(this, body);
        KeyDown += (_, e) => { if (e.Key == Key.Escape) e.Handled = true; };   // 판은 물러날 수 없다

        Refresh();
        Rebuild();
    }

    /// <summary>한 사람 몫 — 이름과 부위 막대 셋.</summary>
    private static StackPanel Column(string name, Border[] bars, TextBlock[] texts)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
        stack.Children.Add(new TextBlock
        {
            Text = name,
            Foreground = GameUi.Text,
            FontWeight = FontWeights.Bold,
        });

        for (int i = 0; i < Duel.Lines; i++)
        {
            var fill = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                Background = Brushes.IndianRed,
            };
            var number = new TextBlock
            {
                Foreground = GameUi.Text,
                FontSize = 11,
                Margin = new Thickness(6, 0, 0, 0),
            };
            bars[i] = fill;
            texts[i] = number;

            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 0) };
            row.Children.Add(new TextBlock
            {
                Text = Duel.Parts[i],
                Foreground = GameUi.Edge,
                Width = 18,
                FontSize = 11,
            });
            row.Children.Add(new Border
            {
                Width = BarWidth,
                Height = BarHeight,
                Background = Brushes.Gainsboro,
                Child = fill,
            });
            row.Children.Add(number);
            stack.Children.Add(row);
        }
        return stack;
    }

    /// <summary>막대와 숫자를 다시 그린다.</summary>
    private void Refresh()
    {
        for (int i = 0; i < Duel.Lines; i++)
        {
            _mine[i].Width = BarWidth * _duel.MyParts[i] / Math.Max(1, _duel.MyFull);
            _theirs[i].Width = BarWidth * _duel.FoeParts[i] / Math.Max(1, _duel.FoeFull);
            _mineText[i].Text = _duel.MyParts[i].ToString();
            _theirsText[i].Text = _duel.FoeParts[i].ToString();
        }

        _phase.Text = _duel.Now switch
        {
            Duel.Phase.Clash => "맞부딪힌다",
            Duel.Phase.Attack => "친다",
            _ => "막는다",
        };
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
        _focus = null;
        _says.Text = "";

        var (mine, theirs, myLunge, foeLunge) = Moves(turn);
        _stage.Play(mine, theirs, myLunge, foeLunge,
                    onSay: () => _says.Text = Line(turn),
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
        _says.Text = Line(turn) + Environment.NewLine + Environment.NewLine + Taunt(turn);

        if (_duel.Over)
        {
            _stage?.Fall(mine: _duel.Won != true);
            _keys.Children.Clear();
            var focus = new GameUi.FocusGroup();
            var ok = focus.Add("확인", () => { DialogResult = _duel.Won; }, 96);
            ok.Height = UiSprites.BandHeight;
            _keys.Children.Add(ok);
            _focus = focus;
            _phase.Text = _duel.Won == true ? "쓰러뜨렸다!" : "쓰러졌다...";
            return;
        }

        _stage?.Rest();
        Rebuild();
    }

    /// <summary>이번 판에 무엇이 오갔는지 한 줄로 적는다.</summary>
    private static string Line(in Duel.Turn turn)
    {
        string what = turn.Was == Duel.Phase.Guard
            ? $"{Duel.Guards[turn.MyMove]} — 상대의 {Duel.Attacks[turn.FoeMove]}"
            : turn.Was == Duel.Phase.Clash
                ? $"{Duel.Attacks[turn.MyMove]} — 상대도 {Duel.Attacks[turn.FoeMove]}"
                : $"{(turn.Finisher ? Duel.Finishers : Duel.Attacks)[turn.MyMove]}"
                  + $" — 상대는 {Duel.Guards[turn.FoeMove]}";

        string how = turn.Blow switch
        {
            Duel.Blow.Blocked => "막혔다.",
            Duel.Blow.MeHit => $"{Duel.Parts[turn.Line]}단을 맞았다! {turn.Hurt}",
            Duel.Blow.MeGrazed => $"{Duel.Parts[turn.Line]}단을 스쳤다. {turn.Hurt}",
            Duel.Blow.FoeGrazed => $"{Duel.Parts[turn.Line]}단을 스쳤다. {turn.Hurt}",
            _ => $"{Duel.Parts[turn.Line]}단에 꽂혔다! {turn.Hurt}",
        };

        if (turn.Critical) how = "회심의 일격! " + how;
        if (turn.Finisher && turn.Blow != Duel.Blow.Blocked) how = "필살! " + how;
        return what + "\n" + how;
    }

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
                            FighterSprites? art = null, int foeSet = 1)
    {
        var window = new DuelDialog(duel, dice, face, art, foeSet) { Owner = owner };
        window.ShowDialog();
        return duel.Won == true;
    }
}
