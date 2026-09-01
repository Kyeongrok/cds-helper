using System.IO;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;
using CdsHelper.Support.Local.Settings;
using CdsHelper.Game.Local.Settings;

namespace CdsHelper.Game.UI.Views;

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
        var framed = TitleFrame(Sprites, title, 1, onClose);
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

    /// <summary>글자 한 줄이 앉는 높이(그림 점). 게임 한글 글리프 14 에 위아래 한 점씩.</summary>
    public const int ItemTextHeight = 16;

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
    /// 남회색 정보 창(도시정보·아이템·교역품)을 두르는 테. 게임은 <b>짙은 선 셋</b>을
    /// 바탕색 사이에 끼워 두른다 — 밝은 선이 아니다.
    /// </summary>
    /// <remarks>
    /// 게임 화면(런던 도시정보 창)의 왼쪽 테를 픽셀로 재어 옮겼다. 바깥부터
    /// <c>짙은 선 2 · 바탕 3 · 짙은 선 2 · 바탕 5 · 짙은 선 2</c> 차례다.
    /// <code>
    ///   x= 4  #242629  ┐ 짙은 선
    ///   x= 5  #212734  ┘
    ///   x= 6  #556789  ┐
    ///   x= 7  #5C6F93  │ 바탕
    ///   x= 8  #556789  ┘
    ///   x= 9  #212734  ┐ 짙은 선
    ///   x=10  #141820  ┘
    ///   …
    ///   x=16  #212835  ┐ 짙은 선
    ///   x=17  #141820  ┘
    ///   x=18  안쪽
    /// </code>
    /// 한동안 <b>밝은 선 둘</b>로 그려 두었는데 그것은 창을 도드라지게 하는 요즘 투라 게임과
    /// 사뭇 달라 보였다. 게임은 거꾸로 어두운 선으로 홈을 파듯 두른다.
    /// </remarks>
    public static Border InfoFrame(UIElement content, Brush back) => InfoFrame(content, back, InfoLine);

    /// <summary>테 선 색까지 골라 두르는 갈래. 보급 화면처럼 밤색 판에 쓴다.</summary>
    public static Border InfoFrame(UIElement content, Brush back, Brush line)
    {
        // 안쪽 선 — 글이 놓이는 자리를 두른다.
        var inner = Line(content, InnerGap);
        // 가운데 선.
        var middle = Line(inner, MiddleGap);
        // 바깥 선. 바탕은 여기서 한 번만 칠한다.
        var outer = Line(middle, 0);
        outer.Background = back;
        return outer;

        Border Line(UIElement child, double gap) => new()
        {
            BorderBrush = line,
            BorderThickness = new Thickness(LineWidth),
            Margin = new Thickness(gap),
            Child = child,
        };
    }

    /// <summary>테 한 줄의 굵기.</summary>
    /// <remarks>
    /// 게임 갈무리에서 잰 값이다. 보급 화면의 왼쪽 테를 화면 점으로 세면
    /// <c>검정 2 · 바탕 3 · 검정 2 · 바탕 5 · 검정 2</c> 인데, 그 갈무리 배율이 1.74 라
    /// 게임 점으로는 <b>1 · 2 · 1 · 3 · 1</b> 이고 모두 <b>8점</b>이다.
    ///
    /// 예전에는 <c>2 · 3 · 2 · 4 · 2</c>(13점)로 두어 테가 굵고 줄 사이가 벌어졌다 —
    /// 게임 것은 훨씬 촘촘해서 세 줄이 거의 붙어 보인다.
    /// </remarks>
    private const double LineWidth = 1;

    /// <summary>테 줄 사이의 빈칸 — 바깥쪽이 좁고 안쪽이 넓다.</summary>
    private const double MiddleGap = 2, InnerGap = 3;

    /// <summary>정보 창 테의 짙은 선. 게임 화면에서 뽑았다.</summary>
    public static readonly Brush InfoLine = Frozen(Color.FromRgb(0x14, 0x18, 0x20));

    /// <summary>남회색 정보 창의 바탕. 게임 화면에서 뽑았다(#5C6F93).</summary>
    public static readonly Brush InfoBack = Frozen(Color.FromRgb(0x5C, 0x6F, 0x93));

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    /// <summary>창 겹테의 검은 줄. 게임 갈무리에서 뽑았다(#110505).</summary>
    public static readonly Brush EdgeDark = Frozen(Color.FromRgb(0x11, 0x05, 0x05));

    /// <summary>
    /// 게임 창을 두르는 <b>겹테</b>. 갈무리를 점 단위로 재어 그대로 옮겼다 —
    /// 밖에서 안으로 <c>검은 줄 1 · 밤색 2 · 검은 줄 1 · 밤색 4</c> 다.
    /// </summary>
    /// <remarks>
    /// 예전에는 창마다 <c>Margin 4 + 밝은 테 2</c> 로 둘렀는데, 그러면 겉테가 두껍고
    /// <b>밝은 선이 바깥</b>에 서서 게임 것과 딴판이 된다. 게임의 밝은 선은 겉이 아니라
    /// 양피지 판 바로 둘레에 있다(그 선은 <see cref="GameList"/> 가 두른다).
    /// </remarks>
    public static Border DialogEdge(UIElement content) => new()
    {
        Background = Back,
        BorderBrush = EdgeDark,
        BorderThickness = new Thickness(1),
        Child = new Border
        {
            Margin = new Thickness(2),
            BorderBrush = EdgeDark,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4),
            Child = content,
        },
    };

    /// <summary>
    /// 제목 줄을 잡아 창을 옮길 수 있게 한다. 제목 줄이 없는 창(<c>WindowStyle.None</c>)이라
    /// 이렇게 붙여 줘야 옮길 데가 생긴다.
    /// </summary>
    /// <summary>
    /// 게임 그림을 <b>화면 점 하나에 그림 점 하나</b>로 놓을 배율.
    /// </summary>
    /// <remarks>
    /// WPF 는 자리를 <b>DIP</b>(96분의 1인치)로 잰다. 화면 배율이 175%면 1 DIP 가
    /// 화면 점 1.75개라, 그림을 그냥 놓으면 1.75배로 부풀어 점이 뭉갠다. 배율로
    /// 나눠 주면 그림 점 하나가 화면 점 하나에 딱 떨어진다.
    ///
    /// <paramref name="zoom"/> 은 <b>화면 점</b> 단위의 곱이다 — 1 이면 원본 크기,
    /// 2 면 화면에서 두 배다. 정수로만 줘야 점이 안 뭉갠다.
    /// </remarks>
    public static double PixelZoom(Visual visual, int zoom = 1)
    {
        double scale = VisualTreeHelper.GetDpi(visual).DpiScaleX;
        return scale > 0 ? zoom / scale : zoom;
    }

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
    /// (<see cref="CityPicView"/> 참고) 그 값을 손으로 붙여 준다.
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
    /// <summary>
    /// 게임 폴더에서 읽은 화면 조각. 게임 폴더를 알게 되면 한 번 넣어 둔다 —
    /// 제목 줄이 있는 창들이 다 같이 쓴다. 못 읽었으면 null 이고 그때는 민색 상자로 물러선다.
    /// </summary>
    public static UiSprites? Sprites { get; set; }

    /// <summary>
    /// 게임 폴더에서 읽은 비트맵 글꼴. 게임 폴더를 알게 되면 한 번 넣어 둔다.
    /// 못 읽었으면 null 이고 그때는 윈도 글꼴로 물러선다.
    /// </summary>
    /// <remarks>
    /// 넣는 순간 이미 지어진 글자 칸들이 다시 찍는다(<see cref="GameLabel"/>). 상단 띠는
    /// 게임 폴더를 알기 전에 지어지는데, 값이 바뀔 때만 다시 찍게 두면 "설정" 처럼 한 번
    /// 적고 마는 칸이 윈도 글꼴로 남아 띠 안에서 글꼴이 섞였다.
    /// </remarks>
    public static GameFont? Font
    {
        get => _font;
        set
        {
            _font = value;
            GameLabel.RedrawAll();
        }
    }

    private static GameFont? _font;

    /// <summary>
    /// 게임 비트맵 글꼴로 찍는 글자 칸. 글이 바뀔 때마다 다시 찍는다 — 상단 띠처럼 값이
    /// 계속 도는 자리에 쓴다(<see cref="GameFontLabel"/> 은 한 번 찍고 마는 것이다).
    /// </summary>
    /// <remarks>
    /// 글꼴은 게임 폴더를 알아야 열리는데 띠는 그 전에 지어진다. 그래서 찍을 때마다
    /// <see cref="Font"/> 를 다시 보고, 아직 없으면 윈도 글꼴로 물러선 채 둔다 —
    /// 글꼴이 들어오면 다음 번 값이 바뀔 때 저절로 게임 글꼴로 갈아탄다.
    /// </remarks>
    public sealed class GameLabel : Border
    {
        private readonly byte _color;
        private readonly int _height;
        private readonly Image _image = new()
        {
            Stretch = Stretch.Fill,
            VerticalAlignment = VerticalAlignment.Center,
        };
        private TextBlock? _fallback;
        private string _text = "";
        private Brush _fallbackBrush;

        /// <summary>
        /// 게임 글꼴을 못 읽어 윈도 글꼴로 물러설 때의 글씨색. 어두운 바탕에 놓는 칸은
        /// 검정으로 두면 안 보이므로 부르는 쪽이 맞춰 준다.
        /// </summary>
        public Brush FallbackBrush
        {
            get => _fallbackBrush;
            set
            {
                _fallbackBrush = value;
                if (_fallback != null) _fallback.Foreground = value;
            }
        }

        /// <summary>
        /// 글자를 굵게 보이게 할지. 게임은 오른쪽 아래로 한 점 겹쳐 찍어 굵기를 낸다 —
        /// 상단 띠의 날짜·소지금 칸이 그렇다.
        /// </summary>
        public bool Bold { get; init; }

        public GameLabel(byte color = GameFont.ButtonColor, int height = ItemTextHeight)
        {
            _color = color;
            _height = height;
            _fallbackBrush = PaletteBrush(color);
            RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.NearestNeighbor);
            RenderOptions.SetEdgeMode(_image, EdgeMode.Aliased);
            VerticalAlignment = VerticalAlignment.Center;
            lock (Living) Living.Add(new WeakReference<GameLabel>(this));
        }

        /// <summary>
        /// 지어 둔 글자 칸들. 글꼴이 들어오면 다시 찍어야 해서 들고 있는다 — 약한 참조라
        /// 창이 닫히면 그대로 걷힌다.
        /// </summary>
        private static readonly List<WeakReference<GameLabel>> Living = [];

        /// <summary>살아 있는 글자 칸을 다 다시 찍는다. 걷힌 것은 목록에서 뺀다.</summary>
        internal static void RedrawAll()
        {
            lock (Living)
                for (int i = Living.Count - 1; i >= 0; i--)
                    if (Living[i].TryGetTarget(out var label)) label.Redraw();
                    else Living.RemoveAt(i);
        }

        /// <summary>
        /// 그 색인의 게임 색. 윈도 글꼴로 물러설 때에도 글씨색은 게임 것으로 둔다 —
        /// 검정으로 두면 게임 글꼴로 찍힌 옆 칸과 색이 어긋난다(띠 글씨는 <c>341C14</c> 다).
        /// </summary>
        private static Brush PaletteBrush(byte color)
        {
            int i = color * 3;
            var brush = new SolidColorBrush(Color.FromRgb(GamePalette.Rgb[i],
                                                          GamePalette.Rgb[i + 1],
                                                          GamePalette.Rgb[i + 2]));
            brush.Freeze();
            return brush;
        }

        public string Text
        {
            get => _text;
            set
            {
                if (_text == value) return;
                _text = value;
                Redraw();
            }
        }

        private void Redraw()
        {
            // 굵게 할 때만 한 점 겹쳐 찍는다. 겹치는 색은 본 글자와 같게 둬야 획만 굵어지고
            // 그림자가 따로 지지 않는다.
            var font = Font;
            if (font != null)
            {
                var bgra = font.Render(_text, _color, Bold, _color, _height, out int w);
                if (bgra != null && w > 0)
                {
                    var bmp = BitmapSource.Create(w, _height, 96, 96,
                                                  PixelFormats.Bgra32, null, bgra, w * 4);
                    bmp.Freeze();
                    _image.Source = bmp;
                    _image.Width = w;
                    _image.Height = _height;
                    Child = _image;
                    return;
                }
            }

            _fallback ??= new TextBlock
            {
                Foreground = _fallbackBrush,
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center,
            };
            _fallback.Text = _text;
            Child = _fallback;
        }
    }

    /// <summary>
    /// 게임 글꼴로 찍은 글자. 띠 위에 겹쳐 놓는다. 글꼴이 없거나 찍을 게 없으면 null.
    /// </summary>
    /// <remarks>
    /// 글자가 <see cref="UiSprites.BandHeight"/> 안에서 세로 가운데로 오게 찍고, 통째로
    /// <paramref name="scale"/> 배 키운다. 늘릴 때 섞으면 획이 흐려지므로 안 섞는다.
    /// </remarks>
    public static Image? GameFontLabel(string text, byte color, int scale,
                                        int height = UiSprites.BandHeight, bool shadow = true)
    {
        if (Font == null) return null;
        var bgra = Font.Render(text, color, shadow, GameFont.ShadowColor, height, out int w);
        if (bgra == null || w <= 0) return null;

        var bmp = BitmapSource.Create(w, height, 96, 96,
                                      PixelFormats.Bgra32, null, bgra, w * 4);
        bmp.Freeze();

        var image = new Image
        {
            Source = bmp,
            Width = w * scale,
            Height = height * scale,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,        // 제목 줄 끌기를 가리지 않게
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(image, EdgeMode.Aliased);
        return image;
    }

    /// <summary>
    /// 게임 원본 조각으로 지은 제목 띠. 조각을 못 읽으면 null 이라 부르는 쪽이 물러설 수 있다.
    /// </summary>
    /// <remarks>
    /// <b>왼끝(16) · 가운데(8, 이어 깔기) · 오른끝(16)</b> 셋을 늘어놓는다 — 게임이 하는
    /// 그대로다(<see cref="UiSprites"/>). 한 장을 9-슬라이스로 늘리는 것이 아니다.
    /// 가운데만 이어 깔면 어떤 폭에도 맞고 도트도 안 뭉개진다.
    ///
    /// 띠 높이는 늘 24점이라 <paramref name="scale"/> 배만큼만 키운다. 늘릴 때 섞으면
    /// 손으로 찍은 덩굴 무늬가 매끄러워져 게임 맛이 죽으므로 안 섞는다.
    /// </remarks>
    public static Border? TitleFrame(UiSprites? sprites, string title, int scale = 1,
                                     Action? onClose = null) =>
        BandFrame(sprites, BandStyle.Title, title, GameFont.TitleColor, shadow: true, scale, onClose);

    /// <summary>
    /// 띠 하나를 짓고 그 위에 글자를 얹는다. 제목 띠와 버튼이 같은 길을 쓴다 —
    /// 무늬 벌만 다르다(<see cref="BandStyle"/>).
    /// </summary>
    public static Border? BandFrame(UiSprites? sprites, BandStyle style, string title,
                                    byte textColor, bool shadow, int scale, Action? onClose)
    {
        if (sprites == null) return null;

        // 띠를 한 장으로 그린다. 왼끝·가운데·오른끝을 WPF 칸 셋으로 나눠 붙이면 칸 경계에서
        // 세로 줄이 죽죽 생긴다 — 가운데 칸 폭이 8의 배수로 안 떨어져 타일이 잘리고,
        // 화면 배율에 따라 칸 경계가 정수 자리에 안 놓이기 때문이다.
        // 게임처럼 칸 수를 세어 통째로 찍으면 이음매가 아예 없다.
        // 그림은 <see cref="Image"/> 가 아니라 <b>배경 솔</b>로 깐다. Image 로 두면 그 그림
        // 크기가 다시 자리 계산에 먹혀 들어가, 넓어질수록 칸 수가 늘고 그래서 또 넓어지는
        // 되먹임이 생긴다(띠가 화면 끝까지 자란다). 배경 솔은 자리 계산에 끼어들지 않는다.
        var back = new Border();
        var grid = new Grid { Height = UiSprites.BandHeight * scale };
        grid.Children.Add(back);

        int drawn = -1;
        void Redraw(double width)
        {
            int cells = UiSprites.CellsFor(width / scale);
            if (cells == drawn) return;                 // 같은 칸 수면 다시 찍을 것 없다
            drawn = cells;

            var bgra = sprites.Band(style, cells, out int w);
            var bmp = BitmapSource.Create(w, UiSprites.BandHeight, 96, 96,
                                          PixelFormats.Bgra32, null, bgra, w * 4);
            bmp.Freeze();

            var brush = new ImageBrush(bmp) { Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.NearestNeighbor);
            RenderOptions.SetEdgeMode(brush, EdgeMode.Aliased);
            brush.Freeze();
            back.Background = brush;
        }

        grid.SizeChanged += (_, e) => Redraw(e.NewSize.Width);

        // 띠가 글자를 <b>마구리 바깥까지</b> 덮도록 최소 폭을 잡는다. 그냥 글자 폭에 맞추면
        // 양 끝 덩굴(마구리 16점씩)이 글자에 먹혀 좌우 여백이 사라지고, 조금만 길어도
        // 끝 글자가 잘린다. 게임 이름표도 이렇게 짓는다(<see cref="UiSprites.CellsAround"/>).
        //
        // 그리는 것만이 아니라 <b>자리도</b> 그만큼 잡아야 한다 — 띠는 배경 솔이라 자리
        // 계산에 안 끼어들어서, 최소 폭을 안 주면 칸이 글자 폭으로 좁아지고 띠가 그 안으로
        // 눌린다. 짧은 줄이 창 폭에 맞춰 늘어나며 다시 그려지는 것과 달리, 가장 긴 줄은
        // 제 폭이 곧 창 폭이라 늘어날 일이 없어 눌린 채로 남는다.
        double least = Math.Max(UiSprites.WidthFor(1),
                                GameSettings.BandPad * 2 + (Font?.TextWidth(title) ?? 0));
        grid.MinWidth = least * scale;
        Redraw(least * scale);

        // 글씨는 띠 전체 위에 얹는다 — 마구리를 넘어가도 가운데에 오게.
        // 게임 비트맵 글꼴을 읽었으면 그것으로 찍는다. 획 굵기까지 게임과 같아진다.
        FrameworkElement? label = GameFontLabel(title, textColor, scale,
                                                UiSprites.BandHeight, shadow);
        label ??= new TextBlock
        {
            Text = title,
            Foreground = Text,
            FontWeight = FontWeights.Bold,
            FontSize = 15,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumnSpan(label, 3);
        grid.Children.Add(label);

        if (onClose != null)
        {
            var close = CloseBox(onClose, scale);
            Grid.SetColumn(close, 2);
            Grid.SetRowSpan(close, 3);
            grid.Children.Add(close);
        }

        return new Border { Child = grid };
    }

    // ── 닫기(X) 단추 ────────────────────────────────────────────────────────

    /// <summary>닫기 단추 한 변. 게임 것도 띠(24) 안에 위아래 4씩을 남기고 이만큼이다.</summary>
    public const int CloseBoxSize = 16;

    /// <summary>
    /// 게임 창 오른쪽 위의 닫기(X) 단추. <b>MISC.CDS 에 그런 조각은 없다</b> —
    /// 게임이 그때그때 그리는 상자다. 그래서 여기서도 점을 찍어 짓는다.
    /// </summary>
    /// <remarks>
    /// 갈무리를 점 단위로 재어 옮겼다(구입 창의 X 단추, 16x16).
    /// <code>
    ///   테     한 줄  #A79787
    ///   위·왼  한 줄  #F7F6F5   ← 도드라져 보이게 하는 빛
    ///   속            #DECEBD
    ///   X      두 점 굵기의 빗금 둘   #311818
    /// </code>
    /// 예전에는 윈도 글꼴로 "✕" 를 찍은 상자였는데, 띠(24점)보다 키가 커서 아래가
    /// 잘렸다(조선소 창에서 그게 보였다).
    /// </remarks>
    public static FrameworkElement CloseBox(Action onClose, int scale = 1)
    {
        var image = new Image
        {
            Source = CloseArt,
            Width = CloseBoxSize * scale,
            Height = CloseBoxSize * scale,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4 * scale, 0),
            Cursor = Cursors.Hand,
            ToolTip = "닫기",
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(image, EdgeMode.Aliased);

        // 누름도 삼킨다 — 제목 줄 끌기가 먼저 걸리면 마우스를 잡아 버려 뗌이 안 온다.
        image.MouseLeftButtonDown += (_, e) => e.Handled = true;
        image.MouseLeftButtonUp += (_, e) => { e.Handled = true; onClose(); };
        return image;
    }

    /// <summary>
    /// 값을 두들겨 넣는 칸 옆의 <b>계산기 단추</b>. 원본 아이콘을 그대로 건다.
    /// </summary>
    /// <remarks>
    /// 조각을 못 읽었으면 "田" 글자 상자로 물러선다 — 예전에는 늘 그 글자였다.
    /// </remarks>
    public static FrameworkElement CalcButton(Action run, double size = UiSprites.IconWidth)
    {
        FrameworkElement box;
        if (GameIcon(UiSprites.IconCalc) is { } art)
        {
            art.Width = art.Height = size;
            box = art;
        }
        else
        {
            box = new Border
            {
                Width = size,
                Height = size,
                Background = ItemFill,
                BorderBrush = ItemEdge,
                BorderThickness = new Thickness(1),
                Child = new TextBlock
                {
                    Text = "田",
                    Foreground = Brushes.Black,
                    FontWeight = FontWeights.Bold,
                    FontSize = size * 0.7,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
        }

        box.Cursor = Cursors.Hand;
        box.VerticalAlignment = VerticalAlignment.Center;
        // 누름은 삼킨다 — 창 끌기가 먼저 걸리면 마우스를 잡아 버려 뗌이 안 온다.
        box.MouseLeftButtonDown += (_, e) => e.Handled = true;
        box.MouseLeftButtonUp += (_, e) => { e.Handled = true; run(); };
        return box;
    }

    private static readonly BitmapSource CloseArt = DrawCloseBox();

    private static BitmapSource DrawCloseBox()
    {
        const uint edge = 0xFFA79787, light = 0xFFF7F6F5, fill = 0xFFDECEBD, ink = 0xFF311818;
        const int n = CloseBoxSize, last = n - 1;

        var bgra = new uint[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
                bgra[y * n + x] = x == 0 || y == 0 || x == last || y == last ? edge
                                : x == 1 || y == 1 ? light
                                : fill;

        // 빗금 둘. 두 점 굵기라 한 줄마다 이웃 칸까지 함께 찍는다.
        for (int k = 3; k <= 12; k++)
            for (int t = 0; t < 2; t++)
            {
                bgra[k * n + Math.Min(k + t, last - 1)] = ink;          // ↘
                bgra[k * n + Math.Max(last - k + t - 1, 1)] = ink;      // ↙
            }

        var bmp = BitmapSource.Create(n, n, 96, 96, PixelFormats.Bgra32, null, bgra, n * 4);
        bmp.Freeze();
        return bmp;
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


    // ── 게임풍 굴림대와 액자 ────────────────────────────────────────────────

    /// <summary>굴림대 한 벌의 폭. 화살표 조각이 열여섯 점이라 그 폭에 맞춘다.</summary>
    public const double ScrollWidth = UiSprites.IconWidth;

    /// <summary>손잡이가 이보다 짧아지지는 않는다.</summary>
    private const double ThumbMin = 16;

    /// <summary>화살표 한 번에 굴러가는 만큼.</summary>
    private const double ScrollStep = 30;

    /// <summary>
    /// 게임풍 굴림대를 단 두루마리 칸. 윈도 굴림대는 숨기고 우리가 그린 것을 옆에 세운다.
    /// </summary>
    /// <remarks>
    /// 윈도 굴림대는 모양이 게임과 너무 다르다. 그렇다고 <c>ControlTemplate</c> 을 통째로
    /// 갈아 끼우기보다, 굴림대를 감추고 <b>화살표 조각(MISC.CDS 파트 3)</b>과 손잡이를
    /// 손으로 세우는 편이 짧고 손댈 데도 적다.
    ///
    /// 조각을 못 읽으면 화살표 자리는 빈 채로 두고 손잡이만 나온다 — 굴리는 데는 지장이 없다.
    /// </remarks>
    /// <param name="content">굴릴 것.</param>
    /// <param name="maxHeight">이 높이를 넘으면 굴린다.</param>
    public static FrameworkElement Scroller(FrameworkElement content, double maxHeight)
    {
        var view = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxHeight = maxHeight,
            Content = content,
        };

        var thumb = new Border
        {
            Background = ItemFill,
            BorderBrush = ItemEdge,
            BorderThickness = new Thickness(1),
            Width = ScrollWidth,
            Cursor = Cursors.Hand,
        };
        var trough = new Canvas { Background = MenuBack, Width = ScrollWidth };
        trough.Children.Add(thumb);

        var bar = new Grid { Width = ScrollWidth, Margin = new Thickness(2, 0, 0, 0) };
        bar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        bar.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        bar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var up = ScrollArrow(UiSprites.IconUp, () => view.ScrollToVerticalOffset(view.VerticalOffset - ScrollStep));
        var down = ScrollArrow(UiSprites.IconDown, () => view.ScrollToVerticalOffset(view.VerticalOffset + ScrollStep));
        Grid.SetRow(up, 0);
        Grid.SetRow(trough, 1);
        Grid.SetRow(down, 2);
        bar.Children.Add(up);
        bar.Children.Add(trough);
        bar.Children.Add(down);

        void Place()
        {
            double room = trough.ActualHeight;
            if (room <= 0 || view.ExtentHeight <= view.ViewportHeight)
            {
                bar.Visibility = Visibility.Collapsed;
                return;
            }
            bar.Visibility = Visibility.Visible;

            double height = Math.Max(ThumbMin, room * view.ViewportHeight / view.ExtentHeight);
            double at = view.ScrollableHeight <= 0
                ? 0
                : (room - height) * view.VerticalOffset / view.ScrollableHeight;
            thumb.Height = height;
            Canvas.SetTop(thumb, at);
        }

        view.ScrollChanged += (_, _) => Place();
        trough.SizeChanged += (_, _) => Place();

        // 손잡이 끌기 — 잡은 자리를 굴림 자리로 되돌려 놓는다.
        double grab = 0;
        thumb.MouseLeftButtonDown += (_, e) =>
        {
            grab = e.GetPosition(thumb).Y;
            thumb.CaptureMouse();
            e.Handled = true;
        };
        thumb.MouseMove += (_, e) =>
        {
            if (!thumb.IsMouseCaptured) return;
            double room = trough.ActualHeight - thumb.ActualHeight;
            if (room <= 0) return;
            double at = Math.Clamp(e.GetPosition(trough).Y - grab, 0, room);
            view.ScrollToVerticalOffset(at / room * view.ScrollableHeight);
        };
        thumb.MouseLeftButtonUp += (_, e) => { thumb.ReleaseMouseCapture(); e.Handled = true; };

        var host = new Grid();
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(view, 0);
        Grid.SetColumn(bar, 1);
        host.Children.Add(view);
        host.Children.Add(bar);
        return host;
    }

    /// <summary>굴림대 끝의 화살표 한 칸. 조각이 없으면 빈 칸이다.</summary>
    private static FrameworkElement ScrollArrow(int icon, Action run)
    {
        var box = new Border
        {
            Width = ScrollWidth,
            Height = UiSprites.IconHeight,
            Background = MenuBack,
            Cursor = Cursors.Hand,
            Child = GameIcon(icon),
        };

        box.MouseLeftButtonDown += (_, e) => e.Handled = true;
        box.MouseLeftButtonUp += (_, e) => { e.Handled = true; run(); };
        return box;
    }

    /// <summary>
    /// 게임 아이콘 한 장(16x16) — 화살표·계산기 따위. 조각을 못 읽었으면 null.
    /// </summary>
    /// <remarks>
    /// 계산기는 게임이 값을 두들겨 넣는 칸 옆에 붙이는 표시다. 예전에는 "田" 글자로
    /// 흉내내고 있었는데, 원본에 제 그림이 있다(<see cref="UiSprites.IconCalc"/>).
    /// </remarks>
    public static Image? GameIcon(int icon, int scale = 1)
    {
        if (Sprites?.Icon(icon) is not { } art) return null;

        var bmp = BitmapSource.Create(UiSprites.IconWidth, UiSprites.IconHeight, 96, 96,
                                      PixelFormats.Bgra32, null, art, UiSprites.IconWidth * 4);
        bmp.Freeze();

        var image = new Image
        {
            Source = bmp,
            Width = UiSprites.IconWidth * scale,
            Height = UiSprites.IconHeight * scale,
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(image, EdgeMode.Aliased);
        return image;
    }

    /// <summary>
    /// 게임 액자를 두른 창 속. 바깥은 구슬 무늬 테(<see cref="FrameArt"/>)고 속은 밤색이다.
    /// </summary>
    /// <remarks>
    /// 예전에는 밝은 선 하나로만 둘렀는데, 게임 창은 그 선이 아니라 구슬 무늬가 든 두꺼운
    /// 액자다 — 상단 띠에 쓰던 그 그림이다. 액자 속살(베이지)은 밤색 판이 덮는다.
    /// </remarks>
    public static FrameworkElement WindowFrame(UIElement content, Brush? inside = null)
    {
        var host = new Grid { MinWidth = FrameArt.Border * 2, MinHeight = FrameArt.Border * 2 };
        var back = new Border();
        host.Children.Add(back);
        host.Children.Add(new Border
        {
            Background = inside ?? MenuBack,
            Margin = new Thickness(FrameArt.Border),
            Child = content,
        });

        void Redraw()
        {
            var art = FrameArt.Draw((int)Math.Round(host.ActualWidth),
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
        private readonly List<GameButton> _items = [];
        private int _index = -1;

        /// <summary>단추 하나를 만들어 묶음에 넣는다.</summary>
        public GameButton Add(string text, Action run, double width = 110)
        {
            var button = new GameButton(text, run, BandStyle.Button, width);
            int index = _items.Count;
            _items.Add(button);

            button.MouseEnter += (_, _) => Focus(index);
            if (_items.Count == 1) Focus(0);   // 첫 단추에 초점을 두고 시작한다
            return button;
        }

        /// <summary>그 단추로 초점을 옮긴다.</summary>
        public void Focus(int index)
        {
            if (index < 0 || index >= _items.Count || index == _index) return;
            if (_index >= 0 && _index < _items.Count) _items[_index].Focused = false;
            _index = index;
            _items[index].Focused = true;
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
                    if (_index >= 0) _items[_index].Run?.Invoke();
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

    /// <summary>
    /// 글자 폭을 미리 셈해 한 번만 찍는 띠. 이름표처럼 글자가 안 바뀌는 것에 쓴다 —
    /// 폭이 정해져 있으니 <see cref="BandFrame"/> 처럼 자리를 잡아 가며 다시 찍을 것이 없다.
    /// </summary>
    /// <remarks>
    /// 칸 수를 <see cref="UiSprites.CellsAround"/> 로 센다. 글자가 가운데 조각 안에만 들어가
    /// 양 끝 덩굴을 밟지 않는다 — 게임 이름표가 그 모양이다.
    /// </remarks>
    private static Border? FixedBand(BandStyle style, string text, byte color, bool shadow)
    {
        if (Sprites == null || Font == null) return null;

        var label = GameFontLabel(text, color, 1, UiSprites.BandHeight, shadow);
        if (label == null) return null;

        var bgra = Sprites.Band(style, UiSprites.CellsAround(Font.TextWidth(text)), out int w);
        var bmp = BitmapSource.Create(w, UiSprites.BandHeight, 96, 96,
                                      PixelFormats.Bgra32, null, bgra, w * 4);
        bmp.Freeze();

        var brush = new ImageBrush(bmp) { Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(brush, EdgeMode.Aliased);
        brush.Freeze();

        var grid = new Grid { Width = w, Height = UiSprites.BandHeight, Background = brush };
        grid.Children.Add(label);
        return new Border { Child = grid };
    }

    /// <summary>건물 위에 커서를 올렸을 때 붙는 이름표.</summary>
    /// <remarks>
    /// 메뉴 타이틀과 같은 진홍 띠를 <b>덩굴 마구리까지 통째로</b> 쓴다. 게임 화면에서 잰
    /// "시장" 이름표가 띠 64점이고 글자가 32점이라, 마구리 둘이 글자 <b>바깥</b>에 서 있다.
    /// 예전에는 가운데 조각만 이어 깔았는데(마구리가 짧은 글자를 덮을까 봐), 그래서 게임 것과
    /// 모양이 달랐다 — 칸 수를 <see cref="UiSprites.CellsAround"/> 로 세면 둘 다 된다.
    ///
    /// 조각을 못 읽었을 때만 민색으로 물러선다. 그때 바탕은 두 색을 바둑판으로 섞은
    /// 무늬다(<see cref="Dither"/>) — 게임 것도 민색이 아니다.
    /// </remarks>
    public static Border NameTag(string text)
    {
        // 글자는 흰빛이다 — 타이틀 띠의 크림색보다 한 단 밝다.
        var band = FixedBand(BandStyle.Title, text, GameFont.WhiteColor, shadow: true);
        if (band != null)
        {
            band.Visibility = Visibility.Collapsed;
            band.HorizontalAlignment = HorizontalAlignment.Left;
            return band;
        }

        return new Border
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

    /// <summary>
    /// 게임의 <c>%-10s</c> 처럼 왼쪽에 붙이고 빈칸으로 채운다.
    /// </summary>
    /// <remarks>
    /// C 의 <c>%-10s</c> 는 <b>바이트</b>로 센다 — CP949 에서 한글 한 자가 두 바이트라
    /// "국왕" 은 넷을 먹고 여섯 칸이 남는다. C# 의 <c>,-10</c> 은 글자로 세어 여덟 칸을
    /// 붙이므로 두 칸이 더 벌어진다. 게임 글꼴도 한글이 빈칸 둘 폭이라 바이트로 세야 맞는다.
    /// </remarks>
    /// <param name="text">채울 말.</param>
    /// <param name="width">몇 칸으로 맞출지(바이트).</param>
    public static string Pad(string text, int width)
    {
        int cells = 0;
        foreach (char c in text) cells += c < 0x80 ? 1 : 2;
        return cells >= width ? text : text + new string(' ', width - cells);
    }

    /// <summary>
    /// 이름 뒤에 붙는 조사. 받침이 있으면 <paramref name="closed"/>, 없으면 <paramref name="open"/> 이다.
    /// </summary>
    /// <param name="word">앞말. 마지막 글자로 가른다.</param>
    /// <param name="closed">받침이 있을 때 붙일 것("을"·"은"·"이").</param>
    /// <param name="open">받침이 없을 때 붙일 것("를"·"는"·"가").</param>
    /// <remarks>
    /// 게임도 조사를 따로 끼워 넣는다 — 발견 알림 "%s%s [%s]%s 발견했습니다"
    /// (<c>0x00538490</c>) 의 두 번째·네 번째 자리가 이것이다. 한글이 아닌 글자로 끝나면
    /// 받침이 없는 쪽을 쓴다.
    /// </remarks>
    public static string Josa(string word, string closed, string open)
    {
        if (word.Length == 0) return open;
        char last = word[^1];
        if (last is < '가' or > '힣') return open;
        return (last - '가') % 28 == 0 ? open : closed;
    }
}
