using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Engine.Sea;

/// <summary>바다에서 하루를 넘길 때 일어날 수 있는 일.</summary>
/// <remarks>
/// 게임은 일곱 갈래를 굴리고(<c>0x004746CD</c> 의 <c>rand(7)</c>) 갈래마다 딴 처리로
/// 뛴다(점프표 <c>0x00474D7C</c>).
/// <code>
///   0  0x004746F9   쥐          3  0x00474B4A  반란
///   1  0x00474812   괴혈병      4  0x00474BD5  폭풍
///   2  0x004749FA   전염병      5  0x00474BD5  눈보라
///                               6  0x004746F9  쥐(0 과 같은 자리)
/// </code>
/// 예전에는 0·1·2 를 암초·무풍·병으로 적어 두었는데 <b>틀렸다</b>. 갈래마다 부르는
/// 문구를 보면 갈린다 — <c>0x00534D38</c> "쥐가 발생하기…" · <c>0x00534F30</c>
/// "…괴혈병에 걸려…" · <c>0x00535090</c> "…전염병으로 인해…" 다.
/// </remarks>
public enum SeaEventKind
{
    /// <summary>폭풍. 갈래 넷.</summary>
    Storm,

    /// <summary>눈보라. 갈래 다섯.</summary>
    Blizzard,

    /// <summary>쥐. 갈래 0 과 6.</summary>
    Rats,

    /// <summary>괴혈병. 갈래 하나.</summary>
    Scurvy,

    /// <summary>전염병. 갈래 둘.</summary>
    Plague,

    /// <summary>괴혈병이 돌기 전의 귀띔 — 터지지는 않는다.</summary>
    Weakening,

    /// <summary>전염병이 돌기 전의 귀띔 — 터지지는 않는다.</summary>
    StrangeIllness,

    /// <summary>반란. 갈래 셋.</summary>
    Mutiny,
}

/// <summary>폭풍이 지나간 뒤에 남은 것.</summary>
/// <param name="Kind">폭풍인지 눈보라인지.</param>
/// <param name="Hurt">배마다 깎인 내구. 함대 차례대로다.</param>
/// <param name="Lost">놓친 배의 이름.</param>
public sealed record SeaEventResult(SeaEventKind Kind, IReadOnlyList<int> Hurt,
                                    IReadOnlyList<string> Lost)
{
    /// <summary>어느 배든 상했는지.</summary>
    public bool AnyHurt => Hurt.Any(h => h > 0);

    /// <summary>폭풍이면 "폭풍", 눈보라면 "눈보라". 게임 문구에 그대로 끼운다.</summary>
    public string Word => Kind == SeaEventKind.Storm ? "폭풍" : "눈보라";
}

/// <summary>
/// 바다 사건 판정. 게임의 <c>0x00474680</c>(일어나는가)과 <c>0x00474DA0</c>(뒷정리)을
/// 옮긴 것이다.
/// </summary>
/// <remarks>
/// <code>
/// ; 일어나는가  0x00474680
/// 474680  if ([0x5A4D40] &lt;= 9) return           ; 열흘 넘게 항해했을 때만
/// 4746a6  edi = [항해사+0x40] * 25
/// 4746b4  edi += [0x5B60D4] + 0x1A              ; 항해 능력 + 26
/// 4746c5  if (edi &gt;= rand(200)) return          ; 안 일어난다
/// 4746cd  edi = rand(7)                          ; 갈래
/// 4746db  if (그 갈래 비트가 이미 서 있으면) return
/// 4746f2  jmp *0x474D7C[edi*4]
/// </code>
/// 사건 갈래는 함대 객체 <c>+0xD4</c> 의 비트 하나씩으로 든다
/// (<c>0x00474630</c> 세우기 · <c>0x00474660</c> 보기). 뒷정리가 그 비트를 도로 끈다.
/// </remarks>
public static class SeaEvents
{
    /// <summary>이 날수를 넘겨야 사건이 일어난다(<c>0x00474680</c> 의 <c>cmpl $9</c>).</summary>
    public const int MinDaysAtSea = 10;

    /// <summary>사건 갈래 수(<c>rand(7)</c>).</summary>
    public const int KindCount = 7;

    /// <summary>폭풍과 눈보라의 갈래 번호. 점프표에서 둘 다 <c>0x00474BD5</c> 로 간다.</summary>
    public const int StormKind = 4, BlizzardKind = 5;

    /// <summary>반란의 갈래 번호(<c>0x00474B4A</c>).</summary>
    public const int MutinyKind = 3;

    /// <summary>쥐의 갈래 번호 둘. 점프표가 0 과 6 을 같은 자리로 보낸다.</summary>
    public const int RatsKind = 0, RatsAgainKind = 6;

    /// <summary>괴혈병(<c>0x00474812</c>)과 전염병(<c>0x004749FA</c>)의 갈래 번호.</summary>
    public const int ScurvyKind = 1, PlagueKind = 2;

    /// <summary>
    /// 병이 터지기 전에 <b>귀띔만 하고 끝나는</b> 주사위 폭.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   474812  괴혈병  if (rand(120) &lt; 항해술 + 1)  "제독! 모두 약해져 있습니다…"  로 끝
    ///   4749fa  전염병  if (rand( 60) &lt; 항해술 + 1)  "제독! 이상한 병이 돌고 있습니다…"
    /// </code>
    /// 폭이 좁을수록 귀띔이 잦다 — 그러니 <b>전염병 쪽이 더 자주 미리 잡힌다</b>.
    /// 항해술이 높을수록 미리 알아채는 것이라, 여기서도 항해술이 배를 지킨다.
    /// </remarks>
    public const int ScurvyNotice = 120, PlagueNotice = 60;

    /// <summary>괴혈병을 막는 의술의 자리(<c>0x0047484C</c> · <c>0x0047486A</c> 의 <c>cmp 3</c>).</summary>
    /// <remarks>
    /// 게임은 부하 둘의 자리를 3 과 견준다. 우리는 부하마다 기능을 들고 있지 않아
    /// <b>주인공의 의학</b>으로 갈음한다 — 전염병 쪽에는 이 관문이 아예 없다.
    /// </remarks>
    public const int MedicineNeeded = 3;

    /// <summary>그 의술의 이름.</summary>
    public const string MedicineSkill = "의학";

    /// <summary>반란을 그냥 보는 주기(<c>mov $0x7,%ecx ; idiv</c>).</summary>
    public const int MutinyPeriod = 7;

    /// <summary>이 피로도를 넘으면 주기와 상관없이 본다(<c>cmpl $0x50, 0x28(%esi)</c>).</summary>
    public const int MutinyFatigue = 80;

    /// <summary>안 일어나게 하는 밑값(<c>add edi, 0x1A</c>).</summary>
    public const int SafeBase = 26;

    /// <summary>항해술 한 자리가 더해 주는 안전(<c>edi * 25</c>).</summary>
    public const int SafePerLevel = 25;

    /// <summary>판정에 굴리는 주사위 폭(<c>push 0xC8</c>).</summary>
    public const int SafeRoll = 200;

    /// <summary>사건이 걸리는 항해 기술 이름.</summary>
    public const string SkillName = "항해술";

    /// <summary>폭풍이 부는 위도 띠(도). 무역풍 자리다.</summary>
    public const double StormLatMin = 10, StormLatMax = 25;

    /// <summary>눈보라가 치는 위도 띠(도).</summary>
    public const double BlizzardLatMin = 60, BlizzardLatMax = 75;

    /// <summary>
    /// 오늘 무슨 일이 있는지. 없으면 <c>null</c>.
    /// </summary>
    /// <param name="player">함대.</param>
    /// <param name="lat">지금 위도(북이 양수).</param>
    /// <param name="rng">주사위.</param>
    public static SeaEventKind? Roll(Player player, double lat, Random rng)
    {
        if (player.DaysAtSea <= MinDaysAtSea) return null;

        int safe = player.LevelOf(SkillName) * SafePerLevel + SafeBase;
        if (safe >= rng.Next(SafeRoll)) return null;

        int sail = player.LevelOf(SkillName);
        return rng.Next(KindCount) switch
        {
            RatsKind or RatsAgainKind => SeaEventKind.Rats,

            // 병 둘은 먼저 귀띔 주사위를 굴린다. 걸리면 그것으로 끝이다.
            ScurvyKind when rng.Next(ScurvyNotice) < sail + 1 => SeaEventKind.Weakening,
            ScurvyKind when player.LevelOf(MedicineSkill) >= MedicineNeeded => null,
            ScurvyKind => SeaEventKind.Scurvy,

            PlagueKind when rng.Next(PlagueNotice) < sail + 1 => SeaEventKind.StrangeIllness,
            PlagueKind => SeaEventKind.Plague,

            MutinyKind => Mutinous(player) ? SeaEventKind.Mutiny : null,
            StormKind or BlizzardKind => BandOf(lat),
            _ => null,
        };
    }

    /// <summary>
    /// 병이나 쥐가 앗아 가는 선원 수. 없으면 0.
    /// </summary>
    /// <remarks>
    /// 게임은 배마다 선원을 담고 하나씩 골라 죽이는데(<c>0x00474941</c> 벌이 이름을 뽑아
    /// "…돌아올 수 없는 사람이 되었다" 를 낸다) 우리는 함대가 통째로 태우므로 <b>머릿수만</b>
    /// 던다. 쥐는 사람을 안 잡고 <b>식량</b>을 축낸다.
    /// </remarks>
    public static int TollOf(SeaEventKind kind, Random rng) => kind switch
    {
        SeaEventKind.Scurvy or SeaEventKind.Plague => rng.Next(3) + 1,
        _ => 0,
    };

    /// <summary>쥐가 축내는 식량 통 수(<c>0x0047476E</c> 벌).</summary>
    public static int RatsEat(Random rng) => rng.Next(3) + 1;

    /// <summary>
    /// 반란이 일 만한지 — <b>이레마다</b>, 또는 피로도가 80 을 넘었으면 언제든.
    /// </summary>
    /// <remarks>
    /// <code>
    /// 474b4a  ecx = 7 ; eax = [0x5A4D40] ; idiv ecx
    /// 474b57  if (나머지 == 0) 그냥 본다
    /// 474b5b  cmpl $0x50, 0x28(%esi) ; jle 끝     ; 아니면 피로도 &gt; 80 이라야
    /// </code>
    /// 피로도가 여기서만 쓰인다 — 폭풍이 왜 피로도를 올리는지가 이 줄에 있다.
    /// </remarks>
    public static bool Mutinous(Player player) =>
        player.DaysAtSea % MutinyPeriod == 0 || player.Fatigue > MutinyFatigue;

    /// <summary>
    /// 그 위도에서 부는 것. 띠 밖이면 <c>null</c>.
    /// </summary>
    /// <remarks>
    /// 게임은 위도를 <c>0x005B63B4</c> 에 0~20000 으로 들고(10000 이 적도) 띠를 이렇게 나눈다.
    /// <code>
    ///   폭풍     0x1C37~0x22B9 · 0x2B67~0x31E9   = 적도에서 10~25도
    ///   눈보라   0x0683~0x0D06 · 0x411A~0x479D   = 적도에서 60~75도
    /// </code>
    /// 여덟 값이 10000 을 가운데 두고 짝을 이룬다 — 남북이 같다.
    /// </remarks>
    public static SeaEventKind? BandOf(double lat)
    {
        double a = Math.Abs(lat);
        if (a is >= StormLatMin and <= StormLatMax) return SeaEventKind.Storm;
        if (a is >= BlizzardLatMin and <= BlizzardLatMax) return SeaEventKind.Blizzard;
        return null;
    }

    /// <summary>
    /// 손상을 재는 기준 내구(<c>0x00474EF9</c> 의 <c>mov $0x64,%esi</c>).
    /// </summary>
    public const int HurtBase = 100;

    /// <summary>폭풍이 올리는 피로도(<c>0x00474D18</c> 의 <c>rand(11) + 0x14</c>).</summary>
    public static int TireOf(Random rng) => rng.Next(11) + 20;

    /// <summary>폭풍이 깎는 사기(<c>0x00474D2D</c> 의 <c>rand(11) + 0x0A</c>, 부호 뒤집음).</summary>
    public static int Dishearten(Random rng) => rng.Next(11) + 10;

    /// <summary>바다에서 하루를 난 끝.</summary>
    /// <param name="WaterLow">오늘 물이 사흘치 밑으로 떨어졌는지.</param>
    /// <param name="FoodLow">오늘 식량이 사흘치 밑으로 떨어졌는지.</param>
    /// <param name="WaterOut">오늘 물이 바닥났는지.</param>
    /// <param name="FoodOut">오늘 식량이 바닥났는지.</param>
    /// <param name="Tired">오늘 오른 피로도.</param>
    /// <param name="Cold">추위 값(0~3).</param>
    /// <param name="Weary">넘어선 피로 문턱(50·70·90). 안 넘었으면 0.</param>
    public sealed record Day(bool WaterLow, bool FoodLow, bool WaterOut, bool FoodOut,
                             int Tired, int Cold, int Weary);

    /// <summary>추위가 한 단씩 오르는 위도(도). 게임 값 <c>0x1C36·0x1E61·0x208D</c> 다.</summary>
    public static readonly double[] ColdLats = [65, 70, 75];

    /// <summary>피로 알림이 뜨는 문턱(<c>0x004757C5</c> 벌).</summary>
    public static readonly int[] WearySteps = [50, 70, 90];

    /// <summary>
    /// 그 위도의 추위 — 65도에서 한 단, 70도에서 두 단, 75도를 넘으면 세 단이다.
    /// </summary>
    /// <remarks>
    /// <code>
    /// 475587  eax = |10000 - 0x5B63B4|            ; 적도에서 떨어진 만큼
    /// 47559e  edx  = (eax &gt;= 0x1C36) ? 1 : 0     ; 7222 = 65도
    /// 4755ac  edx += (eax &gt;= 0x1E61) ? 1 : 0     ; 7777 = 70도
    /// 4755bc  edx += (eax &gt;= 0x208D) ? 1 : 0     ; 8333 = 75도
    /// </code>
    /// 이 값은 그날 오르는 피로도에 그대로 더해진다 — <b>추운 데를 지나면 더 지친다</b>.
    /// </remarks>
    public static int ColdAt(double lat)
    {
        double a = Math.Abs(lat);
        int cold = 0;
        foreach (double step in ColdLats) if (a >= step) cold++;
        return cold;
    }

    /// <summary>
    /// 바다에서 하루를 난다 — 식량과 물을 축내고, 그만큼 지친다.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x00475470</c>(항해 하루치)이다. 부르는 곳은 <c>0x0044B1A2</c> 하나다.
    /// <code>
    /// 4755e6  소모 = max(1, 선원수 * 5 / 6)      ; 식량·물 각각
    /// 475624  사흘치 밑으로 <b>떨어진 그 날</b> "얼마 남지 않았습니다!"
    /// 4756b4  0 이 <b>된 그 날</b>              "바닥을 드러내고 있습니다"
    /// 47578e  피로 += rand(2) + 4 - 항해사등급 + 추위      ; 둘 다 있을 때
    /// 4757a4  피로 += rand(3) + 6 - …                     ; 한쪽이 바닥
    /// 47576e  피로 += rand(3) + 8 - …                     ; 둘 다 바닥
    /// </code>
    /// <b>바다에서는 날마다 지친다</b> — 폭풍이 없어도 오래 나가 있으면 반란이 온다.
    /// 항해사 등급은 우리에게 없어 그 자리에 <b>항해술 자리</b>를 넣는다.
    /// </remarks>
    public static Day PassDay(Player player, double lat, Random rng)
    {
        int use = Supply.DailyUse(player.Crew);
        int warn = use * Supply.LowDays;
        var (water0, food0) = player.UseDailySupply();
        int water = player.SupplyUnitsOf(SupplyKind.Water);
        int food = player.SupplyUnitsOf(SupplyKind.Food);

        static bool Crossed(int before, int after, int mark) =>
            before >= mark && after < mark && after > 0;

        int cold = ColdAt(lat);
        int tired = water == 0 && food == 0 ? rng.Next(3) + 8
                  : water == 0 || food == 0 ? rng.Next(3) + 6
                  : rng.Next(2) + 4;
        tired = Math.Max(0, tired - player.LevelOf(SkillName) + cold);

        int was = player.Fatigue;
        player.Tire(tired);

        int weary = 0;
        foreach (int mark in WearySteps)
            if (was < mark && player.Fatigue >= mark) weary = mark;

        return new Day(
            Crossed(water0, water, warn), Crossed(food0, food, warn),
            water0 > 0 && water == 0, food0 > 0 && food == 0,
            tired, cold, weary);
    }

    /// <summary>선원 대표와 벌인 승부의 끝.</summary>
    /// <param name="Won">이겼는지.</param>
    /// <param name="Mine">내가 굴린 값.</param>
    /// <param name="Rival">대표가 굴린 값.</param>
    /// <param name="Deserted">져서 떠난 선원 수.</param>
    public sealed record Fight(bool Won, int Mine, int Rival, int Deserted);

    /// <summary>승부에 걸리는 기술 이름.</summary>
    public const string SwordName = "검술";

    /// <summary>검술 한 자리가 얹어 주는 값.</summary>
    public const int SwordPerLevel = 15;

    /// <summary>반란을 눌러 앉히면 오르는 사기(<c>0x004753EA</c> 의 <c>push 0x1E</c>).</summary>
    public const int MutinyCheer = 30;

    /// <summary>
    /// 선원 대표와 승부한다.
    /// </summary>
    /// <remarks>
    /// 게임(<c>0x004751E0</c>)은 여기서 <b>진짜 결투 창</b>을 띄운다 —
    /// <c>0x004AA700(0x113, 상대, 7, -1)</c> 이고, 상대는 그 자리에서 지어낸 사람이다.
    /// <code>
    /// 475280  +0x0C = rand(10) + 0x14      ; 20~29
    /// 47528e  +0x20 = rand(16) + 0x45      ; 69~84
    /// 47529e  +0x24 = rand(16) + 0x27      ; 39~54
    /// 4752ae  +0x28 = rand(15) + 0x3B      ; 59~73
    /// 4752be  +0x2C = rand(16) + 0x27      ; 39~54
    /// 4752ce  +0x30 = rand(16) + 0x27      ; 39~54
    /// 4752e3  +0x34 = 0x31                 ; 49
    /// </code>
    /// <b>우리에게는 결투 창이 없어 한 판 주사위로 갈음한다</b> — 상대 값은 게임이 지어내는
    /// 폭(<c>rand(16) + 0x27</c>) 그대로 쓰고, 이쪽은 <c>rand(100)</c> 에 검술 자리를 얹는다.
    ///
    /// 이기면 사기가 30 오른다(<c>0x004753EA</c>). <b>지면 게임이 끝난다</b> —
    /// <c>0x0044AF40(0x5A4D18, 4)</c> 로 놀이 상태를 갈아 버린다. 우리 쪽에는 끝나는 길이
    /// 없어 <b>선원 절반이 배를 버리고 사기가 바닥나는 것</b>으로 갈음한다. 이 벌은 우리가
    /// 지은 것이다.
    /// </remarks>
    public static Fight Duel(Player player, Random rng)
    {
        int rival = rng.Next(16) + 0x27;
        int mine = rng.Next(100) + player.LevelOf(SwordName) * SwordPerLevel;

        if (mine >= rival)
        {
            player.Cheer(MutinyCheer);
            return new Fight(true, mine, rival, 0);
        }

        int gone = player.Crew / 2;
        player.AddCrew(-gone);
        player.SetMorale(0);
        return new Fight(false, mine, rival, gone);
    }

    /// <summary>
    /// 폭풍을 맞는다. 배마다 내구를 깎고, 0 이 된 배는 놓친다.
    /// </summary>
    /// <remarks>
    /// <code>
    /// ; 뒷정리  0x00474DA0 — 폭풍/눈보라 자리(0x00474EB1~)
    /// 474ef9  esi = 100 - 지금내구(0x44C860)
    /// 474f0c  esi = esi / 10 + rand(3)                     ; 손상
    /// 474f28  추진력 = clamp(추진력 - 손상, 0, 150)          ; 0x44C810 세터
    /// 474f5a  내구   = clamp(내구   - 손상, 기함?1:0, 250)   ; 0x44C850 세터
    /// 474f74  if (내구 == 0) 그 배를 함대에서 뺀다(0x473E60) — "눈에 띄지 않습니다"
    /// </code>
    /// <b>손상은 내구가 높을수록 작다.</b> 100 이 기준이라 갤리온(70)은 한 번에 3~5,
    /// 카라벨(20)은 8~10 씩 깎인다 — <b>작은 배가 훨씬 아프다</b>. 그리고 한 번 상하기
    /// 시작하면 다음 폭풍이 더 아프다.
    ///
    /// 우리 선체 값이 게임 화면에서 옮긴 그 값이라 <b>기준 100 을 그대로 쓴다</b>.
    /// 게임은 추진력도 같이 깎지만 우리 추진력은 아직 배를 모는 데 안 쓰여 내구만 깎는다.
    ///
    /// 기함은 안 잃는다. 게임도 기함 자리의 내구를 1 밑으로 안 내린다(<c>ebp</c>).
    /// </remarks>
    public static SeaEventResult Resolve(Player player, SeaEventKind kind, Random rng)
    {
        player.Tire(TireOf(rng));
        player.Cheer(-Dishearten(rng));

        var hurt = new int[player.Ships.Count];
        for (int i = 0; i < hurt.Length; i++)
            hurt[i] = Math.Max(0, HurtBase - player.Ships[i].Hp) / 10 + rng.Next(3);

        // 뒤에서부터 깎아야 배를 잃어도 앞 칸의 짝이 안 어긋난다.
        var lost = new List<string>();
        for (int i = hurt.Length - 1; i >= 0; i--)
        {
            var ship = player.Ships[i];
            bool flag = i == player.Flagship;
            ship.Hurt(hurt[i], floor: flag ? 1 : 0);
            if (ship.Hp == 0 && !flag && player.Ships.Count > 1)
            {
                lost.Add(ship.Name);
                player.LoseShip(i);
            }
        }
        lost.Reverse();

        return new SeaEventResult(kind, hurt, lost);
    }
}
