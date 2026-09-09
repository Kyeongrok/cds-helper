namespace CdsHelper.Game.Engine.Land;

/// <summary>
/// 뭍을 걷다 마주치는 부대 열여섯 — 구역 여덟에 두 벌씩이다(<c>0x00569EC0</c>).
/// </summary>
/// <remarks>
/// <c>0x0048BE80</c> 이 걸음마다 <c>rand(500)</c> 을 굴려 하나를 뽑는다. 구역표 한 칸이
/// 32바이트고 <c>+0x00</c> 경도 아래·위 <c>+0x08</c> 위도 아래·위(모두 16으로 나눈 값),
/// 그 뒤에 벌 둘이 <c>(병력 밑, 굴림 폭)</c> 짝으로 붙는다.
/// <code>
///   0048bf16  벌 = rand(2)
///   0048bf1d  대장 인물 = 0xF6 + 구역*2 + 벌        ; 246 ~ 261
///   0048bf8b  병력 = 밑 + rand(폭)
/// </code>
/// 대장 이름은 EXE 가 아니라 <b>세이브의 인물 표</b>에 있다(<c>0x924A</c>, 한 칸
/// <c>0x90</c>, 이름 <c>+0x32</c>). 자세한 것은 볼트 <c>65.분석-육상전</c> 10.2 절이다.
/// </remarks>
public static class LandFieldFoes
{
    /// <param name="Name">대장 이름 — 인물 246~261 이다.</param>
    /// <param name="Least">병력 밑값.</param>
    /// <param name="Spread">병력 굴림 폭 — <c>밑 + rand(폭)</c> 이다.</param>
    /// <param name="Culture">
    /// 적 그림과 진형을 가르는 문화권.
    /// </param>
    /// <param name="Where">구역 이름 — 네모 안에 드는 도시로 붙였다.</param>
    public readonly record struct Party(string Name, int Least, int Spread, int Culture,
                                        string Where);

    /// <summary>
    /// 구역 여덟 x 두 벌. 차례가 곧 <c>대장 인물 − 246</c> 이다.
    /// </summary>
    /// <remarks>
    /// <b>문화권은 우리가 어림한 것</b>이다. 게임은 적 대장 국적의 <b>수도 도시</b>의
    /// <c>+0x58</c> 을 쓰는데(<c>0x00447070</c>), 우리는 그 인물 레코드를 아직 안 들고
    /// 있어 구역에 맞는 것을 박아 두었다.
    /// </remarks>
    public static readonly Party[] All =
    [
        new("예니체리",     200, 100,  3, "동지중해·근동"),
        new("맘루크",       150, 100,  3, "동지중해·근동"),
        new("마차이족",      50,  50,  8, "아프리카"),
        new("자가족",        30,  50,  8, "아프리카"),
        new("무슬림",       100,  50,  4, "인도·서아시아"),
        new("마라타",       100,  50,  4, "인도·서아시아"),
        new("경비병",       100,  50,  6, "중국"),
        new("도적단",        50, 100,  6, "중국"),
        new("전국 무사단",   100,  50,  7, "일본"),
        new("산적",          50, 100,  7, "일본"),
        new("타타르족",     100, 100,  3, "중앙아시아"),
        new("몽골족",       150, 100,  3, "중앙아시아"),
        new("네그리트",      30,  50,  5, "동남아시아"),
        new("도둑족",        30,  50,  5, "동남아시아"),
        new("수우족",        50,  50,  9, "북아메리카"),
        new("나체즈족",      50,  50,  9, "북아메리카"),
    ];

    /// <summary>그 벌의 병력을 굴린다 — <c>밑 + rand(폭)</c> 이다.</summary>
    public static int MenOf(int at, GameRandom dice)
    {
        var party = All[Math.Clamp(at, 0, All.Length - 1)];
        return party.Least + dice.Next(Math.Max(1, party.Spread));
    }
}
