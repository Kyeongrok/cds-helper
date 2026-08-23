using System.Windows;
using System.Windows.Controls;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 「함대정보」 — 배 하나하나와 실은 보급을 보여 주는 판. 커맨드 → 정보 → 함대정보.
/// </summary>
/// <remarks>
/// 게임의 <c>0x0046F340</c> 이다. 줄 글은 그쪽 서식 그대로다.
/// <code>
///   0x00571388  "%s호"
///   0x00571390  "보급물자"
///   0x005713A0  "식량%4d통    물  %4d통    자재%4d통    탄약%4d통"
///   0x005713D8  "교역품일람"
///   0x00571370  "대열"   0x00571378 "짐"   0x00571380 "취소"
/// </code>
/// <b>대열과 짐(교역품일람)은 아직 안 눌린다</b> — 대열은 진형이라 해전과 함께 와야 하고,
/// 교역품은 배마다 나눠 싣는 것을 아직 안 흉내낸다(우리는 함대가 통째로 싣는다).
/// </remarks>
internal sealed class FleetInfoDialog : InfoDialog
{
    /// <summary>판 크기. 배 줄이 길어 인물정보 판보다 조금 넓다.</summary>
    private const double BoardWidth = 640, BoardHeight = 420;

    /// <summary>배 목록 칸의 높이.</summary>
    private const double ShipHeight = 232;

    private FleetInfoDialog(Player player)
    {
        var rows = new StackPanel();
        rows.Children.Add(Label($"   배 {player.Ships.Count}척    승원 {player.Crew}명" +
                                $"    최저 {player.MinCrew}명    정원 {player.MaxCrew}명"));
        rows.Children.Add(Gap(6));
        rows.Children.Add(Label($"   ★는 기함    포탑 {player.Turrets}자리에 대포 {player.Guns}문"));
        rows.Children.Add(List(Ships(player), ShipHeight));

        rows.Children.Add(Gap());
        rows.Children.Add(Label("   보급물자"));
        rows.Children.Add(Label(
            $"      식량{player.SupplyOf(SupplyKind.Food),4}통    물  {player.SupplyOf(SupplyKind.Water),4}통" +
            $"    자재{player.SupplyOf(SupplyKind.Material),4}통    탄약{player.SupplyOf(SupplyKind.Ammo),4}통"));
        rows.Children.Add(Label($"      남은일수 {player.SupplyDaysLeft,4}일" +
                                $"    짐 {player.LoadedBarrels}/{player.Capacity}통" +
                                $"    무게 {player.LoadedWeight}/{player.Tonnage}"));

        Build("함대정보", rows, BoardWidth, BoardHeight,
              new GameButton("대열", null), new GameButton("짐", null),
              new GameButton("취소", Close));
    }

    /// <summary>배 한 줄씩. 게임처럼 이름 뒤에 "호" 를 붙인다.</summary>
    private static IEnumerable<string> Ships(Player player)
    {
        for (int i = 0; i < player.Ships.Count; i++)
        {
            var s = player.Ships[i];
            yield return $"{(i == player.Flagship ? "★" : "  ")}{GameUi.Pad($"{s.Name}호", 16)}" +
                         $"{GameUi.Pad(s.Hull.Name, 10)}" +
                         $"내구{s.Hp,3}/{s.MaxHp,-3}추진{s.Speed,3} " +
                         $"승원{s.Crew,3} 포{s.Guns,2}/{s.Turrets,-2} " +
                         $"{Cannon.Of(s.Gun)?.Name ?? "—"}";
            yield return $"   {GameUi.Pad("", 14)}{Sails(s)}";
        }
    }

    /// <summary>마스트에 달린 돛을 한 줄로. 안 선 자리는 안 적는다.</summary>
    private static string Sails(Ship ship)
    {
        var parts = new List<string>();
        for (int i = 0; i < Ship.MastSlots; i++)
            if (ship.Sails[i] != Ship.NoSail)
                parts.Add($"{Ship.MastNames[i]} {Ship.SailNames[ship.Sails[i]]}");
        return parts.Count == 0 ? "마스트 없음" : string.Join(" · ", parts);
    }

    /// <summary>함대정보 판을 연다.</summary>
    public static void Show(Window owner, Player player) =>
        new FleetInfoDialog(player) { Owner = owner }.ShowDialog();
}
