using System.Buffers.Binary;
using System.Text;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 발견 이벤트 덩이(조건·본문)를 사람이 읽을 명령으로 푼다.
/// </summary>
/// <remarks>
/// 뜯어 둔 것은 <c>github.com/dkenldlqfur/cds_disev_editor</c> 의
/// <c>Resources/dump_disev.py</c> 를 옮긴 것이다. <b>모르는 바이트는 지어내지 않는다</b> —
/// 짚이지 않는 자리는 「미확인」으로 두고 날바이트를 그대로 보인다. 그래야 고칠 때
/// 원본을 잃지 않는다.
///
/// 명령 꼴은 셋뿐이다.
/// <code>
///   0xFF                        덩이/갈래 끝
///   [창 플래그] 0A [CP949] 00   대사
///   그 밖                       <see cref="Forms"/> 표에 있는 고정 길이 명령
/// </code>
/// </remarks>
public static class DisevScript
{
    /// <summary>명령 한 꼴 — 앞머리 바이트와 길이, 그리고 상대 이동값이 있으면 그 자리.</summary>
    /// <param name="Signature">앞머리 바이트.</param>
    /// <param name="Length">이 명령이 차지하는 바이트 수.</param>
    /// <param name="Kind">사람이 읽을 이름.</param>
    /// <param name="JumpOffset">상대 이동값(u16)이 든 자리. 없으면 −1.</param>
    public readonly record struct Form(byte[] Signature, int Length, string Kind, int JumpOffset = -1);

    /// <summary>푼 명령 하나.</summary>
    /// <param name="Offset">파트 안의 자리.</param>
    /// <param name="Length">바이트 수.</param>
    /// <param name="Kind">갈래 이름. 못 짚으면 「미확인 명령/데이터」.</param>
    /// <param name="Text">사람이 읽을 풀이.</param>
    /// <param name="Hex">날바이트.</param>
    /// <param name="Known">표에 있는 명령인가.</param>
    public readonly record struct Op(int Offset, int Length, string Kind, string Text, string Hex, bool Known);

    private static byte[] Sig(params byte[] bytes) => bytes;

    /// <summary>
    /// 아는 명령 꼴. <b>차례가 중요하다</b> — 앞머리가 겹치면 긴 쪽이 먼저 와야 한다.
    /// </summary>
    public static readonly Form[] Forms =
    [
        // 00 은 본문 하위 명령 묶음이다. 00 02 [u16] 를 먼저 잡지 않으면
        // 뒤의 02 0A 00 을 빈 대사로 잘못 읽는다.
        new(Sig(0x00, 0x02), 4, "AVI 재생"),
        new(Sig(0x00, 0x1F), 4, "EVSTILL 이미지 표시"),
        new(Sig(0x43, 0x2C, 0x08), 15, "교역품 조건 분기", 13),
        new(Sig(0x43, 0x2D, 0x1C), 12, "능력치 비교 분기", 10),
        new(Sig(0x43, 0x2E, 0x1C), 12, "능력치 비교2 분기", 10),
        new(Sig(0x43, 0x2B, 0x1C), 12, "능력치 비교3 분기", 10),
        new(Sig(0x43, 0x2C, 0x1C), 12, "소지금 비교 분기", 10),
        new(Sig(0x43, 0x12, 0x05), 7, "아이템 조건 분기", 5),
        new(Sig(0x43, 0x3A, 0x0B), 7, "발견물 조건 분기", 5),
        new(Sig(0x43, 0x0F, 0x0E), 7, "미확인 0F0E 분기", 5),
        new(Sig(0x43, 0x00, 0x15), 6, "미확인 0015 분기", 4),
        new(Sig(0x43, 0x11), 6, "선택지 분기", 4),
        new(Sig(0x43, 0x45), 4, "이동", 2),
        new(Sig(0x43, 0x47), 4, "예/아니오 응답 분기", 2),
        new(Sig(0x43, 0x4B), 4, "미확인 4B 분기", 2),
        new(Sig(0x43, 0x6D), 4, "STORY0.CDS 외 분기", 2),
        new(Sig(0x43, 0x6E), 4, "STORY1.CDS 외 분기", 2),
        new(Sig(0x17, 0x00), 4, "국가 조건"),
        new(Sig(0x17, 0x08), 4, "도시 조건"),
        new(Sig(0x17, 0x10), 4, "건물 조건"),
        new(Sig(0x17, 0x19), 4, "문화권 조건"),
        new(Sig(0x1B, 0x16), 4, "연도 조건"),
        new(Sig(0x1B, 0x17), 6, "연월 조건"),
        new(Sig(0x1C, 0x16), 4, "연도 상한 조건"),
        new(Sig(0x36, 0x16), 7, "연도 범위 조건"),
        new(Sig(0x1B, 0x0B), 4, "발견 완료 조건"),
        new(Sig(0x5E, 0x0B), 4, "미발견 조건"),
        new(Sig(0x2A, 0x1C), 9, "수치 비교 (이상)"),
        new(Sig(0x2B, 0x1C), 9, "능력치 조건"),
        new(Sig(0x2C, 0x1C), 9, "수치 비교 (미만)"),
        new(Sig(0x2D, 0x1C), 9, "수치 비교 (이하)"),
        new(Sig(0x2E, 0x1A), 11, "무작위 확률 조건"),
        new(Sig(0x37, 0x0D), 4, "인물 런타임 조건"),
        new(Sig(0x37, 0x12), 4, "후원자 런타임 조건"),
        new(Sig(0x19, 0x1C), 9, "능력치 증가"),
        new(Sig(0x1A, 0x1C), 9, "능력치 감소"),
        new(Sig(0x26, 0x1C), 9, "능력치/기한 설정"),
        new(Sig(0x22, 0x1C), 9, "능력치 설정"),
        new(Sig(0x19, 0x14), 6, "금화 증가"),
        new(Sig(0x1A, 0x14), 6, "금화 감소"),
        new(Sig(0x12, 0x05), 4, "아이템 소지 조건"),
        new(Sig(0x0F, 0x05), 4, "아이템 비소지 조건"),
        new(Sig(0x0F, 0x0E), 4, "힌트 상태 활성 조건"),
        new(Sig(0x12, 0x0E), 4, "힌트 상태 미활성 조건"),
        new(Sig(0x00, 0x05), 4, "아이템 획득"),
        new(Sig(0x57, 0x05), 4, "아이템 상실"),
        new(Sig(0x01, 0x15), 4, "이벤트 플래그 설정"),
        new(Sig(0x26, 0x08), 4, "신도시 생성"),
        new(Sig(0x26, 0x10), 7, "특수 건물 생성"),
        new(Sig(0x06, 0x4D), 2, "다음 단계"),
        new(Sig(0x04, 0x4D), 2, "이벤트 완전 종료"),
        new(Sig(0x06, 0xFF), 1, "다음 단계"),
        new(Sig(0x0E, 0x03), 4, "음원 재생"),
        new(Sig(0x5A), 1, "후원자 계약 없음 조건"),
        new(Sig(0x50), 1, "OR 연결(추정)"),
        new(Sig(0x4C), 1, "이벤트 결과 코드 0"),
        new(Sig(0x4D), 1, "이벤트 결과 코드 1"),
        new(Sig(0x4E), 1, "이벤트 결과 코드 2"),
    ];

    /// <summary>능력치 번호 → 이름. 빈 자리는 아직 못 짚은 것이다.</summary>
    public static readonly IReadOnlyDictionary<int, string> StatNames = new Dictionary<int, string>
    {
        [0] = "피로도", [1] = "규율", [2] = "총 선원 수", [3] = "소지금", [4] = "악명",
        [6] = "무력", [7] = "체력", [8] = "생명력", [10] = "동승 인물 체력",
        [11] = "동승 인물 생명력", [17] = "명성", [18] = "운", [20] = "현재 함선 내구도",
        [21] = "지력", [22] = "매력", [23] = "신앙심",
    };

    /// <summary>
    /// 화자 표 — 한국어판인데도 <b>화자 이름만 일본어(CP932) 그대로</b> 남아 있다.
    /// 열쇠는 그 바이트열의 16진 글자다.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> SpeakerNames = new Dictionary<string, string>
    {
        ["8380815B906C"] = "무어인", ["834D838B8368"] = "조합", ["8BB389EF"] = "교회",
        ["959B8AAF"] = "부관", ["8CF088D58F8A"] = "교역소", ["837D836B8347838B88EA90A2"] = "마누엘 1세",
        ["8FE996E5"] = "성문", ["8EB78E96"] = "집사", ["8EF08FEA"] = "술집",
        ["838483528376818183748362834B815B"] = "야코프 푸거", ["96BA"] = "딸",
        ["834A838B838D835888EA90A2"] = "카를로스 1세", ["89C396F592E9"] = "가정제",
        ["838C83498F5C90A2"] = "레오 10세", ["835783878341839393F190A2"] = "조안 2세",
        ["837E8350815B838C818183588373836D8389"] = "미켈레 스피놀라",
        ["8345838B834F81818378834E"] = "우르그 벡", ["89A48F97"] = "왕녀",
        ["83578387834183938181836F838D8358"] = "조안 바로스", ["8F6889AE"] = "여관",
        ["836B83578393834B8181836B834E8345"] = "누진가 누쿠우", ["95BA8E6D"] = "병사",
        ["8EE5906C8CF6"] = "주인공", ["91A291448F8A"] = "조선소", ["837D8380838B815B834E"] = "맘루크",
        ["83438346836A83608346838A"] = "예니체리", ["8AC48E408AAF"] = "검사관",
        ["8343839383668342834982CC8EF197CC"] = "인디오의 족장", ["89A495E682CC94D4906C"] = "왕묘의 파수꾼",
        ["92868D9182CC9856906C"] = "중국의 노인", ["939091AF82CC93AA"] = "도적 두목",
        ["83578383838F82CC9856906C"] = "자와의 노인", ["83438393836882CC9856906C"] = "인도의 노인",
        ["916D97B5"] = "승려", ["92CB8CB483679360"] = "츠카하라 보쿠덴",
        ["837E83508389839383578346838D"] = "미켈란젤로", ["90B39171894082CC94D4906C"] = "쇼소인의 파수꾼",
        ["96EC959A82B982E8"] = "노부세리", ["837A83628365839383678362836791B092B7"] = "호텐토트 족장",
        ["83708368839382CC91B092B7"] = "파돈의 족장",
        ["8341837B838A8357836A82CC91B092B7"] = "아보리지니의 족장",
        ["836A8385815B834D836A834182CC91B092B7"] = "뉴기니아의 족장",
        ["8343836B83438362836782CC91B092B7"] = "이누이트의 족장",
        ["83438393836683428341839382CC8F5592B7"] = "인디언의 족장",
        ["8341837D835D836C835882CC91B092B7"] = "아마조네스의 족장", ["93EC8BC9906C"] = "남극인",
        ["837583898368"] = "블라드", ["8382834E83658358837D93F190A2"] = "목테수마 2세",
        ["8341835E838F838B8370"] = "아타왈파", ["834C8358834C8358"] = "키스키스",
        ["836783708362834E"] = "토팍", ["838C83498369838B8368"] = "레오나르도",
        ["836A8352838983458358"] = "니콜라우스", ["8377838D836A8382"] = "헤로니모",
        ["834183588365834A82CC96F0906C"] = "아스테카 관리", ["8381838A835F82CC90659583"] = "메리다의 아버지",
        ["8368815B836A8383"] = "도냐", ["8367834483898358834A838982CC90C2944E"] = "툴라스칼라의 청년",
        ["8367834483898358834A838982CC90ED8E6D"] = "툴라스칼라의 전사", ["835F839383668342"] = "단디",
        ["8341838B8378815B838B"] = "알베르", ["835783468389838B8368"] = "제라르드",
        ["83798367838D8358"] = "페트로스", ["837D838B834E8358"] = "마르쿠스", ["83578385838A8349"] = "줄리오",
        ["8355834B815B"] = "자가르", ["835A838A836B"] = "세리누",
    };

    private static Encoding? _cp949;
    private static Encoding? _cp932;

    private static Encoding Cp949 => _cp949 ??= GetCodePage(949);
    private static Encoding Cp932 => _cp932 ??= GetCodePage(932);

    private static Encoding GetCodePage(int page)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(page);
    }

    /// <summary>바이트열을 "01 0B 12" 꼴로.</summary>
    public static string Hex(ReadOnlySpan<byte> data)
    {
        var text = new StringBuilder(data.Length * 3);
        foreach (byte value in data)
        {
            if (text.Length > 0) text.Append(' ');
            text.Append(value.ToString("X2"));
        }
        return text.ToString();
    }

    /// <summary>"01 0B 12" 나 "010B12" 를 바이트열로. 못 읽으면 null.</summary>
    public static byte[]? ParseHex(string text)
    {
        var digits = new StringBuilder();
        foreach (char ch in text)
        {
            if (char.IsWhiteSpace(ch) || ch is ',' or '-') continue;
            if (!Uri.IsHexDigit(ch)) return null;
            digits.Append(ch);
        }
        if (digits.Length % 2 != 0) return null;

        var output = new byte[digits.Length / 2];
        for (int i = 0; i < output.Length; i++)
            output[i] = Convert.ToByte(digits.ToString(i * 2, 2), 16);
        return output;
    }

    private static int U16(ReadOnlySpan<byte> data, int at) =>
        at + 2 <= data.Length ? BinaryPrimitives.ReadUInt16LittleEndian(data[at..]) : 0;

    private static long U32(ReadOnlySpan<byte> data, int at) =>
        at + 4 <= data.Length ? BinaryPrimitives.ReadUInt32LittleEndian(data[at..]) : 0;

    /// <summary>덩이 하나를 명령 줄로 푼다.</summary>
    /// <param name="data">파트 알맹이.</param>
    /// <param name="start">덩이 시작.</param>
    /// <param name="end">덩이 끝(다음 덩이 시작).</param>
    /// <param name="stillSlot">
    /// 이 발견물의 DSTILL 그림 번호. <c>01 0B</c> 가 「그림 11 재생」인지
    /// 「발견 처리」인지 가르는 데 쓴다. 모르면 −1.
    /// </param>
    public static List<Op> Parse(byte[] data, int start, int end, int stillSlot = -1)
    {
        var ops = new List<Op>();
        int i = Math.Max(0, start);
        end = Math.Min(end, data.Length);

        while (i < end)
        {
            var span = data.AsSpan();

            if (data[i] == 0xFF)
            {
                ops.Add(new Op(i, 1, "덩이/갈래 끝", "덩이/갈래 끝", "FF", true));
                i++;
                continue;
            }

            // 대사 — 0A 로 바로 열거나, 창 플래그 한 바이트 뒤에 0A 가 온다.
            var form = data[i] == 0x0A ? null : FormAt(data, i, end);
            if (data[i] == 0x0A || (form == null && i + 1 < end && data[i + 1] == 0x0A))
            {
                int? flag = data[i] == 0x0A ? null : data[i];
                int textStart = flag == null ? i + 1 : i + 2;
                int terminator = Array.IndexOf(data, (byte)0, textStart, end - textStart);
                int next = terminator < 0 ? end : terminator + 1;
                if (terminator < 0) terminator = end;

                var (speaker, body) = DecodeDialogue(span[textStart..terminator]);
                string who = speaker == null ? "" : $", 화자 {speaker}";
                string label = flag is null or 0
                    ? $"대사{who}: \"{body}\""
                    : $"대사(창 플래그 {flag}{who}): \"{body}\"";
                ops.Add(new Op(i, next - i, "대사", label, Hex(span[i..next]), true));
                i = next;
                continue;
            }

            // 01/02/0C <u16> 는 그림·동영상 재생이다. 01 0B 만 발견 처리와 겹친다.
            if (i + 3 <= end && data[i] is 0x01 or 0x02 or 0x0C)
            {
                byte opcode = data[i];
                int value = U16(span, i + 1);
                string label;
                int length;
                if (opcode == 0x01 && data[i + 1] == 0x0B)
                {
                    if (stillSlot == 11 && value == 11)
                    {
                        label = "DSTILL 정지 이미지 재생: 슬롯 11 (EXE 매핑으로 판별)";
                        length = 3;
                    }
                    else if (i + 4 <= end)
                    {
                        label = $"발견물 등록/발견 처리: ID {U16(span, i + 2)}";
                        length = 4;
                    }
                    else
                    {
                        label = "01 0B: 정지 이미지 11 / 발견 처리 경계 불명";
                        length = 3;
                    }
                }
                else
                {
                    string media = opcode switch
                    {
                        0x01 => "DSTILL 이미지",
                        0x02 => "AVI",
                        _ => "CG 애니메이션",
                    };
                    label = $"{media} 재생: 슬롯 {value}";
                    length = 3;
                }
                ops.Add(new Op(i, length, label.Split(':')[0], label, Hex(span.Slice(i, length)), true));
                i += length;
                continue;
            }

            form = FormAt(data, i, end);
            if (form is { } known)
            {
                int length = known.Length;
                // 능력치 수식은 상수(1A, 9바이트) 아니면 무작위(20, 13바이트)로 끝난다.
                if (known.Kind is "능력치 증가" or "능력치 감소" or "능력치/기한 설정" or "능력치 설정"
                    && i + 13 <= end && data[i + 4] == 0x20)
                {
                    length = 13;
                }
                var raw = span.Slice(i, Math.Min(length, end - i));
                ops.Add(new Op(i, length, known.Kind, Describe(known, raw, i), Hex(raw), true));
                i += length;
                continue;
            }

            // 못 짚은 바이트는 아는 명령이 다시 나올 때까지 묶는다(최대 16).
            int j = i + 1;
            while (j < end && j - i < 16 && !LikelyStart(data, j, end)) j++;
            var unknown = span[i..j];
            ops.Add(new Op(i, j - i, "미확인 명령/데이터", "미확인 명령/데이터", Hex(unknown), false));
            i = j;
        }

        return ops;
    }

    private static Form? FormAt(byte[] data, int offset, int end)
    {
        foreach (var form in Forms)
        {
            if (offset + form.Length > end) continue;
            if (data.AsSpan(offset).StartsWith(form.Signature)) return form;
        }
        return null;
    }

    private static bool LikelyStart(byte[] data, int offset, int end)
    {
        if (offset >= end) return false;
        if (data[offset] is 0xFF or 0x0A or 0x01 or 0x02 or 0x0C) return true;
        if (offset + 1 < end && data[offset + 1] == 0x0A) return true;
        return FormAt(data, offset, end) != null;
    }

    private static string Describe(Form form, ReadOnlySpan<byte> raw, int offset)
    {
        string kind = form.Kind;
        switch (kind)
        {
            case "AVI 재생":
            case "EVSTILL 이미지 표시":
            case "음원 재생":
                return $"{kind}: 슬롯 {U16(raw, 2)}";
            case "연도 조건":
                return $"연도 >= {U16(raw, 2)}";
            case "연월 조건":
                return $"연월 조건: {U16(raw, 4)}년 {raw[2]}월";
            case "연도 상한 조건":
                return $"연도 <= {U16(raw, 2)}";
            case "연도 범위 조건":
                return $"연도 범위: {U16(raw, 2)}~{U16(raw, 5)}";
            case "무작위 확률 조건":
                if (raw.Length < 11 || raw[6] != 0x1A) return "무작위 확률 조건: 피연산자 형식 미확인";
                return $"무작위 확률 조건: {U32(raw, 7)} / {U32(raw, 2)}";
            case "인물 런타임 조건":
            case "후원자 런타임 조건":
                return $"{kind}: 번호 {U16(raw, 2)}";
            case "발견 완료 조건":
            case "미발견 조건":
                return $"{kind}: 발견물 ID {U16(raw, 2)}";
            case "능력치 조건":
            case "수치 비교 (이상)":
            case "수치 비교 (이하)":
            case "수치 비교 (미만)":
            {
                string op = kind switch
                {
                    "능력치 조건" => ">",
                    "수치 비교 (이상)" => ">=",
                    "수치 비교 (이하)" => "<=",
                    _ => "<",
                };
                return $"조건: {StatName(U16(raw, 2))} {op} {U32(raw, 5)}";
            }
            case "능력치 증가":
            case "능력치 감소":
            case "능력치/기한 설정":
            case "능력치 설정":
            {
                int stat = U16(raw, 2);
                string value;
                if (raw.Length >= 13 && raw[4] == 0x20)
                {
                    long width = U32(raw, 5), from = U32(raw, 9);
                    value = $"무작위 {from}~{from + Math.Max(width - 1, 0)}";
                }
                else
                {
                    value = U32(raw, 5).ToString();
                }
                string symbol = kind == "능력치 증가" ? "+" : kind == "능력치 감소" ? "-" : "=";
                return $"{StatName(stat, "능력치")} {symbol} {value}";
            }
            case "금화 증가":
                return $"금화 +{U32(raw, 2)}";
            case "금화 감소":
                return $"금화 -{U32(raw, 2)}";
            case "아이템 소지 조건":
            case "아이템 비소지 조건":
            case "아이템 획득":
            case "아이템 상실":
                return $"{kind}: 아이템 ID {U16(raw, 2)}";
            case "힌트 상태 활성 조건":
            case "힌트 상태 미활성 조건":
                return $"{kind}: 힌트 상태 ID {U16(raw, 2)}";
            case "신도시 생성":
                return $"신도시 생성: 도시 ID {U16(raw, 2)}";
            case "특수 건물 생성":
                return $"특수 건물 생성: 건물 {U16(raw, 2)}, 도시 {U16(raw, 5)}";
        }

        if (kind.EndsWith("조건") && raw.Length == 4 && raw[0] == 0x17)
            return $"{kind}: 값 {U16(raw, 2)}";

        if (form.JumpOffset >= 0 && form.JumpOffset + 2 <= raw.Length)
        {
            int relative = U16(raw, form.JumpOffset);
            int target = offset + form.Length + relative;
            string extra = kind switch
            {
                "아이템 조건 분기" => $", 아이템 ID {U16(raw, 3)}",
                "교역품 조건 분기" =>
                    $", 원산 도시 {U16(raw, 3)}, 교역품 {U16(raw, 6)}, 수량 {U32(raw, 9)}",
                _ => "",
            };
            return $"{kind}{extra}, 상대 +0x{relative:X} → 파트 +0x{target:X}";
        }
        return kind;
    }

    /// <summary>능력치 이름. 모르는 번호면 <paramref name="fallback"/> 뒤에 번호를 붙인다.</summary>
    private static string StatName(int stat, string fallback = "필드") =>
        StatNames.TryGetValue(stat, out var name) ? name : $"{fallback} {stat}";

    /// <summary>대사 한 줄을 화자와 본문으로 가른다.</summary>
    /// <remarks>
    /// 화자표는 CP932 로 적히고 전각 콜론(<c>81 46</c>)으로 끝난다. 본문은 CP949 다.
    /// 자리표 <c>81 93 82 xx</c> 는 놀이가 그때그때 채우는 것이라 뜻을 적어 둔다.
    /// </remarks>
    public static (string? Speaker, string Body) DecodeDialogue(ReadOnlySpan<byte> data) =>
        DecodeDialogue(data, normalize: true);

    /// <summary>
    /// 같은 것을 <b>손실 없이</b> 푼다 — 고치는 칸에 넣을 글이다.
    /// </summary>
    /// <param name="normalize">
    /// 참이면 전각을 반각으로 고르고 「」 따위를 <c>"</c> 로 바꾼다(읽기 좋으라고).
    /// <b>거짓이면 아무것도 안 고른다</b> — 그래야 다시 구웠을 때 바이트가 같다.
    /// 자리표도 <c>&lt;제독&gt;</c> 처럼 되돌릴 수 있는 꼴로 적는다.
    /// </param>
    public static (string? Speaker, string Body) DecodeDialogue(ReadOnlySpan<byte> data, bool normalize)
    {
        string? speaker = null;
        int bodyStart = 0;

        int look = Math.Min(data.Length, 40);
        for (int i = 0; i + 1 < look; i++)
        {
            if (data[i] != 0x81 || data[i + 1] != 0x46) continue;
            var tag = data[..i];
            string key = Hex(tag).Replace(" ", "");
            speaker = SpeakerNames.TryGetValue(key, out var name) ? name : Cp932.GetString(tag);
            bodyStart = i + 2;
            break;
        }

        var source = data[bodyStart..];
        var cooked = new List<byte>(source.Length);
        for (int i = 0; i < source.Length;)
        {
            if (i + 1 < source.Length && source[i] == 0x81 && source[i + 1] == 0x5E)
            {
                cooked.Add((byte)'/');
                i += 2;
                continue;
            }
            if (i + 4 <= source.Length && source[i] == 0x81 && source[i + 1] == 0x93 && source[i + 2] == 0x82)
            {
                string token = source[i + 3] switch
                {
                    0x93 => normalize ? "제독" : "<제독>",
                    0x76 => normalize ? "협" : "<협>",
                    0x77 => normalize ? "대륙" : "<대륙>",
                    0x6C => normalize ? "남방대륙" : "<남방대륙>",
                    _ => $"<자리표 0x{source[i + 3]:X2}>",
                };
                cooked.AddRange(Cp949.GetBytes(token));
                i += 4;
                continue;
            }
            cooked.Add(source[i]);
            i++;
        }

        string body = Safe(Cp949.GetString(cooked.ToArray()));
        return (speaker, normalize ? Normalize(body).TrimEnd(' ') : body);
    }

    /// <summary>
    /// 눈에 안 보이는 글자를 <c>&lt;01&gt;</c> 꼴로 드러낸다 — 고칠 때 있는 줄 알아야 한다.
    /// </summary>
    private static string Safe(string text)
    {
        var output = new StringBuilder(text.Length);
        foreach (char ch in text)
        {
            output.Append(ch switch
            {
                '\r' => "<CR>",
                '\n' => "<LF>",
                '\t' => "<TAB>",
                _ => ch < 0x20 || ch == 0x7F ? $"<{(int)ch:X2}>" : ch.ToString(),
            });
        }
        return output.ToString();
    }

    /// <summary>전각 문장부호와 전각 영숫자를 보통 글자로 고른다 — 창에서 읽기 좋으라고.</summary>
    private static string Normalize(string text)
    {
        var output = new StringBuilder(text.Length);
        foreach (char ch in text)
        {
            char mapped = ch switch
            {
                '　' => ' ', '。' => '.', '、' => ',', '・' => '·',
                '「' or '」' or '『' or '』' => '"',
                '【' => '[', '】' => ']', '〔' => '(', '〕' => ')',
                _ => ch,
            };
            output.Append(mapped is >= '！' and <= '～' ? (char)(mapped - 0xFEE0) : mapped);
        }
        return output.ToString();
    }
}
