using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using CdsHelper.Support.Local.Helpers;

namespace CdsHelper.Main.UI.Views.D3D;

/// <summary>
/// 함대 창 쪽 물음창·명령창이 함께 쓰는 게임풍 조각. 색은 게임 화면에서 뽑았다 —
/// 짙은 밤색 바탕에 밝은 테를 두르고, 누를 수 있는 것만 양피지에 검은 글씨다.
/// </summary>
internal static class GameUi
{
    public static readonly Brush Back = new SolidColorBrush(Color.FromRgb(0x3A, 0x24, 0x1E));
    public static readonly Brush Edge = new SolidColorBrush(Color.FromRgb(0xC8, 0xB4, 0x90));
    public static readonly Brush Text = new SolidColorBrush(Color.FromRgb(0xF2, 0xEA, 0xD6));
    public static readonly Brush MenuBack = new SolidColorBrush(Color.FromRgb(0x4A, 0x2A, 0x22));
    public static readonly Brush ItemFill = new SolidColorBrush(Color.FromRgb(0xD2, 0xCA, 0xAD));
    public static readonly Brush ItemEdge = new SolidColorBrush(Color.FromRgb(0x4A, 0x40, 0x30));
    public static readonly Brush PageFill = new SolidColorBrush(Color.FromRgb(0xF2, 0xE4, 0xC8));

    /// <summary>도시에 들어가 있는 동안 지도를 덮는 남색. 게임 화면에서 뽑았다.</summary>
    public static readonly Brush MapCover = new SolidColorBrush(Color.FromRgb(0x24, 0x37, 0x5B));

    /// <summary>
    /// 제목 줄. 오른쪽 끝에 닫기(X) 단추를 둔다 — 게임 창들도 그 자리에 있다.
    /// <paramref name="onClose"/> 가 null 이면 단추 없이 제목만 낸다.
    /// </summary>
    public static Border TitleBar(string title, Action? onClose)
    {
        // 게임 원본 조각을 읽었으면 그것으로 짓는다 — 덩굴 무늬가 붙은 제 상자다.
        var framed = TitleFrame(Sprites, title, 2, onClose);
        if (framed != null) return framed;

        var bar = new DockPanel { LastChildFill = true };

        if (onClose != null)
        {
            var close = new Border
            {
                Background = ItemFill,
                BorderBrush = ItemEdge,
                BorderThickness = new Thickness(2),
                Padding = new Thickness(6, 0, 6, 0),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 2, 4, 2),
                ToolTip = "닫기",
                Child = new TextBlock
                {
                    Text = "✕",
                    Foreground = Brushes.Black,
                    FontWeight = FontWeights.Bold,
                    FontSize = 13,
                },
            };
            close.MouseLeftButtonDown += (_, e) => e.Handled = true;   // 제목 줄 끌기에 먹히지 않게
            close.MouseLeftButtonUp += (_, e) => { e.Handled = true; onClose(); };
            DockPanel.SetDock(close, Dock.Right);
            bar.Children.Add(close);
        }

        bar.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = Text,
            FontWeight = FontWeights.Bold,
            FontSize = 15,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 3, 10, 3),
        });

        return new Border
        {
            Background = MenuBack,
            BorderBrush = Edge,
            BorderThickness = new Thickness(1),
            Child = bar,
        };
    }

    /// <summary>명령 창의 한 줄. <paramref name="run"/> 이 null 이면 흐려 두고 안 먹는다.</summary>
    public static Border MenuItem(string text, Action? run)
    {
        var item = new Border
        {
            Background = ItemFill,
            BorderBrush = ItemEdge,
            BorderThickness = new Thickness(2),
            Margin = new Thickness(0, 0, 0, 3),
            Padding = new Thickness(24, 2, 24, 2),
            Cursor = run != null ? Cursors.Hand : Cursors.Arrow,
            Child = new TextBlock
            {
                Text = text,
                Foreground = run != null ? Brushes.Black : Brushes.Gray,
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center,
            },
        };
        if (run != null)
        {
            // 누름도 여기서 삼킨다 — 창 끌기(DragMove)가 먼저 걸리면 마우스를 잡아 버려
            // 뗌이 오지 않는다. 그러면 눌러도 아무 일이 없어 멈춘 것처럼 보인다.
            item.MouseLeftButtonDown += (_, e) => e.Handled = true;
            item.MouseLeftButtonUp += (_, e) => { e.Handled = true; run(); };
        }
        return item;
    }

    /// <summary>제목 한 줄과 항목들을 세로로 쌓은 명령 창.</summary>
    public static Border CommandBox(string title, params (string Text, Action? Run)[] items) =>
        CommandBox(title, null, items);

    /// <summary>제목 없이 줄만 쌓은 창. 기능 창처럼 제목이 없는 것에 쓴다.</summary>
    public static Border MenuBox(params (string Text, Action? Run)[] items)
    {
        var stack = new StackPanel();
        foreach (var (text, run) in items) stack.Children.Add(MenuItem(text, run));
        return new Border
        {
            Background = MenuBack,
            BorderBrush = Edge,
            BorderThickness = new Thickness(3),
            Padding = new Thickness(6),
            Child = stack,
        };
    }

    /// <summary>
    /// 제목 줄에 닫기(X)까지 두는 명령 창. <paramref name="onClose"/> 가 null 이면 제목만 낸다.
    /// </summary>
    public static Border CommandBox(string title, Action? onClose,
                                    params (string Text, Action? Run)[] items)
    {
        var stack = new StackPanel();
        stack.Children.Add(
            onClose != null ? TitleBar(title, onClose)
            : TitleFrame(Sprites, title) ?? new Border
            {
                Background = MenuBack,
                BorderBrush = Edge,
                BorderThickness = new Thickness(2),
                Padding = new Thickness(18, 2, 18, 2),
                Child = new TextBlock
                {
                    Text = title,
                    Foreground = Text,
                    FontWeight = FontWeights.Bold,
                    FontSize = 15,
                    HorizontalAlignment = HorizontalAlignment.Center,
                },
            });
        ((Border)stack.Children[0]).Margin = new Thickness(0, 0, 0, 6);
        foreach (var (text, run) in items) stack.Children.Add(MenuItem(text, run));

        return new Border
        {
            Background = MenuBack,
            BorderBrush = Edge,
            BorderThickness = new Thickness(3),
            Padding = new Thickness(6),
            Child = stack,
        };
    }

    /// <summary>창 아래쪽에 두는 단추(결정·중단 따위).</summary>
    public static Border PushButton(string text, Action? run, double width = 110)
    {
        var b = new Border
        {
            Width = width,
            Background = ItemFill,
            BorderBrush = ItemEdge,
            BorderThickness = new Thickness(2),
            Margin = new Thickness(10, 0, 10, 0),
            Padding = new Thickness(0, 2, 0, 2),
            Cursor = run != null ? Cursors.Hand : Cursors.Arrow,
            Child = new TextBlock
            {
                Text = text,
                Foreground = run != null ? Brushes.Black : Brushes.Gray,
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center,
            },
        };
        if (run != null)
        {
            // 명령 창 줄과 같은 까닭으로 누름도 삼킨다(창 끌기에 먹히지 않게).
            b.MouseLeftButtonDown += (_, e) => e.Handled = true;
            b.MouseLeftButtonUp += (_, e) => { e.Handled = true; run(); };
        }
        return b;
    }

    /// <summary>
    /// 제목 줄을 잡아 창을 옮길 수 있게 한다. 제목 줄이 없는 창(<c>WindowStyle.None</c>)이라
    /// 이렇게 붙여 줘야 옮길 데가 생긴다.
    /// </summary>
    public static void EnableDrag(Window window, UIElement handle)
    {
        handle.MouseLeftButtonDown += (_, _) =>
        {
            // 누르자마자 뗀 경우 DragMove 가 터진다. 아직 눌려 있을 때만 부른다.
            if (Mouse.LeftButton == MouseButtonState.Pressed) window.DragMove();
        };
    }

    /// <summary>
    /// 이 창을 옮기면 딸린 창(<see cref="Window.OwnedWindows"/>)도 같은 만큼 따라 옮긴다.
    /// </summary>
    /// <remarks>
    /// 게임에서는 도시 그림도 커맨드 창도 지도 안에 그려진 것이라 지도가 움직이면 함께
    /// 움직인다. 우리는 D3D 자식 창 위에 제대로 띄우려고 창(HWND)을 따로 쓰므로
    /// (<see cref="CityPicDialog"/> 참고) 그 값을 손으로 붙여 준다.
    ///
    /// 딸린 창에도 이것을 걸어 두면 사슬로 이어진다 — 함대 창을 옮기면 도시 그림이 따라오고,
    /// 그 그림이 옮겨지면 다시 그 옆의 커맨드 창이 따라온다.
    ///
    /// 최대화·최소화로 바뀌는 자리까지 따라가면 딸린 창이 엉뚱한 데로 튄다. 보통 상태일
    /// 때만 옮기고 그 밖에는 기준만 다시 잡는다.
    /// </remarks>
    public static void CarryOwnedWindows(Window window)
    {
        // 아직 안 뜬 창은 Left/Top 이 NaN 이다. 첫 자리를 잡을 때 기준이 채워진다.
        double lastLeft = window.Left, lastTop = window.Top;

        window.LocationChanged += (_, _) =>
        {
            double left = window.Left, top = window.Top;
            double dx = left - lastLeft, dy = top - lastTop;
            lastLeft = left;
            lastTop = top;

            if (window.WindowState != WindowState.Normal) return;
            if (double.IsNaN(dx) || double.IsNaN(dy) || (dx == 0 && dy == 0)) return;

            // 옮기는 사이에 목록이 바뀔 수 있다(창이 닫히는 따위) — 베껴 두고 돈다.
            foreach (var owned in window.OwnedWindows.Cast<Window>().ToArray())
            {
                if (double.IsNaN(owned.Left) || double.IsNaN(owned.Top)) continue;
                owned.Left += dx;
                owned.Top += dy;
            }
        };
    }

    /// <summary>
    /// 게임 원본 조각으로 지은 제목 상자. 조각을 못 읽으면 null 이라 부르는 쪽이 물러설 수 있다.
    /// </summary>
    /// <remarks>
    /// 왼쪽 마구리 · 가운데(옆으로 이어 깔기) · 오른쪽 마구리 셋으로 짓는다. 덩굴 무늬는
    /// 손으로 찍은 도트라 벡터로 그리면 확대할 때 매끄러워져 게임 맛이 죽는다 — 그래서
    /// <see cref="UiSprites"/> 로 원본을 그대로 꺼내 쓰고, 늘릴 때 섞지 않는다.
    /// </remarks>
    /// <summary>
    /// 게임 폴더에서 읽은 화면 조각. 게임 폴더를 알게 되면 한 번 넣어 둔다 —
    /// 제목 줄이 있는 창들이 다 같이 쓴다. 못 읽었으면 null 이고 그때는 민색 상자로 물러선다.
    /// </summary>
    public static UiSprites? Sprites { get; set; }

    /// <summary>
    /// 제목 상자 그림. <c>asset/ui</c> 에 있으면 그것을 쓴다 — 손으로 다듬은 것이 이긴다.
    /// 없으면 게임 CDS 에서 읽은 것으로 물러선다. 한 번 읽고 들고 있는다.
    /// </summary>
    private static BitmapSource? _titleBox;
    private static bool _titleBoxTried;

    private static BitmapSource? TitleBox()
    {
        if (_titleBox != null || _titleBoxTried) return _titleBox;
        _titleBoxTried = true;

        // 손으로 다듬어 둔 것만 쓴다. 게임 CDS 에서 바로 꺼내 쓰지는 않는다 —
        // 파트 4 의 상자들은 가운데까지 무늬가 꽉 차 있어 제목 줄로 늘리면 글씨가 묻힌다.
        // 개발 창의 "MISC.CDS 뽑기" 로 다 뽑아 놓고, 쓸 것을 골라 다듬어 이 이름으로 두면 된다.
        var path = Path.Combine(AppContext.BaseDirectory, "asset", "ui", "title-box.png");
        if (!File.Exists(path)) return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(path);
            bmp.CacheOption = BitmapCacheOption.OnLoad;   // 파일을 잡고 있지 않게
            bmp.EndInit();
            bmp.Freeze();
            return _titleBox = bmp;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GameUi] title-box.png 읽기 실패: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 제목 상자의 테 두께(그림 점). 이 안쪽은 늘어나고 바깥은 그대로 있는다.
    /// </summary>
    private const int SliceX = 6, SliceY = 5;

    /// <summary>
    /// 게임 원본 조각을 9-슬라이스로 늘려 지은 제목 상자.
    /// </summary>
    /// <remarks>
    /// 조각 하나가 이미 <b>완성된 작은 상자</b>다(16x24). 그래서 마구리를 이어 붙이는 것이
    /// 아니라 아홉 칸으로 갈라 늘려야 한다 — 네 귀는 그대로 두고, 변은 한 방향으로만,
    /// 가운데는 두 방향으로 이어 깐다. 늘릴 때 섞으면 도트가 뭉개지므로 안 섞는다.
    /// </remarks>
    public static Border? TitleFrame(UiSprites? sprites, string title, int scale = 2,
                                     Action? onClose = null)
    {
        var box = TitleBox();
        if (box == null) return null;

        int bw = box.PixelWidth, bh = box.PixelHeight;
        if (bw <= SliceX * 2 || bh <= SliceY * 2) return null;

        int midW = bw - SliceX * 2, midH = bh - SliceY * 2;

        Border Cell(int x, int y, int w, int h, bool tileX, bool tileY)
        {
            var crop = new CroppedBitmap(box, new Int32Rect(x, y, w, h));
            crop.Freeze();
            var brush = new ImageBrush(crop) { Stretch = Stretch.Fill };
            if (tileX || tileY)
            {
                brush.TileMode = TileMode.Tile;
                brush.ViewportUnits = BrushMappingMode.Absolute;
                brush.Viewport = new Rect(0, 0, w * scale, h * scale);
            }
            RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.NearestNeighbor);
            RenderOptions.SetEdgeMode(brush, EdgeMode.Aliased);
            brush.Freeze();
            return new Border { Background = brush };
        }

        var grid = new Grid { Height = bh * scale };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(SliceX * scale) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(SliceX * scale) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(SliceY * scale) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(SliceY * scale) });

        void Put(Border b, int col, int row) { Grid.SetColumn(b, col); Grid.SetRow(b, row); grid.Children.Add(b); }

        Put(Cell(0, 0, SliceX, SliceY, false, false), 0, 0);                       // 왼위
        Put(Cell(SliceX, 0, midW, SliceY, true, false), 1, 0);                     // 위
        Put(Cell(bw - SliceX, 0, SliceX, SliceY, false, false), 2, 0);             // 오위
        Put(Cell(0, SliceY, SliceX, midH, false, true), 0, 1);                     // 왼
        Put(Cell(SliceX, SliceY, midW, midH, true, true), 1, 1);                   // 가운데
        Put(Cell(bw - SliceX, SliceY, SliceX, midH, false, true), 2, 1);           // 오
        Put(Cell(0, bh - SliceY, SliceX, SliceY, false, false), 0, 2);             // 왼아래
        Put(Cell(SliceX, bh - SliceY, midW, SliceY, true, false), 1, 2);           // 아래
        Put(Cell(bw - SliceX, bh - SliceY, SliceX, SliceY, false, false), 2, 2);   // 오아래

        // 글씨는 상자 전체 위에 얹는다 — 마구리를 넘어가도 가운데에 오게.
        var label = new TextBlock
        {
            Text = title,
            Foreground = Text,
            FontWeight = FontWeights.Bold,
            FontSize = 15,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumnSpan(label, 3);
        Grid.SetRowSpan(label, 3);
        grid.Children.Add(label);

        if (onClose != null)
        {
            var close = new Border
            {
                Background = ItemFill,
                BorderBrush = ItemEdge,
                BorderThickness = new Thickness(2),
                Padding = new Thickness(5, 0, 5, 0),
                Margin = new Thickness(0, 3, 5, 3),
                Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "닫기",
                Child = new TextBlock
                {
                    Text = "✕",
                    Foreground = Brushes.Black,
                    FontWeight = FontWeights.Bold,
                    FontSize = 12,
                },
            };
            close.MouseLeftButtonDown += (_, e) => e.Handled = true;   // 제목 줄 끌기에 안 먹히게
            close.MouseLeftButtonUp += (_, e) => { e.Handled = true; onClose(); };
            Grid.SetColumn(close, 2);
            Grid.SetRowSpan(close, 3);
            grid.Children.Add(close);
        }

        return new Border { Child = grid };
    }

    /// <summary>
    /// 게임의 상단 띠 액자. 그림을 잘라 늘리지 않고 <see cref="FrameArt"/> 로 그때그때 그린다.
    /// </summary>
    /// <remarks>
    /// 잘라 쓰면 늘릴 때 이음매가 보이고 크기마다 조각을 따로 떠야 한다. 무늬가 규칙적이라
    /// 그릴 수 있으므로 띠 크기가 바뀔 때마다 그 크기로 다시 그린다.
    ///
    /// 안의 것은 테 두께만큼 안으로 들여 놓는다.
    /// </remarks>
    /// <summary>
    /// 띠 안에 놓는 밝은 상자. 게임 날짜 칸처럼 테에 구슬 무늬가 있고 속이 반짝인다.
    /// </summary>
    public static Grid CellFrame(UIElement content)
    {
        var host = new Grid();
        var back = new Border();
        host.Children.Add(back);
        host.Children.Add(new Border
        {
            Margin = new Thickness(FrameArt.CellBorder, 0, FrameArt.CellBorder, 0),
            Child = content,
        });

        host.SizeChanged += (_, _) =>
        {
            var art = FrameArt.DrawCell((int)Math.Round(host.ActualWidth),
                                        (int)Math.Round(host.ActualHeight));
            if (art == null) return;
            var brush = new ImageBrush(art)
            {
                Stretch = Stretch.None,
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top,
            };
            RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.NearestNeighbor);
            RenderOptions.SetEdgeMode(brush, EdgeMode.Aliased);
            brush.Freeze();
            back.Background = brush;
        };
        return host;
    }

    /// <summary>띠 속(테 안쪽)이 적어도 이만큼은 되어야 한다.</summary>
    private const int BarInside = 15;

    public static Grid? BarFrame(UIElement content, bool thin = true)
    {
        int border = thin ? FrameArt.ThinBorder : FrameArt.Border;

        // 액자가 안의 것에 끌려 쪼그라들지 않게 바닥 높이를 정해 둔다. 테 두 겹에 속이 들어갈
        // 만큼은 있어야 액자로 보인다 — 안 그러면 칸 높이가 곧 띠 높이가 되어 테가 사라진다.
        var host = new Grid { MinHeight = border * 2 + BarInside };

        var back = new Border();
        host.Children.Add(back);

        // 안의 것은 액자 <b>위에</b> 얹는다 — 속에 넣지 않는다.
        // 게임도 그렇다. 칸을 액자 속에 넣으면 테가 두 겹으로 겹쳐 글씨 자리가 좁아지고
        // 읽기 나빠진다. 칸이 액자 테를 가리고 올라앉는 것이 맞다.
        // 위아래로 한 칸씩 띄워 액자 바깥 선은 남겨 둔다.
        if (content is FrameworkElement fe) fe.Margin = new Thickness(0, 1, 0, 1);
        host.Children.Add(content);

        void Redraw()
        {
            var art = FrameArt.Draw((int)Math.Round(host.ActualWidth),
                                    (int)Math.Round(host.ActualHeight), thin);
            if (art == null) return;

            // 도트 그림이라 1:1 로 놓는다 — 늘리거나 섞으면 결이 뭉개진다.
            var brush = new ImageBrush(art)
            {
                Stretch = Stretch.None,
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top,
            };
            RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.NearestNeighbor);
            RenderOptions.SetEdgeMode(brush, EdgeMode.Aliased);
            brush.Freeze();
            back.Background = brush;
        }

        host.SizeChanged += (_, _) => Redraw();
        return host;
    }


    /// <summary>초점 표시가 오가는 두 색. 게임도 이 둘을 번갈아 보인다.</summary>
    public static readonly Color FocusLight = Color.FromRgb(0xEC, 0xE4, 0xD2);
    public static readonly Color FocusDark = Color.FromRgb(0x14, 0x0C, 0x0A);

    /// <summary>초점이 깜빡이는 참. 0.5초마다 색이 바뀐다.</summary>
    public static readonly TimeSpan FocusBlink = TimeSpan.FromSeconds(0.5);

    /// <summary>
    /// 초점이 간 것을 알리는 깜빡임. 밝은 색과 검은색을 0.5초마다 갈아 낸다.
    /// </summary>
    /// <remarks>
    /// 색을 서서히 섞지 않고 딱딱 바꾸는 것이 요령이라 <see cref="DiscreteColorKeyFrame"/>
    /// 을 쓴다 — <c>ColorAnimation</c> 은 스며들듯 바뀌어 게임 맛이 안 난다.
    /// </remarks>
    public static void StartBlink(SolidColorBrush brush)
    {
        var blink = new ColorAnimationUsingKeyFrames
        {
            Duration = new Duration(FocusBlink + FocusBlink),
            RepeatBehavior = RepeatBehavior.Forever,
        };
        blink.KeyFrames.Add(new DiscreteColorKeyFrame(FocusLight, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        blink.KeyFrames.Add(new DiscreteColorKeyFrame(FocusDark, KeyTime.FromTimeSpan(FocusBlink)));
        brush.BeginAnimation(SolidColorBrush.ColorProperty, blink);
    }

    /// <summary>깜빡임을 멎고 테를 감춘다.</summary>
    public static void StopBlink(SolidColorBrush brush)
    {
        brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
        brush.Color = Colors.Transparent;
    }

    /// <summary>
    /// 한 창 안에서 초점이 오가는 단추 묶음. 방향키로 옮기고 엔터로 고른다.
    /// </summary>
    /// <remarks>
    /// 게임은 초점이 간 단추의 <b>안쪽 테</b>를 깜빡여 지금 고른 것을 알린다. 그래서 단추마다
    /// 테를 한 겹 더 두고 그 색만 움직인다.
    /// </remarks>
    public sealed class FocusGroup
    {
        private readonly List<(Border Item, SolidColorBrush Ring, Action Run)> _items = [];
        private int _index = -1;

        /// <summary>단추 하나를 만들어 묶음에 넣는다.</summary>
        public Border Add(string text, Action run, double width = 110)
        {
            var label = new TextBlock
            {
                Text = text,
                Foreground = Brushes.Black,
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center,
            };

            var ring = new SolidColorBrush(Colors.Transparent);
            var inner = new Border
            {
                BorderBrush = ring,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(0, 1, 0, 1),
                Child = label,
            };

            var item = new Border
            {
                Width = width,
                Background = ItemFill,
                BorderBrush = ItemEdge,
                BorderThickness = new Thickness(2),
                Margin = new Thickness(10, 0, 10, 0),
                Padding = new Thickness(1),
                Cursor = Cursors.Hand,
                Child = inner,
            };

            int index = _items.Count;
            _items.Add((item, ring, run));

            item.MouseEnter += (_, _) => Focus(index);
            // 누름은 삼킨다 — 창 끌기(DragMove)가 먼저 걸리면 뗌이 안 온다.
            item.MouseLeftButtonDown += (_, e) => e.Handled = true;
            item.MouseLeftButtonUp += (_, e) => { e.Handled = true; run(); };

            if (_items.Count == 1) Focus(0);   // 첫 단추에 초점을 두고 시작한다
            return item;
        }

        /// <summary>그 단추로 초점을 옮긴다.</summary>
        public void Focus(int index)
        {
            if (index < 0 || index >= _items.Count || index == _index) return;
            if (_index >= 0 && _index < _items.Count) StopBlink(_items[_index].Ring);
            _index = index;
            StartBlink(_items[index].Ring);
        }

        /// <summary>방향키·엔터를 받는다. 처리했으면 true.</summary>
        public bool HandleKey(Key key)
        {
            if (_items.Count == 0) return false;
            switch (key)
            {
                case Key.Left or Key.Up:
                    Focus((_index - 1 + _items.Count) % _items.Count);
                    return true;
                case Key.Right or Key.Down:
                    Focus((_index + 1) % _items.Count);
                    return true;
                case Key.Enter or Key.Space:
                    if (_index >= 0) _items[_index].Run();
                    return true;
                default:
                    return false;
            }
        }
    }

    /// <summary>
    /// 두 색을 바둑판으로 섞은 무늬. 게임 그림의 중간색은 다 이렇게 나 있다.
    /// </summary>
    /// <remarks>
    /// 게임은 색인 팔레트(256색)를 쓰는데, 팔레트에 없는 중간색이 필요하면 이웃한 두 색을
    /// 한 점씩 번갈아 찍어 눈에서 섞이게 한다. 건물 이름표 바탕이 그렇게 되어 있다 —
    /// <see cref="GamePalette"/> 의 낮은 색인 값을 그림에서 되짚을 때 쓴 성질이 이것이다.
    ///
    /// WPF 로는 2x2 짜리 그림 하나를 타일로 깔면 된다. 도트가 뭉개지지 않게 늘릴 때 섞지 않고
    /// (<see cref="BitmapScalingMode.NearestNeighbor"/>) 테두리도 안 다듬는다
    /// (<see cref="EdgeMode.Aliased"/>). <paramref name="cell"/> 은 한 칸의 크기다 —
    /// 도시 그림이 정수배로 커지므로 이름표 무늬도 같은 배로 키워야 결이 맞는다.
    /// </remarks>
    public static Brush Dither(Color a, Color b, int cell = 2)
    {
        // 2x2 한 장 — 대각선으로 두 색이 엇갈린다.
        var bmp = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null,
            new[] { Pack(a), Pack(b), Pack(b), Pack(a) }, 2 * 4);
        bmp.Freeze();

        var brush = new ImageBrush(bmp)
        {
            TileMode = TileMode.Tile,
            ViewportUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, 2 * cell, 2 * cell),
            Stretch = Stretch.Fill,
        };
        RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(brush, EdgeMode.Aliased);
        brush.Freeze();
        return brush;
    }

    private static uint Pack(Color c) =>
        (uint)(c.A << 24 | c.R << 16 | c.G << 8 | c.B);

    /// <summary>이름표 바탕에 깔리는 무늬. 짙은 밤색 두 가지를 섞었다.</summary>
    private static readonly Brush TagFill =
        Dither(Color.FromRgb(0x5A, 0x2E, 0x2A), Color.FromRgb(0x3E, 0x1E, 0x1C));

    /// <summary>건물 위에 커서를 올렸을 때 밑에 붙는 이름표.</summary>
    /// <remarks>
    /// 게임 것은 바탕이 민색이 아니라 두 색을 바둑판으로 섞은 무늬다(<see cref="Dither"/>).
    /// 글씨는 그 위에 밝은 색으로 얹힌다.
    /// </remarks>
    public static Border NameTag(string text) => new()
    {
        Background = TagFill,
        BorderBrush = Edge,
        BorderThickness = new Thickness(2),
        Padding = new Thickness(8, 1, 8, 1),
        Visibility = Visibility.Collapsed,
        Child = new TextBlock
        {
            Text = text,
            Foreground = Text,
            FontWeight = FontWeights.Bold,
            FontSize = 14,
        },
    };
}
