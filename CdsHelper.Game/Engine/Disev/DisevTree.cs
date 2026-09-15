using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.Engine.Disev;

/// <summary>
/// 덩이 하나를 <b>분기로 가른 줄 나무</b> — <c>발견이벤트.json</c> 의 <c>Chunks</c> 한 칸이다.
/// </summary>
/// <remarks>
/// 분기(<c>43 xx … [u16 상대]</c>)의 바이트 짜임은 늘 이렇다.
/// <code>
///   [분기 명령][안 뛸 때 줄들 = No][뛴 자리부터 = Yes …]
///   상대값 = No 의 바이트 수
/// </code>
/// 그래서 JSON 에는 상대값을 적지 않는다(<see cref="DisevLine.If"/> 는 상대값 두 바이트를 뗀 머리다).
/// 되짤 때 No 길이로 다시 셈하므로 <b>No 안을 늘리고 줄여도 그 분기가 뛰는 자리는 안 어긋난다.</b>
///
/// <b>다만 <c>30 1D [u16 v]</c> 는 못 고쳐 준다.</b> 대본 곳곳에 있는 이 네 바이트는 값이 늘
/// 파트 +4+v 의 명령 머리에 떨어져(파트 25 여덟 곳 모두) 파트 기준 절대 이동으로 보인다.
/// 아직 명령 표에 없어 날바이트 줄 속에 그대로 있으므로, 그 뒤쪽 길이를 바꾸면 어긋난다.
///
/// <b>Yes 가 어디까지인가.</b>
/// <list type="bullet">
///   <item>No 가 끝 명령(결과 코드·게임 오버)으로 멎으면 뒤따르는 줄은 Yes 로만 가므로
///         <b>남은 줄을 다 Yes 에 넣는다.</b></item>
///   <item>No 가 멎지 않으면 뛴 자리에서 두 갈래가 다시 만난다. 그때는 <b>Yes 가 비고</b>
///         남은 줄은 분기 뒤에 이어 적는다 — 두 갈래가 함께 가는 줄이다.</item>
/// </list>
/// 분기 안의 줄이 밖으로 뛰거나 밖에서 분기 안 한가운데로 뛰어 들면 나무로 못 가르므로
/// 그 분기는 날바이트(상대값 그대로) 줄로 둔다. 되짠 바이트가 한 바이트라도 다르면
/// 덩이 통째로 한 줄에 둔다(<see cref="Build"/>).
///
/// 분기가 아닌 명령은 <b>명령 하나에 한 줄</b>이다. 뜻을 아는 것은 칸으로 푼다.
/// <code>
///   음원 재생   0E 03 [u16]                    → { "Sound": 75 }
///   EVSTILL     00 1F [u16]                    → { "EvStill": 3 }
///   발견 처리   01 0B [u16]                    → { "Discover": 4 }
///   아이템 획득 00 05 [u16]                    → { "GetItem": 172 }
///   AVI 재생    00 02 [u16]                    → { "Avi": 1 }
///   특수 조우   00 1E [u16]                    → { "Encounter": 0 }
///   능력치 +/-  19|1A 1C [u16] 1A [u32]        → { "Stat": 1, "StatName": "규율", "Add": 5 } (빼기는 "Sub")
///   대사        [플래그] 0A [화자 81 46] 글 00  → { "Speaker": "부관", "Flag": 11, "Say": "…" }
/// </code>
/// 화자는 <see cref="DisevScript.SpeakerNames"/> 에 있으면 이름, 없으면 <c>SpeakerTag</c> 에 16진이다.
/// <c>Flag</c> 는 흔한 <c>00 0A</c> 면 안 적고, 플래그 바이트 없이 <c>0A</c> 로 바로 열면 <c>null</c> 로 적는다.
/// 글의 전각 사이띄개는 반각으로 적는다. 칸으로 풀어 되짠 것이 원본과 한 바이트라도
/// 다르면 그 명령은 16진 글로 둔다. 나머지 명령도 16진 글 한 줄씩이다.
/// </remarks>
public static class DisevTree
{
    /// <summary>덩이를 줄 나무로 가른다. 되짜서 원본과 같지 않으면 통째 한 줄이다.</summary>
    /// <param name="chunk">덩이 날바이트.</param>
    public static List<DisevLine> Build(byte[] chunk)
    {
        List<DisevLine> whole = [new DisevLine { Hex = DisevScript.Hex(chunk) }];
        if (chunk.Length == 0) return whole;

        var ops = DisevScript.Parse(chunk, 0, chunk.Length);
        var builder = new Builder(chunk, ops);
        var (lines, _) = builder.Sequence(0, chunk.Length);

        return Flatten(lines, out _) is { } back && back.AsSpan().SequenceEqual(chunk) ? lines : whole;
    }

    // 발견이벤트.json 과 같은 꼴 — 들여 쓰고 한글을 \uXXXX 로 안 깬다.
    private static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>줄 나무를 <c>발견이벤트.json</c> 에 적히는 꼴 그대로 글로 — 편집기 JSON 탭이 쓴다.</summary>
    public static string ToJson(IReadOnlyList<DisevLine> lines) => JsonSerializer.Serialize(lines, Pretty);

    /// <summary>줄 나무를 덩이 날바이트로 되짠다. 분기의 상대값은 No 길이로 다시 셈한다.</summary>
    public static byte[]? Flatten(IReadOnlyList<DisevLine> lines, out string error)
    {
        error = "";
        var output = new List<byte>();
        return Append(output, lines, ref error) ? output.ToArray() : null;
    }

    /// <summary>
    /// 칸으로 풀 수 있는 명령이면 그 줄을 짓는다 — 음원 재생 · EVSTILL · 대사. 못 풀면 null.
    /// </summary>
    private static DisevLine? Describe(byte[] raw, string kind)
    {
        switch (kind)
        {
            case "음원 재생" when raw is [0x0E, 0x03, _, _]:
                return new DisevLine { Sound = BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(2)) };

            case "EVSTILL 이미지 표시" when raw is [0x00, 0x1F, _, _]:
                return new DisevLine { EvStill = BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(2)) };

            case "발견물 등록/발견 처리" when raw is [0x01, 0x0B, _, _]:
                return new DisevLine { Discover = BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(2)) };

            case "아이템 획득" when raw is [0x00, 0x05, _, _]:
                return new DisevLine { GetItem = BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(2)) };

            // 19|1A 1C [u16 능력치] 1A [u32 값] — 더하기·빼기 한 핸들러(0x00409352). 무작위(20) 꼴 13바이트는 16진으로 둔다.
            case "능력치 증가" or "능력치 감소" when raw.Length == 9 && raw[1] == 0x1C && raw[4] == 0x1A:
            {
                int stat = BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(2));
                long value = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(5));
                return new DisevLine
                {
                    Stat = stat,
                    StatName = DisevScript.StatNames.TryGetValue(stat, out var name) ? name : null,
                    Add = raw[0] == 0x19 ? value : null,
                    Sub = raw[0] == 0x1A ? value : null,
                };
            }

            case "특수 조우 연출" when raw is [0x00, 0x1E, _, _]:
                return new DisevLine { Encounter = BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(2)) };

            // AVI 는 00 02 [u16] 네 바이트 꼴만 푼다. 00 없이 온 02 [u16] 세 바이트 꼴은 16진으로 둔다.
            case "AVI 재생" when raw is [0x00, 0x02, _, _]:
                return new DisevLine { Avi = BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(2)) };

            case "대사":
            {
                // 편집기 칸과 같은 가름 — [창 플래그] 0A [화자 태그 81 46] 본문 00.
                var (flag, tag) = DisevForm.SplitDialogue(raw);
                int textStart = (flag == null ? 1 : 2) + (tag.Length > 0 ? tag.Length + 2 : 0);
                int textEnd = raw.Length > 0 && raw[^1] == 0x00 ? raw.Length - 1 : raw.Length;
                var (_, body) = DisevScript.DecodeDialogue(
                    raw.AsSpan(textStart, Math.Max(0, textEnd - textStart)), normalize: false);

                // 전각 사이띄개는 JSON 에서 　 으로 깨져 보이므로 반각으로 적는다 —
                // 되짤 때 BuildDialogue 가 반각을 전각으로 올리니 바이트는 같다(원본에 반각이 있으면 16진으로 남는다).
                var line = new DisevLine { Say = body.Replace('　', ' '), Flag = flag };
                if (tag.Length > 0)
                {
                    if (DisevScript.SpeakerNames.TryGetValue(Convert.ToHexString(tag), out var name)) line.Speaker = name;
                    else line.SpeakerTag = DisevScript.Hex(tag);
                }
                return line;
            }

            default:
                return null;
        }
    }

    /// <summary>화자 이름 → 태그 16진(띄어쓰기 없음). 이름이 겹치면 먼저 것.</summary>
    private static readonly Dictionary<string, string> SpeakerTags = BuildSpeakerTags();

    private static Dictionary<string, string> BuildSpeakerTags()
    {
        var tags = new Dictionary<string, string>();
        foreach (var (tag, name) in DisevScript.SpeakerNames) tags.TryAdd(name, tag);
        return tags;
    }

    private static bool Append(List<byte> output, IReadOnlyList<DisevLine> lines, ref string error)
    {
        foreach (var line in lines)
        {
            if (line.Sound is { } sound)
            {
                output.AddRange([0x0E, 0x03, (byte)sound, (byte)(sound >> 8)]);
                continue;
            }

            if (line.EvStill is { } still)
            {
                output.AddRange([0x00, 0x1F, (byte)still, (byte)(still >> 8)]);
                continue;
            }

            if (line.Discover is { } found)
            {
                output.AddRange([0x01, 0x0B, (byte)found, (byte)(found >> 8)]);
                continue;
            }

            if (line.GetItem is { } item)
            {
                output.AddRange([0x00, 0x05, (byte)item, (byte)(item >> 8)]);
                continue;
            }

            if (line.Avi is { } avi)
            {
                output.AddRange([0x00, 0x02, (byte)avi, (byte)(avi >> 8)]);
                continue;
            }

            if (line.Encounter is { } encounter)
            {
                output.AddRange([0x00, 0x1E, (byte)encounter, (byte)(encounter >> 8)]);
                continue;
            }

            if (line.Stat is { } stat)
            {
                if ((line.Add == null) == (line.Sub == null))
                {
                    error = $"능력치 {stat}: Add 와 Sub 가운데 하나만 적어야 합니다";
                    return false;
                }
                long amount = line.Add ?? line.Sub!.Value;
                if (stat is < 0 or > ushort.MaxValue || amount is < 0 or > uint.MaxValue)
                {
                    error = $"능력치 {stat}: 번호는 0~65535, 값은 0~4294967295 라야 합니다";
                    return false;
                }
                output.AddRange([line.Add != null ? (byte)0x19 : (byte)0x1A, 0x1C, (byte)stat, (byte)(stat >> 8), 0x1A,
                                 (byte)amount, (byte)(amount >> 8), (byte)(amount >> 16), (byte)(amount >> 24)]);
                continue;
            }

            if (line.Say is { } say)
            {
                byte[]? tag = line.Speaker != null
                    ? SpeakerTags.TryGetValue(line.Speaker, out var known) ? Convert.FromHexString(known) : null
                    : line.SpeakerTag != null ? DisevScript.ParseHex(line.SpeakerTag) : [];
                if (tag == null)
                {
                    error = $"화자를 모릅니다: {line.Speaker ?? line.SpeakerTag}";
                    return false;
                }
                if (line.Flag is < 0 or > 255)
                {
                    error = $"창 플래그는 0 ~ 255 라야 합니다: {line.Flag}";
                    return false;
                }
                output.AddRange(DisevForm.BuildDialogue(line.Flag, tag, say));
                continue;
            }

            if (line.If == null)
            {
                if (DisevScript.ParseHex(line.Hex ?? "") is not { } bytes)
                {
                    error = $"줄을 못 읽었습니다: {line.Hex}";
                    return false;
                }
                output.AddRange(bytes);
                continue;
            }

            if (DisevScript.ParseHex(line.If) is not { Length: > 0 } head)
            {
                error = $"분기 머리를 못 읽었습니다: {line.If}";
                return false;
            }

            var no = new List<byte>();
            if (!Append(no, line.No ?? [], ref error)) return false;
            if (no.Count > ushort.MaxValue)
            {
                error = $"분기 No 가 너무 깁니다({no.Count}바이트)";
                return false;
            }

            output.AddRange(head);
            output.Add((byte)no.Count);
            output.Add((byte)(no.Count >> 8));
            output.AddRange(no);
            if (!Append(output, line.Yes ?? [], ref error)) return false;
        }
        return true;
    }

    private sealed class Builder
    {
        private readonly byte[] _chunk;
        private readonly List<DisevScript.Op> _ops;
        private readonly Dictionary<int, int> _index = [];
        private readonly int?[] _targets;

        public Builder(byte[] chunk, List<DisevScript.Op> ops)
        {
            _chunk = chunk;
            _ops = ops;
            for (int i = 0; i < ops.Count; i++) _index[ops[i].Offset] = i;
            _targets = ops.Select(op => DisevFlow.TargetOf(chunk, op)).ToArray();
        }

        /// <summary>
        /// <c>[from, to)</c> 를 줄로 짠다. 둘째 값은 이 줄들이 <b>어느 길로 가도 멎는지</b>다.
        /// </summary>
        public (List<DisevLine> Lines, bool Ends) Sequence(int from, int to)
        {
            var lines = new List<DisevLine>();
            if (!_index.TryGetValue(from, out int i)) return (lines, false);

            for (; i < _ops.Count && _ops[i].Offset < to; i++)
            {
                var op = _ops[i];
                int end = op.Offset + op.Length;
                if (_targets[i] is not { } target || target <= end || target > to || !IsBoundary(target) ||
                    !Contained(end, target))
                {
                    lines.Add(LineOf(op));
                    continue;
                }

                var (no, noEnds) = Sequence(end, target);
                // 모르는 조건은 물음에 「거짓이면 뜀」이 붙어 오는데 Yes=거짓 이 같은 말이라 뗀다.
                var (title, jump, fall) = DisevFlow.Question(_chunk, op);
                title = title.Replace(" — 거짓이면 뜀", "");
                string what = title.StartsWith(op.Kind, StringComparison.Ordinal) ? title : $"{op.Kind} · {title}";
                var branch = new DisevLine
                {
                    If = DisevScript.Hex(_chunk.AsSpan(op.Offset, op.Length - 2)),
                    Note = $"{what} — Yes={jump}(뜀), No={fall}(다음 줄)",
                    No = no,
                    Yes = [],
                };
                lines.Add(branch);

                // No 가 멎으면 남은 줄은 Yes 로만 간다 — 통째로 Yes 에 넣는다.
                if (noEnds && target < to && Contained(target, to))
                {
                    var (yes, yesEnds) = Sequence(target, to);
                    branch.Yes = yes;
                    return (lines, yesEnds);
                }

                // 멎지 않으면 뛴 자리에서 다시 만난다 — 뒤는 이어 적는다.
                if (target >= to) return (lines, false);
                i = _index[target] - 1;
            }

            bool ends = lines.Count > 0 && lines[^1].If == null && LastOpBefore(to) is { } last &&
                        DisevFlow.Ends.Contains(last.Kind);
            return (lines, ends);
        }

        /// <summary>명령 하나를 줄로. 칸으로 푼 것이 되짜서 같지 않으면 16진 글로 둔다.</summary>
        private DisevLine LineOf(DisevScript.Op op)
        {
            var raw = _chunk.AsSpan(op.Offset, Math.Min(op.Length, _chunk.Length - op.Offset)).ToArray();
            var hex = new DisevLine { Hex = DisevScript.Hex(raw) };
            if (Describe(raw, op.Kind) is not { } line) return hex;
            return Flatten([line], out _) is { } back && back.AsSpan().SequenceEqual(raw) ? line : hex;
        }

        private bool IsBoundary(int offset) => offset == _chunk.Length || _index.ContainsKey(offset);

        private DisevScript.Op? LastOpBefore(int to)
        {
            for (int i = _ops.Count - 1; i >= 0; i--)
                if (_ops[i].Offset < to) return _ops[i];
            return null;
        }

        /// <summary>
        /// <c>[from, to)</c> 를 나무 가지로 떼어도 되는지 — 안에서 밖으로 뛰지 않고,
        /// 밖에서 안 한가운데로 뛰어 들지 않아야 한다.
        /// </summary>
        private bool Contained(int from, int to)
        {
            for (int i = 0; i < _ops.Count; i++)
            {
                if (_targets[i] is not { } target) continue;
                bool inside = _ops[i].Offset >= from && _ops[i].Offset < to;
                if (inside ? target < from || target > to : target > from && target < to) return false;
            }
            return true;
        }
    }
}

/// <summary>
/// 줄 나무의 한 줄 — 날바이트 한 토막이거나 분기 하나다.
/// </summary>
/// <remarks>
/// JSON 에서 날바이트 줄은 <b>16진 글 하나</b>, 분기는
/// <c>{ "If", "Note", "Yes": [...], "No": [...] }</c>, 칸으로 푼 명령은 <c>{ "Sound" }</c> ·
/// <c>{ "EvStill" }</c> · <c>{ "Speaker", "Flag", "Say" }</c> 이다. <c>Note</c> 는 사람이 읽으라고
/// 적는 것이라 읽을 때 안 본다.
/// </remarks>
[JsonConverter(typeof(DisevLineConverter))]
public sealed class DisevLine
{
    /// <summary>날바이트 줄. 다른 갈래면 null.</summary>
    public string? Hex { get; set; }

    /// <summary>음원 재생(<c>0E 03</c>) 슬롯.</summary>
    public int? Sound { get; set; }

    /// <summary>EVSTILL 이미지 표시(<c>00 1F</c>) 슬롯.</summary>
    public int? EvStill { get; set; }

    /// <summary>발견물 등록/발견 처리(<c>01 0B</c>) 발견물 번호.</summary>
    public int? Discover { get; set; }

    /// <summary>아이템 획득(<c>00 05</c>) 아이템 번호.</summary>
    public int? GetItem { get; set; }

    /// <summary>AVI 재생(<c>00 02</c>) 슬롯.</summary>
    public int? Avi { get; set; }

    /// <summary>특수 조우 연출(<c>00 1E</c>) 번호(<see cref="DisevScript.Encounters"/>).</summary>
    public int? Encounter { get; set; }

    /// <summary>능력치 더하기·빼기(<c>19|1A 1C</c>)의 능력치 번호.</summary>
    public int? Stat { get; set; }

    /// <summary>능력치 이름(<see cref="DisevScript.StatNames"/>). 적을 때만 쓰고 읽을 때는 안 본다.</summary>
    public string? StatName { get; set; }

    /// <summary>더할 값(<c>19</c>).</summary>
    public long? Add { get; set; }

    /// <summary>뺄 값(<c>1A</c>).</summary>
    public long? Sub { get; set; }

    /// <summary>대사 본문 — 무손실로 푼 글(<see cref="DisevForm.BuildDialogue"/> 가 되돌린다).</summary>
    public string? Say { get; set; }

    /// <summary>대사 화자 이름(<see cref="DisevScript.SpeakerNames"/>).</summary>
    public string? Speaker { get; set; }

    /// <summary>이름을 모르는 화자 태그 16진.</summary>
    public string? SpeakerTag { get; set; }

    /// <summary>대사 창 플래그. null 이면 <c>0A</c> 로 바로 연다. JSON 에서 키가 없으면 0 이다.</summary>
    public int? Flag { get; set; }

    /// <summary>분기 머리 — 상대값 두 바이트를 뗀 명령 바이트. 날바이트 줄이면 null.</summary>
    public string? If { get; set; }

    /// <summary>분기 풀이. 적을 때만 쓴다.</summary>
    public string? Note { get; set; }

    /// <summary>조건 명령이 뛴 쪽.</summary>
    public List<DisevLine>? Yes { get; set; }

    /// <summary>뛰지 않고 다음 줄로 간 쪽.</summary>
    public List<DisevLine>? No { get; set; }
}

/// <summary>
/// <see cref="DisevLine"/> 을 글 하나 또는 분기 객체로 적고 읽는다.
/// </summary>
/// <remarks>
/// <c>Chunks</c> 한 칸도 이것으로 읽는다 — 판 2 파일은 덩이가 <b>글 하나</b>였으므로
/// 글이 오면 한 줄짜리 나무로 받는다(<see cref="DisevChunkConverter"/>).
/// </remarks>
public sealed class DisevLineConverter : JsonConverter<DisevLine>
{
    public override DisevLine? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String) return new DisevLine { Hex = reader.GetString() };
        if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("줄은 글이나 분기 객체라야 합니다");

        // Flag 는 흔한 0 을 안 적으므로 키가 없으면 0 이다. 플래그 바이트가 없는 대사만 null 로 적힌다.
        var line = new DisevLine { Flag = 0 };
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            string? name = reader.GetString();
            reader.Read();
            switch (name)
            {
                case "Sound": line.Sound = reader.GetInt32(); break;
                case "EvStill": line.EvStill = reader.GetInt32(); break;
                case "Discover": line.Discover = reader.GetInt32(); break;
                case "GetItem": line.GetItem = reader.GetInt32(); break;
                case "Avi": line.Avi = reader.GetInt32(); break;
                case "Encounter": line.Encounter = reader.GetInt32(); break;
                case "Stat": line.Stat = reader.GetInt32(); break;
                case "Add": line.Add = reader.GetInt64(); break;
                case "Sub": line.Sub = reader.GetInt64(); break;
                case "Say": line.Say = reader.GetString(); break;
                case "Speaker": line.Speaker = reader.GetString(); break;
                case "SpeakerTag": line.SpeakerTag = reader.GetString(); break;
                case "Flag": line.Flag = reader.TokenType == JsonTokenType.Null ? null : reader.GetInt32(); break;
                case "If": line.If = reader.GetString(); break;
                case "Note": line.Note = reader.GetString(); break;
                case "Yes": line.Yes = JsonSerializer.Deserialize<List<DisevLine>>(ref reader, options) ?? []; break;
                case "No": line.No = JsonSerializer.Deserialize<List<DisevLine>>(ref reader, options) ?? []; break;
                default: reader.Skip(); break;
            }
        }
        if (line.If == null && line.Sound == null && line.EvStill == null && line.Discover == null &&
            line.GetItem == null && line.Avi == null && line.Encounter == null && line.Stat == null && line.Say == null)
            throw new JsonException("줄 객체에 If · Sound · EvStill · Discover · GetItem · Avi · Encounter · Stat · Say 가운데 하나가 있어야 합니다");
        if (line.If != null)
        {
            line.Yes ??= [];
            line.No ??= [];
        }
        return line;
    }

    public override void Write(Utf8JsonWriter writer, DisevLine value, JsonSerializerOptions options)
    {
        if (value.Sound is { } sound)
        {
            writer.WriteStartObject();
            writer.WriteNumber("Sound", sound);
            writer.WriteEndObject();
            return;
        }

        if (value.EvStill is { } still)
        {
            writer.WriteStartObject();
            writer.WriteNumber("EvStill", still);
            writer.WriteEndObject();
            return;
        }

        if (value.Discover is { } found)
        {
            writer.WriteStartObject();
            writer.WriteNumber("Discover", found);
            writer.WriteEndObject();
            return;
        }

        if (value.GetItem is { } item)
        {
            writer.WriteStartObject();
            writer.WriteNumber("GetItem", item);
            writer.WriteEndObject();
            return;
        }

        if (value.Avi is { } avi)
        {
            writer.WriteStartObject();
            writer.WriteNumber("Avi", avi);
            writer.WriteEndObject();
            return;
        }

        if (value.Encounter is { } encounter)
        {
            writer.WriteStartObject();
            writer.WriteNumber("Encounter", encounter);
            writer.WriteEndObject();
            return;
        }

        if (value.Stat is { } stat)
        {
            writer.WriteStartObject();
            writer.WriteNumber("Stat", stat);
            if (value.StatName != null) writer.WriteString("StatName", value.StatName);
            if (value.Add is { } add) writer.WriteNumber("Add", add);
            if (value.Sub is { } sub) writer.WriteNumber("Sub", sub);
            writer.WriteEndObject();
            return;
        }

        if (value.Say is { } say)
        {
            writer.WriteStartObject();
            if (value.Speaker != null) writer.WriteString("Speaker", value.Speaker);
            if (value.SpeakerTag != null) writer.WriteString("SpeakerTag", value.SpeakerTag);
            if (value.Flag is not { } flag) writer.WriteNull("Flag");
            else if (flag != 0) writer.WriteNumber("Flag", flag);
            writer.WriteString("Say", say);
            writer.WriteEndObject();
            return;
        }

        if (value.If == null)
        {
            writer.WriteStringValue(value.Hex ?? "");
            return;
        }

        writer.WriteStartObject();
        writer.WriteString("If", value.If);
        if (value.Note != null) writer.WriteString("Note", value.Note);
        writer.WritePropertyName("Yes");
        JsonSerializer.Serialize(writer, value.Yes ?? [], options);
        writer.WritePropertyName("No");
        JsonSerializer.Serialize(writer, value.No ?? [], options);
        writer.WriteEndObject();
    }
}

/// <summary>덩이 한 칸 — 판 3 은 줄 배열, 판 2 는 16진 글 하나다.</summary>
public sealed class DisevChunkConverter : JsonConverter<List<DisevLine>>
{
    public override List<DisevLine>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String) return [new DisevLine { Hex = reader.GetString() }];

        var lines = new List<DisevLine>();
        if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("덩이는 글이나 배열이라야 합니다");
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            lines.Add(JsonSerializer.Deserialize<DisevLine>(ref reader, options)!);
        return lines;
    }

    public override void Write(Utf8JsonWriter writer, List<DisevLine> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var line in value) JsonSerializer.Serialize(writer, line, options);
        writer.WriteEndArray();
    }
}

/// <summary><c>Chunks</c> 목록 — 칸마다 <see cref="DisevChunkConverter"/> 로 읽는다.</summary>
public sealed class DisevChunksConverter : JsonConverter<List<List<DisevLine>>>
{
    private static readonly DisevChunkConverter Chunk = new();

    public override List<List<DisevLine>>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("Chunks 는 배열이라야 합니다");

        var chunks = new List<List<DisevLine>>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            chunks.Add(Chunk.Read(ref reader, typeof(List<DisevLine>), options) ?? []);
        return chunks;
    }

    public override void Write(Utf8JsonWriter writer, List<List<DisevLine>> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var chunk in value) Chunk.Write(writer, chunk, options);
        writer.WriteEndArray();
    }
}
