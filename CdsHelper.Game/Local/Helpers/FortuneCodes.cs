using CdsHelper.Support.Local.Helpers;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// <b>운명 코드</b> 한 공간 — 주인공의 운명 자리와 여급의 궁합 코드가 여기서 만난다.
/// </summary>
/// <remarks>
/// <b>0~15 가 아니다.</b> 코드는 <b>0~31 한 덩어리</b>고, 아래 절반이 젊은 제독 몫,
/// 위 절반이 그 <b>중년</b> 몫이다 — 초상화가 <c>얼굴 + 16</c> 인 것과 똑같은 짜임이다.
///
/// <code>
///   주인공 코드 = 운명 자리 + (서른여섯 살 이상이면 걸음)      0x00465E90 어름
///   여급   코드 = 표에 적힌 그대로                              표 0x00517AF8 의 +? 칸
///   궁합    = 두 코드가 같거나 하나 차이                        BarmaidTable.DestinedGap
/// </code>
///
/// 여급 127명이 실제로 <b>0~30 을 다 쓴다</b>(아래 83명 · 위 44명). 리스본의 알다가
/// 1480년부터 코드 19 로 서 있는데, 그것은 「운명 자리 3 인 <b>중년</b> 제독」의 짝이라는
/// 뜻이다.
///
/// <b>그래서 얼굴을 하나 더한다고 자리를 0~16 으로 늘릴 수 없다</b> — 16 은 이미
/// 「운명 자리 0 의 중년」이다. 늘리려면 <b>걸음 자체를 키워야</b> 하고, 그러면 여급 코드도
/// 새 걸음으로 옮겨야 한다. <see cref="Translate"/> 가 그 옮김을 맡는다.
/// </remarks>
public static class FortuneCodes
{
    /// <summary>적어 둘 파일 이름.</summary>
    private const string CacheName = "운명자리";

    /// <summary>게임이 쓰는 자리 수. 젊은 얼굴 열여섯과 같다.</summary>
    public const int GameSlots = 16;

    /// <summary>자리를 늘릴 수 있는 데까지 — 코드가 한 바이트에 들어가야 한다.</summary>
    public const int MaxSlots = 64;

    /// <summary>JSON 으로 적어 두는 알맹이.</summary>
    internal sealed record Snapshot(int Slots);

    private static int? _slots;

    /// <summary>자리 수가 바뀌었을 때 알린다.</summary>
    public static event Action? Changed;

    /// <summary>
    /// 운명 자리 수. 곧 젊은 코드와 중년 코드 사이의 <b>걸음</b>이다.
    /// </summary>
    public static int Slots
    {
        get => _slots ??= Load();
        set
        {
            int want = Math.Clamp(value, GameSlots, MaxSlots);
            if (want == Slots) return;

            _slots = want;
            TableCache.Write(CacheName, new TableCache.Cached<Snapshot>(
                $"{want}자리", new Snapshot(want), "사람이 고친 것"));
            Changed?.Invoke();
        }
    }

    /// <summary>게임 그대로인지 — 늘리지 않았으면 참.</summary>
    public static bool Stock => Slots == GameSlots;

    /// <summary>그 자리와 나이가 내는 코드.</summary>
    public static int CodeOf(int slot, bool aged) =>
        Math.Clamp(slot, 0, Slots - 1) + (aged ? Slots : 0);

    /// <summary>
    /// 원본 표에 적힌 코드(걸음 16)를 <b>지금 걸음</b>으로 옮긴다.
    /// </summary>
    /// <remarks>
    /// 자리를 늘려도 여급의 뜻이 안 바뀌게 하는 자리다. 코드 19 는 「자리 3 의 중년」이니
    /// 걸음이 스물이 되면 23 이 된다 — 사람은 그대로고 코드만 새 공간으로 옮겨 앉는다.
    /// </remarks>
    public static int Translate(int stock)
    {
        if (Stock) return stock;

        bool aged = stock >= GameSlots;
        int slot = aged ? stock - GameSlots : stock;
        return CodeOf(slot, aged);
    }

    /// <summary>
    /// 그 자리를 맡은 여급이 하나도 없으면 참 — 늘린 자리는 처음엔 다 비어 있다.
    /// </summary>
    /// <remarks>
    /// 자리를 늘리기만 하고 여급 코드를 안 손대면 <b>그 자리를 받은 제독은 아무와도
    /// 궁합이 안 맞는다</b>. 늘릴 때 이 말을 화면에 같이 낸다.
    /// </remarks>
    public static bool Empty(int slot, BarmaidTable? table)
    {
        if (table == null) return false;

        foreach (var her in table.Barmaids)
            if (her.Fortune % Slots == slot) return false;
        return true;
    }

    private static int Load()
    {
        var saved = TableCache.Read<Snapshot>(CacheName);
        int got = saved?.Data.Slots ?? GameSlots;
        return Math.Clamp(got, GameSlots, MaxSlots);
    }
}
