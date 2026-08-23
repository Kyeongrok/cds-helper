using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 미니 게임 「미궁 64 퍼즐」 화면.
/// </summary>
/// <remarks>
/// 게임의 <c>0x0042C8A0</c> 이다. 규칙과 판정은 <see cref="MazePuzzle"/> 에 모아 두었다.
/// <code>
///   0x00559D40  게임 설명
///   0x00559BE8  보물 상자를 연다 · ＵＮＤＯ(취소) · 포기한다 · 게임 설명
///   0x005597A0  "보물 상자가 있습니다. 열겠습니까?"
///   0x00559938  "[U N D O (취소) ] 는 3회까지입니다. 더 이상 사용할 수 없습니다."
/// </code>
/// 게임은 미궁을 <b>안에서 본 그림</b>으로 그리고 갈 수 있는 쪽에 화살표를 놓는다.
/// 여기서는 네 층을 나란히 펴 놓고 밟은 차례를 적는다 — 해밀턴 경로 놀이라 길을
/// 통째로 보는 편이 낫다.
/// </remarks>
internal sealed class MazePuzzleDialog : InfoDialog
{
    private const double BoardWidth = 700, BoardHeight = 300;

    /// <summary>방 한 칸의 크기.</summary>
    private const double CellSize = 34;

    private static readonly Brush Fresh = Frozen(Color.FromRgb(0x21, 0x11, 0x11));
    private static readonly Brush Walked = Frozen(Color.FromRgb(0x3E, 0x5A, 0x74));
    private static readonly Brush Standing = Frozen(Color.FromRgb(0xE8, 0xC8, 0x60));
    private static readonly Brush Reach = Frozen(Color.FromRgb(0x4C, 0x8C, 0xC8));
    private static readonly Brush Way = Frozen(Color.FromRgb(0x6C, 0xC8, 0x6C));
    private static readonly Brush Chest = Frozen(Color.FromRgb(0x9A, 0x6C, 0x30));

    private readonly MazePuzzle _game;
    private readonly Border[] _cell = new Border[MazePuzzle.Rooms];
    private readonly TextBlock[] _mark = new TextBlock[MazePuzzle.Rooms];
    private readonly GameUi.GameLabel _line = Label("");
    private readonly GameButton _undo;
    private readonly GameButton _open;

    private MazePuzzleDialog(Random rng)
    {
        _game = new MazePuzzle(rng);
        _undo = new GameButton("ＵＮＤＯ", AskUndo);
        _open = new GameButton("보물 상자를 연다", OpenChest);

        var rows = new StackPanel();
        rows.Children.Add(_line);
        rows.Children.Add(Gap(6));

        // 네 층을 왼쪽부터 나란히 편다.
        var layers = new StackPanel { Orientation = Orientation.Horizontal };
        for (int layer = 0; layer < MazePuzzle.Side; layer++)
            layers.Children.Add(Layer(layer));
        rows.Children.Add(layers);

        Build("미궁 64 퍼즐", rows, BoardWidth, BoardHeight,
              _open, _undo,
              new GameButton("게임 설명", Explain),
              new GameButton("포기한다", AskGiveUp));

        KeyDown += OnKey;
        Sync();
    }

    /// <summary>한 층 — 4x4 격자에 층 이름을 붙인다.</summary>
    private UIElement Layer(int layer)
    {
        var grid = new Grid();
        for (int i = 0; i < MazePuzzle.Side; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }

        for (int y = 0; y < MazePuzzle.Side; y++)
        for (int x = 0; x < MazePuzzle.Side; x++)
        {
            int room = layer * MazePuzzle.Side * MazePuzzle.Side + y * MazePuzzle.Side + x;

            _mark[room] = new TextBlock
            {
                Foreground = Brushes.Black,
                FontWeight = FontWeights.Bold,
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };

            var box = new Border
            {
                Width = CellSize,
                Height = CellSize,
                Margin = new Thickness(1),
                BorderBrush = GameUi.ItemEdge,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                Child = _mark[room],
            };
            box.MouseLeftButtonUp += (_, e) => { e.Handled = true; Tap(room); };

            Grid.SetRow(box, y);
            Grid.SetColumn(box, x);
            grid.Children.Add(box);
            _cell[room] = box;
        }

        var stack = new StackPanel { Margin = new Thickness(0, 0, 14, 0) };
        stack.Children.Add(new TextBlock
        {
            Text = $"{layer + 1}층",
            Foreground = Ink,
            FontWeight = FontWeights.Bold,
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 3),
        });
        stack.Children.Add(grid);
        return stack;
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        int step = e.Key switch
        {
            Key.Right => +1,
            Key.Left => -1,
            Key.Down => +MazePuzzle.Side,
            Key.Up => -MazePuzzle.Side,
            Key.PageDown => +MazePuzzle.Side * MazePuzzle.Side,
            Key.PageUp => -MazePuzzle.Side * MazePuzzle.Side,
            _ => 0,
        };
        if (e.Key == Key.Space) { AskUndo(); return; }
        if (step == 0) return;

        int next = MazePuzzle.Neighbour(_game.Here, step);
        if (next >= 0) Tap(next);
        e.Handled = true;
    }

    private void Tap(int room)
    {
        if (_game.Over != MazePuzzle.Result.Playing) return;
        if (!_game.Walk(room)) return;

        Sync();

        // 상자를 밟으면 게임처럼 한 번 묻는다.
        int number = _game.ChestAt(room);
        if (number != 0 && !_game.ChestOpen(number)
            && ConfirmDialog.Ask(this, "보물 상자가 있습니다. 열겠습니까?", "보물 상자 발견"))
        {
            OpenChest();
            if (_game.Over != MazePuzzle.Result.Playing) return;
        }

        if (room != _game.Exit) return;

        int wasRestart = _game.Restarted;
        var result = _game.Arrive();
        Sync();

        if (result == MazePuzzle.Result.Playing && _game.Restarted > wasRestart)
        {
            NoticeDialog.Show(this, "방을 전부 돌지 않았기 때문에" + Environment.NewLine +
                                    "입구로 되돌아 오고 말았다!", "처음부터 다시");
            return;
        }
        if (result != MazePuzzle.Result.Playing) Close();
    }

    private void OpenChest()
    {
        int number = _game.OpenChest();
        if (number == 0) return;

        if (_game.Over == MazePuzzle.Result.Trapped)
        {
            Sync();
            Close();
            return;
        }

        NoticeDialog.Show(this,
            number == MazePuzzle.Chests
                ? "보물을 손에 넣었다!"
                : $"보물 상자 {number + 1}의 열쇠를 손에 넣었다!",
            number == MazePuzzle.Chests ? "보물 발견" : "열쇠 발견");
        Sync();
    }

    private void AskUndo()
    {
        if (_game.Undone >= MazePuzzle.MaxUndo)
        {
            NoticeDialog.Show(this,
                "[U N D O (취소) ] 는 3회까지입니다. 더 이상 사용할 수 없습니다.", "횟수 오버");
            return;
        }
        if (!_game.CanUndo) return;
        if (!ConfirmDialog.Ask(this, "한발 앞의 상태로 돌아가겠습니까?", "앞으로 돌아간다")) return;

        _game.Undo();
        Sync();
    }

    private void Explain() =>
        NoticeDialog.Show(this,
            "바닥을 전부 한번씩만 통과해, 출구로 나가 주십시오." + Environment.NewLine +
            "나아갈 방향의 방을 눌러 이동합니다." + Environment.NewLine +
            "키보드는 ↑↓←→ 와 PageUp·PageDown 으로 움직이고," + Environment.NewLine +
            "[U N D O(취소)]는 스페이스키를 누릅니다." + Environment.NewLine +
            "[U N D O(취소)]를 사용할 수 있는 것은 3회까지입니다." + Environment.NewLine +
            Environment.NewLine +
            "보물 상자는 숫자가 적은 순서로 밖에 열지 못합니다만 열지 않아도 밖으로 나갈 " +
            "수 있습니다. 바닥을 전부 통과하지 않고 출구로 가면 처음으로 돌아갑니다. " +
            "이것도 3회까지입니다.", "게임 설명");

    private void AskGiveUp()
    {
        if (!ConfirmDialog.Ask(this, "미궁으로부터의 탈출을 포기하겠습니까?", "포기한다")) return;
        _game.GiveUp();
        Close();
    }

    private void Sync()
    {
        _line.Text = $"  밟은 방 {_game.Walked}/{MazePuzzle.Rooms}   " +
                     $"상자 {_game.Opened}/{MazePuzzle.Chests}   " +
                     $"취소 {_game.Undone}/{MazePuzzle.MaxUndo}   " +
                     $"다시 {_game.Restarted}/{MazePuzzle.MaxRestart}";

        var reach = _game.Moves().Select(m => m.Room).ToHashSet();

        for (int room = 0; room < MazePuzzle.Rooms; room++)
        {
            int chest = _game.ChestAt(room);
            bool walked = _game.StepAt(room) != 0;

            _cell[room].Background = room == _game.Here ? Standing
                                   : reach.Contains(room) ? Reach
                                   : walked ? Walked
                                   : chest != 0 ? Chest
                                   : Fresh;

            _cell[room].BorderBrush = room == _game.Exit ? Way
                                    : room == _game.Start ? Standing
                                    : GameUi.ItemEdge;
            _cell[room].BorderThickness = new Thickness(
                room == _game.Exit || room == _game.Start ? 3 : 1);

            _mark[room].Text = chest != 0 && !_game.ChestOpen(chest) ? $"{chest}"
                             : walked ? $"{_game.StepAt(room)}"
                             : "";
            _mark[room].Foreground = walked || chest != 0 ? Brushes.Black : Ink;
        }

        _undo.On = _game.CanUndo;
        _open.On = _game.ChestAt(_game.Here) != 0
                   && !_game.ChestOpen(_game.ChestAt(_game.Here));
    }

    /// <summary>
    /// 놀이를 한 판 하고 <c>0x0042C8A0</c> 이 하듯 결과를 알린다.
    /// </summary>
    public static void Play(Window owner, Random rng)
    {
        var dialog = new MazePuzzleDialog(rng) { Owner = owner };
        dialog.ShowDialog();

        switch (dialog._game.Over)
        {
            case MazePuzzle.Result.Trapped:
                NoticeDialog.Show(owner,
                    "순서를 지키지 않았으므로 보물 상자에 장치된 덫이 작동!" +
                    Environment.NewLine + "순식간에 목숨을 잃고 말았다!", "게임 오버");
                break;

            case MazePuzzle.Result.Failed:
                NoticeDialog.Show(owner,
                    "이번엔 되돌아 오지 않았지만 문이 닫히고 말았다. 이제 탈출은 불가능하다.",
                    "클리어 실패");
                NoticeDialog.Show(owner, "게임 오버입니다. 다음 번엔 노력합시다.", "게임 오버");
                break;

            case MazePuzzle.Result.GaveUp:
                NoticeDialog.Show(owner, "포기하자, 바닥이 차츰 웅웅거리기 시작했다!", "게임 오버");
                NoticeDialog.Show(owner, "게임 오버입니다. 다음 번엔 노력합시다.", "게임 오버");
                break;

            case MazePuzzle.Result.Cleared:
                NoticeDialog.Show(owner, "축하하네! 드디어 자네는 미궁을 돌파했네!", "게임 클리어");
                break;

            case MazePuzzle.Result.Perfect:
                NoticeDialog.Show(owner,
                    "축하하네! 자네는 실수하지 않고 미궁을 돌파해 보물을 손에 넣었네!",
                    "게임 클리어");
                break;
        }
    }
}
