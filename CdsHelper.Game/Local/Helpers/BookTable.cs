using System.IO;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// CDS_95.EXE 안의 책 표. 책마다 제목·저자·읽는 데 필요한 언어·나오는 해·놓인 도서관·주는
/// 힌트가 들어 있다.
/// </summary>
/// <remarks>
/// <code>
///   책 표    VA 0x004C4748, 257권 x 0x58 (.rdata)
///   +0x00 제목 ptr      +0x04 저자 ptr
///   +0x0C 책의 언어(0~13, 언어 이름표와 같은 차례)   ← 그 언어 3 이라야 읽는다
///   +0x10 나오는 해(1480 + 값)
///   +0x18~+0x34 놓인 도시 8칸(-1 = 없음)
///   +0x38~+0x54 주는 힌트 8칸(-1 = 없음)
///
///   힌트 표  VA 0x004D8EA0, 0x50 간격
///   +0x00 필요 기능(기능 이름표 색인)   +0x08 필요 수준
/// </code>
/// 자세한 것은 볼트 <c>20.분석-도서관 책과 책등 색</c> 에 있다. 표는 EXE 에서 그때그때
/// 읽는다 — <see cref="CityBuildingTable"/> 과 같은 수다.
/// </remarks>
public sealed class BookTable
{
    private const int BooksVa = 0x004C4748;
    private const int BookCount = 257;
    private const int BookSize = 0x58;
    private const int HintsVa = 0x004D8EA0;
    private const int HintCount = 186;
    private const int HintSize = 0x50;

    /// <summary>책 한 권.</summary>
    /// <param name="Language">책의 언어(언어 이름표 색인 0~13).</param>
    /// <param name="Year">이 해부터 서가에 나온다.</param>
    /// <param name="Cities">놓인 도서관의 도시 번호.</param>
    /// <param name="Hints">읽으면 주는 힌트 번호.</param>
    public readonly record struct Book(
        int Index, string Title, string Author, int Language, int Year,
        IReadOnlyList<int> Cities, IReadOnlyList<int> Hints);

    /// <summary>힌트를 알아들으려면 있어야 하는 기능과 그 수준.</summary>
    public readonly record struct HintNeed(int Skill, int Level);

    private readonly List<Book> _books;
    private readonly HintNeed[] _hintNeeds;

    private BookTable(List<Book> books, HintNeed[] hintNeeds)
    {
        _books = books;
        _hintNeeds = hintNeeds;
    }

    /// <summary>왜 못 읽었는지. 잘 열렸으면 빈 문자열.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>책 전부(257권).</summary>
    public IReadOnlyList<Book> Books => _books;

    /// <summary>그 힌트를 알아들으려면 있어야 하는 것. 번호가 표 밖이면 기능 -1.</summary>
    public HintNeed NeedFor(int hint) =>
        hint >= 0 && hint < _hintNeeds.Length ? _hintNeeds[hint] : new HintNeed(-1, 0);

    /// <summary>
    /// 그 도시 도서관 서가에 꽂히는 책. 그 도시에 놓였고 <paramref name="year"/> 까지
    /// 나온 것만 고른다. 게임도 들어갈 때마다 이렇게 훑는다(색인을 안 만든다).
    /// </summary>
    public List<Book> InLibrary(int cityId, int year)
    {
        var got = new List<Book>();
        foreach (var b in _books)
            if (b.Year <= year && b.Cities.Contains(cityId))
                got.Add(b);
        return got;
    }

    /// <summary>게임 폴더의 CDS_95.EXE 에서 읽는다. 못 읽으면 null.</summary>
    public static BookTable? Open(string gameDirectory)
    {
        LastError = "";
        var exe = PeImage.Read(Path.Combine(gameDirectory, "CDS_95.EXE"), out string error);
        if (exe == null) { LastError = error; return null; }

        var books = new List<Book>(BookCount);
        for (int k = 0; k < BookCount; k++)
        {
            int row = BooksVa + k * BookSize;
            var title = exe.Text(exe.Word(row + 0x00));
            var author = exe.Text(exe.Word(row + 0x04));
            if (title == null || author == null) continue;

            var cities = new List<int>(8);
            var hints = new List<int>(8);
            for (int i = 0; i < 8; i++)
            {
                int city = exe.Int(row + 0x18 + i * 4);
                if (city >= 0) cities.Add(city);
                int hint = exe.Int(row + 0x38 + i * 4);
                if (hint >= 0) hints.Add(hint);
            }
            books.Add(new Book(k, title, author, exe.Int(row + 0x0C),
                               1480 + exe.Int(row + 0x10), cities, hints));
        }

        // 판이 다른 EXE 를 잘못 읽지 않게 첫 권을 확인한다.
        if (books.Count == 0 || books[0].Title != "형이상학")
        {
            LastError = "책 표가 기대한 모양이 아닙니다(다른 판의 EXE 일 수 있습니다)";
            return null;
        }

        var needs = new HintNeed[HintCount];
        for (int h = 0; h < HintCount; h++)
            needs[h] = new HintNeed(exe.Int(HintsVa + h * HintSize),
                                    exe.Int(HintsVa + h * HintSize + 0x08));

        return new BookTable(books, needs);
    }
}
