namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 인물 표 — 281명. 술집·여관에 앉는 사람과 부하 후보가 여기서 온다.
/// </summary>
/// <remarks>
/// <b>적어 둔 <c>인물표.json</c> 이 원본이다.</b> 인물은 EXE 의 정적 표가 아니라 세이브에만
/// 있어서 예전에는 <c>SAVEDATA.CDS</c> 를 그때그때 뜯어 읽었는데, 그러면 놀이가 남의
/// 세이브에 매인다 — 세이브가 없으면 술집이 텅 비고, 고치려면 남의 세이브를 건드려야 했다.
/// 그래서 한 번만 구워 두고 그 뒤로는 <b>세이브를 보지 않는다</b>.
///
/// <code>
///   %APPDATA%\CdsHelper\exe-tables\인물표.json   사람이 고친 것   ← 있으면 이것
///   실행 파일 옆 인물표.json                      같이 깔린 본     ← 놀이는 보통 이 길
///   세이브(SAVEDATA.CDS)                          씨앗            ← 둘 다 없을 때만
///   아무것도 없다                                 빈 표 — 술집에는 지나가는 사람만 선다
/// </code>
///
/// <b>본이 같이 깔리므로 세이브가 아예 없어도 놀이가 돈다.</b> 본은 <c>CdsHelper</c> 프로젝트에
/// 든 <c>인물표.json</c> 이고 놀이 exe 옆으로 함께 복사된다(<c>cities.json</c> 과 같은 결이다).
///
/// 도시 표(<see cref="CityTable"/>)와 규칙이 반대다. 그쪽은 원본(앱 DB)이 늘 살아 있어
/// 원본을 먼저 보지만, 여기서는 <b>적어 둔 것이 곧 원본</b>이라 다시 굽는 것은 사람이
/// 시킬 때뿐이다(<see cref="Bake"/>).
///
/// 칸의 뜻은 볼트 <c>72.분석-인물 이동(역사 항해사와 매달 굴림)</c> 에 있다.
/// </remarks>
public sealed class PersonTable
{
    /// <summary>적어 둘 파일 이름(<c>%APPDATA%\CdsHelper\exe-tables\인물표.json</c>).</summary>
    private const string CacheName = "인물표";

    /// <summary>알맹이 모양 판. 칸을 더하면 올린다 — 옛 파일은 버리고 다시 굽는다.</summary>
    private const int Shape = 1;

    /// <summary>인물 칸 수. EXE 가 못박은 값이다(<c>0x004319D0</c> 의 <c>cmp eax, 0x119</c>).</summary>
    public const int Count = 281;

    /// <summary>기술 열셋 · 언어 열넷 · 능력 여섯.</summary>
    public const int SkillCount = 13, LangCount = 14, StatCount = 6;

    /// <summary>번호 이 앞은 역사 항해사다 — <c>HISTCHR.CDS</c> 각본이 옮긴다.</summary>
    public const int VoyagerCount = 14;

    /// <summary>번호 이 뒤는 이벤트 인물·괴물·누적 캐릭터라 달마다 굴리지 않는다.</summary>
    public const int MovingEnd = 201;

    /// <summary>도시 칸 수. 이 밖의 번호는 "없음" 이다(<c>0x00429950</c> 의 <c>cmp eax, 0xE2</c>).</summary>
    public const int CityCount = 226;

    /// <summary>건물 번호.</summary>
    public const int Tavern = 4, Inn = 5;

    /// <summary>고용 상태. 2 라야 부하로 삼을 수 있다.</summary>
    public const int TalkOnly = 1, Hireable = 2, Hired = 3;

    /// <summary>
    /// 인물 한 명. 고치는 창이 이것을 그대로 묶으므로 <b>갈아 끼울 수 있는 칸</b>이다.
    /// </summary>
    public sealed class Row
    {
        /// <summary>인물 번호. 그림을 고르는 씨앗으로도 쓴다.</summary>
        public int Id { get; set; }

        public string First { get; set; } = "";
        public string Last { get; set; } = "";

        /// <summary>등급 0~3. 2 이상이면 술집, 아니면 여관에 앉는다.</summary>
        public int Grade { get; set; }

        public int Fame { get; set; }

        /// <summary>소재 도시. <b>-1 이면 어느 도시에도 없다</b>(이동 중이거나 미배치).</summary>
        public int City { get; set; } = -1;

        /// <summary>건물. 4 주점 · 5 여관 · -1 없음.</summary>
        public int Building { get; set; } = -1;

        public int Age { get; set; }

        /// <summary>고용 상태. 1 대화만 · 2 고용가능 · 3 고용중.</summary>
        public int Hire { get; set; }

        /// <summary>이동 갈래. 0 해역 · 1 문화권 · 2 안 움직임 · 3 나라.</summary>
        public int Kind { get; set; }

        /// <summary>등장 여부. 0 이면 나오지 않는다.</summary>
        public int Appear { get; set; }

        /// <summary>목적지 도시. -1 이면 가는 데가 없다.</summary>
        public int Dest { get; set; } = -1;

        /// <summary>날 셈. 음수면 그만큼 더 쉰다(도착하면 -60 이 박힌다).</summary>
        public int Wait { get; set; }

        /// <summary>얼굴 코드.</summary>
        public int Face { get; set; }

        /// <summary>능력 여섯. <b>세이브에 적힌 날값</b>이라 게임이 쓰는 값보다 하나 크다.</summary>
        public int[] Stats { get; set; } = new int[StatCount];

        /// <summary>기술 열셋(0~3). 차례는 <see cref="Support.Local.Models.Skill.Names"/> 와 같다.</summary>
        public int[] Skills { get; set; } = new int[SkillCount];

        /// <summary>언어 열넷(0~3).</summary>
        public int[] Languages { get; set; } = new int[LangCount];

        /// <summary>이름·성을 이어 놓은 것. 셈해 내는 것이라 적어 두지 않는다.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string Name => (First, Last) switch
        {
            ("", "") => "???",
            (var f, "") => f,
            ("", var l) => l,
            var (f, l) => $"{f}·{l}",
        };

        /// <summary>칸이 모자란 옛 파일을 읽었을 때를 메운다.</summary>
        internal Row Fixed()
        {
            Stats = Sized(Stats, StatCount);
            Skills = Sized(Skills, SkillCount);
            Languages = Sized(Languages, LangCount);
            return this;
        }

        private static int[] Sized(int[]? source, int want)
        {
            var made = new int[want];
            for (int i = 0; source != null && i < Math.Min(source.Length, want); i++)
                made[i] = source[i];
            return made;
        }
    }

    /// <summary>JSON 으로 적어 두는 알맹이.</summary>
    internal sealed record Snapshot(List<Row> People);

    private readonly List<Row> _rows;

    private PersonTable(List<Row> rows) => _rows = rows;

    /// <summary>인물 전부. 차례는 번호 차례다.</summary>
    public IReadOnlyList<Row> People => _rows;

    /// <summary>표가 비었는지. 세이브도 없고 적어 둔 것도 없을 때 그렇다.</summary>
    public bool IsEmpty => _rows.Count == 0;

    /// <summary>어디서 왔는지 — 게임데이터 창과 고치는 창의 상태줄이 보여 준다.</summary>
    public static string Source { get; private set; } = "";

    /// <summary>왜 못 읽었는지. 잘 됐으면 빈 문자열.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>표가 고쳐졌을 때 알린다 — 들고 있던 쪽이 다시 읽는다.</summary>
    public static event Action? Changed;

    /// <summary>
    /// 표를 고칠 때마다 하나씩 오른다. 표를 들고 있는 쪽이 이 값만 견주면 되므로
    /// <see cref="Changed"/> 를 물지 않아도 된다(놀이의 <c>Game.Roster</c> 가 그렇게 한다).
    /// </summary>
    public static int Revision { get; private set; }

    /// <summary>그 인물. 없으면 null.</summary>
    public Row? Find(int id) => _rows.FirstOrDefault(r => r.Id == id);

    /// <summary>
    /// 표를 연다. 적어 둔 <c>인물표.json</c> 을 먼저 보고, 없으면 세이브에서 한 번 굽는다.
    /// </summary>
    /// <param name="seedSavePath">
    /// 적어 둔 것이 없을 때 구울 씨앗 세이브. null 이면 <see cref="Settings.AppSettings"/> 가
    /// 기억하는 마지막 세이브를 쓴다.
    /// </param>
    public static PersonTable Open(string? seedSavePath = null)
    {
        LastError = "";

        // ① 사람이 고친 것
        var saved = TableCache.Read<Snapshot>(CacheName);
        if (saved is { Version: Shape } && saved.Data.People.Count > 0)
        {
            Edited = true;
            Source = saved.Source.Length > 0 ? saved.Source : "고친 것";
            return new PersonTable(Fix(saved.Data.People));
        }
        Edited = false;

        // ② 같이 깔린 본
        if (Shipped() is { Count: > 0 } shipped)
        {
            Source = "인물표.json";
            return new PersonTable(shipped);
        }

        // ③ 씨앗 세이브 — 본까지 없는 자리에서만 온다
        string? seed = seedSavePath ?? Support.Local.Settings.AppSettings.LastSaveFilePath;
        if (PersonFile.ReadAll(seed) is { } baked)
        {
            Source = System.IO.Path.GetFileName(seed) ?? "SAVEDATA.CDS";
            return new PersonTable(baked);
        }

        LastError = PersonFile.LastError;
        Source = "";
        return new PersonTable([]);
    }

    /// <summary>지금 쓰는 표가 사람이 고쳐 둔 것인지. 아니면 같이 깔린 본이다.</summary>
    public static bool Edited { get; private set; }

    /// <summary>실행 파일 옆에 같이 깔린 본. 없으면 null.</summary>
    /// <remarks>
    /// 본은 손으로 굽는 것이라 <see cref="TableCache"/> 의 껍데기(<c>Stamp</c>·<c>Data</c>)가
    /// 있을 수도 없을 수도 있다. 둘 다 받아 준다.
    /// </remarks>
    private static List<Row>? Shipped()
    {
        try
        {
            string path = System.IO.Path.Combine(AppContext.BaseDirectory, CacheName + ".json");
            if (!System.IO.File.Exists(path)) return null;

            string text = System.IO.File.ReadAllText(path);
            var bare = System.Text.Json.JsonSerializer.Deserialize<Snapshot>(text);
            if (bare?.People is { Count: > 0 }) return Fix(bare.People);

            var wrapped = System.Text.Json.JsonSerializer
                .Deserialize<TableCache.Cached<Snapshot>>(text);
            return wrapped?.Data.People is { Count: > 0 } rows ? Fix(rows) : null;
        }
        catch (Exception ex)
            when (ex is System.IO.IOException or UnauthorizedAccessException
                     or System.Text.Json.JsonException)
        {
            LastError = ex.Message;
            return null;
        }
    }

    private static List<Row> Fix(List<Row> rows) => rows.Select(r => r.Fixed()).ToList();

    /// <summary>
    /// 세이브에서 표를 <b>다시 굽는다</b>. 손으로 고쳐 둔 것은 이때 사라진다.
    /// 잘 되면 빈 문자열, 아니면 까닭을 돌려준다.
    /// </summary>
    public static string Bake(string savePath)
    {
        var baked = PersonFile.ReadAll(savePath);
        if (baked == null) return PersonFile.LastError;

        Write(baked, System.IO.Path.GetFileName(savePath));
        Source = System.IO.Path.GetFileName(savePath);
        Edited = true;
        Revision++;
        Changed?.Invoke();
        return "";
    }

    /// <summary>고친 표를 적어 둔다. 창이 한 줄을 만질 때마다 부른다.</summary>
    public void Save()
    {
        Write(_rows, Source.Length > 0 ? Source : "고친 것");
        Edited = true;
        Revision++;
        Changed?.Invoke();
    }

    /// <summary>고쳐 둔 것을 지운다 — 다음에 열 때 같이 깔린 본으로 돌아간다.</summary>
    public static void Forget()
    {
        try
        {
            string path = TableCache.PathFor(CacheName);
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            LastError = ex.Message;
            return;
        }
        Edited = false;
        Revision++;
        Changed?.Invoke();
    }

    private static void Write(List<Row> rows, string source) =>
        TableCache.Write(CacheName, new TableCache.Cached<Snapshot>(
            $"{rows.Count}명", new Snapshot(rows), source, Shape));
}
