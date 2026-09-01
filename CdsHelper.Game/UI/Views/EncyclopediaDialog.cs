using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 자택 → <b>백과사전을 본다</b>. 책장에 갈래마다 한 권씩 꽂혀 있고, 발견한 것이
/// 그 갈래 책에 <b>한 쪽씩</b> 쌓인다.
/// </summary>
/// <remarks>
/// 게임의 갈래는 여덟이고 이름표가 EXE 에 그대로 있다(<c>0x00560C60</c>).
/// <code>
///   지리 · 역사 · 보물 · 종교 · 교역품 · 미신 · 생물 · 민족
/// </code>
/// 발견물 표의 <c>+0x04</c> 가 이 차례를 가리키므로(<see cref="DiscoveryTable.CategoryNames"/>)
/// 갈래는 새로 정할 것이 없다 — 발견물이 곧 제 책을 안다.
/// <code>
///   0x00471B5A  갈래 이름표 0x560C60[갈래]
///   0x00471B62  창 제목 "백과사전 (%s)"
///   0x00471B7F  갈래마다 72바이트 레코드 — 0x581120 + 갈래 x 72
///   0x00471BAA  책 이름표 "「%s」"
/// </code>
/// 책장 그림과 책등은 도서관 열람 화면과 같은 <c>BOOKSHEL.CDS</c> 다
/// (<see cref="BookShelf"/>). 게임 갈무리에서 백과사전 책등은 <b>빨강</b>이다.
///
/// 쪽은 펼친 책(<see cref="OpenBookDialog"/>)으로 한 장씩 넘긴다 — 도서관에서 힌트를
/// 얻을 때 쓰는 그 화면이다. 쪽 글은 그 발견물에 딸린 힌트 글이다.
/// </remarks>
public sealed class EncyclopediaDialog : Window
{
    /// <summary>책이 꽂히는 선반 — 백과사전은 <b>맨 윗줄 한 줄</b>이면 다 든다(여덟 권).</summary>
    private const double ShelfTop = 66;

    /// <summary>첫 자리와 자리 사이. 도서관 서가와 같은 치수다(책등이 반쯤 겹쳐 꽂힌다).</summary>
    private const double FirstSlotX = 30, SlotStep = 16.1;

    /// <summary>백과사전 책등의 빛깔. 0 초록 · 1 파랑 · <b>2 빨강</b>.</summary>
    private const int SpineRed = 2;

    /// <summary>닫기 조각의 크기와 양피지 모서리에서 떨어진 거리. 도서관 것과 같다.</summary>
    private const double CloseInset = 10;

    private readonly Player _player;
    private readonly DiscoveryTable _table;
    private readonly HintTable? _hints;
    private readonly OpenBookArt? _book;
    private readonly Canvas _layer = new();
    private readonly Border _tag;
    private readonly GameUi.GameLabel _tagText;
    private readonly int _scale;

    private EncyclopediaDialog(BookShelf art, Player player, DiscoveryTable table,
                               HintTable? hints, OpenBookArt? book, int scale)
    {
        _player = player;
        _table = table;
        _hints = hints;
        _book = book;
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
            Width = BookShelf.ShelfWidth * scale,
            Height = BookShelf.ShelfHeight * scale,
        };
        box.Children.Add(shelf);
        box.Children.Add(_layer);

        var spine = ToBitmap(art.Spines[SpineRed], BookShelf.SpineWidth, BookShelf.SpineHeight);
        for (int i = 0; i < DiscoveryTable.CategoryNames.Length; i++) AddBook(i, spine);

        // 책등 밑에 뜨는 이름표 — 게임도 「지리」 처럼 낫표를 두른다.
        _tagText = new GameUi.GameLabel(GameFont.WhiteColor) { FallbackBrush = GameUi.Text };
        _tag = new Border
        {
            Background = GameUi.Back,
            BorderBrush = GameUi.Edge,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4, 1, 4, 1),
            Visibility = Visibility.Collapsed,
            Child = _tagText,
        };
        _layer.Children.Add(_tag);

        var close = GameUi.CloseBox(Close, scale);
        close.Margin = new Thickness(0);
        close.MouseLeftButtonDown += (_, e) => { e.Handled = true; Close(); };
        Canvas.SetLeft(close, (BookShelf.ShelfWidth - GameUi.CloseBoxSize - CloseInset) * scale);
        Canvas.SetTop(close, CloseInset * scale);
        _layer.Children.Add(close);

        Content = GameUi.DialogEdge(box);
        GameUi.EnableDrag(this, box);

        KeyDown += (_, e) => { if (e.Key is Key.Escape) Close(); };
        MouseRightButtonUp += (_, _) => Close();
    }

    /// <summary>갈래 한 권을 서가에 꽂는다.</summary>
    private void AddBook(int category, BitmapSource spine)
    {
        double x = FirstSlotX + category * SlotStep;

        var image = new Image
        {
            Source = spine,
            Width = BookShelf.SpineWidth * _scale,
            Height = BookShelf.SpineHeight * _scale,
            Stretch = Stretch.Fill,
            Cursor = Cursors.Hand,
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        Canvas.SetLeft(image, x * _scale);
        Canvas.SetTop(image, ShelfTop * _scale);

        image.MouseEnter += (_, _) => ShowTag(category, x);
        image.MouseLeave += (_, _) => _tag.Visibility = Visibility.Collapsed;
        image.MouseLeftButtonDown += (_, e) => e.Handled = true;
        image.MouseLeftButtonUp += (_, e) => { e.Handled = true; Read(category); };
        _layer.Children.Add(image);
    }

    /// <summary>책등 밑에 갈래 이름을 띄운다 — 몇 쪽이 찼는지도 함께 낸다.</summary>
    private void ShowTag(int category, double x)
    {
        var pages = PagesOf(category);
        _tagText.Text = $"「{DiscoveryTable.CategoryNames[category]}」 {pages.Count}";
        _tag.Visibility = Visibility.Visible;
        _tag.UpdateLayout();

        double w = _tag.ActualWidth > 0 ? _tag.ActualWidth : 120;
        double left = (x + BookShelf.SpineWidth / 2.0) * _scale - w / 2;
        Canvas.SetLeft(_tag, Math.Clamp(left, 0, Math.Max(0, BookShelf.ShelfWidth * _scale - w)));
        Canvas.SetTop(_tag, (ShelfTop + BookShelf.SpineHeight + 2) * _scale);
    }

    /// <summary>
    /// 그 갈래 책에 적힌 쪽 — <b>발견한 것만</b>이다. 발견물 번호 차례로 쌓인다.
    /// </summary>
    private List<DiscoveryTable.Record> PagesOf(int category)
    {
        var pages = new List<DiscoveryTable.Record>();
        foreach (var row in _table.Discoveries)
            if (row.Category == category && _player.HasFound(row.Id)) pages.Add(row);
        return pages;
    }

    /// <summary>한 권을 펴서 한 쪽씩 넘긴다. 빈 책이면 그렇다고 이른다.</summary>
    private void Read(int category)
    {
        string name = DiscoveryTable.CategoryNames[category];
        var pages = PagesOf(category);
        if (pages.Count == 0)
        {
            NoticeDialog.Show(this, $"「{name}」은 아직 백지다.");
            return;
        }

        for (int i = 0; i < pages.Count; i++)
        {
            var row = pages[i];
            string text = _hints?.Find(row.Hint)?.Text ?? "";
            // 왼쪽 면이 홀수 쪽이다 — 한 발견물이 한 장을 차지한다.
            if (!OpenBookDialog.Show(this, _book, row.Name, text, i * 2 + 1)) break;
        }
    }

    private static BitmapSource ToBitmap(uint[] bgra, int width, int height)
    {
        var bmp = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null,
                                      bgra, width * 4);
        bmp.Freeze();
        return bmp;
    }

    /// <summary>백과사전 책장을 연다. 그림이나 발견물 표가 없으면 그렇다고 이른다.</summary>
    public static void Show(Window owner, string gameDirectory, Player player,
                            DiscoveryTable? table, HintTable? hints, OpenBookArt? book)
    {
        if (table == null)
        {
            NoticeDialog.Show(owner, "발견물 표를 읽지 못했다.");
            return;
        }

        var art = BookShelf.Open(gameDirectory);
        if (art == null)
        {
            NoticeDialog.Show(owner, $"책장을 열지 못했다 — {BookShelf.LastError}");
            return;
        }

        int scale = owner.ActualHeight > 800 ? 2 : 1;
        new EncyclopediaDialog(art, player, table, hints, book, scale) { Owner = owner }
            .ShowDialog();
    }
}
