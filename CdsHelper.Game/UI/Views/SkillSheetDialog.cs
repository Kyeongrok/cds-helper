using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 인물정보의 「특기」 — 기술 열셋과 어학 열넷을 <b>두 칸으로</b> 늘어놓는다.
/// </summary>
/// <remarks>
/// 게임 화면 그대로다. 인물정보와 같은 강청색 판이고, 묶음마다 머리에
/// <c>━━━━━━━━기술━━━━━━━━</c> 을 두른다. 줄은 <b>왼쪽 칸을 다 채우고 오른쪽 칸으로</b>
/// 넘어간다 — 기술은 일곱·여섯, 어학은 일곱·일곱이다.
///
/// 예전에는 힌트 일람 창을 빌려 한 줄로 스물일곱 개를 세로로 쌓았는데, 게임은 두 칸이라
/// 모양이 아주 달랐다.
/// </remarks>
internal sealed class SkillSheetDialog : InfoDialog
{
    /// <summary>판 크기. 두 칸이 들어가야 해서 인물정보보다 넓다.</summary>
    private const double BoardWidth = 470, BoardHeight = 322;

    /// <summary>이름을 채우는 칸 수와 자릿수. 이름이 길어도 값이 세로로 맞게 못 박는다.</summary>
    private const int NameCells = 20, LevelCells = 3;

    /// <summary>두 칸 사이 틈.</summary>
    private const int GapCells = 2;

    /// <inheritdoc/>
    protected override Brush Board => Steel;

    /// <inheritdoc/>
    protected override Brush BoardEdge => SteelEdge;

    private SkillSheetDialog(Player player)
    {
        var rows = new StackPanel();
        rows.Children.Add(Divider("기술"));
        rows.Children.Add(Gap(8));
        AddPairs(rows, Skill.Names, name => player.LevelOf(name));

        rows.Children.Add(Gap(22));
        rows.Children.Add(Divider("어학"));
        rows.Children.Add(Gap(8));
        AddPairs(rows, Skill.Languages, name => player.TongueOf(name));

        Build("", rows, BoardWidth, BoardHeight, new GameButton("취소", Close));
    }

    /// <summary>
    /// 목록을 반으로 갈라 왼쪽·오른쪽 칸에 나란히 적는다. 홀수면 왼쪽이 하나 더 갖는다 —
    /// 게임도 기술 열셋을 일곱·여섯으로 가른다.
    /// </summary>
    private static void AddPairs(StackPanel rows, IReadOnlyList<string> names,
                                 Func<string, int> levelOf)
    {
        int half = (names.Count + 1) / 2;
        for (int i = 0; i < half; i++)
        {
            string line = Cell(names[i], levelOf(names[i])) + new string(' ', GapCells);
            int right = i + half;
            if (right < names.Count) line += Cell(names[right], levelOf(names[right]));
            rows.Children.Add(Label(line, GameFont.BlackColor));
        }
    }

    /// <summary>칸 하나 — 이름을 채우고 값을 오른쪽에 붙인다.</summary>
    private static string Cell(string name, int level) =>
        $"{GameUi.Pad(name, NameCells)}{level,LevelCells}";

    /// <summary>특기 판을 연다.</summary>
    public static void Show(Window owner, Player player) =>
        new SkillSheetDialog(player) { Owner = owner }.ShowDialog();
}
