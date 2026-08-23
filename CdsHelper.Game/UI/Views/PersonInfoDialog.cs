using System.Windows;
using System.Windows.Controls;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 「인물정보」 — 제독 자신을 보여 주는 판. 커맨드 → 정보 → 인물정보.
/// </summary>
/// <remarks>
/// 게임의 <c>0x0046DF70</c> 이다. 줄 글은 그쪽 서식 그대로다.
/// <code>
///   0x005712D8  "국적      %s"
///   0x005712E8  "함대좌표  %s위 %3d도  %s경 %3d도"   (남/북 0x0057130C · 서/동 0x00571314)
///   0x00571320  "함대좌표  위도 ---도  경도 ---도"    (바다에 안 나가 있을 때)
///   0x00571348  "피로도"   0x00571350 "총승원수"   0x00571360 "짐용량"   0x00571368 "짐중량"
///   0x00571290  "현재 계약중입니다."
///   0x0056BE20  "━━━━━━━━기술━━━━━━━━"  ·  "%-18s%2d"  ·  "━━━━━━━━어학━━━━━━━━"
/// </code>
/// <b>국적은 우리 쪽에 없다</b> — 게임은 새 놀이를 시작할 때 정하는데 우리는 아직 안 묻는다.
/// 화면에서 본 대로 포르투갈로 둔다.
///
/// <b>어학 칸은 비워 둔다</b> — 언어를 배우는 길(교회·상관)을 아직 안 옮겼다.
/// </remarks>
internal sealed class PersonInfoDialog : InfoDialog
{
    /// <summary>판 크기. 계약 정보 창과 나란히 보이게 같은 크기로 잡는다.</summary>
    private const double BoardWidth = 560, BoardHeight = 420;

    /// <summary>기술·어학 칸에 비워 두는 높이.</summary>
    private const double SkillHeight = 118, LanguageHeight = 52;

    /// <summary>아직 안 묻는 국적. 게임 시작 화면에서 정하는 값이다.</summary>
    public const string DefaultNation = "포르투갈";

    private PersonInfoDialog(Player player, double? lat, double? lon)
    {
        var rows = new StackPanel();
        rows.Children.Add(Label($"   이름      {player.Name}"));
        rows.Children.Add(Label($"   국적      {DefaultNation}"));
        rows.Children.Add(Label($"   명성      {player.Fame,8}"));
        rows.Children.Add(Label($"   소지금    {player.Gold,8}닢"));
        rows.Children.Add(Label($"   {Coords(lat, lon)}"));

        rows.Children.Add(Gap());
        rows.Children.Add(Label($"   피로도    {player.Fatigue,4}      사기      {player.Morale,4}"));
        rows.Children.Add(Label($"   총승원수  {player.Crew,4}      항해일    {player.DaysAtSea,4}일"));
        rows.Children.Add(Label($"   짐용량    {player.LoadedBarrels,4}/{player.Capacity,-4}" +
                                $" 짐중량    {player.LoadedWeight,5}/{player.Tonnage,-5}"));
        if (player.Contract != null) rows.Children.Add(Label("   현재 계약중입니다."));

        rows.Children.Add(Gap());
        rows.Children.Add(Divider("기술"));
        rows.Children.Add(List(Skills(player), SkillHeight));
        rows.Children.Add(Divider("어학"));
        rows.Children.Add(List([], LanguageHeight));

        Build("인물정보", rows, BoardWidth, BoardHeight, new GameButton("취소", Close));
    }

    /// <summary>
    /// 배운 기술을 게임 서식(<c>%-18s%2d</c>)으로. 하나도 없으면 빈 칸이다.
    /// </summary>
    private static IEnumerable<string> Skills(Player player)
    {
        foreach (var (name, level) in player.Skills)
            if (level > 0)
                yield return $"{GameUi.Pad(name, 18)}{level,2}";
    }

    /// <summary>
    /// 함대좌표 줄. 바다에 안 나가 있으면 게임처럼 <c>---</c> 로 둔다.
    /// </summary>
    private static string Coords(double? lat, double? lon) =>
        lat is not { } y || lon is not { } x
            ? "함대좌표  위도 ---도  경도 ---도"
            : $"함대좌표  {(y >= 0 ? "북" : "남")}위 {Math.Abs(y),3:F0}도  " +
              $"{(x >= 0 ? "동" : "서")}경 {Math.Abs(x),3:F0}도";

    /// <summary>인물정보 판을 연다.</summary>
    /// <param name="lat">지금 위도. 바다에 안 나가 있으면 null.</param>
    /// <param name="lon">지금 경도.</param>
    public static void Show(Window owner, Player player, double? lat = null, double? lon = null) =>
        new PersonInfoDialog(player, lat, lon) { Owner = owner }.ShowDialog();
}
