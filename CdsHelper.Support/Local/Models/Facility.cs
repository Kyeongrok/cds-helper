using CdsHelper.Support.Local.Helpers;

namespace CdsHelper.Support.Local.Models;

/// <summary>도시 그림에서 눌러 들어갈 수 있는 시설.</summary>
public enum FacilityKind
{
    Harbor,       // 항구
    Shipyard,     // 조선소
    Tavern,       // 술집
    Inn,          // 여관
    Market,       // 시장
    TradingPost,  // 교역소
    Church,       // 교회
    Palace,       // 왕궁
    Library,      // 도서관
    Guild,        // 조합
    Home,         // 자택 — 내 집이다(저택 = 귀족 저택은 딴 건물이다)
    Gate,         // 성문
    Other,        // 그 밖(저택·상관·모스크·사원 …) — 아직 흉내내지 않는다
}

/// <summary>
/// 시설 한 채 — 이름과 명령 창에 늘어놓을 줄, 그리고 들어가 있는 동안 도는 곡.
/// </summary>
/// <remarks>
/// 게임도 같은 모양이다. 시설 객체 표(<c>0x56A038</c>) 하나를 공통 실행(<c>0x4A2530</c>)이
/// 돌리고, 시설마다 다른 것은 가상함수표의 메뉴 칸(<c>+0x34~+0x48</c>)뿐이다. 그래서 여기서도
/// 창을 시설마다 만들지 않고 이 표로 짓는다.
///
/// 메뉴 문구는 CDS_95.EXE 에서 그대로 읽어 온 것이다(볼트 <c>15.분석-시설 화면 엔진</c>).
/// 차례는 게임 화면에서 본 대로 맞췄다.
/// </remarks>
public sealed record Facility(FacilityKind Kind, string Name, string[] Menu, int? BgmTrack = null)
{
    /// <summary>이 줄을 누르면 명령 창이 닫힌다. 시설마다 말이 다르다.</summary>
    public string ExitItem => Menu[^1];

    /// <summary>
    /// 게임에 있는 시설들. 지금 도시 그림에서 자리를 아는 것은 항구·조선소·술집 셋이고,
    /// 나머지는 자리를 찾으면 그대로 뜬다(<see cref="CityBuildings"/>).
    /// </summary>
    public static readonly Facility[] All =
    [
        new(FacilityKind.Harbor, "항구",
            ["출항", "보급", "함대편성", "선원편성", "마을정보", "마을로 돌아간다"]),

        new(FacilityKind.Shipyard, "조선소",
            ["구입", "매각", "수리", "개조", "조선소를 나온다"]),

        // 앞의 세 줄은 마실 것이 아니라 그 자리에 있는 손님이다. 게임은 인물 표
        // (0x4FF978 + i*20)에서 이름을 골라 메뉴 앞에 붙인다 — 와인·브랜디·럼주가 그것이다.
        new(FacilityKind.Tavern, "술집",
            ["와인", "브랜디", "럼주", "포카를 권한다", "부하편성", "술집을 나온다"],
            BgmPlayer.TavernTrack),

        new(FacilityKind.Inn, "여관",
            ["숙박", "매매", "허드렛일", "기능", "부하편성", "여관을 나온다"]),

        new(FacilityKind.Market, "시장",
            ["구입", "매각", "시장을 나온다"]),

        new(FacilityKind.TradingPost, "교역소",
            ["매매", "회화", "교역소를 나온다"]),

        new(FacilityKind.Church, "교회",
            ["수련", "교회를 나온다"]),

        new(FacilityKind.Palace, "왕궁",
            ["감찰관을 매수", "배를 빌린다", "왕궁을 나온다"]),

        new(FacilityKind.Library, "도서관",
            ["열람", "검색", "구입", "매각", "도서관을 나온다"]),

        new(FacilityKind.Guild, "조합",
            ["수련", "조합을 나온다"]),

        // 자택은 내 집이다. "저택"(귀족 저택)은 딴 건물이라 이 줄이 아니다 — 건물 표에서도
        // 자택이 코드 11, 저택이 12 로 갈려 있다.
        new(FacilityKind.Home, "자택",
            ["휴양", "보관", "저금", "후손을 남긴다", "교육", "세대교체",
             "백과사전을 본다", "연표를 본다", "은퇴한다", "기능", "밖으로 나간다"]),

        new(FacilityKind.Gate, "성문",
            ["탐험을 떠난다", "마을로 돌아간다"]),
    ];

    /// <summary>
    /// 어느 시설에서 "기능" 을 골랐을 때 뜨는 줄들. 게임은 여기서 저장·로드까지 한다.
    /// </summary>
    public static readonly string[] SystemMenu = ["저장", "로드", "게임 종료", "게임 재개"];

    /// <summary>
    /// 건물 표(<c>CityBuildingTable</c>)의 종류 이름으로 시설을 찾는다. 아직 흉내내지 않는
    /// 종류(성문·상관·모스크 …)는 나가기 한 줄만 있는 창을 준다.
    /// </summary>
    public static Facility For(string kind)
    {
        foreach (var f in All)
            if (f.Name == kind) return f;
        return new Facility(FacilityKind.Other, kind, [$"{kind}에서 나온다"]);
    }
}
