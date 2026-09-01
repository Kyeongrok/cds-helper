using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Game.Engine.Sea;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 해전 판 — 바다 바탕 위에 격자를 깔고 두 함대를 세운다.
/// </summary>
/// <remarks>
/// 그림은 게임 것 그대로다(<see cref="CombatArt"/>, 볼트 <c>61.분석-해전 그림</c>).
/// 바다는 <b>800x600</b> 이고 격자 한 칸은 <b>32점</b>이다 — 게임이 칸 좌표에
/// <c>shl eax, 5</c> 를 먹인다(<c>0x00440074</c> 벌).
///
/// <b>아직 판만 있다.</b> 한 턴의 차례·움직임·포격은 안 옮겼다 — 볼트 <c>47.분석-해전</c>
/// 의 "판의 크기와 좌표계", "한 턴의 차례 정하기와 배 움직임" 이 남은 숙제다. 지금은
/// 배를 눌러 고르고 빈 칸을 눌러 옮기는 것까지만 된다.
/// </remarks>
public sealed class SeaCombatDialog : Window
{
    /// <summary>판의 칸 수. 게임 칸 수를 아직 못 짚어 바다 크기에서 나눈 값이다.</summary>
    private const int Cols = CombatArt.SeaWidth / CombatArt.Cell;
    private const int Rows = CombatArt.SeaHeight / CombatArt.Cell;

    /// <summary>배가 칸 가운데에 서게 밀어 주는 만큼.</summary>
    private const int ShipDx = (CombatArt.Cell - CombatArt.ShipSize) / 2;
    private const int ShipDy = -CombatArt.ShipSize / 2;

    /// <summary>배 한 척.</summary>
    private sealed class Hull
    {
        public int Col, Row, Way, Fleet;
        public bool Mine;
        public Image Art = new();
    }

    private readonly CombatArt _art;
    private readonly Canvas _board = new()
    {
        Width = CombatArt.SeaWidth,
        Height = CombatArt.SeaHeight,
    };
    private readonly List<Hull> _hulls = [];
    private readonly Image _cell = new();

    private Hull? _picked;

    private SeaCombatDialog(CombatArt art, Player player, in Enemy foe, int scale)
    {
        _art = art;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        Put(_board, art.Sea(), 0, 0, CombatArt.SeaWidth, CombatArt.SeaHeight);

        // 짚은 칸 표시 — 처음에는 숨겨 둔다.
        _cell.Source = Bitmap(art.CellArt(lit: true));
        _cell.Width = 48;
        _cell.Height = 32;
        _cell.Visibility = Visibility.Collapsed;
        _cell.IsHitTestVisible = false;
        Panel.SetZIndex(_cell, 5);
        _board.Children.Add(_cell);

        // 내 함대는 왼쪽에서 오른쪽을 보고, 적은 오른쪽에서 왼쪽을 본다.
        int mine = Math.Max(1, player.Ships.Count);
        for (int i = 0; i < mine; i++) Add(new Hull
        {
            Col = 3, Row = Rows / 2 - mine / 2 + i, Way = 3, Fleet = 0, Mine = true,
        });
        for (int i = 0; i < foe.Ships; i++) Add(new Hull
        {
            Col = Cols - 4, Row = Rows / 2 - foe.Ships / 2 + i, Way = 9, Fleet = 4,
        });

        _board.Background = Brushes.Transparent;
        _board.MouseLeftButtonUp += Touch;

        // 그림 점 하나가 화면 점 하나가 되게.
        double zoom = GameUi.PixelZoom(this, scale);
        _board.LayoutTransform = new ScaleTransform(zoom, zoom);

        Content = GameUi.GoldFrame(_board);
        GameUi.EnableDrag(this, _board);

        MouseRightButtonUp += (_, e) => GameUi.ContextMenu(
            this, PointToScreen(e.GetPosition(this)),
            [("항복한다", Close), ("게임 복귀", () => { })]);

        KeyDown += (_, e) => { if (e.Key is Key.Escape) Close(); };
    }

    private void Add(Hull hull)
    {
        hull.Art.Source = Bitmap(_art.Ship(hull.Fleet, hull.Way));
        hull.Art.Width = hull.Art.Height = CombatArt.ShipSize;
        RenderOptions.SetBitmapScalingMode(hull.Art, BitmapScalingMode.NearestNeighbor);
        Panel.SetZIndex(hull.Art, 10);
        _board.Children.Add(hull.Art);
        _hulls.Add(hull);
        Place(hull);
    }

    private static void Place(Hull hull)
    {
        Canvas.SetLeft(hull.Art, hull.Col * CombatArt.Cell + ShipDx);
        Canvas.SetTop(hull.Art, hull.Row * CombatArt.Cell + ShipDy);
    }

    /// <summary>배를 누르면 고르고, 빈 칸을 누르면 그리로 옮긴다.</summary>
    private void Touch(object sender, MouseButtonEventArgs e)
    {
        var at = e.GetPosition(_board);
        int col = (int)(at.X / CombatArt.Cell), row = (int)(at.Y / CombatArt.Cell);
        if (col < 0 || col >= Cols || row < 0 || row >= Rows) return;

        var here = _hulls.Find(h => h.Col == col && h.Row == row);
        if (here is { Mine: true }) { _picked = here; Mark(col, row); return; }

        if (_picked == null || here != null) return;

        // 옮기면 그쪽을 보게 뱃머리를 돌린다 — 열두 방향이라 서른 도마다 한 장이다.
        _picked.Way = WayTo(col - _picked.Col, row - _picked.Row);
        _picked.Art.Source = Bitmap(_art.Ship(_picked.Fleet, _picked.Way));
        _picked.Col = col;
        _picked.Row = row;
        Place(_picked);
        Mark(col, row);
    }

    /// <summary>그 쪽을 보는 방향 번호. 0 이 위고 시계 방향으로 열둘이다.</summary>
    private static int WayTo(int dx, int dy)
    {
        double turn = Math.Atan2(dx, -dy) / (Math.PI * 2) * CombatArt.Ways;
        return ((int)Math.Round(turn) % CombatArt.Ways + CombatArt.Ways) % CombatArt.Ways;
    }

    private void Mark(int col, int row)
    {
        Canvas.SetLeft(_cell, col * CombatArt.Cell - 8);
        Canvas.SetTop(_cell, row * CombatArt.Cell);
        _cell.Visibility = Visibility.Visible;
    }

    private static BitmapSource? Bitmap(string? path)
    {
        if (path == null) return null;
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.UriSource = new Uri(System.IO.Path.GetFullPath(path));
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    private static void Put(Canvas board, string? path, double x, double y, double w, double h)
    {
        if (Bitmap(path) is not { } source) return;

        var image = new Image { Source = source, Width = w, Height = h };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        Canvas.SetLeft(image, x);
        Canvas.SetTop(image, y);
        board.Children.Add(image);
    }

    /// <summary>해전 판을 연다. 그림을 못 읽었으면 그렇다고 이른다.</summary>
    public static void Play(Window owner, Player player, Random rng)
    {
        var art = CombatArt.Open();
        if (art == null)
        {
            NoticeDialog.Show(owner, $"해전 그림을 못 읽었다 — {CombatArt.LastError}");
            return;
        }

        // 붙는 무리는 조우 쪽 굴림을 그대로 쓴다.
        var foe = Encounter.Roll(rng);
        int scale = owner.ActualHeight > 1000 ? 1 : 1;
        new SeaCombatDialog(art, player, foe, scale) { Owner = owner }.ShowDialog();
    }
}
