using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 미니 게임 「코인 게임」(천칭 퍼즐) 화면.
/// </summary>
/// <remarks>
/// 게임의 <c>0x004531F0</c> 이다. 규칙과 판정은 <see cref="CoinPuzzle"/> 에 모아 두었다.
/// 금화를 왼쪽 단추로 누르면 왼쪽 접시에, 오른쪽 단추로 누르면 오른쪽 접시에 놓인다.
/// </remarks>
internal sealed class CoinPuzzleDialog : InfoDialog
{
    private const double BoardWidth = 660, BoardHeight = 250;

    /// <summary>금화 한 닢의 지름.</summary>
    private const double CoinSize = 40;

    private static readonly Brush Gold = Frozen(Color.FromRgb(0xC8, 0xA0, 0x40));
    private static readonly Brush OnLeft = Frozen(Color.FromRgb(0x4C, 0x8C, 0xC8));
    private static readonly Brush OnRight = Frozen(Color.FromRgb(0x6C, 0xC8, 0x6C));
    private static readonly Brush Picked = Frozen(Color.FromRgb(0xE8, 0x60, 0x60));

    private readonly CoinPuzzle _game;
    private readonly Border[] _coin;
    private readonly GameUi.GameLabel _line = Label("");
    private readonly StackPanel _sheet = new();
    private readonly GameButton _weigh;
    private readonly GameButton _decide;

    private int _pick = -1;

    private CoinPuzzleDialog(Random rng)
    {
        _game = new CoinPuzzle(rng);
        _coin = new Border[_game.Coins];
        _weigh = new GameButton("무게를 단다", DoWeigh);
        _decide = new GameButton("가짜 금화 선택", DoDecide);

        var rows = new StackPanel();
        rows.Children.Add(_line);
        rows.Children.Add(Gap(6));

        var strip = new WrapPanel { Width = BoardWidth - 40 };
        for (int i = 0; i < _game.Coins; i++)
        {
            int coin = i;
            var text = new TextBlock
            {
                Text = $"{i + 1}",
                Foreground = Brushes.Black,
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var box = new Border
            {
                Width = CoinSize,
                Height = CoinSize,
                CornerRadius = new CornerRadius(CoinSize / 2),
                Background = Gold,
                BorderBrush = GameUi.ItemEdge,
                BorderThickness = new Thickness(2),
                Margin = new Thickness(4),
                Cursor = Cursors.Hand,
                Child = text,
            };
            box.MouseLeftButtonUp += (_, e) => { e.Handled = true; Tap(coin, left: true); };
            box.MouseRightButtonUp += (_, e) => { e.Handled = true; Tap(coin, left: false); };
            _coin[i] = box;
            strip.Children.Add(box);
        }
        rows.Children.Add(strip);

        rows.Children.Add(Gap(8));
        rows.Children.Add(Divider("천칭 자취"));
        rows.Children.Add(_sheet);

        Build("천칭 퍼즐", rows, BoardWidth, BoardHeight,
              _weigh,
              new GameButton("금화를 내린다", () => { _game.Clear(); _pick = -1; Sync(); }),
              _decide,
              new GameButton("게임 설명", Explain),
              new GameButton("포기한다", AskGiveUp));

        MouseRightButtonUp += (_, _) => { _pick = -1; Sync(); };
        Sync();
    }

    /// <summary>금화를 눌렀다 — 접시에 놓거나, 이미 접시에 있으면 도로 내린다.</summary>
    private void Tap(int coin, bool left)
    {
        if (_game.Won != null) return;

        if (_game.PanOf(coin) != 0)
        {
            // 접시에 있는 것을 다시 누르면 두 접시를 통째로 비운다(게임의 CLEAR 와 같다).
            _game.Clear();
            Sync();
            return;
        }

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
        _pick = -1;
        Sync();
    }

    /// <summary>「가짜 금화 선택(DECIDE)」 — 어느 닢인지 고르게 하고 한 번 더 묻는다.</summary>
    private void DoDecide()
    {
        var names = Enumerable.Range(1, _game.Coins).Select(n => $"{n}번 금화").ToList();
        int pick = MapPointDialog.Ask(this, names, "가짜 금화 선택");
        if (pick < 0) return;

        _pick = pick;
        Sync();

        if (!ConfirmDialog.Ask(this, "이 금화가 딴 것과 무게가 다르다고 단정해도 좋습니까?",
                               "천칭 퍼즐"))
        {
            _pick = -1;
            Sync();
            return;
        }

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
        _line.Text = $"  금화 {_game.Coins}닢   천칭 {_game.Weighed}/{CoinPuzzle.Weighings}회";

        for (int i = 0; i < _game.Coins; i++)
        {
            int pan = _game.PanOf(i);
            _coin[i].Background = pan > 0 ? OnLeft : pan < 0 ? OnRight : Gold;
            _coin[i].BorderBrush = i == _pick ? Picked : GameUi.ItemEdge;
            _coin[i].BorderThickness = new Thickness(i == _pick ? 3 : 2);
        }

        _sheet.Children.Clear();
        foreach (var (record, n) in _game.Log.Select((r, n) => (r, n + 1)))
        {
            string left = string.Join(" ", record.Left.Select(c => c + 1));
            string right = string.Join(" ", record.Right.Select(c => c + 1));
            string tilt = record.Result switch
            {
                CoinPuzzle.Tilt.Left => "＞",
                CoinPuzzle.Tilt.Right => "＜",
                _ => "＝",
            };
            _sheet.Children.Add(Label($"  {n}회   [{left}]  {tilt}  [{right}]"));
        }

        _weigh.On = _game.CanWeigh;
        _decide.On = _game.Won == null;
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
