using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Engine.Sea;

/// <summary>
/// 함대가 얼마나 빨리 가는가 — 바람과 돛과 선원이 정한다.
/// </summary>
/// <remarks>
/// 한 줄 답: <b>배속도 = 추진력 x (풍속+1) x 돛효율 / 100</b> 이고, 함대속도는
/// <b>기함 속도와 함대 평균의 가운데</b>다.
/// <code>
/// ; 함대 속도  0x0048BCF0
/// if 뭍(말)이면              return 2
/// 풍향, 풍속 = 0x00424F40()
/// if 풍속 == 0               return 1                 ; 무풍
/// rel = (풍향 - 뱃머리) &amp; 15
///
/// for 배 여덟 칸:
///     효율 = 돛효율표[조합(배+0x68) * 16 + rel]
///     v = 추진력(배+0x38) * (풍속 + 1) * 효율 / 100
///     if 필요선원(배+0x30 + 10) &gt; 선원(배+0x34):
///         v = min(선원 * v / 필요, (v+1)/2)            ; 반토막 아래로
///     합 += v ; 수 += 1
///     기함이면 기함v = v
///
/// return (기함v + 합/수) / 2
/// </code>
/// <b>한 배만 좋아도, 다 좋아도 안 된다</b> — 느린 배 하나가 함대를 반쯤 끌어내린다.
///
/// 그 값을 칸으로 바꾸는 것은 발밑 지형 부류로 갈린다(<c>0x0048D0F0</c> 언저리).
/// <code>
///   부류 == 1 :  이동 = 9 * 속도 / 10        + 해류
///   그 밖     :  이동 = (3 * 속도 + 54) / 10 , 해류 없음
/// </code>
/// 누산기는 <b>64</b> 마다 한 칸을 넘긴다. 바다 칸은 두 부류가 지도에 반반 섞여 있어
/// 실제로는 한 칸 걸러 두 식을 오간다 — <b>좋은 바람의 값어치가 절반쯤 깎여</b> 들어오고,
/// 무풍이어도 느린 식이 5.4 를 내주므로 발이 아주 묶이지는 않는다.
///
/// 자세한 것은 볼트 <c>30.분석-항해 속도(돛·바람·해류)</c>.
/// </remarks>
public static class Sailing
{
    /// <summary>누산기가 한 칸을 넘기는 값. 방위 벡터의 크기와 같다.</summary>
    public const int CellUnits = 64;

    /// <summary>뭍(말)일 때의 붙박이 속도.</summary>
    public const int LandSpeed = 2;

    /// <summary>무풍일 때의 붙박이 속도.</summary>
    public const int CalmSpeed = 1;

    /// <summary>
    /// 함대 속도. 게임의 <c>0x0048BCF0</c> 이다.
    /// </summary>
    /// <param name="player">함대.</param>
    /// <param name="sails">돛 효율표. 못 읽었으면 null — 그때는 돛이 없는 셈 친다.</param>
    /// <param name="windDir">풍향(16방위).</param>
    /// <param name="windSpeed">풍속.</param>
    /// <param name="heading">뱃머리(16방위).</param>
    /// <param name="onLand">뭍에 있는지.</param>
    public static int SpeedOf(Player player, SailTable? sails,
                              int windDir, int windSpeed, int heading, bool onLand)
    {
        if (onLand) return LandSpeed;
        if (windSpeed <= 0 || sails == null) return CalmSpeed;
        if (player.Ships.Count == 0) return CalmSpeed;

        int relative = (windDir - heading) & 0xF;
        int sum = 0, count = 0, flagship = 0;

        for (int i = 0; i < player.Ships.Count; i++)
        {
            var ship = player.Ships[i];
            int v = ship.Speed * (windSpeed + 1) * sails.Efficiency(ship.Sails, relative) / 100;

            // 사람이 모자라면 반토막 아래로 떨어진다.
            int need = ship.Crew;
            int aboard = CrewOn(player, i);
            if (need > aboard) v = Math.Min(aboard * v / Math.Max(1, need), (v + 1) / 2);

            sum += v;
            count++;
            if (i == player.Flagship) flagship = v;
        }

        return (flagship + sum / Math.Max(1, count)) / 2;
    }

    /// <summary>
    /// 그 배에 탄 선원 수.
    /// </summary>
    /// <remarks>
    /// 게임은 배마다 선원을 따로 담는데(<c>배+0x34</c>) 우리는 함대가 통째로 태운다.
    /// 그래서 <b>필요승원에 견주어 나눠 준다</b> — 함대 선원이 최저 승원에 못 미치면
    /// 배마다 고르게 모자란 셈이 된다.
    /// </remarks>
    private static int CrewOn(Player player, int index)
    {
        int need = player.MinCrew;
        if (need <= 0) return player.Crew;
        return (int)((long)player.Crew * player.Ships[index].Crew / need);
    }

    /// <summary>
    /// 속도를 한 틱에 나아갈 칸 수로. 발밑 부류가 1 이면 빠른 식, 아니면 느린 식이다.
    /// </summary>
    /// <remarks>
    /// 게임은 그림 번호가 <c>...80</c> 인 칸만 부류 1 로 보고 나머지 바다 칸은 부류 0 이다.
    /// 그 둘이 지도에 <b>반반 섞여</b> 있어 한 칸 걸러 두 식을 오간다. 우리 쪽에도 부류를
    /// 물어보는 길이 있으므로 그대로 쓴다.
    /// </remarks>
    public static double CellsPerTick(int speed, bool fastTile) =>
        (fastTile ? 9.0 * speed / 10.0 : (3.0 * speed + 54.0) / 10.0) / CellUnits;

    /// <summary>
    /// 해류가 미는 만큼(칸). 빠른 부류의 칸에서만 받는다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   dx += 벡터[해류방위].dx * 세기 / 8 * 경도보정 / 100
    ///   dy += 벡터[해류방위].dy * 세기 / 8
    /// </code>
    /// <b>세기 7이면 칸당 7/8칸씩 옆으로 밀린다</b> — 무시할 크기가 아니다.
    /// 경도 보정은 위도가 높을수록 경도 한 칸이 짧아지는 것을 메우는 값이다.
    /// </remarks>
    public static (double X, double Y) Drift((int X, int Y) vector, int strength, double lonScale) =>
        (vector.X * strength / 8.0 / CellUnits * lonScale,
         vector.Y * strength / 8.0 / CellUnits);

    /// <summary>
    /// 경도 보정 — 위도가 높을수록 경도 한 칸이 짧다.
    /// </summary>
    /// <remarks>
    /// 게임은 <c>65536*cos</c> 표(<c>0x005695F8</c>)를 63도에서 자르고 보간해 쓴다.
    /// 우리는 그냥 <c>1 / cos</c> 을 쓴다 — 게임 보간에는 부호가 뒤집힌 데가 있는데
    /// (더해야 할 자리에서 빼지 않는다) 어긋남이 0.5% 라 굳이 옮기지 않는다.
    /// </remarks>
    public static double LonScale(double lat)
    {
        double capped = Math.Min(Math.Abs(lat), MaxLatForScale);
        return 1.0 / Math.Max(0.25, Math.Cos(capped * Math.PI / 180.0));
    }

    /// <summary>경도 보정을 자르는 위도(게임은 <c>7000</c> = 63도에서 자른다).</summary>
    public const double MaxLatForScale = 63;
}
