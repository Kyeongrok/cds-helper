using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows;
using CdsHelper.Game.Engine.Town;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 도서관 → 열람 의 책장 화면. 그 도시에 놓인 책을 책등으로 꽂아 두고, 누르면 읽는다.
/// </summary>
/// <remarks>
/// 책등 색은 게임 규칙 그대로다(볼트 <c>20.분석-도서관 책과 책등 색</c>).
/// <list type="bullet">
///   <item><b>초록</b> — 이 책이 나에게 더 줄 힌트가 없다(안 읽었어도 초록일 수 있다).</item>
///   <item><b>파랑</b> — 지금 읽으면 새 힌트가 들어온다.</item>
///   <item><b>빨강</b> — 줄 힌트는 남았는데 조건(책의 언어 3 · 힌트의 필요 기능)이 모자란다.</item>
/// </list>
/// 칸은 선반 세 줄에 열일곱 자리씩이다 — 게임 화면에서 재어 맞췄다. 책이 51권을 넘으면
/// 뒤는 안 꽂는다(넘기는 장치는 아직 흉내내지 않았다).
/// </remarks>
public sealed class LibraryDialog : Window
{
    /// <summary>
    /// 선반 세 줄. 책등 그림의 <b>위</b>가 놓이는 높이다(책장 그림 384x320 기준).
    /// </summary>
    /// <remarks>
    /// 게임 화면에 책장 그림을 맞춰 끼워(1.74배로 맞았다) 책등 자리를 되돌린 값이다 —
    /// 첫 칸 x 30, 간격 16.1, 줄 사이 80.5. 간격이 책등 너비(32)의 절반이라 책이 반쯤씩
    /// 겹쳐 꽂힌다. 게임도 그렇다.
    /// </remarks>
    private static readonly double[] ShelfTops = [66, 146.5, 227];

    /// <summary>한 줄에 꽂히는 자리 수와 첫 자리·간격.</summary>
    private const int SlotsPerShelf = 17;
    private const double FirstSlotX = 30, SlotStep = 16.1;

    private readonly Player _player;
    private readonly BookTable _books;
    private readonly CityBuildingTable _names;
    private readonly Func<int, string> _hintName;
    private readonly Canvas _layer = new();
    private readonly Border _tag;
    private readonly TextBlock _tagText;
    private readonly int _scale;

    /// <summary>
    /// 못 읽는 책이 어느 말로 적혔는지 이르는 곳 — <b>게임 화면 맨 아래 띠</b>다.
    /// </summary>
    /// <remarks>
    /// 왕궁에서 "명성치가 모자랍니다." 가 뜨는 그 자리다(<see cref="ShipMapWindow.Say"/>).
    /// 책장 밑에 따로 띠를 두는 것이 아니다.
    /// </remarks>
    private readonly Action<string>? _say;

    /// <summary>닫기 조각의 크기와 양피지 모서리에서 떨어진 거리. 게임 갈무리에서 잰 값이다.</summary>
    private const double CloseSize = 16, CloseInset = 10;
    private readonly OpenBookArt? _book;
    private readonly Func<int, string>? _hintText;

    private LibraryDialog(string cityName, BookShelf art, IReadOnlyList<Library.Slot> shelved,
                          Player player, BookTable table, CityBuildingTable names,
                          Func<int, string> hintName, int scale,
                          OpenBookArt? bookArt, Func<int, string>? hintText, Action<string>? say)
    {
        _player = player;
        _books = table;
        _names = names;
        _hintName = hintName;
        _scale = scale;
        _book = bookArt;
        _say = say;
        _hintText = hintText;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        var shelf = new Image
        {
            Source = ToBitmap(art.Shelf, BookShelf.ShelfWidth, BookShelf.ShelfHeight),
            Width = BookShelf.ShelfWidth * scale,
            Height = BookShelf.ShelfHeight * scale,
            Stretch = Stretch.Fill,
        };
        RenderOptions.SetBitmapScalingMode(shelf, BitmapScalingMode.NearestNeighbor);

        var box = new Grid
        {
            Width = shelf.Width,
            Height = shelf.Height,
            Children = { shelf, _layer },
        };

        _tagText = new TextBlock
        {
            Foreground = Brushes.Black,
            FontWeight = FontWeights.Bold,
            FontSize = 13,
        };
        _tag = new Border
        {
            Background = GameUi.ItemFill,
            BorderBrush = GameUi.ItemEdge,
            BorderThickness = new Thickness(2),
            Padding = new Thickness(8, 1, 8, 1),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
            Child = _tagText,
        };
        _layer.Children.Add(_tag);
        Panel.SetZIndex(_tag, 20);

        var spines = new[]
        {
            ToBitmap(art.Spines[0], BookShelf.SpineWidth, BookShelf.SpineHeight),
            ToBitmap(art.Spines[1], BookShelf.SpineWidth, BookShelf.SpineHeight),
            ToBitmap(art.Spines[2], BookShelf.SpineWidth, BookShelf.SpineHeight),
        };
        for (int i = 0; i < shelved.Count && i < ShelfTops.Length * SlotsPerShelf; i++)
        {
            if (shelved[i].Book is { } book) AddBook(book, i, spines);
            else if (shelved[i].Filler) AddFiller(i, spines);
        }

        // 게임에는 제목 띠가 없다 — 양피지 오른쪽 위에 닫기 조각만 얹혀 있다.
        var close = new Border
        {
            Width = CloseSize * _scale,
            Height = CloseSize * _scale,
            Background = GameUi.ItemFill,
            BorderBrush = GameUi.ItemEdge,
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            Child = new TextBlock
            {
                Text = "×",
                FontSize = 11 * _scale,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.Black,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        // 누른 자리에서 바로 닫는다 — 창 끌기가 ButtonUp 을 삼킨다.
        close.MouseLeftButtonDown += (_, e) => { e.Handled = true; Close(); };
        Canvas.SetLeft(close, (BookShelf.ShelfWidth - CloseSize - CloseInset) * _scale);
        Canvas.SetTop(close, CloseInset * _scale);
        Panel.SetZIndex(close, 30);
        _layer.Children.Add(close);

        Content = new Border
        {
            Background = GameUi.Back,
            BorderBrush = GameUi.Edge,
            BorderThickness = new Thickness(2),
            Child = box,
        };
        GameUi.EnableDrag(this, box);
        Closed += (_, _) => _say?.Invoke("");

        KeyDown += (_, e) => { if (e.Key is Key.Escape) Close(); };
        MouseRightButtonUp += (_, _) => Close();
    }

    /// <summary>
    /// 읽을 수 없는 책 한 권. 게임이 책 번호 -1 로 끼워 넣는 것이라 늘 초록이고,
    /// 이름표도 없고 눌러도 열리지 않는다 — 서가를 채우는 것이 하는 일의 전부다.
    /// </summary>
    private void AddFiller(int slot, BitmapSource[] spines)
    {
        int shelfRow = slot / SlotsPerShelf, column = slot % SlotsPerShelf;

        var image = new Image
        {
            Source = spines[0],                     // 0 = 초록
            Width = BookShelf.SpineWidth * _scale,
            Height = BookShelf.SpineHeight * _scale,
            Stretch = Stretch.Fill,
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        Canvas.SetLeft(image, (FirstSlotX + column * SlotStep) * _scale);
        Canvas.SetTop(image, ShelfTops[shelfRow] * _scale);
        _layer.Children.Add(image);
    }

    /// <summary>책 한 권을 서가에 꽂는다.</summary>
    private void AddBook(BookTable.Book book, int slot, BitmapSource[] spines)
    {
        int shelfRow = slot / SlotsPerShelf, column = slot % SlotsPerShelf;
        double x = FirstSlotX + column * SlotStep;
        double y = ShelfTops[shelfRow];

        var image = new Image
        {
            Source = spines[SpineColor(book)],
            Width = BookShelf.SpineWidth * _scale,
            Height = BookShelf.SpineHeight * _scale,
            Stretch = Stretch.Fill,
            Cursor = Cursors.Hand,
            Tag = book,
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        Canvas.SetLeft(image, x * _scale);
        Canvas.SetTop(image, y * _scale);

        image.MouseEnter += (_, _) => ShowTag(book, x, y);
        image.MouseLeave += (_, _) => { _tag.Visibility = Visibility.Collapsed; _say?.Invoke(""); };
        image.MouseLeftButtonDown += (_, e) => e.Handled = true;
        image.MouseLeftButtonUp += (_, e) => { e.Handled = true; Read(book, image, spines); };
        _layer.Children.Add(image);
    }

    /// <summary>책등 밑에 제목·저자를 띄운다.</summary>
    private void ShowTag(BookTable.Book book, double x, double y)
    {
        // 읽을 수 없는 책은 이름이 안 보인다 — 글자마다 x 로 가린다.
        bool readable = CanRead(book);
        string title = readable ? book.Title : Masked(book.Title);
        string author = readable ? book.Author : Masked(book.Author);
        _tagText.Text = $"「{title}」{author}";
        _say?.Invoke(readable
            ? ""
            : $"{LanguageOf(book)}{GameUi.Josa(LanguageOf(book), "으로", "로")} 표기되어 있습니다");
        _tag.Visibility = Visibility.Visible;
        _tag.UpdateLayout();
        double w = _tag.ActualWidth > 0 ? _tag.ActualWidth : 160;
        double left = (x + BookShelf.SpineWidth / 2.0) * _scale - w / 2;
        Canvas.SetLeft(_tag, Math.Clamp(left, 0, Math.Max(0, BookShelf.ShelfWidth * _scale - w)));
        Canvas.SetTop(_tag, Math.Min((y + BookShelf.SpineHeight + 2) * _scale,
                                     BookShelf.ShelfHeight * _scale - 24));
    }

    private string LanguageOf(BookTable.Book book) =>
        book.Language >= 0 && book.Language < _names.LanguageNames.Count
            ? _names.LanguageNames[book.Language]
            : $"언어 {book.Language}";

    /// <summary>
    /// 책등 색을 고른다 — 게임 <c>0x4716A0</c> 의 규칙을 그대로 옮겼다.
    /// 이미 얻은 힌트는 세지 않으므로, 한 번도 안 편 책이 곧장 초록일 수 있다.
    /// </summary>
    private int SpineColor(BookTable.Book book)
    {
        bool left = false;      // 아직 못 얻은 힌트가 남았나
        foreach (int hint in book.Hints)
        {
            if (_player.HasHint(hint)) continue;
            left = true;
            if (CanRead(book) && Understands(hint)) return 1;   // 파랑
        }
        return left ? 2 : 0;                                    // 빨강 / 초록
    }

    /// <summary>책을 읽으려면 그 언어가 있어야 하는 자리 — 게임도 <c>3</c> 이다.</summary>
    /// <remarks><c>0x00463E41</c> 의 <c>cmp eax,3 / jl</c> 이다.</remarks>
    private const int ReadLevel = 3;

    /// <summary>
    /// 책을 읽을 수 있는지 — <b>그 언어를 3 자리까지</b> 배워야 한다(<c>0x00463DB0</c>).
    /// </summary>
    /// <remarks>
    /// 언어는 기술과 <b>딴 칸</b>에 적힌다(<see cref="Player.TongueOf"/>). 예전에는 여기서
    /// <c>LevelOf</c>(기술 칸)를 봐서 <b>언어 자리가 늘 0 으로 읽혔고</b>, 그래서 파란 책이
    /// 한 권도 안 나오고 아무 책도 못 읽었다.
    ///
    /// 게임은 <b>함대에 탄 사람 전부</b>를 훑어 그 언어를 가장 잘 아는 이를 찾는다
    /// (<c>0x0047CD20</c>) — 부하가 대신 읽어 준다. 우리는 부하의 언어를 아직 안 적어 두어
    /// 주인공만 본다.
    /// </remarks>
    private bool CanRead(BookTable.Book book) => _player.TongueOf(LanguageOf(book)) >= ReadLevel;

    /// <summary>
    /// 힌트를 알아들을 수 있는지 — 기능만으로는 안 되는 힌트가 있다.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x00463E50</c> 이다. 기능을 재기 <b>앞에</b> <c>0x0042CCC0</c> 으로
    /// <b>선행 발견물 여덟 칸</b>을 훑어, 한 칸이라도 아직 못 찾았으면 물린다.
    /// <code>
    ///   0042CC93  [힌트+0x04] &amp; 0x08   개방 비트 — 놀이가 켜 준다
    ///   0042CCA4  힌트 번호 != 184
    ///   0042CCC0  선행 발견물 여덟 칸이 다 발견되었나
    ///   00463E72  필요 기능(+0x20)과 그 자리(+0x28)
    /// </code>
    /// 톨레도 도서관의 <b>카파도키아</b>(힌트 52)가 그렇다 — 신학 3 만으로는 안 되고
    /// <b>산티아고 대성당</b>(발견물 50)을 먼저 봐야 한다. 성지순례를 다녀와야 읽힌다는
    /// 것이 이것이다. 「성스러운 유물상자」(힌트 101)도 성 마르틴 교회(62)가 앞선다.
    ///
    /// 개방 비트와 184번 자리는 아직 안 옮겼다 — 그 비트를 켜는 곳이 이벤트 스크립트
    /// 실행기 안(<c>0x0040A0F8</c>)이라 스크립트 쪽을 더 뜯어야 한다.
    /// </remarks>
    private bool Understands(int hint)
    {
        var need = _books.NeedFor(hint);

        // 먼저 찾아 두어야 할 것이 남아 있으면 아무리 배워도 안 들어온다.
        if (need.Parents is { } parents)
            foreach (int id in parents)
                if (!_player.HasFound(id)) return false;

        if (need.Skill < 0 || need.Skill >= _names.SkillNames.Count) return true;
        return _player.LevelOf(_names.SkillNames[need.Skill]) >= need.Level;
    }

    /// <summary>책을 읽는다. 시간도 돈도 들지 않고, 알아들을 수 있는 힌트만 들어온다.</summary>
    private void Read(BookTable.Book book, Image image, BitmapSource[] spines)
    {
        if (!CanRead(book))
        {
            NoticeDialog.Show(this, $"{LanguageOf(book)}를 더 익혀야 읽을 수 있다.");
            return;
        }

        var got = new List<string>();
        var gotIds = new List<int>();
        foreach (int hint in book.Hints)
        {
            if (_player.HasHint(hint) || !Understands(hint)) continue;
            if (!_player.GainHint(hint)) continue;
            got.Add(_hintName(hint));
            gotIds.Add(hint);
        }

        image.Source = spines[SpineColor(book)];   // 읽고 나면 색이 바뀐다

        // 게임은 알림 창이 아니라 <b>펼친 책</b>으로 이른다 — 얻은 힌트마다 한 번씩 편다.
        bool opened = false;
        foreach (int hint in gotIds)
            if (OpenBookDialog.Show(this, _book, _hintName(hint), _hintText?.Invoke(hint) ?? "",
                                    Pages(hint)))
                opened = true;
        if (opened) return;

        NoticeDialog.Show(this, got.Count == 0
            ? $"「{book.Title}」{GameUi.Josa(book.Title, "을", "를")} 읽었다. 새로 알게 된 것은 없다."
            : $"「{book.Title}」{GameUi.Josa(book.Title, "을", "를")} 읽었다!"
              + Environment.NewLine
              + $"{string.Join(" · ", got)}에 대해 알게 되었다!");
    }

    /// <summary>
    /// 펼친 쪽 번호. 게임 갈무리는 <c>-3-</c>·<c>-4-</c> 였는데 무엇으로 정하는지는
    /// 못 짚었다 — 힌트마다 늘 같은 쪽이 나오게 홀수로 짓는다.
    /// </summary>
    /// <remarks>
    /// <b>발견물 번호가 아니라 이 책의 쪽수다.</b> 예전에는 힌트 번호를 그대로 불려
    /// <c>-33-</c> 처럼 큰 수가 나왔는데, 게임 것은 늘 한 자리였다 — 책 한 권이 그만큼
    /// 얇다. 그래서 <b>1~10</b> 안으로 접는다.
    /// </remarks>
    private const int PagesPerBook = 5;

    private static int Pages(int hint) => hint % PagesPerBook * 2 + 1;

    /// <summary>글자마다 <c>x</c> 로 가린다. 띄어쓰기는 그대로 둔다.</summary>
    private static string Masked(string text) =>
        new([.. text.Select(c => c == ' ' ? ' ' : 'x')]);

    private static BitmapSource ToBitmap(uint[] bgra, int width, int height)
    {
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null,
                                         bgra, width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>
    /// 책장을 띄운다. 그림이나 표를 못 읽으면 그 까닭을 알리고 만다.
    /// </summary>
    /// <param name="book">펼친 책 그림. 없으면 알림 창으로만 이른다.</param>
    /// <param name="hintText">그 힌트의 설명 — 펼친 책 오른쪽 면에 적힌다.</param>
    public static void Show(Window owner, string gameDirectory, string cityName, int cityId,
                            Player player, BookTable table, CityBuildingTable names,
                            Func<int, string> hintName,
                            OpenBookArt? book = null, Func<int, string>? hintText = null,
                            Action<string>? say = null)
    {
        var art = BookShelf.Open(gameDirectory);
        if (art == null)
        {
            NoticeDialog.Show(owner, $"책장을 열지 못했다 — {BookShelf.LastError}");
            return;
        }

        var books = table.InLibrary(cityId, player.Date.Year);
        if (books.Count == 0)
        {
            NoticeDialog.Show(owner, "서가가 비어 있다.");
            return;
        }

        // 진짜 책 사이사이에 읽을 수 없는 초록 책이 끼인다 — 그 마을 그 해면 늘 같은 모양이다.
        var shelved = Library.Shelve(books, Library.RandomFor(cityId, player.Date.Year));

        // 창 크기에 맞춰 정수배로 키운다(책장이 384x320 이라 두 배면 넉넉하다).
        int scale = owner.ActualHeight > 800 ? 2 : 1;
        new LibraryDialog(cityName, art, shelved, player, table, names, hintName, scale,
                          book, hintText, say)
        {
            Owner = owner,
        }.ShowDialog();
    }
}
