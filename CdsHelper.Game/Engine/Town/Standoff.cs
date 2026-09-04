using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Engine.Town;

/// <summary>
/// 적대 도시 앞에서 벌어지는 일 — 공격 · 잠입 · 교섭 · 떠난다.
/// </summary>
/// <remarks>
/// 게임은 입항을 시도할 때(<c>0x00468790</c>) 그 도시가 딸린 나라의 <b>적대도</b>를 먼저
/// 본다. 나라마다 열여섯 바이트짜리 형편 레코드가 <c>0x005859C0</c> 에 있고 적대도는
/// <c>+0x0C</c> 다(<c>0x00429D90</c>).
/// <code>
///   468796  적대도 = 형편(그 도시의 나라)[+0x0C]
///   468804  적대도 &lt;= 0  →  여느 입항 흐름
///           적대도 &gt;  0  →  적대 차림표 0x004A56F0(항구면 1, 마을이면 0)
/// </code>
/// 차림표는 열두 바이트짜리 칸 넷을 쌓아 <c>0x00469A70(칸, 4, 1, …)</c> 로 낸다.
/// <b>꺼진 칸도 자리를 안 비우므로 고른 값은 언제나 붙박이 번호</b>이고, 고른 뒤
/// <c>0x004A5840</c> 뜀표로 갈린다.
///
/// 볼트 <c>65.분석-육상전</c> 의 「문 — 도시 상태와 적대 차림표」 를 옮긴 것이다.
/// </remarks>
public static class Standoff
{
    /// <summary>
    /// 그 나라의 <b>출입여부</b> — 0 자유 · 1 배로는 못 들어감 · 2 아주 막힘.
    /// </summary>
    /// <remarks>
    /// 게임은 판을 열 때 나라 표 <c>+0x14</c> 를 형편 칸으로 뜨고(<c>0x0041B320</c>), 그 뒤로는
    /// 형편 칸만 본다. 여기서는 <b>주인공이 아직 그 나라를 건드린 적이 없으면 표 값을 그대로
    /// 쓴다</b> — 결과가 같으면서, 표를 고치면 놀이에 곧장 먹는다.
    /// </remarks>
    public static int EntryOf(Player player, NationTable? nations, int nation)
    {
        if (nation < 0) return 0;
        if (player.Hostility.TryGetValue(nation, out int moved)) return moved;
        return nations?.Find(nation)?.Entry ?? 0;
    }

    /// <summary>
    /// 그 문이 막혔는가 — <b>항구와 뭍의 잣대가 다르다</b>.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   004687fd  출입여부 &gt; 0   → 적대 차림표 0x004A56F0(1)   ; 1 = 항구
    ///   004770bd  출입여부 == 2  → 적대 차림표 0x004A56F0(0)   ; 0 = 마을(뭍)
    /// </code>
    /// 그래서 <b>1 은 배로만 막고 뭍은 연다.</b> 출입여부가 1 인 나라 열다섯이 죄다
    /// 이슬람권과 명인 것이 이 읽기와 맞는다 — 배로 항구에 들이대면 쫓기지만 뭍길로는
    /// 드나든다. 2 인 그라나다와 오스만·투르크만 뭍까지 막힌다.
    /// </remarks>
    public static bool Barred(int entry, bool byLand) => byLand ? entry >= 2 : entry > 0;

    /// <summary>고른 값. 꺼진 칸도 자리를 지키므로 붙박이 번호다.</summary>
    public const int Attack = 0, Sneak = 1, Talk = 2, Leave = 3;

    /// <summary>차림표 넉 줄(<c>0x00552198</c> 부터).</summary>
    public static readonly string[] Choices = ["공격한다", "잠입한다", "교섭한다", "떠난다"];

    /// <summary>「떠난다」를 골랐을 때의 말(<c>0x005521D0</c>).</summary>
    public const string GiveUpWord = "할 수 없군요. 포기합시다.";

    /// <summary>공격 전에 두 번 묻는 말(<c>0x00551BF0</c> · <c>0x00551C00</c>).</summary>
    public const string SureWord = "진심이십니까!?";
    public const string AttackWord = "육상 전투에 들어가겠습니다. 좋습니까?";

    // ── 잠입 ──────────────────────────────────────────────────────────────

    /// <summary>
    /// 그 나라에 숨어들 수 있는지 — <b>갈래가 3 이나 4</b> 라야 한다.
    /// </summary>
    /// <remarks>
    /// <c>0x004A56D0</c> → <c>0x004A1800</c> 이 그 도시의 나라를 찾아
    /// <c>0x004CA37C + 나라*24</c>(나라 표 <c>+0x0C</c>)를 읽고 3 이나 4 일 때만 켠다.
    /// 이슬람권이 3(사파비만 4)이고 유럽은 0, 명·조선은 7, 일본은 6 이라 죄다 꺼진다 —
    /// <b>명에 잠입이 없는 까닭이 여기 있다</b>.
    /// </remarks>
    public static bool CanSneak(int sect) => sect is 3 or 4;

    /// <summary>잠입에 보태 주는 물건(<c>0x00551F18</c>). 들고 있으면 백 점이다.</summary>
    public const string TurbanName = "터번";

    /// <summary>말이 이만큼은 돼야 겁을 안 낸다(<c>0x004A5315</c>).</summary>
    public const int SafeTongue = 3;

    /// <summary>말이 서툴 때 대원이 말리는 소리(<c>0x00551EB8</c>).</summary>
    public const string TongueTooThin =
        "제독, 소용없습니다! 말이 통하지 않는 게 알려지면, 잡히고 맙니다.";

    /// <summary>말이 통할 때(<c>0x00551F00</c>).</summary>
    public const string TakeCare = "제독, 조심하십시오.";

    /// <summary>들킨 순간(<c>0x00551F30</c>) · 빠져나온 뒤(<c>0x00551F48</c>).</summary>
    public const string Spotted = "침입자다! 잡아라!!";
    public const string SneakedIn = "제독, 무사하셨습니까!? 여기서부터는 안전합니다.";

    /// <summary>잡혔을 때(<c>0x00551F78</c> 부터).</summary>
    public const string Caught = "침입자를 잡았다! 재판소에 세워라!!";
    public const string Banished =
        "마을에서 추방을 명한다. 목숨만이라도 구한걸 알라신에게 감사해라.";
    public const string Fined =
        "벌금형 또는 추방을 명한다. 목숨을 구한걸 알라신에게 감사해라.";
    public const string Robbed = "소지금 전부를 빼앗겼다!";

    /// <summary>달아났을 때(<c>0x00552040</c>).</summary>
    public const string GotAway = "제독, 무사합니까! 여기는 위험하니 포기합시다.";

    /// <summary>잠입이 되는지 굴린다(<c>0x004A539A</c> ~ <c>0x004A53CC</c>).</summary>
    /// <remarks>
    /// <code>
    ///   4a539a  점수 = 그 도시 말 수준 x 33
    ///   4a53a4  점수 += 터번을 들었으면 100
    ///   4a53af  점수 += (운 + 1) / 2
    ///   4a53ca  점수 &gt;= rand(250) 이면 숨어들었다
    /// </code>
    /// </remarks>
    public static bool Sneaks(Player player, int tongue, bool turban, GameRandom dice) =>
        tongue * 33 + (turban ? 100 : 0) + (player.AbilityOf(Ability.Luck) + 1) / 2
        >= dice.Next(250);

    /// <summary>들킨 뒤에 달아나는지(<c>0x004A5401</c>). 못 달아나면 재판이다.</summary>
    /// <remarks><c>rand(90) &lt;= 무력 + 1</c> 이라야 빠져나온다.</remarks>
    public static bool Escapes(Player player, GameRandom dice) =>
        dice.Next(90) <= player.AbilityOf(Ability.Might) + 1;

    // ── 교섭 ──────────────────────────────────────────────────────────────

    /// <summary>교섭이 되는지 굴린다(<c>0x004A55C6</c> ~ <c>0x004A55EB</c>).</summary>
    /// <remarks>
    /// <code>
    ///   4a55ce  점수 = 웅변 x 33
    ///   4a55da  점수 += 매력
    ///   4a55e5  점수 += 1
    ///   4a55e9  점수 &gt;= rand(200) 이면 교섭이 됐다
    /// </code>
    /// </remarks>
    public static bool Talks(Player player, GameRandom dice) =>
        player.LevelOf(Skill.Names[Skill.Rhetoric]) * 33
        + player.AbilityOf(Ability.Charm) + 1 >= dice.Next(200);

    /// <summary>
    /// 교섭이 되면 얼마를 건네는지(<c>0x004A55FB</c> ~ <c>0x004A5624</c>).
    /// </summary>
    /// <remarks>
    /// <c>rand(500) + (5 - 웅변) x 100</c> 이고 <b>소지금에서 잘린다</b> — 모자라면
    /// 있는 만큼만 낸다. 그러니 웅변이 오르면 굴림도 잘 되고 값도 싸진다.
    /// </remarks>
    public static int Price(Player player, GameRandom dice) =>
        dice.Next(500) + (5 - player.LevelOf(Skill.Names[Skill.Rhetoric])) * 100;

    /// <summary>교섭이 됐을 때(<c>0x005220A0</c> · <c>0x005220C0</c> · <c>0x005220F0</c>).</summary>
    public const string PaidWord = "금화 {0}닢을 건네었습니다.";
    public const string TalkWonWord = "잘됐습니다. 이것으로 {0}에 들어갈 수 있습니다";
    public const string TalkWonNews = "교섭에 성공했습니다. {0}에 들어갈 수 있습니다";

    /// <summary>어그러졌을 때(<c>0x00552130</c> · <c>0x00552158</c>).</summary>
    public const string TalkLostWord = "교섭할 수 없군요. 제독, 어떻게 할까요?";
    public const string TalkLostNews = "교섭에 실패했습니다. {0}에 들어갈 수 없습니다";

    /// <summary>돈이 없을 때(<c>0x00551CE0</c>). 검사는 <c>0x00468BF0</c> 이 한다.</summary>
    public const string TooPoorWord = "소지금이 모자랍니다!";

    /// <summary>배로 왔으면 「항구」, 말로 왔으면 「마을」(<c>0x00552120</c>).</summary>
    public static string Where(bool byLand) => byLand ? "마을" : "항구";

    // ── 트루데시야스 조약 ─────────────────────────────────────────────────

    /// <summary>조약이 서는 해(<c>0x00469880</c> 의 <c>cmp [0x005A4D20], 0x5D6</c>).</summary>
    public const int TreatyYear = 1494;

    /// <summary>조약 이름과 문구(<c>0x005521F0</c> · <c>0x00552208</c>).</summary>
    public const string TreatyName = "트루데시야스 조약";
    public const string TreatyWord = "{0}에 의해 {1}의 선원을 마을에 들여보낼 수는 없다.";

    /// <summary>
    /// 조약에 막히는지 — <b>1494년부터 포르투갈과 에스파니아가 서로를 막는다</b>.
    /// </summary>
    /// <remarks>
    /// <c>0x00469880</c> 이 해를 보고, 이어서 내 나라(<c>0x005B394C</c>: 0 포르투갈 ·
    /// 1 에스파니아)와 그 도시 나라를 견준다. 막히면 적대도가 0 이어도 같은 차림표가
    /// 뜬다(<c>0x0046ABB0</c>) — 적대도만으로는 안 열리는 문이 하나 더 있는 셈이다.
    /// </remarks>
    /// <param name="year">지금 해.</param>
    /// <param name="mine">내 나라 번호(0 포르투갈 · 1 에스파니아).</param>
    /// <param name="theirs">그 도시가 딸린 나라 번호.</param>
    public static bool TreatyBars(int year, int mine, int theirs) =>
        year >= TreatyYear && theirs is 0 or 1 && theirs != mine;
}
