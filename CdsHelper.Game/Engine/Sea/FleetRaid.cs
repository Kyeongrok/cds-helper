namespace CdsHelper.Game.Engine.Sea;

/// <summary>
/// 지도에 보이는 인물 함대와의 만남 — 운세 칸, 해전 뒤 명성·악명·전리품, 우호 거래 값의 셈.
/// </summary>
/// <remarks>
/// 볼트 <c>전투/93.분석-보이는 함대 습격(습격한다·우호적으로 접근한다)</c> 를 옮겼다. 화면은 모른다.
/// <code>
///   0x0048C049  두 칸 안에 든 배를 묻는다        0x0048C265  상대 갈래
///   0x0048C76D  습격한다                         0x0048C3B4  우호적으로 접근한다
///   0x0048CC20  해전(플래그 0)                   0x004350F0  판 끝 — 명성·악명·전리품
///   0x00432400  상대를 제 나라 수도로 · 60일      0x0048CCF0  우호 거래(보급물자)
/// </code>
/// </remarks>
public static class FleetRaid
{
    // ── 운세 칸 — 0x00477FE0 ────────────────────────────────────────────────

    /// <summary>운세 칸 수.</summary>
    public const int FortuneSlots = 8;

    /// <summary>별자리 표(<c>0x00568578</c>, 12 x 8 dword). EXE 에서 그대로 옮겼다.</summary>
    private static readonly int[,] ZodiacFortune =
    {
        { 2, 1, 0, 2, 1, 1, 1, 0 }, { 1, 0, 2, 1, 1, 0, 1, 2 }, { 1, 0, 0, 1, 1, 2, 2, 0 },
        { 0, 1, 1, 0, 2, 1, 1, 2 }, { 2, 1, 1, 2, 1, 1, 0, 0 }, { 1, 2, 0, 0, 1, 1, 2, 2 },
        { 2, 2, 1, 1, 0, 1, 1, 1 }, { 1, 1, 2, 1, 2, 0, 2, 0 }, { 1, 2, 0, 1, 2, 2, 1, 2 },
        { 2, 1, 2, 1, 1, 0, 1, 1 }, { 1, 2, 0, 1, 1, 1, 0, 1 }, { 0, 0, 1, 1, 2, 1, 1, 1 },
    };

    /// <summary>혈액형 표(<c>0x005686F8</c>, 4 x 8 dword).</summary>
    private static readonly int[,] BloodFortune =
    {
        { 0, 0, 0, 0, 0, 0, 1, -1 }, { 0, 1, 0, 0, 0, 0, -1, 0 },
        { 1, 0, 0, 0, -1, 0, 0, 0 }, { 0, 0, -1, 0, 0, 1, 0, 0 },
    };

    /// <summary>
    /// 인물의 운세 여덟 칸(<c>vtbl+0x24</c> = <c>0x00477FE0</c>).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   별자리 = (얼굴 + 혈액형 + 나라) % 12          ; 0x004780B0 (vtbl+0x28)
    ///   칸[k]  = 별자리표[별자리*32 + k*4] + 혈액형표[혈액형*32 + k*4]
    ///   칸[k]  = clamp(칸[k], 0, 2)                    ; 0x0049E540(값, 0, 2)
    /// </code>
    /// 셋째 칸(<c>[3]</c>)이 2 면 조약국 배가 「조약따윈 모른다!」로 덤비고, 넷째 칸(<c>[4]</c>)이
    /// 우호 거래의 값과 성사를 가른다. 얼굴·혈액형·나라는 인물 밑표(<c>0x004DF3F0</c>)에서 온다.
    /// </remarks>
    public static int[] FortuneOf(int face, int blood, int nation)
    {
        int zodiac = ((face + blood + nation) % 12 + 12) % 12;
        int b = Math.Clamp(blood, 0, 3);
        var slots = new int[FortuneSlots];
        for (int k = 0; k < FortuneSlots; k++)
            slots[k] = Math.Clamp(ZodiacFortune[zodiac, k] + BloodFortune[b, k], 0, 2);
        return slots;
    }

    // ── 해전 뒤 — 0x004350F0 (플래그 0) ─────────────────────────────────────

    /// <summary>
    /// 밑값 — 적장 나라가 내 나라면 악명 100, 아니면 명성 100(<c>0x00435151</c>).
    /// </summary>
    public static (int Fame, int Infamy) BaseOf(bool sameNation) => sameNation ? (0, 100) : (100, 0);

    /// <summary>
    /// 적 기함을 꺾었거나 달아나게 했을 때 얹는 값 — 플래그 0 이면 명성 +120 · 악명 +180(<c>0x004355F4</c>).
    /// </summary>
    public const int WinFame = 120, WinInfamy = 180;

    /// <summary>내 기함이 퇴각했을 때 얹는 악명 — 플래그 0 이면 +200. 명성은 없다.</summary>
    public const int FleeInfamy = 200;

    /// <summary>명성·악명의 끝(<c>0x004800E0</c> 이 0..99999 로 자른다).</summary>
    public const int MaxRenown = 99_999;

    /// <summary>
    /// 전리품(<c>0x00435666</c>) — 꺾은 배가 하나라도 있을 때만.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   규모 = 도시[ 나라형편[적장.나라].수도 ].+0x08
    ///   금화 = (100*(규모+1) + rand(100)) * 꺾음
    /// </code>
    /// 규모는 살아 있는 도시 레코드 값인데, 우리는 도시가 자라는 셈을 안 들고 있어
    /// <b>EXE 의 처음 규모</b>(<c>도시표 +0x28</c>)로 갈음한다.
    /// </remarks>
    public static int Loot(int scale, int downed, Random rng) =>
        downed <= 0 ? 0 : (100 * (Math.Max(0, scale) + 1) + rng.Next(100)) * downed;

    /// <summary>
    /// 무력 오름(<c>0x00455CA0(0)</c>) — 스물에 하나. 누가 오르는지와 얼마인지를 낸다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   rand(인자==0 ? 20 : 100) != 0 이면 없음
    ///   갈래 = (제독 무력 != 99 ? 1 : 0) + (부관 있고 부관 무력 != 99 ? 2 : 0)
    ///   오름 = rand(2) + 1
    ///   1 「%s의 무력이 %d 상승했다!」 · 2 「부관의 무력이 %d 상승했다!」 · 3 「%s, 부관의 무력이 %d 상승했다!」 (제목 「성장」)
    /// </code>
    /// </remarks>
    /// <returns>(제독이 오르는지, 부관이 오르는지, 오름). 안 오르면 오름이 0.</returns>
    public static (bool Admiral, bool Mate, int Amount) MightUp(int admiralMight, int? mateMight, Random rng)
    {
        if (rng.Next(20) != 0) return (false, false, 0);
        bool admiral = admiralMight != MaxMight;
        bool mate = mateMight is { } m && m != MaxMight;
        if (!admiral && !mate) return (false, false, 0);
        return (admiral, mate, rng.Next(2) + 1);
    }

    /// <summary>능력의 끝.</summary>
    public const int MaxMight = 99;

    // ── 우호 거래 — 0x0048CCF0 ──────────────────────────────────────────────

    /// <summary>
    /// 한 통 값 — <c>k = 운세[4]==0 ? 2 : 1</c>, 물 <c>(rand(3)+k)*10</c> · 식량 <c>(rand(3)+k)*15</c>.
    /// </summary>
    public static (int Water, int Food) PricesOf(int[] fortune, Random rng)
    {
        int k = fortune[4] == 0 ? 2 : 1;
        int water = (rng.Next(3) + k) * 10;
        int food = (rng.Next(3) + k) * 15;
        return (water, food);
    }

    /// <summary>한 번에 살 수 있는 통의 끝.</summary>
    public const int MaxBarrels = 100;

    /// <summary>나눠 주는지 — <c>rand(100) ≤ 매력 + 1</c>.</summary>
    public static bool Shares(int charm, Random rng) => rng.Next(100) <= charm + 1;

    /// <summary>토르데시야스선(1/16 칸 x) — 1493년만 16223, 그 뒤로 15000(<c>0x0048C551</c>).</summary>
    public static int TreatyLine(int year) => year == 1493 ? 16223 : 15000;
}
