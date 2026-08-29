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

    private LibraryDialog(string cityName, BookShelf art, IReadOnlyList<Library.Slot> shelved,
                          Player player, BookTable table, CityBuildingTable names,
                          Func<int, string> hintName, int scale)
    {
        _player = player;
        _books = table;
        _names = names;
        _hintName = hintName;
        _scale = scale;

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

        var title = GameUi.TitleBar($"{cityName} 도서관", Close);
        GameUi.EnableDrag(this, title);

        var stack = new StackPanel();
        stack.Children.Add(title);
        stack.Children.Add(new Border
        {
            BorderBrush = GameUi.Edge,
            BorderThickness = new Thickness(2),
            Margin = new Thickness(4, 4, 4, 4),
            Child = box,
        });
        Content = new Border
        {
            Background = GameUi.Back,
            BorderBrush = GameUi.Edge,
            BorderThickness = new Thickness(2),
            Margin = new Thickness(4),
            Child = stack,
        };

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
        image.MouseLeave += (_, _) => _tag.Visibility = Visibility.Collapsed;
        image.MouseLeftButtonDown += (_, e) => e.Handled = true;
        image.MouseLeftButtonUp += (_, e) => { e.Handled = true; Read(book, image, spines); };
        _layer.Children.Add(image);
    }

    /// <summary>책등 밑에 제목·저자를 띄운다.</summary>
    private void ShowTag(BookTable.Book book, double x, double y)
    {
        _tagText.Text = $"{book.Title} — {book.Author} ({LanguageOf(book)})";
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

    /// <summary>책을 읽을 수 있는지 — 그 언어를 3 자리까지 배워야 한다.</summary>
    private bool CanRead(BookTable.Book book) => _player.LevelOf(LanguageOf(book)) >= 3;

    /// <summary>힌트를 알아들을 수 있는지 — 힌트마다 필요한 기능과 그 자리가 있다.</summary>
    private bool Understands(int hint)
    {
        var need = _books.NeedFor(hint);
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
        foreach (int hint in book.Hints)
        {
            if (_player.HasHint(hint) || !Understands(hint)) continue;
            if (_player.GainHint(hint)) got.Add(_hintName(hint));
        }

        image.Source = spines[SpineColor(book)];   // 읽고 나면 색이 바뀐다
        NoticeDialog.Show(this, got.Count == 0
            ? $"「{book.Title}」을 읽었다. 새로 알게 된 것은 없다."
            : $"「{book.Title}」을 읽었다!\n{string.Join(" · ", got)} 에 대해 알게 되었다!");
    }

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
    public static void Show(Window owner, string gameDirectory, string cityName, int cityId,
                            Player player, BookTable table, CityBuildingTable names,
                            Func<int, string> hintName)
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
        new LibraryDialog(cityName, art, shelved, player, table, names, hintName, scale)
        {
            Owner = owner,
        }.ShowDialog();
    }
}
