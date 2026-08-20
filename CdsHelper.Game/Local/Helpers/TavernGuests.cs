using System.IO;
using CdsHelper.Support.Local.Helpers;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 술집·여관에 서 있는 손님 그림 146명. <see cref="BuildingPhoto"/> 와 같은 MPCG.CDS 에 있다.
/// </summary>
/// <remarks>
/// 자세한 것은 볼트 <c>14.분석-술집 화면과 대사</c> 에 있다.
/// <code>
///   파트 170 + n     손님 n (n = 0~145)   — 읽기 0x0042DDB0 이 파트에 0xAA 를 더한다
///   표 0x0056E3A0    146행 x 12바이트 = (성별, 폭, 높이)   — 크기 재기 0x0049D710
/// </code>
/// 성별은 0 남자 · 1 여자, 폭은 48·56·64·66, 높이는 72·80·88·96·104 가 나온다.
/// 크기가 다 64 의 배수라 <c>길이/64</c> 로 재면 폭이 64 가 아닌 넷이 어긋난 줄무늬가 된다 —
/// 그래서 짐작하지 않고 표를 읽는다.
///
/// <b>색은 사진 팔레트의 앞 64색</b>이다. 팔레트 84개 전부에서 0~63 구간이 한 바이트도
/// 다르지 않아, 아무 사진 팔레트나 써도 손님 색은 같다. <b>비침은 색인 55</b> 다
/// (건물 사진 쪽 64 와 다르다).
///
/// 문화권마다 쓰는 구간이 정해져 있다(<see cref="Ranges"/>). 게임은 시작을
/// <c>0x0049D580(문화권)</c>, 개수를 <c>0x0049D500(문화권)</c> 이 낸다 — 둘 다 점프표를 쓰는
/// switch 라 값을 읽어 옮겨 적었다.
/// </remarks>
public sealed class TavernGuests
{
    /// <summary>손님이 시작되는 파트. 게임도 손님 번호에 이 값을 더해 읽는다.</summary>
    private const int FirstPart = 170;

    /// <summary>손님 수.</summary>
    public const int Count = 146;

    /// <summary>크기·성별 표(EXE). 146행 x 12바이트.</summary>
    private const int TableVa = 0x0056E3A0;
    private const int RowSize = 12;

    /// <summary>손님 그림의 비침 색인.</summary>
    private const byte Transparent = 55;

    /// <summary>색을 꺼내 올 사진 팔레트 파트. 앞 64색은 어느 것이나 같다.</summary>
    private const int PalettePart = 3;

    /// <summary>한 화면에 서는 손님 수. 게임도 <c>cmp eax,5</c> 로 잘라 낸다(0x0042DB18).</summary>
    public const int MaxOnScreen = 5;

    /// <summary>손님 한 명.</summary>
    /// <param name="Index">손님 번호(0~145). 파트 번호는 여기에 170 을 더한 것이다.</param>
    public readonly record struct Guest(int Index, bool Female, int Width, int Height);

    /// <summary>문화권 이름 -> 손님 구간(시작, 개수).</summary>
    /// <remarks>
    /// 이베리아·지중해가 같은 구간을 함께 쓴다. 합이 딱 146 이다. 게임에 없는 "발칸"(한 도시)은
    /// 지중해로 본다 — <see cref="BuildingPhoto.RowFor"/> 와 같은 셈이다.
    /// </remarks>
    private static readonly Dictionary<string, (int Start, int Count)> Ranges = new()
    {
        ["북유럽"] = (0, 17),
        ["이베리아"] = (17, 21),
        ["지중해"] = (17, 21),
        ["발칸"] = (17, 21),
        ["인도"] = (38, 17),
        ["중국"] = (55, 13),
        ["아프리카"] = (68, 16),
        ["이슬람"] = (84, 13),
        ["중근동"] = (84, 13),
        ["일본"] = (97, 11),
        ["동남아시아"] = (108, 11),
        ["중앙아시아"] = (119, 11),
        ["아메리카"] = (130, 16),
    };

    private readonly Ls12Reader _archive;
    private readonly byte[] _palette;
    private readonly Guest[] _guests;

    private TavernGuests(Ls12Reader archive, byte[] palette, Guest[] guests)
    {
        _archive = archive;
        _palette = palette;
        _guests = guests;
    }

    /// <summary>왜 못 열었는지. 잘 열렸으면 빈 문자열.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>MPCG.CDS 와 EXE 의 크기 표를 함께 연다. 하나라도 어긋나면 null.</summary>
    public static TavernGuests? Open(string gameDirectory)
    {
        LastError = "";

        var path = Path.Combine(gameDirectory, "MPCG.CDS");
        if (!File.Exists(path)) { LastError = $"{path} 가 없습니다"; return null; }

        var archive = Ls12Reader.Open(path);
        if (archive == null) { LastError = $"{path} 를 읽지 못했습니다"; return null; }
        if (archive.PartCount < FirstPart + Count)
        {
            LastError = "MPCG.CDS 에 손님이 모자랍니다";
            return null;
        }

        var palette = archive.Decode(PalettePart);
        if (palette == null || palette.Length < 64 * 3) { LastError = "손님 팔레트를 못 풀었습니다"; return null; }

        var exe = PeImage.Read(Path.Combine(gameDirectory, "CDS_95.EXE"), out string error);
        if (exe == null) { LastError = error; return null; }

        var guests = new Guest[Count];
        for (int i = 0; i < Count; i++)
        {
            int row = TableVa + i * RowSize;
            guests[i] = new Guest(i, exe.Int(row) == 1, exe.Int(row + 4), exe.Int(row + 8));
        }

        // 판이 다른 EXE 를 잘못 읽지 않도록 크기가 말이 되는지 본다.
        if (guests[0].Width is < 32 or > 128 || guests[0].Height is < 48 or > 160)
        {
            LastError = "손님 표가 기대한 모양이 아닙니다(다른 판의 EXE 일 수 있습니다)";
            return null;
        }

        return new TavernGuests(archive, palette, guests);
    }

    /// <summary>자리 하나 — 그림과 그 자리에 앉은 인물(<paramref name="Person"/>, 없으면 -1).</summary>
    public readonly record struct Slot(Guest Art, int Person);

    /// <summary>
    /// 술집에 세울 사람들을 자리 차례대로 고른다. <b>맨 앞(가장 왼쪽)은 여급</b>, 그 뒤는
    /// 세이브에 그 술집으로 적힌 인물, 남는 자리는 지나가는 남자 손님이다.
    /// </summary>
    /// <param name="personKeys">
    /// 그 술집에 앉힐 인물의 고유값(세이브 인물 번호). 그림은 이 값으로 정해지므로
    /// 같은 인물은 늘 같은 모습으로 선다.
    /// </param>
    /// <remarks>
    /// 게임도 이 차례로 짓는다 — 인물이 있는 자리는 인물에서 그림을 얻고
    /// (<c>0x0049D440</c>: 인물 고유값을 그 문화권 남자 수로 나눈 나머지 번째),
    /// 빈 자리는 무작위 남자로 채운다(<c>0x0049D6C0</c>).
    ///
    /// 여급과 지나가는 손님은 <paramref name="seed"/> 로 도시마다 고정한다 — 창을 여닫을
    /// 때마다 얼굴이 바뀌면 그 도시 술집처럼 보이지 않는다.
    /// </remarks>
    public IReadOnlyList<Slot> Seat(string? culture, int seed, IReadOnlyList<int> personKeys)
    {
        if (!Ranges.TryGetValue(culture ?? "", out var range)) range = Ranges["이베리아"];

        var women = new List<int>();
        var men = new List<int>();
        for (int i = 0; i < range.Count; i++)
            (_guests[range.Start + i].Female ? women : men).Add(range.Start + i);
        if (men.Count == 0) return [];

        var seats = new List<Slot>(MaxOnScreen);

        // 여급 — 그 문화권에 여자가 없으면 그냥 건너뛴다(구간이 열한 명뿐인 문화권도 있다).
        var rng = new Random(seed);
        if (women.Count > 0) seats.Add(new Slot(_guests[women[rng.Next(women.Count)]], -1));

        // 인물 — 각자 제 그림으로 앉는다.
        for (int i = 0; i < personKeys.Count && seats.Count < MaxOnScreen; i++)
            seats.Add(new Slot(_guests[men[Mod(personKeys[i], men.Count)]], i));

        // 남는 자리는 지나가는 손님. 앞서 선 사람과 겹치지 않게 고른다.
        var taken = new HashSet<int>();
        foreach (var s in seats) taken.Add(s.Art.Index);
        Shuffle(men, rng);
        foreach (int i in men)
        {
            if (seats.Count >= MaxOnScreen) break;
            if (taken.Add(i)) seats.Add(new Slot(_guests[i], -1));
        }
        return seats;
    }

    /// <summary>음수가 나오지 않는 나머지. 세이브 번호는 늘 0 이상이지만 눌러 둔다.</summary>
    private static int Mod(int value, int n) => ((value % n) + n) % n;

    private static void Shuffle(List<int> list, Random rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    /// <summary>
    /// 손님 한 명을 BGRA 로 푼다. 크기는 <see cref="Guest.Width"/> x <see cref="Guest.Height"/>
    /// 다. 못 풀면 null.
    /// </summary>
    public uint[]? TryGetBgra(Guest guest)
    {
        int pixels = guest.Width * guest.Height;
        if (pixels <= 0) return null;

        var idx = _archive.Decode(FirstPart + guest.Index);
        if (idx == null || idx.Length < pixels) return null;

        var bgra = new uint[pixels];
        for (int i = 0; i < pixels; i++)
        {
            byte v = idx[i];
            if (v == Transparent) continue;
            int k = v * 3;
            // 파일 속 팔레트는 (파랑, 빨강, 초록) 순이다.
            bgra[i] = (uint)(0xFF << 24 | _palette[k + 1] << 16 | _palette[k + 2] << 8 | _palette[k]);
        }
        return bgra;
    }
}
