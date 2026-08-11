using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using CdsHelper.Support.Local.Helpers;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace CdsHelper.Main.UI.Views.D3D;

/// <summary>
/// 자식 창 하나를 만들어 그 위에 DXGI 스왑체인을 걸고 지도와 함대를 그린다.
/// </summary>
/// <remarks>
/// 세계지도 탭은 <c>D3DImage</c> 를 쓸 수 없는 것이 아니라 안 쓴다 — 그쪽은 마커·라벨이
/// WPF 요소로 지도 위에 얹혀 있어 비주얼 트리 안에 남아야 하기 때문이다. 이 창은 마커가
/// 없으므로 자식 창에 스왑체인을 곧바로 걸었다. 공유 표면을 거치지 않아 그만큼 짧다.
///
/// 대신 airspace 규칙대로 이 자식 창은 WPF 콘텐츠보다 늘 위에 그려진다. 이 창 안에서
/// D3D 화면 위에 WPF 로 무언가를 얹으려 해도 가려진다.
/// </remarks>
public sealed class ShipMapHost : HwndHost
{
    // 창 클래스를 새로 등록하지 않고 미리 있는 STATIC 을 쓴다. 스왑체인을 걸 HWND 하나가
    // 필요할 뿐이고, 정적 컨트롤은 기본적으로 WM_NCHITTEST 에 HTTRANSPARENT 를 돌려주므로
    // 마우스가 이 자식 창에 먹히지 않고 WPF 쪽으로 그대로 넘어간다 — 끌기·휠이 살아 있다.
    private const string WndClass = "STATIC";
    private const int WsChild = 0x40000000;
    private const int WsVisible = 0x10000000;

    // CharSet 을 적어야 한다. 빠뜨리면 W 함수에 ANSI 문자열이 넘어가 클래스 이름을 못 찾는다
    // (Win32 1407). 이것 때문에 창이 안 만들어져 HwndHost 가 통째로 터졌었다.
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(int exStyle, string cls, string? name, int style,
                                                 int x, int y, int w, int h,
                                                 IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hwnd);

    private readonly MapD3DRenderer _renderer = new();
    private readonly GameShipReader _ship = new();

    /// <summary>WORLD.CDS 원본. 배가 육지에 걸리는지 보려고 들고 있는다.</summary>
    private byte[]? _world;

    private IDXGISwapChain1? _swapChain;
    private ID3D11RenderTargetView? _backBufferView;
    private IntPtr _hwnd;
    private int _pixelW, _pixelH;
    private bool _ready;

    /// <summary>
    /// 화면 한 점이 나아가는 칸 수. 작을수록 확대다. 뒤집으면 "칸당 화면 픽셀"이 된다 —
    /// 1/16 이면 칸당 16점으로 타일이 원본 크기, 1/32 면 그 두 배로 커진다.
    /// 게임 화면과 나란히 놓고 맞춘 값이 1/32 다(게임이 640x480 을 늘려 띄우기 때문).
    /// </summary>
    private double _cellsPerPixel = 1.0 / 32;

    /// <summary>화면 한가운데가 가리키는 칸 좌표.</summary>
    private double _centerX = 1185, _centerY = 357;

    /// <summary>리스본. 게임 도시 표의 0번이다.</summary>
    private const int LisbonCityId = 0;

    private bool _follow = true;
    private bool _dragging;
    private Point _dragStart;
    private double _dragCx, _dragCy;

    private double _shipX, _shipY;      // 지금 배가 있는 자리(칸)
    private double _targetX, _targetY;  // 배가 향하는 자리(칸)
    private bool _shipKnown;

    /// <summary>한 틱의 길이. 게임처럼 틱마다 한 걸음씩 나아간다.</summary>
    private const double TickSeconds = 0.1;

    /// <summary>한 틱에 나아가는 칸 수. 화면 배율과 상관없이 일정하다.</summary>
    private const double CellsPerTick = 1.0;

    /// <summary>커서가 이 칸 수 안에 있으면 뱃머리를 그대로 둔다 — 배 위에서 빙빙 돌지 않게.</summary>
    private const double TurnDeadZoneCells = 1.0;

    private double _tickAccum;

    // 마지막 프레임의 화면 원점. 클릭한 자리를 칸으로 되돌릴 때 쓴다.
    private (double X, double Y) _lastOrigin;
    private double _lastDpiX = 1, _lastDpiY = 1;

    /// <summary>
    /// 해안 칸은 바다와 육지가 섞여 있다. 육지 비율이 이보다 높으면 못 지나간다.
    /// 낮추면 해안에 더 바짝 붙을 수 있고, 높이면 좁은 해협을 더 잘 빠져나간다.
    /// </summary>
    private const double LandBlockRatio = 0.5;

    /// <summary>육지에 막혀 있는지. 상태 줄에 알리려고 둔다.</summary>
    private bool _blocked;

    /// <summary>
    /// 방향 번호를 통째로 돌리는 값. 뱃머리가 일정하게 어긋날 때만 손대면 된다.
    /// 게임 방향은 0 = 북, 4 = 서, 8 = 남, 12 = 동으로 <b>반시계</b>로 돈다.
    /// </summary>
    private const int HeadingZeroOffset = 0;

    private int _heading;                  // 그림에 쓸 게임 방향 번호(반시계)
    private double _dirX, _dirY;           // 실제로 나아가는 쪽(단위 벡터)
    private Point _mouse;                  // 마지막 커서 자리(WPF 단위, 이 요소 기준)
    private bool _mouseInside;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private TimeSpan _lastFrame;
    private bool _spriteReady;

    /// <summary>배 그림을 올릴 때 쓰는 임시 자리(BGRA). 프레임마다 새로 잡지 않으려고 둔다.</summary>
    private readonly uint[] _spriteBuf = new uint[GameShipReader.SpriteSize];

    /// <summary>커서를 따라 배를 몬다. 끄면 게임 함대 자리를 그대로 따라간다.</summary>
    public bool SteerWithMouse { get; set; } = true;

    public string Status { get; private set; } = "";

    /// <summary>Present 까지 마친 프레임 수. 진짜 그려지고 있는지 밖에서 볼 때 쓴다.</summary>
    public long FrameCount { get; private set; }

    /// <summary>스왑체인을 걸다 난 문제. 없으면 빈 문자열.</summary>
    public string SwapChainError { get; private set; } = "";

    /// <summary>
    /// 지금 배가 있는 위도·경도. 칸 좌표를 도로 바꾼 것이다 —
    /// 가로 2500칸이 경도 -180~180, 세로 1250칸이 위도 90~-90 에 그대로 대응한다.
    /// </summary>
    public (double Lat, double Lon) ShipLatLon => (
        90.0 - _shipY * 180.0 / WorldMapRenderer.CellH,
        _shipX * 360.0 / WorldMapRenderer.UnfoldedW - 180.0);

    /// <summary>지금 스왑체인 크기(화면 실픽셀).</summary>
    public (int W, int H) SurfaceSize => (_pixelW, _pixelH);

    /// <summary>배가 화면 밖으로 나가지 않게 따라다닐지.</summary>
    public bool Follow
    {
        get => _follow;
        set => _follow = value;
    }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        _hwnd = CreateWindowExW(0, WndClass, null, WsChild | WsVisible, 0, 0, 1, 1,
                                hwndParent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero)
            throw new InvalidOperationException(
                $"함대 창의 자식 창을 만들지 못했습니다 (Win32 {Marshal.GetLastWin32Error()})");
        return new HandleRef(this, _hwnd);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        CompositionTarget.Rendering -= OnFrame;
        _backBufferView?.Dispose();
        _swapChain?.Dispose();
        _renderer.Dispose();
        _ship.Dispose();
        if (_hwnd != IntPtr.Zero) { DestroyWindow(_hwnd); _hwnd = IntPtr.Zero; }
    }

    /// <summary>WORLD.CDS / OCEAN.CDS 를 올리고 스왑체인을 건다. 실패하면 까닭을 남기고 false.</summary>
    public bool Start(string gameDir)
    {
        var world = WorldMapRenderer.LoadWorldData(System.IO.Path.Combine(gameDir, "WORLD.CDS"));
        if (world == null) { Status = "WORLD.CDS 를 읽지 못했습니다"; return false; }
        _world = world;
        var ocean = OceanTiles.LoadFromDirectory(gameDir);
        if (ocean == null) { Status = $"OCEAN.CDS 를 읽지 못했습니다 ({OceanTiles.LastError})"; return false; }

        _renderer.Initialize(world, ocean);

        // 배는 리스본에서 시작한다. 자리는 게임 원본 도시 표에서 가져온다.
        if (GameMapCoords.TryCityCell(LisbonCityId, out double lx, out double ly))
        {
            // 도시 칸은 육지다. 그대로 두면 배가 땅 위에서 시작해 한동안 못 움직이므로
            // 가장 가까운 물칸으로 밀어 낸다(리스본이면 바로 앞바다다).
            (lx, ly) = NearestWater(lx, ly);
            _shipX = _targetX = lx;
            _shipY = _targetY = ly;
            _centerX = lx;
            _centerY = ly;
            _shipKnown = true;
        }

        _ready = true;
        CompositionTarget.Rendering += OnFrame;
        return true;
    }

    private void EnsureSwapChain(int w, int h)
    {
        if (_hwnd == IntPtr.Zero || w <= 0 || h <= 0) return;
        if (_swapChain != null && _pixelW == w && _pixelH == h) return;
        try
        {
            EnsureSwapChainCore(w, h);
            SwapChainError = "";
        }
        catch (Exception ex)
        {
            SwapChainError = ex.Message;
        }
    }

    private void EnsureSwapChainCore(int w, int h)
    {

        _backBufferView?.Dispose();
        _backBufferView = null;

        if (_swapChain == null)
        {
            using var dxgiDevice = _renderer.Device.QueryInterface<IDXGIDevice>();
            using var adapter = dxgiDevice.GetAdapter();
            using var factory = adapter.GetParent<IDXGIFactory2>();
            var desc = new SwapChainDescription1
            {
                Width = (uint)w,
                Height = (uint)h,
                Format = Format.B8G8R8A8_UNorm,
                BufferCount = 2,
                BufferUsage = Usage.RenderTargetOutput,
                SampleDescription = new SampleDescription(1, 0),
                SwapEffect = SwapEffect.FlipDiscard,
                Scaling = Scaling.None,
            };
            _swapChain = factory.CreateSwapChainForHwnd(_renderer.Device, _hwnd, desc);
        }
        else
        {
            _swapChain.ResizeBuffers(2, (uint)w, (uint)h, Format.B8G8R8A8_UNorm, SwapChainFlags.None);
        }

        using var back = _swapChain.GetBuffer<ID3D11Texture2D>(0);
        _backBufferView = _renderer.Device.CreateRenderTargetView(back);
        _pixelW = w;
        _pixelH = h;
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        if (!_ready || _hwnd == IntPtr.Zero) return;

        var dpi = VisualTreeHelper.GetDpi(this);
        int w = (int)Math.Round(ActualWidth * dpi.DpiScaleX);
        int h = (int)Math.Round(ActualHeight * dpi.DpiScaleY);
        EnsureSwapChain(w, h);
        if (_backBufferView == null) return;

        var now = _clock.Elapsed;
        double dt = Math.Min((now - _lastFrame).TotalSeconds, 0.1);   // 창이 멈췄다 살아나도 튀지 않게
        _lastFrame = now;

        // 커서를 칸 좌표로 옮기려면 이번 프레임의 원점이 필요하다. 배를 옮기기 전 값으로 잡는다.
        var origin = (_centerX - w / 2.0 * _cellsPerPixel, _centerY - h / 2.0 * _cellsPerPixel);
        _lastOrigin = origin;
        _lastDpiX = dpi.DpiScaleX;
        _lastDpiY = dpi.DpiScaleY;
        UpdateShip(dt, origin, dpi.DpiScaleX, dpi.DpiScaleY);

        // 배를 따라간다 — 화면 한가운데에 둔다.
        if (_follow && _shipKnown) { _centerX = _shipX; _centerY = _shipY; }
        origin = (_centerX - w / 2.0 * _cellsPerPixel, _centerY - h / 2.0 * _cellsPerPixel);

        var rect = (0f, 0f, 0f, 0f);
        if (_shipKnown && _spriteReady)
        {
            // 배 그림 한 장은 48x48 이고 게임에서 한 칸이 16점이니 세 칸을 덮는다.
            float size = (float)(3.0 * OceanTiles.TileW / (_cellsPerPixel * OceanTiles.TileW));
            float sx = (float)((_shipX - origin.Item1) / _cellsPerPixel - size / 2);
            float sy = (float)((_shipY - origin.Item2) / _cellsPerPixel - size / 2);
            rect = (sx, sy, size, size);
        }

        _renderer.RenderTo(_backBufferView, w, h, origin, (_cellsPerPixel, _cellsPerPixel), rect);
        _swapChain!.Present(1, PresentFlags.None);
        FrameCount++;
    }

    private void UpdateShip(double dt, (double X, double Y) origin, double dpiX, double dpiY)
    {
        // 그림은 게임 것을 쓴다 — 게임이 떠 있어야 배 모양이 나온다.
        if (!_ship.IsAttached) _ship.TryAttach();

        if (SteerWithMouse)
        {
            if (_mouseInside)
            {
                // 커서가 가리키는 칸으로 뱃머리를 돌린다.
                _targetX = origin.X + _mouse.X * dpiX * _cellsPerPixel;
                _targetY = origin.Y + _mouse.Y * dpiY * _cellsPerPixel;
            }
            Sail(dt);
            Status = $"배 {_shipX:F1}, {_shipY:F1} 칸 · 방향 {_heading}/16 · " +
                     (!_mouseInside ? "커서를 지도 위에 올리면 움직입니다"
                                    : _blocked ? "육지에 막혔습니다" : "커서 쪽으로 항해 중") +
                     (_ship.IsAttached ? "" : " · 그림은 구워 둔 것");
        }
        else
        {
            // 게임 함대를 따라가는 예전 방식.
            var cell = _ship.TryReadCell();
            if (cell != null)
            {
                _targetX = cell.Value.CellX;
                _targetY = cell.Value.CellY;
                if (!_shipKnown) { _shipX = _targetX; _shipY = _targetY; _shipKnown = true; }
                Status = $"게임 함대 {_targetX:F1}, {_targetY:F1} 칸";
            }
            else
            {
                Status = _ship.IsAttached ? "게임이 아직 항해 중이 아닙니다" : "게임(cds_95)이 떠 있지 않습니다";
            }
            // 표본이 띄엄띄엄 와도 이어져 보이도록 조금씩 따라붙는다.
            _shipX += (_targetX - _shipX) * 0.15;
            _shipY += (_targetY - _shipY) * 0.15;
            var spr0 = _ship.TryReadSprite();
            if (spr0 != null) UploadGameSprite(spr0);
            return;
        }

        // 게임이 떠 있으면 그 그림을(함선 종류에 맞는 4벌 중 하나), 아니면 asset/ship 의 것을 쓴다.
        var indices = _ship.IsAttached ? _ship.TryReadSprite(_heading, onLand: false) : null;
        if (indices != null) UploadGameSprite(indices);
        else
        {
            var frame = ShipSprites.Frame(_heading);
            if (!frame.IsEmpty) { _renderer.SetSprite(frame); _spriteReady = true; }
        }
    }

    /// <summary>게임에서 읽은 팔레트 색인 그림을 색으로 풀어 올린다. 색인 0 은 비침이다.</summary>
    private void UploadGameSprite(byte[] indices)
    {
        for (int i = 0; i < _spriteBuf.Length; i++)
        {
            int ix = indices[i];
            _spriteBuf[i] = ix == 0
                ? 0u
                : 0xFF000000u | (uint)((OceanPalette.Rgb[ix * 3] << 16)
                                     | (OceanPalette.Rgb[ix * 3 + 1] << 8)
                                     | OceanPalette.Rgb[ix * 3 + 2]);
        }
        _renderer.SetSprite(_spriteBuf);
        _spriteReady = true;
    }

    /// <summary>
    /// 커서 쪽으로 뱃머리를 돌리고, 틱마다 그 방향으로 한 걸음 나아간다.
    /// 커서는 방향만 정한다 — 커서 자리에 도착해서 멈추는 것이 아니라 계속 나아간다.
    /// </summary>
    private void Sail(double dt)
    {
        if (!_mouseInside) return;      // 창 밖으로 나가면 배도 선다

        double dx = _targetX - _shipX, dy = _targetY - _shipY;
        if (dx * dx + dy * dy > TurnDeadZoneCells * TurnDeadZoneCells)
        {
            // atan2(dx, -dy) 는 북쪽이 0 이고 시계방향으로 느는 값이다.
            double a = Math.Atan2(dx, -dy);
            if (a < 0) a += Math.PI * 2;
            int clockwise = (int)Math.Round(a / (Math.PI * 2 / 16)) & 0xF;

            // 나아가는 쪽은 시계방향 번호를 그대로 각으로 되돌려 쓴다. 16방향에 맞춰
            // 꺾어야 그림과 가는 쪽이 어긋나지 않는다.
            double qa = clockwise * (Math.PI * 2 / 16);
            _dirX = Math.Sin(qa);
            _dirY = -Math.Cos(qa);

            // 그림 번호만 게임식으로 바꾼다. 게임 방향은 반시계로 돈다 —
            // 0 이 북, 4 가 서, 8 이 남, 12 가 동이다(4번 그림이 왼쪽을 보는 것으로 확인했다).
            // 이 번호로 이동 벡터를 다시 만들면 두 번 뒤집혀 배가 뒤로 간다. 그래서 나눠 둔다.
            _heading = (16 - clockwise + HeadingZeroOffset) & 0xF;
        }

        _tickAccum += dt;
        while (_tickAccum >= TickSeconds)
        {
            _tickAccum -= TickSeconds;
            Step(_dirX * CellsPerTick, _dirY * CellsPerTick);
        }
    }

    /// <summary>
    /// 한 걸음 옮긴다. 육지에 걸리면 가로·세로를 따로 밀어 본다 — 해안을 따라 미끄러지듯
    /// 나아가게 하려는 것이다. 둘 다 막히면 그 자리에 선다.
    /// </summary>
    private void Step(double dx, double dy)
    {
        if (CanGo(_shipX + dx, _shipY + dy)) { Move(dx, dy); _blocked = false; return; }
        if (dx != 0 && CanGo(_shipX + dx, _shipY)) { Move(dx, 0); _blocked = true; return; }
        if (dy != 0 && CanGo(_shipX, _shipY + dy)) { Move(0, dy); _blocked = true; return; }
        _blocked = true;
    }

    private void Move(double dx, double dy)
    {
        _shipX += dx;
        _shipY += dy;
        // 지도 밖으로는 못 나간다. 가로는 이어져 있으므로 나머지로 접는다.
        _shipX -= Math.Floor(_shipX / WorldMapRenderer.UnfoldedW) * WorldMapRenderer.UnfoldedW;
        _shipY = Math.Clamp(_shipY, 0, WorldMapRenderer.CellH - 1);
    }

    private bool CanGo(double cellX, double cellY) => !IsLand(cellX, cellY);

    /// <summary>
    /// 그 자리에서 가장 가까운 물칸. 한 칸씩 넓혀 가며 테두리만 훑는다.
    /// 못 찾으면(있을 수 없지만) 준 자리를 그대로 돌려준다.
    /// </summary>
    private (double X, double Y) NearestWater(double cellX, double cellY)
    {
        if (!IsLand(cellX, cellY)) return (cellX, cellY);
        for (int r = 1; r <= 64; r++)
        {
            for (int dy = -r; dy <= r; dy++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Abs(dx) != r && Math.Abs(dy) != r) continue;   // 테두리만
                    double x = cellX + dx, y = cellY + dy;
                    if (!IsLand(x, y)) return (x, y);
                }
            }
        }
        return (cellX, cellY);
    }

    /// <summary>
    /// 그 칸이 배가 못 가는 땅인지. WORLD.CDS 의 지형값을 그대로 본다 —
    /// 0 이 바다, 1 이 육지, 그 밖은 바다와 육지가 섞인 해안 칸이다.
    /// </summary>
    private bool IsLand(double cellX, double cellY)
    {
        if (_world == null) return false;

        int cx = (int)Math.Floor(cellX);
        int cy = (int)Math.Floor(cellY);
        cx -= (int)Math.Floor(cx / (double)WorldMapRenderer.UnfoldedW) * WorldMapRenderer.UnfoldedW;
        if (cy < 0 || cy >= WorldMapRenderer.CellH) return true;

        // 짝수 행이 지도의 왼쪽 절반, 홀수 행이 오른쪽 절반이다.
        bool right = cx >= WorldMapRenderer.CellW;
        int col = right ? cx - WorldMapRenderer.CellW : cx;
        int row = cy * 2 + (right ? 1 : 0);
        int off = row * WorldMapRenderer.RawStride + col * 2;

        byte terrain = (byte)(_world[off] & 0x7F);
        if (terrain == 0) return false;   // 바다
        if (terrain == 1) return true;    // 육지
        return WorldMapRenderer.GetCoastLandRatio(terrain) >= LandBlockRatio;
    }

    /// <summary>커서 자리를 알려 준다. 배는 이 쪽으로 나아간다.</summary>
    public void SetMouse(Point p, bool inside)
    {
        _mouse = p;
        _mouseInside = inside;
    }

    /// <summary>휠 확대. 커서 밑 지점이 제자리에 남도록 한다.</summary>
    public void Zoom(int dir, Point cursor)
    {
        double f = dir > 0 ? 1 / 1.25 : 1.25;
        double next = Math.Clamp(_cellsPerPixel * f, 1.0 / 64, 4.0);
        if (Math.Abs(next - _cellsPerPixel) < 1e-9) return;

        // 커서가 가리키던 칸을 구해 두고, 배율을 바꾼 뒤 그 칸이 같은 자리에 오도록 가운데를 민다.
        double dx = cursor.X - ActualWidth / 2, dy = cursor.Y - ActualHeight / 2;
        double atX = _centerX + dx * _cellsPerPixel, atY = _centerY + dy * _cellsPerPixel;
        _cellsPerPixel = next;
        _centerX = atX - dx * _cellsPerPixel;
        _centerY = atY - dy * _cellsPerPixel;
    }

    public void BeginDrag(Point p) { _dragging = true; _dragStart = p; _dragCx = _centerX; _dragCy = _centerY; }
    public void EndDrag() => _dragging = false;

    public void Drag(Point p)
    {
        if (!_dragging) return;
        _follow = false;   // 손으로 끌면 따라다니기를 놓는다
        _centerX = _dragCx - (p.X - _dragStart.X) * _cellsPerPixel;
        _centerY = _dragCy - (p.Y - _dragStart.Y) * _cellsPerPixel;
    }

    /// <summary>
    /// 배를 그 화면 자리로 옮긴다. 뭍이면 가장 가까운 물칸으로 밀어 넣는다.
    /// 시작 자리를 손으로 잡을 때 쓴다.
    /// </summary>
    public void PlaceShipAt(Point p)
    {
        if (!_ready) return;
        double cx = _lastOrigin.X + p.X * _lastDpiX * _cellsPerPixel;
        double cy = _lastOrigin.Y + p.Y * _lastDpiY * _cellsPerPixel;
        cy = Math.Clamp(cy, 0, WorldMapRenderer.CellH - 1);
        (cx, cy) = NearestWater(cx, cy);

        _shipX = _targetX = cx;
        _shipY = _targetY = cy;
        _shipKnown = true;
        _blocked = false;
        _tickAccum = 0;
        if (_follow) { _centerX = cx; _centerY = cy; }
    }

    /// <summary>배를 리스본 앞바다로 되돌린다.</summary>
    public void ResetToLisbon()
    {
        if (!GameMapCoords.TryCityCell(LisbonCityId, out double lx, out double ly)) return;
        (lx, ly) = NearestWater(lx, ly);
        _shipX = _targetX = lx;
        _shipY = _targetY = ly;
        _shipKnown = true;
        _blocked = false;
        _tickAccum = 0;
        _centerX = lx;
        _centerY = ly;
        _follow = true;
    }

    /// <summary>배가 있는 자리로 되돌아가 다시 따라다닌다.</summary>
    public void RecenterOnShip()
    {
        if (!_shipKnown) return;
        _centerX = _shipX;
        _centerY = _shipY;
        _follow = true;
    }
}
