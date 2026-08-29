namespace CdsHelper.Support.Local.Models;

/// <summary>
/// 조합에서 배울 수 있는 기술.
/// </summary>
/// <remarks>
/// 무엇을 가르치는지는 건물마다 다르다 — 건물 표(<c>CityBuildingTable</c>, CdsHelper.Game)의
/// 비트마스크에서 온다. 조합·교회·학자 저택이 가르친다.
/// </remarks>
public static class Skill
{
    /// <summary>배울 수 있는 가장 높은 자리.</summary>
    public const int MaxLevel = 3;

    /// <summary>
    /// 기술 열셋. 게임 표(<c>0x00560A10</c>) 차례 그대로다.
    /// </summary>
    public static readonly string[] Names =
    [
        "항해술", "운용술", "검술", "포술", "사격술", "의학", "웅변",
        "측량", "역사학", "회계", "조선기술", "신학", "과학",
    ];

    /// <summary>기술 번호. 표 차례와 같다.</summary>
    public const int Sailing = 0, Handling = 1, Sword = 2, Gunnery = 3, Shooting = 4,
                     Medicine = 5, Rhetoric = 6, Survey = 7, History = 8, Accounting = 9,
                     Shipwright = 10, Theology = 11, Science = 12;

    /// <summary>
    /// 언어 열넷. 게임 표(<c>0x00560A48</c>) 차례 그대로다.
    /// </summary>
    /// <remarks>
    /// 새 놀이 화면에서는 앞의 <b>여섯</b>만 찍을 수 있고 나머지는 흐리다 — 페르시아어부터는
    /// 놀이 안에서 배워야 한다.
    /// </remarks>
    public static readonly string[] Languages =
    [
        "스페인어", "포르투갈어", "로망스어", "게르만어", "슬라브·그리스어", "아랍어",
        "페르시아어", "중국어", "힌두어", "위굴어",
        "아프리카토착어", "중남미토착어", "동남아시아토착어", "동아시아토착어",
    ];

    /// <summary>새 놀이에서 찍을 수 있는 언어 수.</summary>
    public const int LanguagesAtStart = 6;

    /// <summary>
    /// 기술·언어 자리를 다 더해 넘을 수 없는 값 — <b>지력 곱하기 3 나누기 5</b>.
    /// </summary>
    /// <remarks>
    /// <code>
    /// 45dfe4  열셋을 다 더한다
    /// 45dff6  eax = 지력
    /// 45e002  eax = eax * 3 / 5
    /// 45e008  if (eax &lt; 합 + 1) "더 이상 지식을 습득할 수 없습니다"   0x005718E0
    /// </code>
    /// 언어에도 같은 꼴의 검사가 따로 있다("더 이상 언어를 습득할 수 없습니다").
    /// </remarks>
    public static int CapFor(int mind) => mind * 3 / 5;

    /// <summary>
    /// 국적이 처음부터 주는 언어 — (언어 번호, 자리).
    /// </summary>
    /// <remarks>
    /// <b>모국어 3 에 이웃 2</b> 다. 직업 넷을 한 번씩 만들어 기능 창을 맞대어 보고
    /// 얻었다 — 포르투갈 국적으로 만든 넷이 모두 포르투갈어 3 · 스페인어 2 로 같았고,
    /// 에스파니아로 만든 판은 그 둘이 뒤집혔다.
    ///
    /// 예전에는 여기에 로망스어 3 도 얹었는데 그것은 <b>국적이 아니라 직업</b> 몫이었다
    /// (탐험가만 받는다 — <see cref="Job.Tongues"/>).
    /// </remarks>
    public static (int Language, int Level)[] TongueOf(int nation) => nation == Spain
        ? [(Spanish, 3), (Portuguese, 2)]
        : [(Portuguese, 3), (Spanish, 2)];

    /// <summary>언어 번호. 표 차례와 같다.</summary>
    public const int Spanish = 0, Portuguese = 1, Romance = 2, German = 3,
                     SlavGreek = 4, Arabic = 5;

    /// <summary>국적 번호 — 새 놀이에서 고르는 둘.</summary>
    public const int Portugal = 0, Spain = 1;

    /// <summary>한 자리 올리는 데 드는 값(닢). 자리와 상관없이 같다.</summary>
    public const int Price = 120;

    /// <summary>
    /// 그 자리까지 올리는 데 걸리는 달수. 0→1 은 석 달, 2 는 여섯 달, 3 은 열두 달이다.
    /// </summary>
    public static int MonthsFor(int nextLevel) => nextLevel switch
    {
        1 => 3,
        2 => 6,
        3 => 12,
        _ => 0,
    };
}
