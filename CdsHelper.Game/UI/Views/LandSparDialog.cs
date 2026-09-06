using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Game.Engine;
using CdsHelper.Game.Engine.Land;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 육상전 <b>모의전</b>을 차리는 창 — 미니 게임에서만 연다.
/// </summary>
/// <remarks>
/// 도시도 나라도 없이 싸움만 돌려 본다. 양쪽 여섯 자리의 병종과 병력 합을 골라
/// <see cref="LandBattle"/> 의 모의전 생성자에 그대로 넘긴다.
///
/// 게임에 없는 화면이라 밤색 판이 아니라 <b>여느 개발 창</b>의 꼴로 짓는다 —
/// 부대 편성 창(<see cref="LandFormationDialog"/>)과 같은 결이다.
/// </remarks>
internal sealed class LandSparDialog : Window
{
    /// <summary>한 쪽이 세울 수 있는 자리 수.</summary>
    private const int Slots = LandBattle.PerSide;

    /// <summary>고르는 칸의 폭과 병력 칸의 폭.</summary>
    private const double PickWidth = 150, MenWidth = 90;

    private readonly ComboBox[] _mine = new ComboBox[Slots];
    private readonly ComboBox[] _theirs = new ComboBox[Slots];
    private readonly TextBox _myMen = new() { Width = MenWidth, Text = "300" };
    private readonly TextBox _foeMen = new() { Width = MenWidth, Text = "300" };
    private readonly ComboBox _terrain = new() { Width = PickWidth };
    private readonly ComboBox _culture = new() { Width = PickWidth };

    /// <summary>고르고 나면 그 짜임. 물렀으면 null.</summary>
    private Setup? _made;

    /// <summary>모의전 한 판의 짜임.</summary>
    /// <param name="Mine">아군 여섯 자리의 병종. −1 이면 빈 자리다.</param>
    /// <param name="Theirs">적 여섯 자리.</param>
    internal readonly record struct Setup(int[] Mine, int[] Theirs, int MyMen, int FoeMen,
                                          int Culture, int Terrain);

    /// <summary>싸움터 그림 넷 — <see cref="LandBattle.Terrain"/> 차례다.</summary>
    private static readonly string[] Fields = ["도시", "초지", "숲", "황무지"];

    private LandSparDialog()
    {
        Title = "육상전 모의전";
        Width = 640;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var page = new StackPanel { Margin = new Thickness(14) };
        page.Children.Add(Head("싸울 자리와 부대를 고르고 「싸운다」를 누른다."));

        var top = new StackPanel { Orientation = Orientation.Horizontal,
                                   Margin = new Thickness(0, 10, 0, 6) };
        top.Children.Add(Label("싸움터", 52));
        top.Children.Add(_terrain);
        top.Children.Add(Label("문화권", 60));
        top.Children.Add(_culture);
        page.Children.Add(top);

        foreach (string field in Fields) _terrain.Items.Add(field);
        _terrain.SelectedIndex = 0;

        // 문화권은 적 그림을 가른다(LandUnitArt.PartOf) — 이름은 도시 표의 것을 쓴다.
        for (int i = 0; i < CultureNames.Length; i++) _culture.Items.Add($"{i} {CultureNames[i]}");
        _culture.SelectedIndex = 0;

        var sides = new StackPanel { Orientation = Orientation.Horizontal };
        sides.Children.Add(Side("아군", _mine, _myMen));
        sides.Children.Add(new Border { Width = 20 });
        sides.Children.Add(Side("적군", _theirs, _foeMen));
        page.Children.Add(sides);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0),
        };
        var fight = new Button { Content = "싸운다", Padding = new Thickness(16, 3, 16, 3),
                                 Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        fight.Click += (_, _) => Decide();
        buttons.Children.Add(fight);

        var stop = new Button { Content = "그만둔다", Padding = new Thickness(16, 3, 16, 3),
                                IsCancel = true };
        stop.Click += (_, _) => Close();
        buttons.Children.Add(stop);
        page.Children.Add(buttons);

        Content = page;
    }

    /// <summary>문화권 이름 열하나. 적 그림과 진형이 이것으로 갈린다.</summary>
    private static readonly string[] CultureNames =
    [
        "서유럽", "북유럽", "동유럽", "이슬람", "인도", "동남아시아",
        "동아시아", "일본", "아프리카", "중남미", "오세아니아",
    ];

    /// <summary>한 쪽의 여섯 자리와 병력 칸.</summary>
    private UIElement Side(string title, ComboBox[] slots, TextBox men)
    {
        var box = new StackPanel { Width = 290 };
        box.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 6, 0, 4),
        });

        for (int i = 0; i < Slots; i++)
        {
            var pick = new ComboBox { Width = PickWidth, Margin = new Thickness(0, 0, 0, 3) };
            pick.Items.Add("— 빈 자리 —");
            foreach (string name in LandUnits.Names) pick.Items.Add(name);

            // 처음에는 아군 셋 · 적 셋을 세워 둔다 — 열자마자 싸울 수 있게.
            pick.SelectedIndex = i < 3 ? Opening[i] + 1 : 0;
            slots[i] = pick;

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(Label($"{i + 1}", 20));
            row.Children.Add(pick);
            box.Children.Add(row);
        }

        var last = new StackPanel { Orientation = Orientation.Horizontal,
                                    Margin = new Thickness(0, 6, 0, 0) };
        last.Children.Add(Label("병력", 20));
        last.Children.Add(men);
        last.Children.Add(new TextBlock { Text = " 명", VerticalAlignment = VerticalAlignment.Center });
        box.Children.Add(last);
        return box;
    }

    /// <summary>열 때 세워 두는 병종 셋 — 제독 · 기병 · 화승총대다.</summary>
    private static readonly int[] Opening =
        [LandUnits.Admiral, LandUnits.Horse, LandUnits.Matchlock];

    private static TextBlock Label(string text, double width) => new()
    {
        Text = text,
        Width = width,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static TextBlock Head(string text) => new()
    {
        Text = text,
        Foreground = Brushes.DimGray,
        TextWrapping = TextWrapping.Wrap,
    };

    private void Decide()
    {
        var mine = Picked(_mine);
        var theirs = Picked(_theirs);

        if (mine.All(k => k < 0) || theirs.All(k => k < 0))
        {
            MessageBox.Show(this, "양쪽 다 적어도 한 자리는 세워야 합니다.", Title,
                            MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _made = new Setup(mine, theirs, Men(_myMen), Men(_foeMen),
                          _culture.SelectedIndex, _terrain.SelectedIndex);
        Close();
    }

    /// <summary>고른 여섯 자리. 「빈 자리」는 −1 이다.</summary>
    private static int[] Picked(ComboBox[] slots) =>
        [.. slots.Select(s => s.SelectedIndex - 1)];

    /// <summary>병력 칸. 숫자가 아니거나 0 이하면 백으로 친다.</summary>
    private static int Men(TextBox box) =>
        int.TryParse(box.Text, out int n) && n > 0 ? Math.Min(n, 9999) : 100;

    /// <summary>
    /// 모의전을 차리고 그대로 싸운다.
    /// </summary>
    public static void Play(Window owner, Engine.Game game)
    {
        var setup = new LandSparDialog { Owner = owner };
        setup.ShowDialog();
        if (setup._made is not { } made) return;

        // 싸움 쪽은 제 주사위를 쓴다 — 성문에서 들어갈 때와 같은 결이다.
        var dice = new GameRandom(Environment.TickCount);

        var player = game.Player;
        var aide = player.Mates.Count > 0 && player.Mates[0].Length > 0
            ? player.MateInfoOf(player.Mates[0]) : null;

        var field = new LandBattle(made.Mine, made.Theirs, made.MyMen, made.FoeMen,
                                   player, aide, made.Culture, made.Terrain, dice);
        LandBattleScene.Run(owner, game, field, dice);
    }
}
