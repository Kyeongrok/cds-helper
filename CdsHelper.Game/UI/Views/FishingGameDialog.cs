using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 미니 게임 「낚시 게임」 화면.
/// </summary>
/// <remarks>
/// 게임의 <c>0x0047BDD0</c> 이고, 규칙은 <see cref="FishingGame"/> 에 모아 두었다.
/// 게임은 바늘이 줄을 타고 <b>스스로 내려가는 것</b>을 보여 주고 그 사이에 ←→ 를
/// 받는다. 여기서는 한 줄씩 끊어 내려가게 했다 — 왼쪽·오른쪽을 잡고 「내린다」를
/// 누르면 한 줄 내려간다.
/// </remarks>
internal sealed class FishingGameDialog : InfoDialog
{
    private const double BoardWidth = 560, BoardHeight = 400;

    /// <summary>칸 하나의 크기.</summary>
    private const double CellW = 54, CellH = 40;

    private static readonly Brush Sea = Frozen(Color.FromRgb(0x14, 0x2A, 0x44));
    private static readonly Brush Rope = Frozen(Color.FromRgb(0x3E, 0x5A, 0x74));
    private static readonly Brush Hook = Frozen(Color.FromRgb(0xE8, 0xC8, 0x60));
    private static readonly Brush Beast = Frozen(Color.FromRgb(0x9A, 0x4C, 0x6C));
    private static readonly Brush Deep = Frozen(Color.FromRgb(0x6C, 0xC8, 0x6C));

    private readonly FishingGame _game;
    private readonly Border[] _cell = new Border[FishingGame.Cells];
    private readonly TextBlock[] _mark = new TextBlock[FishingGame.Cells];
    private readonly Border[] _floor = new Border[FishingGame.Columns];
    private readonly GameUi.GameLabel _line = Label("");

    private FishingGameDialog(Random rng)
    {
        _game = new FishingGame(rng);

        var rows = new StackPanel();
        rows.Children.Add(_line);
        rows.Children.Add(Gap(6));

        var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Center };
        for (int c = 0; c < FishingGame.Columns; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (int r = 0; r <= FishingGame.Rows; r++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        for (int at = 0; at < FishingGame.Cells; at++)
        {
            _mark[at] = new TextBlock
            {
                Foreground = Ink,
                FontWeight = FontWeights.Bold,
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var box = new Border
            {
                Width = CellW,
                Height = CellH,
                Margin = new Thickness(1),
                Background = Sea,
                BorderBrush = Rope,
                BorderThickness = new Thickness(1),
                Child = _mark[at],
            };
            Grid.SetColumn(box, at % FishingGame.Columns);
            Grid.SetRow(box, at / FishingGame.Columns);
            grid.Children.Add(box);
            _cell[at] = box;
        }

        // 맨 밑줄이 바닥이다. 대어가 어느 칸에 있는지는 낚아 봐야 안다.
        for (int c = 0; c < FishingGame.Columns; c++)
        {
            var box = new Border
            {
                Width = CellW,
                Height = 20,
                Margin = new Thickness(1),
                Background = Rope,
                BorderBrush = GameUi.ItemEdge,
                BorderThickness = new Thickness(1),
            };
            Grid.SetColumn(box, c);
            Grid.SetRow(box, FishingGame.Rows);
            grid.Children.Add(box);
            _floor[c] = box;
        }

        rows.Children.Add(grid);

        Build("낚시 게임", rows, BoardWidth, BoardHeight,
              new GameButton("←", () => Steer(-1)),
              new GameButton("내린다", Fall),
              new GameButton("→", () => Steer(+1)),
              new GameButton("게임 설명", Explain));

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Left) Steer(-1);
            else if (e.Key == Key.Right) Steer(+1);
            else if (e.Key is Key.Down or Key.Enter or Key.Space) Fall();
        };

        Sync();
    }

    private void Steer(int way)
    {
        _game.Steer(way);
        Sync();
    }

    private void Fall()
    {
        if (_game.Got != FishingGame.Catch.None) return;

        _game.Fall();
        Sync();
        if (_game.Got != FishingGame.Catch.None) Close();
    }

    private void Explain() =>
        NoticeDialog.Show(this,
            "바다에서 바늘을 떨어뜨려서 바닥에 있는 대어를 낚는 게임입니다. " +
            "낚시바늘은 줄을 따라 내려갑니다." + Environment.NewLine +
            "내려가는 도중에 화살표를 클릭하든지 ←→버튼을 누르면 교차하는 데에서 " +
            "낚시바늘을 옆으로 이동할 수 있습니다만, 다음에 교차하는 데에서는 반드시 " +
            "밑으로 내려갑니다.", "게임 설명");

    private void Sync()
    {
        string way = _game.Lean > 0 ? "오른쪽" : _game.Lean < 0 ? "왼쪽" : "곧장";
        _line.Text = $"  {_game.Row + 1}/{FishingGame.Rows}줄   " +
                     $"{_game.Column + 1}칸   다음 걸음: {way}";

        for (int at = 0; at < FishingGame.Cells; at++)
        {
            bool here = at == _game.At;
            int what = _game.CellAt(at);

            // 지나온 자리만 무엇이 있었는지 드러난다. 앞은 캄캄한 바다다.
            bool seen = at / FishingGame.Columns < _game.Row;

            _cell[at].Background = here ? Hook
                                 : seen && what >= FishingGame.Squid ? Beast : Sea;
            _mark[at].Text = here ? "낚시" : seen && what >= FishingGame.Squid ? "×" : "";
            _mark[at].Foreground = here ? Brushes.Black : Ink;
        }

        for (int c = 0; c < FishingGame.Columns; c++)
            _floor[c].Background = _game.Got != FishingGame.Catch.None
                                   && c == _game.BigOneColumn ? Deep : Rope;
    }

    /// <summary>
    /// 한 판 한다. 결과 글은 <c>0x0047AD31</c> 의 뜀표 그대로다.
    /// </summary>
    public static void Play(Window owner, Random rng)
    {
        var dialog = new FishingGameDialog(rng) { Owner = owner };
        dialog.ShowDialog();

        switch (dialog._game.Got)
        {
            case FishingGame.Catch.SquidCaught:
                NoticeDialog.Show(owner, "왓! 오징어가 얼굴에 먹물을 토했다!", "오징어를 낚았다");
                break;

            case FishingGame.Catch.OctopusCaught:
                NoticeDialog.Show(owner,
                    "악마의 물고기다! 너무 징그러워서" + Environment.NewLine +
                    "갑판에 내동댕이쳤다.", "낙지를 낚았다");
                break;

            case FishingGame.Catch.SmallFry:
            case FishingGame.Catch.SmallFryToo:
                NoticeDialog.Show(owner,
                    "재수없게 잡어를 낚았군." + Environment.NewLine +
                    "주방장에게 갖다 줄까···", "잡어를 낚았다");
                break;

            case FishingGame.Catch.BigOne:
                NoticeDialog.Show(owner, "잘 됐다! 바다 깊숙히 있는 고기를 낚았다!",
                                  "대어을 낚았다");
                break;

            case FishingGame.Catch.Seabed:
                NoticeDialog.Show(owner,
                    "아무리 당겨도 끌어올릴 수 없다." + Environment.NewLine +
                    "[지구를 낚았다]고 해야하나.", "바닥에 걸렸다");
                break;
        }
    }
}
