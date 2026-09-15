using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 발견물 지도 — 양피지 세계지도 위에 <b>발견물 자리</b>와 <b>내 자리</b>를 찍어 보인다.
/// 휠로 키우고 줄이며, 끌어서 옮긴다.
/// </summary>
/// <remarks>
/// <b>게임에는 없는 창이다.</b> 원본 항해지도(<c>0x00416A00</c>)는 밝힌 바다만 드러내고
/// 표식을 하나도 안 찍는다(볼트 <c>91.분석-지도를 본다</c>). 이쪽은 <c>cds95-mod</c> 의
/// <c>WorldMapKR</c> 처럼 <b>어디에 무엇이 있는지</b> 보려고 둔 것이라 햄버거 차림표에서 연다.
///
/// 모드의 손놀림을 그대로 옮겼다(<c>mapwin.c</c>).
/// <code>
///   휠      커서 밑에 있던 자리가 <b>제자리에 남도록</b> 배율만 바꾼다(ZoomAt)
///   끌기    누른 자리를 붙잡고 지도를 민다(g_drag)
///   열 때   함대 자리를 가운데 두고 중간 배율로 연다(ZOOM_START)
///   이름표  배율이 어느 구간일 때만 단다 — 너무 키우면 이름이 그림을 덮는다
/// </code>
/// 바탕은 항해지도를 짓는 손(<see cref="Rendering.ShipMapHost.Chart"/>)을 <b>온 지도를 밝힌
/// 채</b> 부른 것이라 점 하나가 칸 <c>4x4</c> 다 — 모드처럼 타일을 다시 그리지는 않으므로
/// 아주 키우면 네모가 커질 뿐이다.
/// </remarks>
public sealed class DiscoveryMapDialog : GameWindow
{
    /// <summary>보이는 자리의 크기(점).</summary>
    private const double ViewW = 940, ViewH = 470;

    /// <summary>휠로 오갈 배율 — 지도 점 하나가 화면 몇 점인지. 첫 칸이 <b>온 지도</b>다.</summary>
    private static readonly double[] Zooms = [1.5, 2, 3, 4, 6, 8, 12, 16];

    /// <summary>열 때의 배율 자리.</summary>
    private const int ZoomStart = 2;

    /// <summary>이름표를 다는 배율 구간 — 너무 키우면 이름이 그림을 덮는다.</summary>
    private const double LabelFrom = 4, LabelTo = 12;

    /// <summary>표식 크기(지도 점).</summary>
    private const double MarkSize = 3, ShipSize = 5;

    private static readonly Brush Found = Frozen(Color.FromRgb(0xC0, 0x30, 0x20));
    private static readonly Brush Yet = Frozen(Color.FromRgb(0x50, 0x50, 0x50));
    private static readonly Brush Mine = Frozen(Color.FromRgb(0x20, 0x40, 0xC0));

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private readonly Canvas _world;
    private readonly List<FrameworkElement> _labels = [];
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly TranslateTransform _shift = new(0, 0);
    private readonly TextBlock _note;

    private readonly int _chartW, _chartH, _found, _done;
    private int _zoom = ZoomStart;
    private double _vx, _vy;              // 보이는 자리의 왼쪽 위(지도 점)
    private bool _dragging;
    private Point _grab;
    private double _grabVx, _grabVy;

    private double Z => Zooms[_zoom];

    private DiscoveryMapDialog(uint[] chart, int width, int height,
                               DiscoveryTable table, Player player, (double X, double Y)? ship)
    {
        _chartW = width;
        _chartH = height;

        Title = "발견물 지도";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        var bmp = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null,
                                      chart, width * 4);
        bmp.Freeze();

        _world = new Canvas { Width = width, Height = height };
        _world.Children.Add(new Image
        {
            Source = bmp,
            Width = width,
            Height = height,
            SnapsToDevicePixels = true,
        });

        // 옮기고 키우는 것은 한 덩이로 — 먼저 밀고 나서 키운다.
        var moves = new TransformGroup();
        moves.Children.Add(_shift);
        moves.Children.Add(_scale);
        _world.RenderTransform = moves;

        int shown = 0, done = 0;
        for (int id = 0; id < DiscoveryTable.Count; id++)
        {
            if (table.Find(id) is not { } row || !row.HasPlace) continue;

            bool found = player.HasFound(id);
            // 자리는 네모라 한가운데를 찍는다.
            Mark((row.X1 + row.X2) / 2.0 / ExploredMap.CellsPerBlock,
                 (row.Y1 + row.Y2) / 2.0 / ExploredMap.CellsPerBlock,
                 MarkSize, found ? Found : Yet, row.Name, label: true);
            shown++;
            if (found) done++;
        }

        _found = shown;
        _done = done;

        if (ship is { } at)
            Mark(at.X / ExploredMap.CellsPerBlock, at.Y / ExploredMap.CellsPerBlock,
                 ShipSize, Mine, "지금 자리", label: false);

        var viewport = new Border
        {
            Width = ViewW,
            Height = ViewH,
            BorderBrush = GameUi.Edge,
            BorderThickness = new Thickness(1),
            Background = Brushes.Black,
            ClipToBounds = true,
            Child = new Canvas { Children = { _world } },
            Cursor = Cursors.SizeAll,
        };

        viewport.MouseWheel += (_, e) => ZoomAt(e.Delta > 0 ? 1 : -1, e.GetPosition(viewport));
        viewport.MouseLeftButtonDown += (_, e) =>
        {
            _dragging = true;
            _grab = e.GetPosition(viewport);
            _grabVx = _vx;
            _grabVy = _vy;
            viewport.CaptureMouse();
        };
        viewport.MouseMove += (_, e) =>
        {
            if (!_dragging) return;
            var now = e.GetPosition(viewport);
            _vx = _grabVx - (now.X - _grab.X) / Z;
            _vy = _grabVy - (now.Y - _grab.Y) / Z;
            Apply();
        };
        viewport.MouseLeftButtonUp += (_, _) =>
        {
            _dragging = false;
            viewport.ReleaseMouseCapture();
        };

        _note = new TextBlock
        {
            Foreground = GameUi.Text,
            FontSize = 14,
            Margin = new Thickness(6, 4, 6, 6),
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        var ok = GameUi.PushButton("확인", Close, 88);
        ok.HorizontalAlignment = HorizontalAlignment.Center;
        ok.Margin = new Thickness(0, 0, 0, 8);

        var stack = new StackPanel { Margin = new Thickness(8) };
        stack.Children.Add(viewport);
        stack.Children.Add(_note);
        stack.Children.Add(ok);
        Content = stack;

        KeyDown += OnKey;

        // 함대 자리를 가운데 두고 연다 — 모드도 그렇게 연다.
        if (ship is { } spot)
            CenterOn(spot.X / ExploredMap.CellsPerBlock, spot.Y / ExploredMap.CellsPerBlock);
        else
            CenterOn(width / 2.0, height / 2.0);
    }

    /// <summary>글쇠 — 화살표로 밀고 <c>+ -</c> 로 키우고 줄인다.</summary>
    private void OnKey(object sender, KeyEventArgs e)
    {
        const double Step = 40;
        switch (e.Key)
        {
            case Key.Escape: Close(); return;
            case Key.Left: _vx -= Step / Z; break;
            case Key.Right: _vx += Step / Z; break;
            case Key.Up: _vy -= Step / Z; break;
            case Key.Down: _vy += Step / Z; break;
            case Key.OemPlus or Key.Add: ZoomAt(1, new Point(ViewW / 2, ViewH / 2)); return;
            case Key.OemMinus or Key.Subtract: ZoomAt(-1, new Point(ViewW / 2, ViewH / 2)); return;
            default: return;
        }
        e.Handled = true;
        Apply();
    }

    /// <summary>그 자리가 화면 한가운데 오게 민다(지도 점).</summary>
    private void CenterOn(double x, double y)
    {
        _vx = x - ViewW / Z / 2;
        _vy = y - ViewH / Z / 2;
        Apply();
    }

    /// <summary>
    /// 커서 밑에 있던 자리가 <b>제자리에 남도록</b> 배율만 바꾼다(모드 <c>ZoomAt</c>).
    /// </summary>
    private void ZoomAt(int by, Point at)
    {
        int want = Math.Clamp(_zoom + by, 0, Zooms.Length - 1);
        if (want == _zoom) return;

        double x = _vx + at.X / Z;          // 커서가 짚고 있던 지도 점
        double y = _vy + at.Y / Z;
        _zoom = want;
        _vx = x - at.X / Z;
        _vy = y - at.Y / Z;
        Apply();
    }

    /// <summary>보이는 자리를 지도 밖으로 못 나가게 자르고, 화면에 먹인다.</summary>
    private void Apply()
    {
        double seeW = ViewW / Z, seeH = ViewH / Z;
        _vx = seeW >= _chartW ? (_chartW - seeW) / 2 : Math.Clamp(_vx, 0, _chartW - seeW);
        _vy = seeH >= _chartH ? (_chartH - seeH) / 2 : Math.Clamp(_vy, 0, _chartH - seeH);

        _scale.ScaleX = _scale.ScaleY = Z;
        _shift.X = -_vx;
        _shift.Y = -_vy;

        // 이름표는 어느 구간에서만 단다 — 아주 키우면 이름이 그림을 덮는다.
        var show = Z >= LabelFrom && Z <= LabelTo ? Visibility.Visible : Visibility.Collapsed;
        foreach (var tag in _labels) tag.Visibility = show;

        _note.Text = $"발견물 {_found}곳 · 찾은 것 {_done}곳 · 배율 x{Z:0.#}"
                   + "   (휠 키우기·줄이기 · 끌어서 옮기기 · 빨강 찾음 · 회색 아직 · 파랑 내 자리)";
    }

    /// <summary>점 하나와 이름표를 찍는다. 자리는 <b>지도 점</b>(칸/4)이다.</summary>
    private void Mark(double x, double y, double size, Brush fill, string name, bool label)
    {
        var dot = new System.Windows.Shapes.Ellipse
        {
            Width = size,
            Height = size,
            Fill = fill,
            ToolTip = name,
        };
        Canvas.SetLeft(dot, x - size / 2);
        Canvas.SetTop(dot, y - size / 2);
        _world.Children.Add(dot);

        if (!label) return;

        var tag = new TextBlock
        {
            Text = name,
            Foreground = fill,
            FontSize = 4,
            FontWeight = FontWeights.Bold,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(tag, x + size / 2 + 1);
        Canvas.SetTop(tag, y - 2.5);
        _world.Children.Add(tag);
        _labels.Add(tag);
    }

    /// <summary>창을 연다. 지도를 못 지으면 아무 일도 안 한다.</summary>
    public static void Show(Window owner, uint[]? chart, int width, int height,
                            DiscoveryTable? table, Player player, (double X, double Y)? ship)
    {
        if (chart == null || table == null || width <= 0 || height <= 0) return;
        new DiscoveryMapDialog(chart, width, height, table, player, ship) { Owner = owner }.ShowDialog();
    }
}
