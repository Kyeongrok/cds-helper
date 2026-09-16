using System.Buffers.Binary;
using System.Text.Json.Nodes;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.Engine.Disev;

/// <summary>
/// 대본 명령 — 해석기(<c>0x004080F0</c>)가 부르는 <b>함수 이름</b>이다. 바이트 꼴과 인자는 <see cref="DisevCalls"/> 표가 짝짓는다.
/// </summary>
/// <remarks>
/// 대본은 작은 가상 기계 코드다. 명령 하나는 게임 함수 한 번 부르기고, 인자는 그 뒤 바이트(수·값 식·글)다.
/// 몇몇 호출은 <b>레지스터</b>에 값을 남긴다.
/// <code>
///   결과 [ebp-0x1C] (밑값 1)   AskYesNo · Minigame · PuzzleMinigame · LandBattle · SeaBattle · AbilityCheck · ClearResult
///   선택값 [ebp-0x44]          AskChoice · AskChoiceWide (고른 자리 + Base)
///   앞 조건 [ebp-0x14]         분기(43)의 조건식마다
/// </code>
/// 분기(<c>43 [조건식] [u16]</c>)는 조건식을 한 번 부르고 <b>거짓이면</b> 뛴다(<c>0x0040BCF9</c>). 조건식 이름도 여기 있다 —
/// 조건 덩이에서는 조건식이 홀로 불린다.
/// </remarks>
public enum DisevCall
{
    /// <summary>뜻을 모르는 바이트 — <c>Args.Bytes</c> 에 그대로 둔다.</summary>
    Raw,

    // ── 표시·연출 ─────────────────────────────────────────────
    Say, AskYesNo, SayBare, AskChoice, AskChoiceWide,
    ShowDStill, ShowEvStill, CloseImage, PlayVideo, PlayCgAnimation, SpecialEncounter,
    PlaySound, StopSound, HideDialog, ShowDialog, DelphiOracle,

    // ── 진행·등록 ─────────────────────────────────────────────
    Discover, SetDiscoveryName, InputDiscoveryName, GiveItem, AddEventItem, MarkEventItem, RemoveItem,
    GiveHint, AddCityRumor, CreateCity, BuildSpecialBuilding, ActivateGoods,

    // ── 상태 변경 ─────────────────────────────────────────────
    AddStat, SubStat, SetStat, SetStat22, HalveTroops, AddGold, SubGold,
    MeetPerson, HireInterpreter, ChangeCityNation, OccupyCity, ReleaseCity, RemoveCity, RemoveFacility,
    MoveEventTarget, DestroyNation,

    // ── 판정·전투·미니게임·시간 ───────────────────────────────
    AbilityCheck, Duel, SetDuelSet, LandBattle, LandBattleCity, SeaBattle, Minigame, PuzzleMinigame, PuzzleMinigame1A,
    Wait, AdvanceDays,

    // ── 끝 ───────────────────────────────────────────────────
    ClearResult, GameOver, EndDone, EndFailed, EndUnhandled, NextStep, NextStepFF, EndEventCompletely, End,

    // ── 조건식 ───────────────────────────────────────────────
    Result, ResultFalse, LastConditionFalse, LastCondition, NoAide, HasAide, ChoiceIs, ChoiceIsNot,
    HintActive, HintInactive, HasItem, LacksItem, Discovered, NotDiscovered, DiscoveryDone, DiscoveryNotDone,
    YearAtLeast, YearBefore, YearAtMost, YearAfter, YearIs, YearBetween, YearOutside, YearMonthIs,
    InNation, InCity, NotInCity, InBuilding, InCulture, PersonUnmet, PersonMet, SponsorActive, SponsorInactive,
    CityNationCheck, Story0, NotStory0, Story1, NotStory1, Unknown0015, NoContract, Or, RandomChance,
    GreaterThan, GreaterOrEqual, LessThan, LessOrEqual, EqualTo, NotEqualTo,
}

/// <summary>
/// 명령 표 — 호출 이름 ↔ 바이트 꼴 ↔ 인자. 읽기(<see cref="Decode"/>)와 짓기(<see cref="Encode"/>)가 이 표 하나를 쓴다.
/// </summary>
/// <remarks>
/// 꼴은 낱말로 적는다.
/// <code>
///   XX     그 바이트 그대로
///   u8 · u16 · u32   수 인자
///   expr   값 식 — 00 [u16] · 14|1A [u32] 상수 · 1C [u16] 상태값 · 20 [u32 폭][u32 시작] 난수 ·
///          08 [u16 도시] 15 [u16 교역품] 적재량 (0x00406D30)
///   say    [화자 태그 81 46] 글 00 — 인자 Speaker(또는 SpeakerTag)·Text
///   str    글 00 — 화자 없음
/// </code>
/// </remarks>
public static class DisevCalls
{
    private sealed record Spec(DisevCall Call, string[] Tokens, string[] Names, DisevCall? Inverse = null);

    private static Spec S(DisevCall call, string pattern, params string[] names) =>
        new(call, pattern.Split(' ', StringSplitOptions.RemoveEmptyEntries), names);

    private static Spec C(DisevCall call, DisevCall? inverse, string pattern, params string[] names) =>
        new(call, pattern.Split(' ', StringSplitOptions.RemoveEmptyEntries), names, inverse);

    /// <summary>본문 명령. 차례가 중요하다 — 앞의 것이 먼저 맞춰진다.</summary>
    private static readonly Spec[] Commands =
    [
        S(DisevCall.Say, "00 0A say"),
        S(DisevCall.AskYesNo, "0B 0A say"),
        S(DisevCall.SayBare, "0A say"),
        S(DisevCall.AskChoice, "10 0A say u8", "Base"),
        S(DisevCall.AskChoiceWide, "18 0A say u8", "Base"),
        S(DisevCall.SetDiscoveryName, "1F 0A str 0B u16", "Name", "Discovery"),
        S(DisevCall.InputDiscoveryName, "1F 0B u16 0A str", "Discovery", "Suffix"),
        S(DisevCall.AddCityRumor, "20 0A str 08 u16", "Text", "City"),

        S(DisevCall.ShowDStill, "00 01 u16", "Id"),
        S(DisevCall.PlayVideo, "00 02 u16", "Id"),
        S(DisevCall.GiveItem, "00 05 u16", "Item"),
        S(DisevCall.PlayCgAnimation, "00 0C u16", "Id"),
        S(DisevCall.SpecialEncounter, "00 1E u16", "Scene"),
        S(DisevCall.ShowEvStill, "00 1F u16", "Id"),
        S(DisevCall.PlaySound, "0E 03 u16", "Id"),
        S(DisevCall.StopSound, "66 03 u16", "Id"),
        S(DisevCall.Minigame, "0E 04 u16", "Game"),
        S(DisevCall.PuzzleMinigame, "0E 14 u32 04 u16", "Discs", "Game"),
        S(DisevCall.PuzzleMinigame1A, "0E 1A u32 04 u16", "Discs", "Game"),

        S(DisevCall.Discover, "01 0B u16", "Discovery"),
        S(DisevCall.ActivateGoods, "01 15 u16", "Goods"),
        S(DisevCall.AddEventItem, "05 05 u16", "Item"),
        S(DisevCall.MarkEventItem, "26 05 u16", "Item"),
        S(DisevCall.RemoveItem, "57 05 u16", "Item"),
        S(DisevCall.GiveHint, "05 0E u16", "Hint"),
        S(DisevCall.CreateCity, "26 08 u16", "City"),
        S(DisevCall.BuildSpecialBuilding, "26 10 u16 08 u16", "Building", "City"),
        S(DisevCall.DestroyNation, "22 00 u16", "Nation"),
        S(DisevCall.MeetPerson, "38 0D u16", "Person"),
        S(DisevCall.HireInterpreter, "3D 0D u16", "Person"),
        S(DisevCall.OccupyCity, "23 08 u16", "City"),
        S(DisevCall.ReleaseCity, "25 08 u16", "City"),
        S(DisevCall.RemoveCity, "22 08 u16", "City"),
        S(DisevCall.RemoveFacility, "22 10 u16 08 u16", "Facility", "City"),
        S(DisevCall.MoveEventTarget, "3C 08 u16", "City"),
        S(DisevCall.ChangeCityNation, "26 1C 1A 00 08 u16", "City"),

        S(DisevCall.AbilityCheck, "35 1C u16", "Stat"),
        S(DisevCall.Duel, "0C 0D u16", "Person"),
        S(DisevCall.SetDuelSet, "26 0F u16", "Set"),
        S(DisevCall.LandBattle, "2F 0D u16", "Person"),
        S(DisevCall.LandBattleCity, "2F 08 u16", "City"),
        S(DisevCall.SeaBattle, "0D 0D u16", "Person"),

        S(DisevCall.AddGold, "19 14 u32", "Amount"),
        S(DisevCall.SubGold, "1A 14 u32", "Amount"),
        S(DisevCall.AddStat, "19 1C u16 expr", "Stat", "Value"),
        S(DisevCall.SubStat, "1A 1C u16 expr", "Stat", "Value"),
        S(DisevCall.SetStat, "26 1C u16 expr", "Stat", "Value"),
        S(DisevCall.SetStat22, "22 1C u16 expr", "Stat", "Value"),
        S(DisevCall.HalveTroops, "34 1C u16 expr", "Stat", "Value"),

        S(DisevCall.Wait, "29 1A u32", "Ticks"),
        S(DisevCall.AdvanceDays, "32 expr", "Days"),

        S(DisevCall.DelphiOracle, "31"),
        S(DisevCall.CloseImage, "33"),
        S(DisevCall.HideDialog, "48"),
        S(DisevCall.ShowDialog, "49"),
        S(DisevCall.ClearResult, "46"),
        S(DisevCall.GameOver, "4A"),
        S(DisevCall.EndDone, "4C"),
        S(DisevCall.EndFailed, "4D"),
        S(DisevCall.EndUnhandled, "4E"),
        S(DisevCall.NextStep, "06 4D"),
        S(DisevCall.EndEventCompletely, "04 4D"),
        S(DisevCall.NextStepFF, "06 FF"),
        S(DisevCall.End, "FF"),
    ];

    /// <summary>
    /// 조건식. <c>Inverse</c> 는 「이 조건이 거짓」을 뜻하는 이름이다 — 분기는 거짓일 때 뛰므로
    /// <c>43 [이 조건]</c> 을 <c>GotoIf Inverse</c> 로 적는다(읽기 좋으라고).
    /// </summary>
    private static readonly Spec[] Conditions =
    [
        C(DisevCall.Result, DisevCall.ResultFalse, "45"),
        C(DisevCall.ResultFalse, DisevCall.Result, "47"),
        C(DisevCall.LastConditionFalse, DisevCall.LastCondition, "4B"),
        C(DisevCall.NoAide, DisevCall.HasAide, "56"),
        C(DisevCall.ChoiceIs, DisevCall.ChoiceIsNot, "11 0A u8", "Value"),
        C(DisevCall.HintInactive, DisevCall.HintActive, "12 0E u16", "Hint"),
        C(DisevCall.HintActive, DisevCall.HintInactive, "0F 0E u16", "Hint"),
        C(DisevCall.LacksItem, DisevCall.HasItem, "12 05 u16", "Item"),
        C(DisevCall.HasItem, DisevCall.LacksItem, "0F 05 u16", "Item"),
        C(DisevCall.NotDiscovered, DisevCall.Discovered, "3A 0B u16", "Discovery"),
        C(DisevCall.Discovered, DisevCall.NotDiscovered, "02 0B u16", "Discovery"),
        C(DisevCall.DiscoveryDone, null, "1B 0B u16", "Discovery"),
        C(DisevCall.DiscoveryNotDone, null, "5E 0B u16", "Discovery"),
        C(DisevCall.YearAtLeast, DisevCall.YearBefore, "1B 16 u16", "Year"),
        C(DisevCall.YearAtMost, DisevCall.YearAfter, "39 16 u16", "Year"),
        C(DisevCall.YearIs, null, "1C 16 u16", "Year"),
        C(DisevCall.YearBetween, DisevCall.YearOutside, "36 16 u16 16 u16", "From", "To"),
        C(DisevCall.YearMonthIs, null, "1B 17 u8 16 u16", "Month", "Year"),
        C(DisevCall.InNation, null, "17 00 u16", "Nation"),
        C(DisevCall.InCity, DisevCall.NotInCity, "17 08 u16", "City"),
        C(DisevCall.InBuilding, null, "17 10 u16", "Building"),
        C(DisevCall.InCulture, null, "17 19 u16", "Culture"),
        C(DisevCall.PersonUnmet, DisevCall.PersonMet, "37 0D u16", "Person"),
        C(DisevCall.SponsorActive, DisevCall.SponsorInactive, "37 12 u16", "Sponsor"),
        C(DisevCall.CityNationCheck, null, "28 00 u16 08 u16", "Nation", "City"),
        C(DisevCall.Story0, DisevCall.NotStory0, "6D"),
        C(DisevCall.Story1, DisevCall.NotStory1, "6E"),
        C(DisevCall.Unknown0015, null, "00 15 u16", "Value"),
        C(DisevCall.NoContract, null, "5A"),
        C(DisevCall.Or, null, "50"),
        // 조건 덩이의 2E 1A 는 비교가 아니라 확률이다 — 비교보다 먼저 맞춘다.
        C(DisevCall.RandomChance, null, "2E 1A u32 1A u32", "Denominator", "Success"),
        C(DisevCall.GreaterThan, DisevCall.LessOrEqual, "2A expr expr", "A", "B"),
        C(DisevCall.GreaterOrEqual, DisevCall.LessThan, "2B expr expr", "A", "B"),
        C(DisevCall.LessThan, DisevCall.GreaterOrEqual, "2C expr expr", "A", "B"),
        C(DisevCall.LessOrEqual, DisevCall.GreaterThan, "2D expr expr", "A", "B"),
        C(DisevCall.EqualTo, DisevCall.NotEqualTo, "2E expr expr", "A", "B"),
    ];

    /// <summary>분기 한 줄을 푼 것.</summary>
    /// <param name="If">참이면 조건이 참일 때(GotoIf), 거짓이면 거짓일 때(GotoUnless) 뛴다.</param>
    public readonly record struct Branch(bool If, DisevCall Condition, JsonObject Args);

    // ── 읽기 ────────────────────────────────────────────────

    /// <summary>명령 한 줄(점프 없는)을 호출로 푼다. 되짜서 같은 바이트가 아니면 null.</summary>
    public static (DisevCall Call, JsonObject Args, string OpCode)? Decode(byte[] raw)
    {
        foreach (var spec in Commands.Concat(Conditions))
            if (Match(spec, raw) is { } args && Build(spec, args) is { } back && back.AsSpan().SequenceEqual(raw))
                return (spec.Call, args, OpCodeOf(spec));
        return null;
    }

    /// <summary>
    /// 분기 <c>43 [조건식] [u16]</c> 의 조건식(점프값 뗀 머리 뒤)을 푼다. 거꾸로 이름이 있으면 <c>If</c> 로 낸다.
    /// </summary>
    public static (Branch Branch, string OpCode)? DecodeBranch(byte[] head)
    {
        if (head.Length < 2 || head[0] != 0x43) return null;
        var condition = head[1..];
        foreach (var spec in Conditions)
        {
            if (Match(spec, condition) is not { } args || Build(spec, args) is not { } back ||
                !back.AsSpan().SequenceEqual(condition)) continue;
            var branch = spec.Inverse is { } inverse
                ? new Branch(true, inverse, args)
                : new Branch(false, spec.Call, args);
            return (branch, "43 " + OpCodeOf(spec));
        }
        return null;
    }

    /// <summary>
    /// 분기 머리(<c>43 [조건식]</c>, 점프값 뗀 것)의 조건식을 <b>원문 이름 그대로</b> 푼다 — 러너가 부르고, 거짓이면 뛴다.
    /// </summary>
    public static (DisevCall Call, JsonObject Args)? ConditionOf(byte[] head)
    {
        if (head.Length < 2 || head[0] != 0x43) return null;
        var condition = head[1..];
        foreach (var spec in Conditions)
            if (Match(spec, condition) is { } args && Build(spec, args) is { } back && back.AsSpan().SequenceEqual(condition))
                return (spec.Call, args);
        return null;
    }

    private static string OpCodeOf(Spec spec) =>
        string.Join(" ", spec.Tokens.TakeWhile(IsFixed));

    private static bool IsFixed(string token) => token.Length == 2 && Uri.IsHexDigit(token[0]) && Uri.IsHexDigit(token[1]);

    /// <summary>꼴에 바이트를 맞춰 인자를 뽑는다. 끝까지 딱 맞지 않으면 null.</summary>
    private static JsonObject? Match(Spec spec, byte[] raw)
    {
        var args = new JsonObject();
        int at = 0, name = 0;
        foreach (string token in spec.Tokens)
        {
            if (IsFixed(token))
            {
                if (at >= raw.Length || raw[at] != Convert.ToByte(token, 16)) return null;
                at++;
                continue;
            }

            switch (token)
            {
                case "u8":
                    if (at + 1 > raw.Length) return null;
                    args[spec.Names[name++]] = raw[at];
                    at += 1;
                    break;
                case "u16":
                    if (at + 2 > raw.Length) return null;
                    AddNumber(args, spec.Names[name++], BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(at)));
                    at += 2;
                    break;
                case "u32":
                    if (at + 4 > raw.Length) return null;
                    args[spec.Names[name++]] = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(at));
                    at += 4;
                    break;
                case "expr":
                    if (ReadExpr(raw, ref at) is not { } expr) return null;
                    args[spec.Names[name++]] = expr;
                    break;
                case "say":
                case "str":
                {
                    int term = Array.IndexOf(raw, (byte)0, at);
                    if (term < 0) return null;
                    var text = raw.AsSpan(at, term - at);
                    if (token == "say")
                    {
                        int tag = FindSpeaker(text);
                        if (tag >= 0)
                        {
                            string key = Convert.ToHexString(text[..tag]);
                            if (DisevScript.SpeakerNames.TryGetValue(key, out var who)) args["Speaker"] = who;
                            else args["SpeakerTag"] = DisevScript.Hex(text[..tag]);
                            text = text[(tag + 2)..];
                        }
                        args["Text"] = BodyOf(text);
                    }
                    else
                    {
                        args[spec.Names[name++]] = BodyOf(text);
                    }
                    at = term + 1;
                    break;
                }
                default:
                    return null;
            }
        }
        return at == raw.Length ? args : null;
    }

    /// <summary>상태값 번호 인자에는 읽기용 이름을 곁들인다(읽을 때 안 본다).</summary>
    private static void AddNumber(JsonObject args, string name, int value)
    {
        args[name] = value;
        if (name == "Stat" && DisevScript.StatNames.TryGetValue(value, out var stat)) args["StatName"] = stat;
    }

    /// <summary>화자 태그 끝(<c>81 46</c>) 자리. 없으면 −1. 앞 40바이트 안에서만 찾는다.</summary>
    private static int FindSpeaker(ReadOnlySpan<byte> text)
    {
        int look = Math.Min(text.Length, 40);
        for (int i = 0; i + 1 < look; i++)
            if (text[i] == 0x81 && text[i + 1] == 0x46) return i;
        return -1;
    }

    /// <summary>글 — 무손실로 풀고 전각 사이띄개만 반각으로(되짤 때 도로 올린다).</summary>
    private static string BodyOf(ReadOnlySpan<byte> text)
    {
        var (_, body) = DisevScript.DecodeDialogue(text, normalize: false);
        // DecodeDialogue 는 앞 40바이트에서 화자를 또 찾는다 — 글에 81 46 이 있으면 되짠 바이트가 달라져 Raw 로 떨어진다.
        return body.Replace('　', ' ');
    }

    /// <summary>값 식 하나(<c>0x00406D30</c>).</summary>
    private static JsonObject? ReadExpr(byte[] raw, ref int at)
    {
        if (at >= raw.Length) return null;
        byte form = raw[at];
        int need = form switch { 0x00 or 0x1C => 3, 0x14 or 0x1A => 5, 0x20 => 9, 0x08 => 6, _ => -1 };
        if (need < 0 || at + need > raw.Length) return null;

        var span = raw.AsSpan(at + 1);
        JsonObject? expr = form switch
        {
            0x1A => new JsonObject { ["Const"] = BinaryPrimitives.ReadUInt32LittleEndian(span) },
            0x14 => new JsonObject { ["Const"] = BinaryPrimitives.ReadUInt32LittleEndian(span), ["Form"] = "14" },
            0x00 => new JsonObject { ["Const"] = BinaryPrimitives.ReadUInt16LittleEndian(span), ["Form"] = "u16" },
            0x1C => StatExpr(BinaryPrimitives.ReadUInt16LittleEndian(span)),
            0x20 => new JsonObject
            {
                ["Random"] = new JsonObject
                {
                    ["From"] = BinaryPrimitives.ReadUInt32LittleEndian(span[4..]),
                    ["Width"] = BinaryPrimitives.ReadUInt32LittleEndian(span),
                },
            },
            0x08 when span[2] == 0x15 => new JsonObject
            {
                ["Cargo"] = new JsonObject
                {
                    ["City"] = BinaryPrimitives.ReadUInt16LittleEndian(span),
                    ["Goods"] = BinaryPrimitives.ReadUInt16LittleEndian(span[3..]),
                },
            },
            _ => null,
        };
        if (expr == null) return null;
        at += need;
        return expr;
    }

    private static JsonObject StatExpr(int stat)
    {
        var expr = new JsonObject { ["Stat"] = stat };
        if (DisevScript.StatNames.TryGetValue(stat, out var name)) expr["Name"] = name;
        // 성격 칸은 값이 0·1·2 하나라 무슨 낱말인지 곁들인다.
        if (DisevScript.TraitWordsOf(stat) is { } words) expr["Values"] = $"0 {words.Low} · 1 보통 · 2 {words.High}";
        return expr;
    }

    // ── 짓기 ────────────────────────────────────────────────

    /// <summary>호출 한 줄을 바이트로. 모르는 호출이거나 인자가 모자라면 null 이고 까닭은 <paramref name="error"/>.</summary>
    public static byte[]? Encode(DisevCall call, JsonObject? args, out string error)
    {
        error = "";
        args ??= [];
        if (call == DisevCall.Raw)
        {
            if (args["Bytes"]?.GetValue<string>() is { } hex && DisevScript.ParseHex(hex) is { } bytes) return bytes;
            error = "Raw 에는 Args.Bytes(16진)가 있어야 합니다";
            return null;
        }

        var spec = Commands.Concat(Conditions).FirstOrDefault(s => s.Call == call);
        if (spec == null)
        {
            error = $"짓는 꼴을 모르는 호출입니다: {call}";
            return null;
        }
        var built = Build(spec, args, out error);
        return built;
    }

    /// <summary>분기 머리(<c>43 [조건식]</c>, 점프값 없음)를 짓는다.</summary>
    public static byte[]? EncodeBranch(bool jumpIf, DisevCall condition, JsonObject? args, out string error)
    {
        error = "";
        args ??= [];
        var spec = jumpIf
            ? Conditions.FirstOrDefault(s => s.Inverse == condition)
            : Conditions.FirstOrDefault(s => s.Call == condition);
        if (spec == null)
        {
            error = jumpIf ? $"GotoIf 로 쓸 수 없는 조건식입니다: {condition}" : $"조건식이 아닙니다: {condition}";
            return null;
        }
        if (Build(spec, args, out error) is not { } body) return null;
        return [0x43, .. body];
    }

    private static byte[]? Build(Spec spec, JsonObject args) => Build(spec, args, out _);

    private static byte[]? Build(Spec spec, JsonObject args, out string error)
    {
        error = "";
        var output = new List<byte>();
        int name = 0;

        long Number(string key)
        {
            var node = args[key];
            if (node == null) throw new KeyNotFoundException($"{spec.Call}: 인자 {key} 가 없습니다");
            return AsLong(node);
        }

        try
        {
            foreach (string token in spec.Tokens)
            {
                if (IsFixed(token))
                {
                    output.Add(Convert.ToByte(token, 16));
                    continue;
                }

                switch (token)
                {
                    case "u8":
                        output.Add((byte)Number(spec.Names[name++]));
                        break;
                    case "u16":
                    {
                        long v = Number(spec.Names[name++]);
                        output.Add((byte)v);
                        output.Add((byte)(v >> 8));
                        break;
                    }
                    case "u32":
                        AddU32(output, Number(spec.Names[name++]));
                        break;
                    case "expr":
                    {
                        string key = spec.Names[name++];
                        if (args[key] is not JsonObject expr || WriteExpr(output, expr) is { Length: > 0 } why)
                        {
                            error = $"{spec.Call}: 값 식 {key} 를 못 읽었습니다";
                            return null;
                        }
                        break;
                    }
                    case "say":
                    {
                        byte[] tag = [];
                        if (args["Speaker"]?.GetValue<string>() is { } who)
                        {
                            if (DisevTree.SpeakerTagOf(who) is not { } known)
                            {
                                error = $"화자를 모릅니다: {who}";
                                return null;
                            }
                            tag = known;
                        }
                        else if (args["SpeakerTag"]?.GetValue<string>() is { } hex)
                        {
                            tag = DisevScript.ParseHex(hex) ?? [];
                        }
                        string text = args["Text"]?.GetValue<string>() ?? "";
                        output.AddRange(DisevForm.BuildDialogue(null, tag, text).AsSpan(1).ToArray());
                        break;
                    }
                    case "str":
                    {
                        string text = args[spec.Names[name++]]?.GetValue<string>() ?? "";
                        output.AddRange(DisevForm.BuildDialogue(null, [], text).AsSpan(1).ToArray());
                        break;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or FormatException)
        {
            error = ex.Message;
            return null;
        }
        return output.ToArray();
    }

    /// <summary>수 칸 — 지은 JsonValue(uint 따위)와 읽은 JsonElement 를 가리지 않고 받는다.</summary>
    private static long AsLong(JsonNode node) => long.Parse(node.ToJsonString());

    private static void AddU32(List<byte> output, long v)
    {
        output.Add((byte)v);
        output.Add((byte)(v >> 8));
        output.Add((byte)(v >> 16));
        output.Add((byte)(v >> 24));
    }

    /// <summary>값 식을 짓는다. 못 지으면 까닭 글, 지었으면 빈 글.</summary>
    private static string WriteExpr(List<byte> output, JsonObject expr)
    {
        if (expr["Const"] is { } constant)
        {
            long v = AsLong(constant);
            switch (expr["Form"]?.GetValue<string>())
            {
                case "u16":
                    output.AddRange([0x00, (byte)v, (byte)(v >> 8)]);
                    return "";
                case "14":
                    output.Add(0x14);
                    AddU32(output, v);
                    return "";
                default:
                    output.Add(0x1A);
                    AddU32(output, v);
                    return "";
            }
        }
        if (expr["Stat"] is { } stat)
        {
            long v = AsLong(stat);
            output.AddRange([0x1C, (byte)v, (byte)(v >> 8)]);
            return "";
        }
        if (expr["Random"] is JsonObject random)
        {
            output.Add(0x20);
            AddU32(output, random["Width"] is { } width ? AsLong(width) : 0);
            AddU32(output, random["From"] is { } from ? AsLong(from) : 0);
            return "";
        }
        if (expr["Cargo"] is JsonObject cargo)
        {
            long city = cargo["City"] is { } c ? AsLong(c) : 0, goods = cargo["Goods"] is { } g ? AsLong(g) : 0;
            output.AddRange([0x08, (byte)city, (byte)(city >> 8), 0x15, (byte)goods, (byte)(goods >> 8)]);
            return "";
        }
        return "모르는 값 식";
    }
}
