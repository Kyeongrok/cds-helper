using System.Windows;
using System.Windows.Controls;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// NEW GAME 마지막 걸음 — 지금까지 정한 것을 한 번 더 보여 주고 "결정" 을 받는다.
/// </summary>
/// <remarks>
/// 게임의 <c>0x0045E260</c> 이다. 화면 글은 신상 걸음의 서식을 그대로 쓴다.
/// <code>
///   0x00571B08  "%s·%s"                                  ; 명·성
///   0x00571B10  "%2d월%2d일생(%2d세)  %-6s  %s형"
///   0x00571B30  "국적:%-10s  직업:%-8s"
/// </code>
/// "결정" 을 누르면 새 놀이가 시작되고, 고른 국적의 <b>자택이 열린다</b> —
/// 포르투갈이면 리스본, 에스파니아면 세빌리아다.
/// </remarks>
internal sealed class CharacterSheetDialog : InfoDialog
{
    private const double BoardWidth = 520, BoardHeight = 300;

    /// <summary>기술 칸에 비워 두는 높이.</summary>
    private const double ListHeight = 130;

    private bool _ok;

    private CharacterSheetDialog(Player player)
    {
        var rows = new StackPanel();
        rows.Children.Add(Label($"  {player.Name}"));
        rows.Children.Add(Label($"   {player.BirthMonth,2}월{player.BirthDay,2}일생" +
                                $"({player.Age,2}세)   {player.Zodiac}  {player.BloodName}형"));
        rows.Children.Add(Label($"  국적: {GameUi.Pad(player.NationName, 16)}직업: {player.Work.Name}"));
        rows.Children.Add(Gap(6));

        // 왼쪽에 능력치 다섯, 오른쪽에 기술·언어 목록.
        var left = new StackPanel { Width = 130 };
        for (int i = 0; i < Ability.Shown; i++)
            left.Children.Add(Label($"  {GameUi.Pad(Ability.Names[i], 8)}{player.AbilityOf(i),3}"));

        var body = new StackPanel { Orientation = Orientation.Horizontal };
        body.Children.Add(left);
        body.Children.Add(new Border { Width = 340, Child = List(Learned(player), ListHeight) });
        rows.Children.Add(body);

        Build("", rows, BoardWidth, BoardHeight,
              new GameButton("취소", Close), new GameButton("결정", Decide));
    }

    /// <summary>배운 기술과 언어를 한 줄씩. 자리가 0 인 것도 게임처럼 다 적는다.</summary>
    private static IEnumerable<string> Learned(Player player)
    {
        foreach (string name in Skill.Names)
            yield return $"{GameUi.Pad(name, 16)}{player.LevelOf(name),3}";
        foreach (string name in Skill.Languages)
            yield return $"{GameUi.Pad(name, 16)}{player.TongueOf(name),3}";
    }

    private void Decide()
    {
        _ok = true;
        Close();
    }

    /// <summary>확인 판을 띄운다. "결정" 을 누르면 true.</summary>
    public static bool Show(Window owner, Player player)
    {
        var dialog = new CharacterSheetDialog(player) { Owner = owner };
        dialog.ShowDialog();
        return dialog._ok;
    }
}
