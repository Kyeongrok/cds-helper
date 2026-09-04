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
    /// <b>보고</b>가 올리는 명성 — 항구 <b>발표</b>와 셈이 다르다
    /// (<see cref="Harbor.FameFor"/> 는 보수/70).
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x004111D0</c> 이다. 발견물 하나마다 이 셈을 하고 <c>0x004697C0(0, 명성)</c>
    /// 으로 올린다.
    /// <code>
    ///   411477  ebx = (발견물 번호 == 7)          ; 세계일주항로만 따로 센다
    ///   41146c  이미 알려진 것이면 명성 칸을 통째로 건너뛴다   ★
    ///   411483  세계일주 · 기한 안 : max(10, 보수/50)
    ///   4114b4  세계일주 · 늦음    : max(10, 보수/60)
    ///   4114e6  그 밖  · 기한 안   : max(10, 보수/50)
    ///   411510  그 밖  · 늦음      : max(10, 보수/50) / 2
    /// </code>
    /// 명성은 <b>깎이지 않은 표의 보수</b>로 센다 — <see cref="CreditFor"/> 가 깎는 것과
    /// 다른 값이다(<c>0x00411495</c> 가 표를 다시 읽는다).
    /// </remarks>
    /// <param name="known">
    /// 이미 세상에 알려진 발견물인가(<c>0x004AADB0</c>). 남이 먼저 보고해 사람 칸 2 에
    /// 이름이 올라가 있으면 참이고, <b>그러면 명성이 한 톨도 안 오른다</b>. 한 번 남의
    /// 이름이 올라가면 <c>0x004AACA0</c> 첫 줄이 되돌리지 않으므로 영영 그렇다.
    /// </param>
    public static int FameFor(DiscoveryTable.Record row, bool inTime, bool known)
    {
        if (known) return 0;

        if (row.Id == WorldRoute)
            return Math.Max(FameFloor, row.Reward / (inTime ? FameDivisor : LateFameDivisor));

        int fame = Math.Max(FameFloor, row.Reward / FameDivisor);
        return inTime ? fame : fame / 2;
    }

    /// <summary>보고가 올리는 명성의 나눗수(<c>0x004114E6</c>).</summary>
    public const int FameDivisor = 50;

    /// <summary>세계일주항로를 늦게 보고했을 때만 쓰는 나눗수(<c>0x004114B4</c>).</summary>
    public const int LateFameDivisor = 60;

    /// <summary>아무리 하찮아도 이만큼은 오른다(<c>0x004114D0</c>).</summary>
    public const int FameFloor = Harbor.FameFloor;

    /// <summary>
    /// 세계일주항로의 발견물 번호. 이것만 셈이 따로다(<c>0x004111ED</c> 의 <c>sub eax, 7</c>).
    /// </summary>
    /// <remarks>
    /// 보수가 300000 닢으로 표에서 홀로 크다 — 늦어도 반토막이 나지 않고 나눗수만
    /// 50 에서 60 으로 바뀐다. 친밀도도 기한을 따지지 않고 오른다.
    /// </remarks>
    public const int WorldRoute = 7;

    /// <summary>
    /// 보고가 움직이는 <b>친밀도</b>(<c>0x00478530</c> 이 0~100 으로 자른다).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   411220  세계일주        : max(1, 보수/10000) + 덤
    ///   4112a2  기한 안         : max(1, 보수/10000) + 덤
    ///   4112fc  늦음            : (max(2, 보수/10000) + 덤) / 2
    ///   411362  이미 알려진 것  : 기한 안이면 그대로, 늦었으면 -(rand(5)+5)
    /// </code>
    /// <b>덤</b>은 후원자가 그 갈래를 좋아할 때만 굴리는 <c>rand(10)</c> 이다
    /// (<c>0x004ADAE0</c> — 후원자 표 <c>+0x38</c> 의 비트, <see cref="Persuasion.Likes"/>).
    /// </remarks>
    public static int ClosenessFor(DiscoveryTable.Record row, bool inTime, bool known,
                                   bool likes, Random random)
    {
        if (known && row.Id != WorldRoute)
            return inTime ? 0 : -(random.Next(ClosenessDrop) + ClosenessDrop);

        int bonus = likes ? random.Next(ClosenessBonus) : 0;
        if (row.Id == WorldRoute || inTime)
            return Math.Max(1, row.Reward / ClosenessPerReward) + bonus;

        return (Math.Max(2, row.Reward / ClosenessPerReward) + bonus) / 2;
    }

    /// <summary>친밀도 한 칸을 만드는 보수(<c>0x0041125E</c> 의 <c>0x2710</c>).</summary>
    public const int ClosenessPerReward = 10000;

    /// <summary>좋아하는 갈래일 때 굴리는 덤의 폭(<c>0x0041124D</c>).</summary>
    public const int ClosenessBonus = 10;

    /// <summary>늦게 온 데다 알려진 것이면 깎이는 폭 — <c>-(rand(5)+5)</c>(<c>0x00411366</c>).</summary>
    public const int ClosenessDrop = 5;

    /// <summary>
    /// 보고한 발견물이 <b>후원자에게 쌓는 값</b>(<c>0x004113B4</c>, 후원자 <c>+0x24</c>).
    /// </summary>
    /// <remarks>
    /// 표의 보수를 그대로 쌓되 <b>이미 알려진 것이면 깎는다</b> — 기한 안이면 1/4,
    /// 늦었으면 1/5 다(<c>0x0041139B</c>). 상한은 후원자 표 <c>+0x2C</c> 의 만 배다.
    ///
    /// 이 값이 무엇에 쓰이는지는 아직 못 짚었다(후원자의 재산으로 보인다). 내 소지금과는
    /// 다른 자리다 — 내가 받는 돈은 <see cref="RewardFor"/> 뿐이다.
    /// </remarks>
    public static int CreditFor(int reward, bool inTime, bool known) =>
        known ? reward / (inTime ? 4 : 5) : reward;

    /// <summary>
    /// 보고할 때 후원자가 <b>돌려주는</b> 아이템인가 — 분류 7(서적·유물)만 그렇다.
    /// </summary>
    /// <remarks>
    /// <c>0x004113F5</c> 가 아이템 표(<c>0x004FD558</c>) <c>+0x14</c> 를 7 과 견주고,
    /// 맞으면 "이것은 자네가 가지고 가게" 하고 <c>0x004B1710</c> 으로 소지품에 넣는다
    /// — 문구도 <b>"…을 손에 넣었다!"</b> 다. <b>빼앗기는 것이 아니다.</b>
    /// </remarks>
    public const int KeepsakeCategory = 7;

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
        return To100((int)((long)unpaid * rate / 100));
    }

    /// <summary>100닢 단위로 내린다(<c>0x004117D0</c>). 100 이하면 그대로 둔다.</summary>
    public static int To100(int coins) => coins > 100 ? coins / 100 * 100 : coins;

    /// <summary>
    /// <b>남이 먼저 발표해 버렸을 때</b>의 사례(<c>0x004117F0</c>).
    /// </summary>
    /// <remarks>
    /// 위약금을 무는 것이 아니라 <b>받을 사례가 깎이는 것</b>이다.
    /// <code>
    ///   411805  eax = [0x005B619C]      ; 계약금
    ///   41180a  eax /= 4                ; 사분의 일
    ///   411814  0x004117D0(eax)         ; 100닢 단위 내림
    ///   4119e8  기한까지 넘겼으면 이 셈에 들지도 못하고 한 푼도 없다
    /// </code>
    /// 말도 따로 있다 — 기한 안이면 <b>"안됐지만, %ld닢 밖에 지불할 수 없습니다."</b>
    /// (<c>0x00530100</c>), 넘겼으면 <b>"계약기한이 지나 버렸으니, 사례는 지불할 수
    /// 없습니다."</b>(<c>0x00530238</c>)다.
    ///
    /// 세 갈래를 가르는 곳은 <c>0x00411FC0</c> 이고, 어느 쪽이든 <c>0x0041200E</c> 가
    /// <b>더하기만</b> 한다(<c>if (eax &gt; 0)</c>) — 소지금이 줄어드는 길은 여기에 없다.
    /// 위약금은 딴 자리다: 계약중단이 계약금/2(<c>0x0044F827</c>), 감찰관 사고가
    /// 후원자 표 <c>+0x20</c> x (n+1) x 1000(<c>0x0044FA76</c>)이다.
    /// </remarks>
    /// <param name="amount">계약금(<see cref="Support.Local.Models.Contract.Unpaid"/> 의 두 배).</param>
    public static int ScoopedRewardFor(int amount, bool inTime) =>
        inTime ? To100(amount / 4) : 0;

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
