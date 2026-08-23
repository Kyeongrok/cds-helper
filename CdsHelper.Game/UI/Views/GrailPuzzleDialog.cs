using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 미니 게임 「성배 퍼즐」 화면.
/// </summary>
/// <remarks>
/// 게임의 <c>0x00467D50</c> 이다. 규칙과 판정은 <see cref="GrailPuzzle"/> 에 모아 두었다.
/// <code>
///   0x00559068  게임 설명
///   0x0056DFB0  "%d번째"          — 지금 몇 수인지
///   0x0056DFC8  "한 수 되돌립니까?"
///   0x0056DFE8  "다시 할 수 없습니다"
///   0x0056E000  한 수 되돌림 · 포기한다 · 게임 설명 · 게임 복귀 · 항복
///   0x0056E048  "게임을 포기하겠습니까?"
/// </code>
/// 그릇을 하나 누르면 <b>주는 쪽</b>이 잡히고, 다음에 누른 것이 <b>받는 쪽</b>이 된다.
/// 오른쪽 단추로 잡은 것을 놓는다.
/// </remarks>
internal sealed class GrailPuzzleDialog : InfoDialog
{
    private const double BoardWidth = 660, BoardHeight = 430;

    /// <summary>그릇 그림 한 칸의 폭과, 한 줄이 차지하는 키.</summary>
    private const double CellWidth = 46, RowHeight = 150;

    /// <summary>그릇 키는 용량대로 잡는다 — 한 홉에 이만큼씩 얹는다.</summary>
    private const double Floor = 24, PerUnit = 9.6;

    /// <summary>물 색과 잡은 그릇을 두르는 색.</summary>
    private static readonly Brush Water = Frozen(Color.FromRgb(0x4C, 0x8C, 0xC8));
    private static readonly Brush Empty = Frozen(Color.FromRgb(0x21, 0x11, 0x11));
    private static readonly Brush Picked = Frozen(Color.FromRgb(0xE8, 0xC8, 0x60));
    private static readonly Brush Done = Frozen(Color.FromRgb(0x6C, 0xC8, 0x6C));

    private readonly GrailPuzzle _game;
    private readonly Dictionary<int, Border> _box = [];
    private readonly Dictionary<int, Border> _fill = [];
    private readonly Dictionary<int, TextBlock> _text = [];
    private readonly GameUi.GameLabel _count = Label("");
    private readonly GameButton _undo;

    private int _pick = -1;

    private GrailPuzzleDialog(int problem)
    {
        _game = new GrailPuzzle(problem);
        _undo = new GameButton("한 수 되돌림", AskUndo);

        var rows = new StackPanel();
        rows.Children.Add(_count);
        rows.Children.Add(Gap(4));

        // 위 줄 — 큰 항아리와 바가지 셋.
        var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 0, 0) };
        top.Children.Add(Vessel(GrailPuzzle.Jar, "항아리", RowHeight * 0.62));
        top.Children.Add(new Border { Width = 24 });
        for (int i = 0; i < GrailPuzzle.Dippers; i++)
            top.Children.Add(Vessel(GrailPuzzle.FirstDipper + i, GrailPuzzle.DipperNames[i],
                                    Floor + _game.SizeAt(GrailPuzzle.FirstDipper + i) * PerUnit));
        rows.Children.Add(top);

        rows.Children.Add(Gap(10));
        rows.Children.Add(Divider("성배"));
        rows.Children.Add(Gap(4));

        // 아래 줄 — 성배 열. 한 홉부터 열 홉까지 하나씩 크다.
        var bottom = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 0, 0) };
        for (int i = 0; i < GrailPuzzle.Grails; i++)
            bottom.Children.Add(Vessel(GrailPuzzle.FirstGrail + i, "",
                                       Floor + _game.SizeAt(GrailPuzzle.FirstGrail + i) * PerUnit));
        rows.Children.Add(bottom);

        Build("성배 퍼즐", rows, BoardWidth, BoardHeight,
              _undo,
              new GameButton("게임 설명", Explain),
              new GameButton("항복", AskGiveUp));

        MouseRightButtonUp += (_, _) => { _pick = -1; Sync(); };
        KeyDown += (_, e) => { if (e.Key is Key.Escape) { _pick = -1; Sync(); } };

        Sync();
    }

    /// <summary>
    /// 그릇 한 칸 — <b>키가 곧 용량</b>이고, 물이 아래에서 차오른다.
    /// </summary>
    /// <remarks>
    /// 게임도 성배를 한 홉짜리부터 열 홉짜리까지 <b>왼쪽에서 오른쪽으로 커지게</b>
    /// 늘어놓는다(<c>0x00559040</c> 의 x 자리 열). 바닥을 맞춰 세워야 크기가 눈에 든다.
    /// </remarks>
    private UIElement Vessel(int slot, string name, double height)
    {
        var fill = new Border
        {
            Background = Water,
            VerticalAlignment = VerticalAlignment.Bottom,
            Height = 0,
        };
        _fill[slot] = fill;

        var box = new Border
        {
            Width = CellWidth,
            Height = height,
            Background = Empty,
            BorderBrush = GameUi.ItemEdge,
            BorderThickness = new Thickness(2),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Bottom,
            Child = new Grid { Children = { fill } },
        };
        box.MouseLeftButtonUp += (_, e) => { e.Handled = true; Tap(slot); };
        _box[slot] = box;

        _text[slot] = Caption("");

        // 위·가운데·아래 셋으로 나눠 바닥을 맞춘다.
        var cell = new Grid { Height = RowHeight, Margin = new Thickness(0, 0, 6, 0) };
        cell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        cell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        cell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = Caption(name);
        title.VerticalAlignment = VerticalAlignment.Bottom;
        title.Margin = new Thickness(0, 0, 0, 2);
        Grid.SetRow(title, 0);
        Grid.SetRow(box, 1);
        Grid.SetRow(_text[slot], 2);

        cell.Children.Add(title);
        cell.Children.Add(box);
        cell.Children.Add(_text[slot]);
        return cell;
    }

    /// <summary>칸 밑에 붙는 작은 글.</summary>
    private static TextBlock Caption(string text) => new()
    {
        Text = text,
        Foreground = Ink,
        FontWeight = FontWeights.Bold,
        FontSize = 12,
        TextAlignment = TextAlignment.Center,
        Width = CellWidth,
        Margin = new Thickness(0, 2, 0, 0),
    };

    /// <summary>그릇을 눌렀다 — 처음이면 잡고, 두 번째면 붓는다.</summary>
    private void Tap(int slot)
    {
        if (_game.Over != null) return;

        if (_pick < 0)
        {
            if (_game.WaterAt(slot) == 0) return;   // 빈 그릇은 못 잡는다
            _pick = slot;
            Sync();
            return;
        }

        if (_pick == slot) { _pick = -1; Sync(); return; }

        _game.Pour(_pick, slot);
        _pick = -1;
        Sync();

        if (_game.Over != null) Finish();
    }

    private void AskUndo()
    {
        if (!_game.CanUndo)
        {
            NoticeDialog.Show(this, "다시 할 수 없습니다", "경고");
            return;
        }
        if (!ConfirmDialog.Ask(this, "한 수 되돌립니까?", "취소")) return;

        _game.Undo();
        _pick = -1;
        Sync();
    }

    private void Explain() =>
        NoticeDialog.Show(this,
            "성공조건 [바로 앞에 있는 10개의 성배를 성수로 채워라.]" + Environment.NewLine +
            Environment.NewLine +
            "대·중·소의 물바가지를 잘 써서 큰 항아리 속의 성수로 모든 성배를 채워라." +
            Environment.NewLine +
            "탐험자가 움직일 수 있는 것은 물바가지 뿐이다. 큰 항아리는 물을 풀 수도 있고 " +
            "다시 놓을 수도 있다. 바가지와 바가지의 이동으로는 물이 넘칠 일은 없다." +
            Environment.NewLine +
            "성배에서 물이 넘치게 되면 당신은 죽게 된다.", "게임 설명");

    private void AskGiveUp()
    {
        if (!ConfirmDialog.Ask(this, "게임을 포기하겠습니까?", "항복")) return;
        _game.GiveUp();
        Finish();
    }

    private void Sync()
    {
        _count.Text = $"  {_game.Moves}번째";

        for (int slot = 0; slot < GrailPuzzle.Slots; slot++)
        {
            if (!_box.TryGetValue(slot, out var box)) continue;

            int size = _game.SizeAt(slot), water = _game.WaterAt(slot);
            bool jar = _game.KindAt(slot) == GrailPuzzle.KindJar;
            bool grail = _game.KindAt(slot) == GrailPuzzle.KindGrail;

            _fill[slot].Height = jar
                ? box.Height - 4
                : size == 0 ? 0 : (box.Height - 4) * water / size;

            _text[slot].Text = jar ? "∞" : $"{water}/{size}";

            box.BorderBrush = slot == _pick ? Picked
                            : grail && water == size ? Done
                            : GameUi.ItemEdge;
            box.BorderThickness = new Thickness(slot == _pick ? 3 : 2);
        }

        _undo.On = _game.CanUndo;
    }

    /// <summary>끝났다 — <c>0x004684D0</c> 의 갈림길 그대로 알린다.</summary>
    private void Finish()
    {
        Sync();
        Close();
    }

    /// <summary>
    /// 놀이를 한 판 하고, <c>0x004684D0</c> 이 하듯 결과를 알린 뒤 상금을 준다.
    /// </summary>
    /// <remarks>
    /// 게임은 「대실패」와 「다시 한번 찬스」 뒤에 "다시 도전하겠습니까?" 를 묻고
    /// 그러겠다면 문제를 <b>새로 굴려</b> 다시 시작한다(<c>0x00468511</c> 로 돌아간다).
    /// </remarks>
    public static void Play(Window owner, Player player, Random rng)
    {
        while (true)
        {
            var dialog = new GrailPuzzleDialog(rng.Next(GrailPuzzle.Problems.Length))
            {
                Owner = owner,
            };
            dialog.ShowDialog();

            switch (dialog._game.Over ?? GrailPuzzle.Result.GaveUp)
            {
                case GrailPuzzle.Result.GaveUp:
                    NoticeDialog.Show(owner, "근성이 없는 녀석이로군···", "성스러운 항아리");
                    return;

                case GrailPuzzle.Result.Spilled:
                    NoticeDialog.Show(owner, "성배에서 물이 넘쳤다!", "대실패");
                    NoticeDialog.Show(owner, "재주가 없는 녀석이로군···한번 더 찬스를 주겠다",
                                      "성스러운 항아리");
                    if (!ConfirmDialog.Ask(owner, "다시 도전하겠습니까?", "메시지")) return;
                    break;

                case GrailPuzzle.Result.Slow:
                    NoticeDialog.Show(owner, "성수를 가득 채운 항아리가 뭔가 말하기 시작했습니다!",
                                      "메시지");
                    NoticeDialog.Show(owner,
                                      "재주가 없는 녀석이로군···으음···다시 한번 찬스를 주겠다",
                                      "성스러운 항아리");
                    if (!ConfirmDialog.Ask(owner, "다시 도전하겠습니까?", "메시지")) return;
                    break;

                case GrailPuzzle.Result.Good:
                    NoticeDialog.Show(owner, "모든 성배를 성수로 채웠다!", "성공");
                    return;

                case GrailPuzzle.Result.Great:
                    NoticeDialog.Show(owner, "성배로부터 눈부신 빛이 넘치기 시작했다!", "멋지게 성공");
                    NoticeDialog.Show(owner, $"금화 {GrailPuzzle.Prize} 닢을 손에 넣었습니다!", "성공");
                    player.Earn(GrailPuzzle.Prize);
                    return;
            }
        }
    }
}
