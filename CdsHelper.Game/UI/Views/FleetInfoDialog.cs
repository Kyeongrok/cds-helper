using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 「함대정보」 — 가진 배를 죽 늘어놓고 함대 전체의 형편을 보여 주는 판.
/// 커맨드 → 정보 → 함대정보.
/// </summary>
/// <remarks>
/// 게임의 <c>0x0046F340</c> 이다. 화면을 재어 맞췄다(갈무리 1.7778배).
/// <code>
///   판 속     375 x 382
///   배 단추   304 x 24 · 가운데 · 위에서 17 부터 붙여 쌓는다
///   글 줄     왼쪽 16 부터 · 값과 막대는 86 부터 · 줄마다 16
///   막대      154 x 5 · 검정 바탕에 붉은 살
///   아래 줄   대열 · 짐 · 취소
/// </code>
/// <b>배 하나하나의 자세한 것은 이 판에 없다</b> — 이름 단추를 누르면 그 배의 판이 열린다
/// (<see cref="ShipInfoDialog"/>). 게임도 목록이 먼저고 배는 그 다음이다.
///
/// <b>대열과 짐(교역품일람)은 아직 안 눌린다</b> — 대열은 진형이라 해전과 함께 와야 하고,
/// 교역품은 배마다 나눠 싣는 것을 아직 안 흉내낸다(우리는 함대가 통째로 싣는다).
/// </remarks>
internal sealed class FleetInfoDialog : InfoDialog
{
    /// <summary>판 크기. 게임 화면에서 잰 그림 점 그대로다.</summary>
    private const double BoardWidth = 375, BoardHeight = 382;

    /// <summary>배 단추 폭. 마구리 둘에 가운데 서른네 칸이다(16+8*34+16).</summary>
    private const double ShipWidth = 304;

    /// <summary>글 줄의 왼쪽 여백과 값이 서는 자리.</summary>
    private const double RowInset = 16, ValueLeft = 70;

    /// <summary>막대 크기.</summary>
    private const double BarWidth = 154, BarHeight = 5;

    /// <summary>막대의 빈 쪽과 찬 쪽. 화면에서 그대로 뽑았다.</summary>
    private static readonly Brush BarBack = Frozen(Color.FromRgb(0, 0, 0));
    private static readonly Brush BarFill = Frozen(Color.FromRgb(135, 21, 10));

    /// <inheritdoc/>
    protected override Brush Board => Steel;

    /// <inheritdoc/>
    protected override Brush BoardEdge => SteelEdge;

    private FleetInfoDialog(Player player, string coord, ItemTable? items)
    {
        var rows = new StackPanel();

        // 배 목록. 이름 단추를 붙여 쌓는다 — 게임도 줄 사이가 벌어져 있지 않다.
        var ships = new StackPanel
        {
            Width = ShipWidth,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 7, 0, 0),
        };
        for (int i = 0; i < player.Ships.Count; i++)
        {
            int at = i;
            ships.Children.Add(new GameButton($"{player.Ships[at].Name}호",
                                              () => ShipInfoDialog.Show(this, player, at, items))
            {
                Margin = default,
            });
        }
        rows.Children.Add(ships);

        rows.Children.Add(Gap(13));
        rows.Children.Add(Row("국적", Text(player.NationName)));
        rows.Children.Add(Row("함대좌표", Text(coord.Length > 0 ? coord : "---")));
        rows.Children.Add(Row("피로도", Bar(player.Fatigue, Player.MaxFatigue)));
        rows.Children.Add(Row("총승원수", Bar(player.Crew, player.MaxCrew)));
        rows.Children.Add(Row("짐용량", Bar(player.LoadedBarrels, player.Capacity)));
        rows.Children.Add(Row("짐중량", Bar(player.LoadedWeight, player.Tonnage)));

        Build("함대정보", rows, BoardWidth, BoardHeight,
              new GameButton("대열", null), new GameButton("짐", null),
              new GameButton("취소", Close));
    }

    /// <summary>이름 한 칸에 값 한 칸인 줄. 값 자리는 줄마다 같다.</summary>
    private static UIElement Row(string name, UIElement value)
    {
        var line = new Grid { Height = GameUi.ItemTextHeight, Margin = new Thickness(RowInset, 0, 0, 0) };
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ValueLeft) });
        line.ColumnDefinitions.Add(new ColumnDefinition());

        var label = Label(name);
        label.VerticalAlignment = VerticalAlignment.Center;
        line.Children.Add(label);

        Grid.SetColumn(value, 1);
        line.Children.Add(value);
        return line;
    }

    private static UIElement Text(string text)
    {
        var label = Label(text);
        label.VerticalAlignment = VerticalAlignment.Center;
        return label;
    }

    /// <summary>
    /// 찬 만큼 붉게 물드는 막대와 그 옆의 숫자.
    /// </summary>
    /// <remarks>
    /// 숫자는 게임처럼 자릿수를 맞춰 적는다 — 게임 글꼴은 ASCII 가 8점으로 고정이라
    /// 빈칸으로 밀면 줄마다 자리가 딱 맞는다.
    /// </remarks>
    private static UIElement Bar(int now, int max)
    {
        double part = max > 0 ? Math.Clamp((double)now / max, 0, 1) : 0;

        var fill = new Border
        {
            Width = BarWidth * part,
            Height = BarHeight,
            Background = BarFill,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        var gauge = new Grid
        {
            Width = BarWidth,
            Height = BarHeight,
            Background = BarBack,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { fill },
        };

        var number = Label($"{now,6}/{max,-6}");
        number.VerticalAlignment = VerticalAlignment.Center;
        number.Margin = new Thickness(4, 0, 0, 0);

        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(gauge);
        line.Children.Add(number);
        return line;
    }

    /// <summary>함대정보 판을 연다.</summary>
    /// <param name="coord">함대좌표에 적을 글. 도시 안이면 비워 둔다 — 게임처럼 <c>---</c> 다.</param>
    /// <param name="items">아이템 표. 배 정보의 선두상 이름을 여기서 낸다.</param>
    public static void Show(Window owner, Player player, string coord = "",
                            ItemTable? items = null) =>
        new FleetInfoDialog(player, coord, items) { Owner = owner }.ShowDialog();
}
