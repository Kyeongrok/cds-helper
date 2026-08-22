
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.Engine.Models;

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
        // "발표" 는 발표할 발견물이 있을 때만 뜬다 — 게임도 그 줄의 <b>보임</b> 쪽을
        // 조건(0x00476DE0)으로 켠다(CityPicDialog.BuildMenu 가 없으면 뺀다).
        new(FacilityKind.Harbor, "항구",
            ["출항", "보급", "함대편성", "선원편성", "마을정보", Announce, "마을로 돌아간다"]),

        new(FacilityKind.Shipyard, "조선소",
            ["구입", "매각", "수리", "개조", "조선소를 나온다"]),

        // 앞의 세 줄은 그 도시가 파는 술이다. 게임은 술 표(0x4FF978, 55행 x 20바이트 —
        // 이름·도수·도시·값·별칭)에서 도시 번호가 맞는 것만 골라 메뉴 앞에 붙인다
        // (0x0042E710 이 센다). 도시마다 파는 술이 다르므로 이 세 줄도 도시마다 달라야 하는데,
        // 우리는 아직 표를 안 읽고 세비야 것을 박아 두었다.
        new(FacilityKind.Tavern, "술집",
            ["와인", "브랜디", "럼주", "포카를 권한다", "부하편성", "술집을 나온다"],
            BgmPlayer.TavernTrack),

        new(FacilityKind.Inn, "여관",
            ["숙박", "허드렛일", "부하편성", "기능", "여관을 나온다"]),

        new(FacilityKind.Market, "시장",
            ["구입", "매각", "시장을 나온다"]),

        new(FacilityKind.TradingPost, "교역소",
            ["매매", "회화", "교역소를 나온다"]),

        new(FacilityKind.Church, "교회",
            ["수련", "교회를 나온다"],
            BgmPlayer.ChurchTrack),

        // "설득" 은 여기 적지 않는다 — 그 건물에 후원자가 앉아 있을 때만 붙는 줄이라
        // 도시마다 다르다(CityPicDialog.BuildMenu 가 맨 앞에 끼워 넣는다).
        //
        // "감찰관을 매수"(조건 0x44EA30)와 "배를 빌린다"(조건 0x44EA80)도 조건이 맞을 때만
        // 나온다. 게임은 조건이 어긋난 줄을 흐리게 두지 않고 아예 감춘다 — 두카레 궁전
        // 화면에도 설득과 나가기 두 줄만 떴다. 조건은 이렇다:
        //   감찰관을 매수 — 계약한 뒤 한 자리에서 발견물이 둘 이상 나왔을 때
        //                   (코드도 개수를 세어 cmp eax,1 / jle 로 둘 미만이면 끈다)
        //   배를 빌린다   — 계약할 때 스폰서가 배를 빌리겠냐 물었는데 아니라고 답했을 때
        //                   (그 답이 플래그 한 비트로 남아 test byte [eax+0x1C],1 로 본다)
        new(FacilityKind.Palace, "왕궁",
            ["왕궁을 나온다"]),

        // 도서관은 열람만 둔다 — 게임에는 검색·구입·매각도 있지만 우리는 안 흉내낸다.
        new(FacilityKind.Library, "도서관",
            ["열람", "도서관을 나온다"]),

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
    /// 항구에서 "함대편성" 을 골랐을 때 뜨는 줄들. 게임처럼 제목 없이 줄만 쌓인다.
    /// </summary>
    public static readonly string[] FleetMenu =
        ["기함 변경", "선박 편입", "선박 삭제", "선박 파기", FleetExit];

    /// <summary>함대편성 창을 접고 항구 창으로 돌아가는 줄.</summary>
    public const string FleetExit = "편성 종료";

    /// <summary>
    /// 선원편성 창의 줄. 게임 것 그대로다(<c>0x00545250</c> 벌 — 항구 <c>0x004774E0</c>).
    /// </summary>
    public static readonly string[] CrewMenu = ["선원모집", "선원해고", CrewExit];

    /// <summary>선원편성 창을 접고 항구 창으로 돌아가는 줄.</summary>
    public const string CrewExit = "돌아간다";

    /// <summary>항구에서 발견물을 알리는 줄. 알릴 것이 없으면 아예 안 뜬다.</summary>
    public const string Announce = "발표";

    /// <summary>
    /// 자택 휴양 창의 줄. 게임 것 그대로다(<c>0x00539778</c> 벌 — 휴양 <c>0x00460660</c>).
    /// </summary>
    public static readonly string[] RestMenu = ["한 달 휴양", "장기 휴양", RestExit];

    /// <summary>휴양 창을 접고 자택 창으로 돌아가는 줄.</summary>
    public const string RestExit = "취소";

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
