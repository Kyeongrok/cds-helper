using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.Engine.Town;

/// <summary>
/// 도서관 서가를 채우는 규칙 — 진짜 책 사이사이에 <b>읽을 수 없는 책</b>을 끼운다.
/// </summary>
/// <remarks>
/// 그 마을에 놓인 책만 꽂으면 서가가 휑하다. 게임은 진짜 책 한 권을 꽂기 전에 가짜를
/// 몇 권 끼워 넣어 책장을 채운다(<c>0x004721E3</c>).
/// <code>
///   4721e3  남은 자리가 없으면 그냥 진짜 책을 꽂는다
///   4721e9  rand(3) 이 0 이 아니면 → 끼울 수 = rand(3) &lt; 1 ? 1 : 0     (거의 한 칸)
///   472209  0 이면            → 끼울 수 = min(rand(남은자리 + 1), 8)   (뭉텅이)
///   472228  그 수만큼 돌며 <b>rand(3) 이 0 일 때만</b> 실제로 꽂는다 —
///           안 꽂아도 자리는 넘어가므로 빈 자리가 생긴다
///   472236  꽂을 때 책 번호가 <b>-1</b> 이다
/// </code>
/// 책 번호 -1 은 책등 색을 정하는 <c>0x004716A0</c> 이 맨 앞에서 걸러 <c>0</c>(초록)을
/// 돌려준다(<c>0x00471768</c>). 그래서 가짜는 늘 초록이다.
///
/// 무작위는 <b>씨앗을 박고</b> 돌린다(<c>0x004721C9</c> 가 srand). 그래서 같은 마을에
/// 같은 때 들어가면 책장이 늘 같은 모양이다 — 들어갈 때마다 뒤바뀌지 않는다.
/// </remarks>
public static class Library
{
    /// <summary>책장 자리 수. 게임도 이만큼에서 그만 꽂는다(<c>0x004715E0</c> 의 <c>cmp 0x33</c>).</summary>
    public const int Slots = 51;

    /// <summary>한 번에 끼우는 가짜 책의 가장 많은 수(<c>0x00472216</c> 의 <c>cmp eax,8</c>).</summary>
    public const int MaxFillers = 8;

    /// <summary>세 번에 한 번 꼴로 일어난다는 뜻 — 게임이 <c>rand(3)</c> 로 던지는 주사위다.</summary>
    private const int Dice = 3;

    /// <summary>
    /// 책장 한 자리 — 진짜 책이거나, 읽을 수 없는 초록 책이거나, 빈 자리다.
    /// </summary>
    /// <param name="Book">진짜 책. 가짜이거나 빈 자리면 null.</param>
    /// <param name="Filler">읽을 수 없는 초록 책이면 true.</param>
    public readonly record struct Slot(BookTable.Book? Book, bool Filler);

    /// <summary>
    /// 그 마을·그 해의 책장은 늘 같은 모양이다. 게임도 씨앗을 박아 돌린다.
    /// </summary>
    public static Random RandomFor(int cityId, int year) => new(cityId * 397 + year);

    /// <summary>
    /// 책장에 꽂을 차례를 짓는다. 자리 차례대로 <see cref="Slots"/> 칸까지다.
    /// </summary>
    /// <param name="books">그 마을에 놓인 진짜 책.</param>
    /// <param name="random">씨앗을 박은 주사위(<see cref="RandomFor"/>).</param>
    public static List<Slot> Shelve(IReadOnlyList<BookTable.Book> books, Random random)
    {
        var shelf = new List<Slot>(Slots);
        int spare = Slots - books.Count;      // 가짜로 채울 수 있는 자리

        foreach (var book in books)
        {
            if (shelf.Count >= Slots) break;

            if (spare > 0)
            {
                // 대개는 한 칸, 세 번에 한 번은 뭉텅이로 끼운다.
                int fillers = random.Next(Dice) != 0
                    ? (random.Next(Dice) < 1 ? 1 : 0)
                    : Math.Min(random.Next(spare + 1), MaxFillers);

                for (int i = 0; i < fillers && shelf.Count < Slots; i++)
                    // 자리는 넘어가되 세 번에 한 번만 실제로 꽂는다 — 그래서 빈 자리가 남는다.
                    shelf.Add(new Slot(null, random.Next(Dice) == 0));

                spare -= fillers;
            }

            shelf.Add(new Slot(book, false));
        }
        return shelf;
    }
}
