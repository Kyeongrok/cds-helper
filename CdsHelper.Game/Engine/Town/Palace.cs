using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Engine.Town;

/// <summary>
/// 후원자와의 계약 규칙 — 보고할 것 고르기 · 사례 · 계약을 깰 때의 눈감아 주기.
/// </summary>
/// <remarks>
/// 후원자는 왕궁에만 앉는 것이 아니다 — 총독부·상관·학자 저택 어디든 앉고, 앉은 자리에
/// 설득·보고·계약중단 줄이 붙는다(<see cref="TownWorks"/>). 그래서 이름은 왕궁이지만
/// 자리가 아니라 <b>후원자와의 일</b>을 든다.
/// </remarks>
public static class Palace
{
    /// <summary>
    /// 그 후원자에게 보고할 발견물. 계약의 유적 번호를 가진 것 중 발견했고 아직 안 알린 것이다.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x0044EA00</c> 이다 — 계약을 맺은 그 자리인지(<c>0x0044E550</c>) 보고,
    /// 계약의 유적 번호로 모은다(<c>0x00493E60</c>). 게임은 도시와 <b>시설 종류</b>까지
    /// 견주는데 우리 계약은 후원자 이름과 마을을 들고 있으므로 그 둘로 가른다 —
    /// 결과는 같다(한 사람은 한 자리에만 앉는다).
    /// </remarks>
    public static List<DiscoveryTable.Record> ReportTargets(Player player, string patronName,
                                                            string cityName,
                                                            DiscoveryTable? table,
                                                            HintTable? hints)
    {
        if (player.Contract is not { } contract) return [];
        if (contract.Sponsor != patronName || contract.City != cityName) return [];
        if (table == null) return [];
        if (hints?.Find(contract.Hint) is not { } hint) return [];

        var rows = new List<DiscoveryTable.Record>();
        foreach (int id in player.Discoveries.Order())
        {
            if (player.HasAnnounced(id)) continue;
            if (table.Find(id) is not { } row || row.Hint != hint.Discovery) continue;
            rows.Add(row);
        }
        return rows;
    }

    /// <summary>
    /// 보고 사례. 미불(계약금의 반)에 비율을 먹이고 100닢 단위로 내린다.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x00411D10</c> · <c>0x004117D0</c> 그대로다.
    /// <code>
    ///   411d29  기한 안이면 120 + rand(30) %
    ///   411d47  늦었으면    90 - rand(20) %
    ///   411d3e  미불 x 비율 / 100, 100 을 넘으면 100닢 단위로 내림
    /// </code>
    /// </remarks>
    public static int RewardFor(int unpaid, bool inTime, Random random)
    {
        int rate = inTime ? 120 + random.Next(30) : 90 - random.Next(20);
        int paid = (int)((long)unpaid * rate / 100);
        return paid > 100 ? paid / 100 * 100 : paid;
    }

    /// <summary>기한 안에 깰 때 굴리는 주사위 폭(<c>add $0x64,%eax</c>).</summary>
    public const int OnTimeRoll = 100;

    /// <summary>기한을 넘겨 깰 때의 폭 — 반쯤 넓어져 통과하기 어렵다(<c>and $0x32</c>).</summary>
    public const int LateRoll = 150;

    /// <summary>문턱을 자르는 값(<c>cmp $0x61,%ecx</c>).</summary>
    public const int ForgiveCap = 97;

    /// <summary>
    /// 계약을 깨는 것을 후원자가 눈감아 주는지(<c>0x0044F8B0</c>).
    /// </summary>
    /// <remarks>
    /// 서로의 이름값이 높을수록 잘 봐 준다 — 문턱은 <c>후원자 명성/100 + 내 명성/100 + 1</c>
    /// 이고 아무리 높아도 97 에서 잘린다. 기한을 넘겼으면 주사위 폭이 넓어져 더 어렵다.
    /// </remarks>
    public static bool Forgiven(int patronFame, int playerFame, bool overdue, Random random) =>
        random.Next(overdue ? LateRoll : OnTimeRoll)
            < Math.Min(ForgiveCap, patronFame / 100 + playerFame / 100 + 1);
}
