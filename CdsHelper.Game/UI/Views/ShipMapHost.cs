using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Helpers;
using Vortice.DXGI;
using Vortice.Direct3D11;

namespace CdsHelper.Game.UI.Views;

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

    /// <summary>
    /// 시작 칸 — 리스본 앞바다. 열(칸 X) 1184 이고 행(칸 Y)은 357 이다.
    /// </summary>
    /// <remarks>
    /// 도시 칸(1185.5, 357.5)에서 가장 가까운 물칸을 고르면 1186 이 나오는데, 그 자리는
    /// 도시 바로 옆 강어귀라 사방이 뭍에 가깝다. 한 칸 서쪽 1184 는 강 건너 앞바다다 —
    /// 1184 는 357 행에서만 물이므로(356 행은 뭍) 행까지 같이 박아 둔다.
    /// 칸 가운데를 가리키려고 0.5 를 더한다.
    /// </remarks>
    private const double StartCellX = 1184.5, StartCellY = 357.5;

    /// <summary>
    /// 배가 화면 가장자리에서 이 점 수 안에 들어와야 화면을 다음 자리로 넘긴다(화면 실픽셀).
    /// </summary>
    /// <remarks>
    /// 예전에는 프레임마다 배를 화면 한가운데에 다시 놓았다. 배가 한 걸음 옮길 때마다
    /// 지도 전체를 새 원점으로 다시 그려야 해서, 배가 조금만 움직여도 화면이 통째로 갈렸다.
    /// 지금은 배가 가운데 여백 안을 다니는 동안 지도가 멈춰 있고 배만 그 위를 지난다 —
    /// 원점이 그대로면 <see cref="OnFrame"/> 이 아예 다시 그리지 않는다.
    /// </remarks>
    private const double EdgeMarginPixels = 200;

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

    /// <summary>바람·해류 표. 못 열면 물결도 화살표도 안 나온다(지도는 그대로 돈다).</summary>
    private WindTable? _wind;

    /// <summary>물결이 흐른 틱 수. 게임의 <c>0x00569554</c> 자리다.</summary>
    private int _rippleTick;
    private double _rippleAccum;

    /// <summary>화살표 격자를 구운 달. 달이 바뀌면 바람 표가 갈리므로 다시 굽는다.</summary>
    private int _flowMonth = -1;

    /// <summary>지금 달을 알려 주는 이. 안 주면 4월로 본다(놀이 시작 달).</summary>
    public Func<int>? MonthOf { get; set; }

    /// <summary>
    /// 바람·해류 화살표를 지도에 얹을지. 게임에는 없는 것이라 커맨드 창에서 끄고 켠다.
    /// 물결은 이것과 상관없이 늘 흐른다 — 그쪽이 원본 모습이다.
    /// </summary>
    public bool ShowFlowArrows
    {
        get => _renderer.ShowArrows;
        set
        {
            if (_renderer.ShowArrows == value) return;
            _renderer.ShowArrows = value;
            _dirty = true;
        }
    }

    // 마지막 프레임의 화면 원점. 클릭한 자리를 칸으로 되돌릴 때 쓴다.
    private (double X, double Y) _lastOrigin;
    private double _lastDpiX = 1, _lastDpiY = 1;

    /// <summary>
    /// 해안 칸은 바다와 육지가 섞여 있어서, 지날 수 있는 기준이 모드마다 다르다.
    /// </summary>
    /// <remarks>
    /// 하나로 두면 물가에서 말이 갇힌다 — 상륙한 자리 둘레가 죄다 모래톱(육지 비율이
    /// 반 미만)이라 "바다" 로 판정돼 갈 데가 없어진다. 그래서 둘로 나눴다.
    /// 배는 육지가 반을 넘으면 못 가고, 말은 육지가 조금이라도 있으면 갈 수 있다.
    /// </remarks>
    private const double SailMaxLandRatio = 0.5;
    private const double WalkMinLandRatio = 0.2;

    /// <summary>육지에 막혀 있는지. 상태 줄에 알리려고 둔다.</summary>
    private bool _blocked;

    /// <summary>상륙해 뭍에 있는지. 배 대신 말이 나오고 지날 수 있는 칸이 뒤집힌다.</summary>
    private bool _onLand;

    /// <summary>닻을 내렸는지. 내리면 그 자리에 서고, 올려야 다시 나아간다.</summary>
    private bool _anchored;

    /// <summary>지금 뭍에 있는지.</summary>
    public bool IsOnLand => _onLand;

    /// <summary>지금 정박 중인지.</summary>
    public bool IsAnchored => _anchored;

    /// <summary>
    /// 방향 번호를 통째로 돌리는 값. 뱃머리가 일정하게 어긋날 때만 손대면 된다.
    /// 게임 방향은 0 = 북, 4 = 서, 8 = 남, 12 = 동으로 <b>반시계</b>로 돈다.
    /// </summary>
    private const int HeadingZeroOffset = 0;

    /// <summary>
    /// 8방위 이름. 게임 이름표(<c>0x569790</c>)와 같은 차례로 <b>반시계</b>로 돈다.
    /// </summary>
    private static readonly string[] CompassNames =
        ["북", "북서", "서", "남서", "남", "남동", "동", "북동"];

    /// <summary>
    /// 지금 뱃머리를 8방위 이름으로. 속으로는 16방위 그대로 두고 보여줄 때만 절반으로 깎는다 —
    /// 게임도 <c>0x48ABA2</c> 에서 방위를 2로 나눠 8방위 이름표를 찾는다. 그래서 화면에 "서" 로
    /// 보여도 속은 4일 수도 5일 수도 있다.
    /// </summary>
    public string HeadingName => CompassNames[(_heading & 0xF) >> 1];

    private int _heading;                  // 그림에 쓸 게임 방향 번호(반시계, 16방위)
    private double _dirX, _dirY;           // 실제로 나아가는 쪽(단위 벡터)
    private Point _mouse;                  // 마지막 커서 자리(WPF 단위, 이 요소 기준)
    private bool _mouseInside;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private TimeSpan _lastFrame;
    private bool _spriteReady;

    /// <summary>배 그림을 올릴 때 쓰는 임시 자리(BGRA). 프레임마다 새로 잡지 않으려고 둔다.</summary>
    private readonly uint[] _spriteBuf = new uint[GameShipReader.SpriteSize];

    /// <summary>지난번에 올린 색인 그림. 같은 그림이면 다시 올리지도, 다시 그리지도 않는다.</summary>
    private byte[]? _lastIndices;

    /// <summary>텍스처에 올라가 있는 그림이 무엇인지. 같으면 게임 메모리를 읽지도 않는다.</summary>
    private (int Heading, bool OnLand, bool FromGame)? _spriteKey;

    // 지난번에 실제로 그려 낸 값. 그대로면 이번 프레임은 건너뛴다.
    private (double X, double Y) _drawnOrigin;
    private (float X, float Y, float W, float H) _drawnShip, _drawnAnchor;

    /// <summary>지난 프레임에 그린 물결 무늬 자리. 이것이 그대로면 다시 그릴 것이 없다.</summary>
    private int _drawnFlow = -1;

    /// <summary>다음 프레임은 값이 같아도 반드시 그려야 하는지. 창 크기·그림이 바뀌면 선다.</summary>
    private bool _dirty = true;

    /// <summary>
    /// 참이면 배가 서 있는다. 물음창이 떠 있는 동안 계속 나아가지 않게 하려고 둔다 —
    /// 모달 창을 띄워도 CompositionTarget.Rendering 은 그대로 돈다.
    /// </summary>
    public bool Paused { get; set; }

    /// <summary>커서를 따라 배를 몬다. 끄면 게임 함대 자리를 그대로 따라간다.</summary>
    public bool SteerWithMouse { get; set; } = true;

    /// <summary>
    /// 도시에 들어가 있는지. 참이면 지도 위에 남색 막을 씌운다 — 색을 칠하는 것이 아니라
    /// 지도가 그 밑으로 비쳐 보인다(게임도 그렇다).
    /// </summary>
    public bool InCity
    {
        get => _inCity;
        set
        {
            if (_inCity == value) return;
            _inCity = value;
            // 게임 화면에서 뽑은 남색. 짙기는 지도가 비쳐 보이는 만큼만 준다.
            _renderer.Cover = value ? (0x24 / 255f, 0x37 / 255f, 0x5B / 255f, 0.72f) : default;
            _dirty = true;
        }
    }

    private bool _inCity;

    /// <summary>
    /// 참이면 바다 명령(닻·상륙·출항·배 놓기·조종)을 받지 않는다. 도시 화면이 떠 있는
    /// 동안이 그렇다 — 게임도 도시에 들어가면 함대 명령 대신 도시 커맨드만 낸다.
    /// 막는 곳을 창이 아니라 여기에 둔 것은, 지도를 만지는 길이 여럿이라
    /// (마우스·커맨드 창·조작 줄) 부르는 쪽마다 검사를 흩어 놓으면 하나씩 새기 때문이다.
    /// </summary>
    public bool SeaBlocked => _inCity;

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

        // 칸을 지날 수 있는지는 게임 표가 가른다. 못 읽으면 옛 어림으로 물러선다.
        _terrain = TerrainTable.Open(gameDir);
        if (_terrain == null)
            System.Diagnostics.Debug.WriteLine($"[ShipMap] 지형표 없음: {TerrainTable.LastError}");

        var ocean = OceanTiles.LoadFromDirectory(gameDir);
        if (ocean == null) { Status = $"OCEAN.CDS 를 읽지 못했습니다 ({OceanTiles.LastError})"; return false; }

        _renderer.Initialize(world, ocean);

        // 닻. 배 그림과 같은 48x48 이라 배가 놓일 자리에 그대로 겹치면 된다.
        var anchorSprite = AnchorSprite.LoadFromDirectory(gameDir);
        if (anchorSprite == null)
            System.Diagnostics.Debug.WriteLine($"[ShipMap] 닻 없음: {AnchorSprite.LastError}");
        else
        {
            _anchorPixels = [.. anchorSprite.Pixels];    // 덧그림을 나눠 쓰므로 들고 있는다
            _renderer.SetOverlay(_anchorPixels);
        }

        // 바람·해류. 못 열어도 지도는 그대로 돈다 — 물결이 안 일고 화살표가 안 나올 뿐이다.
        _wind = WindTable.Open(gameDir);
        if (_wind == null)
            System.Diagnostics.Debug.WriteLine($"[ShipMap] 바람표 없음: {WindTable.LastError}");
        else
            _renderer.SetRippleTiles(_wind.BuildRippleTiles(_terrain));

        // 구름. 없으면 안 뜰 뿐 나머지는 그대로 돈다.
        var clouds = CloudSprites.LoadFromDirectory(gameDir);
        if (clouds == null)
            System.Diagnostics.Debug.WriteLine($"[ShipMap] 구름 없음: {CloudSprites.LastError}");
        else
            _renderer.SetCloudSprites(clouds.Bgra);

        // 배는 리스본 앞바다에서 시작한다.
        var (sx, sy) = LisbonStart();
        _shipX = _targetX = sx;
        _shipY = _targetY = sy;
        _centerX = sx;
        _centerY = sy;
        _shipKnown = true;

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
        _dirty = true;   // 새 백버퍼는 비어 있다 — 값이 같아도 한 번은 그려야 한다
    }

    /// <summary>
    /// 자식 창이 지워졌으면(가려졌다 드러나거나 창을 옮겼을 때) 한 번은 다시 그린다.
    /// 값이 그대로라고 건너뛰면 지워진 자리가 그대로 남는다.
    /// </summary>
    protected override IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WmPaint = 0x000F;
        if (msg == WmPaint) _dirty = true;
        return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
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

        int flowKey = UpdateFlow(dt);

        // 커서를 칸 좌표로 옮기려면 이번 프레임의 원점이 필요하다. 배를 옮기기 전 값으로 잡는다.
        var origin = (_centerX - w / 2.0 * _cellsPerPixel, _centerY - h / 2.0 * _cellsPerPixel);
        _lastOrigin = origin;
        _lastDpiX = dpi.DpiScaleX;
        _lastDpiY = dpi.DpiScaleY;
        UpdateShip(dt, origin, dpi.DpiScaleX, dpi.DpiScaleY);

        // 배를 따라간다 — 가장자리에 다가왔을 때만 화면을 넘긴다.
        if (_follow && _shipKnown) FollowShip(w, h);
        origin = (_centerX - w / 2.0 * _cellsPerPixel, _centerY - h / 2.0 * _cellsPerPixel);

        var rect = _shipKnown && _spriteReady ? SpriteRectAt(_shipX, _shipY, origin)
                                             : (0f, 0f, 0f, 0f);

        // 덧그림 한 장을 두 가지로 나눠 쓴다. 둘이 같이 뜰 일은 없다 — 상륙하면 닻이 풀린다.
        //   정박 중  닻. 그림이 배와 같은 48x48 이고 그 안에서 왼쪽 아래에만 찍혀 있으므로
        //           배와 같은 자리에 겹치면 게임처럼 배 왼쪽 아래에 걸린다.
        //   뭍에 있을 때  대 둔 배. 어디로 상륙했는지 그 자리에 남는다.
        var overlay = (0f, 0f, 0f, 0f);
        if (_anchored && _shipKnown && _spriteReady) overlay = rect;
        else if (_onLand && _moored) overlay = SpriteRectAt(_mooredX, _mooredY, origin);
        SyncOverlaySprite();

        // 지난 프레임과 똑같으면 그리지 않는다. 배는 0.1초에 한 걸음씩 옮기고 지도는
        // 가장자리에 닿아야 넘어가므로, 60fps 로 도는 동안 거의 다 같은 그림이다.
        if (!_dirty && origin == _drawnOrigin && rect == _drawnShip && overlay == _drawnAnchor
            && flowKey == _drawnFlow) return;

        _renderer.RenderTo(_backBufferView, w, h, origin, (_cellsPerPixel, _cellsPerPixel), rect, overlay);
        _swapChain!.Present(1, PresentFlags.None);
        _drawnOrigin = origin;
        _drawnShip = rect;
        _drawnAnchor = overlay;
        _drawnFlow = flowKey;
        _dirty = false;
        FrameCount++;
    }

    /// <summary>
    /// 칸 좌표에 48x48 그림 한 장이 놓일 화면 사각형. 게임에서 한 칸이 16점이니 세 칸이다.
    /// </summary>
    private (float X, float Y, float W, float H) SpriteRectAt(
        double cellX, double cellY, (double X, double Y) origin)
    {
        float size = (float)(3.0 / _cellsPerPixel);
        return ((float)((cellX - origin.X) / _cellsPerPixel - size / 2),
                (float)((cellY - origin.Y) / _cellsPerPixel - size / 2),
                size, size);
    }

    // ── 대 둔 배 ────────────────────────────────────────────────────────────

    /// <summary>상륙하며 배를 대 둔 자리와 그때의 뱃머리.</summary>
    private double _mooredX, _mooredY;
    private int _mooredHeading;
    private bool _moored;

    /// <summary>덧그림 자리에 지금 무엇이 올라가 있는지.</summary>
    private enum OverlayArt { None, Anchor, MooredShip }

    private OverlayArt _overlayArt = OverlayArt.None;
    private uint[]? _anchorPixels;

    /// <summary>
    /// 덧그림을 지금 쓸 것으로 갈아 끼운다. 닻과 대 둔 배가 같은 자리를 나눠 쓰므로,
    /// 무엇이 올라가야 하는지 바뀔 때만 텍스처를 올린다.
    /// </summary>
    private void SyncOverlaySprite()
    {
        var want = _anchored ? OverlayArt.Anchor
                 : _onLand && _moored ? OverlayArt.MooredShip
                 : OverlayArt.None;
        if (want == _overlayArt || want == OverlayArt.None) { _overlayArt = want; return; }

        if (want == OverlayArt.Anchor)
        {
            if (_anchorPixels != null) _renderer.SetOverlay(_anchorPixels);
        }
        else
        {
            // 대 둔 배는 상륙할 때의 뱃머리 그대로 둔다. 게임 그림이 아니라 asset 것을 쓴다 —
            // 살아 있는 게임 함대를 읽는 길(GameShipReader)은 지금 뱃머리만 내주므로
            // 대 둔 배의 방향을 물을 수가 없다.
            var frame = ShipSprites.Frame(_mooredHeading, onLand: false);
            if (!frame.IsEmpty) _renderer.SetOverlay(frame);
        }
        _overlayArt = want;
        _dirty = true;
    }

    /// <summary>표를 못 열었을 때 볼 달. 놀이가 시작하는 달이다.</summary>
    private const int DefaultMonth = 4;

    /// <summary>
    /// 물결과 화살표 자료를 이번 프레임 것으로 맞춘다. 돌려주는 값은 <b>물결 무늬가 선 자리</b>라,
    /// 이것이 지난 프레임과 같으면 다시 그릴 것이 없다.
    /// </summary>
    /// <remarks>
    /// 게임은 항해 루프를 한 번 돌 때마다 틱을 하나 올린다(<c>0x0048EF82</c>). 여기서도 한
    /// 걸음(<see cref="TickSeconds"/>)과 같은 길이로 올린다 — 60fps 로 올리면 물결이 게임보다
    /// 여섯 배 빨리 흐르고, 프레임마다 지도를 다시 그리게 된다.
    ///
    /// 무늬는 <c>세기 x 틱 x 16 / 64</c> 만큼 흐르므로 세기가 1 이면 네 틱에 한 칸이다.
    /// 그 몫이 바뀔 때만 다시 그린다.
    /// </remarks>
    private int UpdateFlow(double dt)
    {
        if (_wind == null) return 0;

        // 도시 창이 떠 있는 동안에는 물결도 구름도 선다 — 지도가 남색 막 아래로 물러난 채
        // 혼자 흐르면 어색하다. 커맨드 창이나 물음창 때문에 멈춘 동안(<see cref="Paused"/>)에는
        // 그대로 흐른다. 게임도 그때는 하늘이 살아 있다.
        int ticks = 0;
        if (!_inCity)
        {
            _rippleAccum += dt;
            while (_rippleAccum >= TickSeconds) { _rippleAccum -= TickSeconds; _rippleTick++; ticks++; }
        }

        int month = MonthOf?.Invoke() ?? DefaultMonth;
        if (month != _flowMonth) { _flowMonth = month; RefreshFlowGrid(month); }

        // 게임은 함대가 선 칸의 바람·해류 하나로 화면 전체를 흘린다. 여기서도 그대로 한다.
        int cell = WindTable.CellOf((int)(_shipX * OceanTiles.TileW), (int)(_shipY * OceanTiles.TileW));
        var flow = cell < 0 ? default : _wind.CurrentAt(cell);
        var (dx, dy) = _wind.Vector(flow.Dir);
        _renderer.Ripple = (dx, dy, flow.Speed, _rippleTick);

        var wind = cell < 0 ? default : _wind.WindAt(cell, month);
        UpdateClouds(wind.Dir, wind.Speed, ticks);

        // 구름이 떠 있으면 틱마다 자리가 달라지므로 틱 자체가 곧 그림이다.
        if (_cloudCount > 0) return _rippleTick;
        return (flow.Dir << 26) | (flow.Speed << 22) | ((flow.Speed * _rippleTick / 4) & 0x3FFFFF);
    }

    /// <summary>
    /// 화살표가 읽을 50x25 격자를 굽는다. 달이 바뀔 때만 부른다.
    /// </summary>
    /// <remarks>
    /// 표에는 뭍 칸에도 값이 들어 있다 — 격자 한 칸이 지도 50x50 칸(경위도 7.2도)이라
    /// 대륙 한가운데도 제 방위를 갖는다. 그대로 그리면 아메리카 복판에 바람 화살표가 뜬다.
    /// 배가 갈 데가 아니니 <b>물이 넉넉한 칸만</b> 남긴다.
    /// </remarks>
    private void RefreshFlowGrid(int month)
    {
        if (_wind == null) return;
        var grid = new uint[WindTable.Count];
        for (int i = 0; i < WindTable.Count; i++)
        {
            if (!WorthDrawing(i)) continue;   // 0 이면 세기가 0 이라 셰이더가 안 그린다
            grid[i] = Pack(_wind.WindAt(i, month)) | (Pack(_wind.CurrentAt(i)) << 16);
        }
        _renderer.SetFlowGrid(grid);
        _dirty = true;
    }

    /// <summary>격자 한 칸에서 물이 이만큼은 돼야 화살표를 그린다.</summary>
    private const double ArrowMinWaterRatio = 0.25;

    /// <summary>격자 한 칸을 몇 칸 걸러 재는지. 50x50 을 다 보지 않아도 비율은 나온다.</summary>
    private const int ArrowProbeStep = 5;

    private bool WorthDrawing(int flowCell)
    {
        if (_world == null || _terrain == null) return true;   // 못 재면 다 그린다

        int cellsPerSide = WindTable.CellRaw / OceanTiles.TileW;    // 800 / 16 = 50
        int x0 = flowCell % WindTable.Cols * cellsPerSide;
        int y0 = flowCell / WindTable.Cols * cellsPerSide;

        int water = 0, total = 0;
        for (int y = 0; y < cellsPerSide; y += ArrowProbeStep)
            for (int x = 0; x < cellsPerSide; x += ArrowProbeStep)
            {
                total++;
                if (_terrain.CanSail(_world[RawAt(x0 + x, y0 + y).Offset])) water++;
            }
        return water >= total * ArrowMinWaterRatio;
    }

    /// <summary>화살표가 읽는 낱말. 방위와 세기를 게임 표와 같은 자리에 넣는다.</summary>
    private static uint Pack(WindTable.Flow f) => (uint)(f.Dir | (f.Speed << 4));

    // ── 구름 ─────────────────────────────────────────────────────────────────

    /// <summary>구름 한 장의 지금 상태. 자리는 <b>게임 점</b>(칸당 16점) 기준이다.</summary>
    private struct Cloud { public int X, Y, AccX, AccY, Shape; }

    private readonly Cloud[] _cloudState = new Cloud[MapD3DRenderer.MaxClouds];
    private readonly MapD3DRenderer.CloudDraw[] _cloudDraw =
        new MapD3DRenderer.CloudDraw[MapD3DRenderer.MaxClouds];
    private bool _cloudsPlaced;
    private int _cloudCount;
    private readonly Random _cloudRng = new();

    /// <summary>구름 여섯의 밑그림 번호(<c>0x00519C70</c>). 작은 것 셋, 큰 것 셋이다.</summary>
    private static readonly int[] CloudBase = [3, 3, 3, 0, 0, 0];

    /// <summary>구름 여섯의 속도 배수(<c>0x00519C88</c>). 큰 것이 조금 빠르다.</summary>
    private static readonly int[] CloudSpeed = [3, 3, 3, 4, 4, 4];

    /// <summary>그림 넘김표(<c>0x00519CA0</c>). 색인은 틱마다 (색인+1) % 3 으로 돈다.</summary>
    private static readonly int[] CloudShape = [0, 1, 2, 1];

    /// <summary>게임 화면 크기. 구름 몇 장이 어울리는지 이것으로 견준다.</summary>
    private const double CloudRefW = 640, CloudRefH = 480;

    /// <summary>이보다 작아지면 안 그린다. 멀리서 보면 점 여섯 개라 티끌만 남는다.</summary>
    private const double CloudMinPixels = 48;

    /// <summary>게임 한 점이 덮는 칸 수. 게임은 칸 하나를 16점으로 그린다.</summary>
    private const double GamePixelsPerCell = OceanTiles.TileW;

    /// <summary>
    /// 구름을 한 틱 흘리고 이번 프레임에 그릴 자리를 renderer 에 건넨다.
    /// </summary>
    /// <remarks>
    /// 게임(<c>0x004893D0</c>)은 구름을 <b>화면 좌표</b>로 들고 640x480 을 돌린다. 여기서는
    /// 지도를 키우고 줄일 수 있으니 <b>게임 점</b>(칸당 16점)으로 들고 있다가 그릴 때만
    /// 배율을 곱한다 — 그래야 구름 한 장이 늘 지도 10칸 x 7.5칸을 덮어, 게임에서 보던
    /// 크기 그대로다. 장 수도 보이는 넓이에 맞춰 줄인다(640x480 에 여섯 장 꼴).
    /// </remarks>
    private void UpdateClouds(int windDir, int windSpeed, int ticks)
    {
        double scale = 1.0 / (_cellsPerPixel * GamePixelsPerCell);   // 실픽셀 / 게임점
        if (scale <= 0 || _pixelW <= 0 || _pixelH <= 0
            || CloudSprites.Width * scale < CloudMinPixels)
        {
            _cloudCount = 0;
            _renderer.SetClouds(default);
            return;
        }

        int gw = Math.Max(1, (int)(_pixelW / scale));
        int gh = Math.Max(1, (int)(_pixelH / scale));
        _cloudCount = Math.Clamp(
            (int)Math.Round(MapD3DRenderer.MaxClouds * (gw * (double)gh) / (CloudRefW * CloudRefH)),
            1, MapD3DRenderer.MaxClouds);

        if (!_cloudsPlaced) PlaceClouds(gw, gh);

        var (vx, vy) = _wind!.Vector(windDir);
        for (int t = 0; t < ticks; t++)
            for (int i = 0; i < _cloudCount; i++)
                DriftCloud(ref _cloudState[i], vx * windSpeed * CloudSpeed[i],
                           vy * windSpeed * CloudSpeed[i], gw, gh);

        for (int i = 0; i < _cloudCount; i++)
            _cloudDraw[i] = new MapD3DRenderer.CloudDraw(
                (float)(_cloudState[i].X * scale), (float)(_cloudState[i].Y * scale),
                CloudBase[i] + CloudShape[_cloudState[i].Shape], (float)scale);
        _renderer.SetClouds(_cloudDraw.AsSpan(0, _cloudCount));
    }

    /// <summary>게임과 같이 3열로 벌려 놓는다(<c>0x0048906B</c>).</summary>
    private void PlaceClouds(int gw, int gh)
    {
        for (int i = 0; i < _cloudState.Length; i++)
        {
            _cloudState[i] = new Cloud { X = i % 3 * 128 % gw, Y = i / 3 * 128 % gh };
            FixParity(ref _cloudState[i]);
        }
        _cloudsPlaced = true;
    }

    private void DriftCloud(ref Cloud c, int stepX, int stepY, int gw, int gh)
    {
        c.AccX += stepX;
        c.AccY += stepY;
        while (c.AccX >= WindTable.VectorLength) { c.AccX -= WindTable.VectorLength; c.X++; }
        while (c.AccX <= -WindTable.VectorLength) { c.AccX += WindTable.VectorLength; c.X--; }
        while (c.AccY >= WindTable.VectorLength) { c.AccY -= WindTable.VectorLength; c.Y++; }
        while (c.AccY <= -WindTable.VectorLength) { c.AccY += WindTable.VectorLength; c.Y--; }

        // 화면 밖으로 나가면 반대쪽 끝에서 아무 자리로 다시 들어온다(0x00489456~).
        int w = CloudSprites.Width, h = CloudSprites.Height;
        if (c.X <= -w) { c.X = gw - 1; c.Y = _cloudRng.Next(gh) - (h - 1); }
        else if (c.X >= gw) { c.X = -(w - 1); c.Y = _cloudRng.Next(gh) - (h - 1); }
        if (c.Y <= -h) { c.Y = gh - 1; c.X = _cloudRng.Next(gw) - (w - 1); }
        else if (c.Y >= gh) { c.Y = -(h - 1); c.X = _cloudRng.Next(gw) - (w - 1); }

        FixParity(ref c);
        c.Shape = (c.Shape + 1) % 3;
    }

    /// <summary>
    /// <c>x + y</c> 를 짝수로 맞춘다(<c>0x004890CF</c>). 구름은 바둑판으로 반만 찍힌
    /// 반투명 그림이라, 격자 짝이 어긋나면 무늬가 뭉개진다.
    /// </summary>
    private static void FixParity(ref Cloud c)
    {
        if (((c.X + c.Y) & 1) != 0) c.X++;
    }

    /// <summary>
    /// 배가 화면 가장자리 <see cref="EdgeMarginPixels"/> 점 안에 들어왔으면 화면을 다음
    /// 자리로 넘긴다. 여백 안에 있는 동안에는 화면을 그대로 둔다.
    /// </summary>
    private void FollowShip(int w, int h)
    {
        // 창이 여백 두 겹보다 좁으면 여백이 화면을 다 먹는다 — 반보다는 작게 잡는다.
        double mx = Math.Min(EdgeMarginPixels, w / 2.0 - 1);
        double my = Math.Min(EdgeMarginPixels, h / 2.0 - 1);

        double sx = w / 2.0 + WrapDx(_shipX - _centerX) / _cellsPerPixel;
        double sy = h / 2.0 + (_shipY - _centerY) / _cellsPerPixel;
        if (sx >= mx && sx <= w - mx && sy >= my && sy <= h - my) return;

        // 넘길 때는 배를 화면 한가운데에 놓는다. 어느 쪽으로 가든 다시 여백에 닿을 때까지
        // 반 화면이 남으므로, 가장자리를 스치듯 지나도 화면이 들썩이지 않는다.
        _centerX = _shipX;
        _centerY = _shipY;
    }

    /// <summary>가로로 이어진 지도에서 가장 가까운 쪽으로 잰 가로 차이.</summary>
    private static double WrapDx(double dx) =>
        dx - Math.Floor(dx / WorldMapRenderer.UnfoldedW + 0.5) * WorldMapRenderer.UnfoldedW;

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
            Status = $"{(_onLand ? "말" : "배")} {_shipX:F1}, {_shipY:F1} 칸 · 방향 {HeadingName} · " +
                     (_anchored ? "닻을 내리고 정박 중"
                               : _blocked ? (_onLand ? "바다에 막혔습니다" : "육지에 막혔습니다")
                               : !_mouseInside ? "가던 쪽으로"
                               : _onLand ? "커서 쪽으로 이동 중" : "커서 쪽으로 항해 중") +
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
            _spriteKey = null;   // 이쪽에서 올린 그림은 우리 뱃머리와 무관하다 — 돌아가면 다시 올린다
            return;
        }

        // 게임이 떠 있으면 그 그림을(함선 종류에 맞는 4벌 중 하나), 아니면 asset/ship 의 것을 쓴다.
        // 같은 그림이면 게임 메모리를 읽지도, 텍스처를 올리지도 않는다 — 뱃머리가 그대로면
        // 프레임마다 할 일이 없다.
        var key = (_heading, _onLand, _ship.IsAttached);
        if (_spriteKey == key) return;

        var indices = _ship.IsAttached ? _ship.TryReadSprite(_heading, _onLand) : null;
        if (indices != null) UploadGameSprite(indices);
        else
        {
            var frame = ShipSprites.Frame(_heading, _onLand);
            if (!frame.IsEmpty)
            {
                _renderer.SetSprite(frame);
                _spriteReady = true;
                _lastIndices = null;
                _dirty = true;
            }
        }
        _spriteKey = key;
    }

    /// <summary>게임에서 읽은 팔레트 색인 그림을 색으로 풀어 올린다. 색인 0 은 비침이다.</summary>
    private void UploadGameSprite(byte[] indices)
    {
        // 같은 그림이면 아무것도 하지 않는다. 게임 함대를 따라가는 쪽은 방향을 우리가 모르므로
        // 그림 자체를 견줘야 안다(2304바이트뿐이라 프레임마다 견줘도 싸다).
        if (_lastIndices != null && _lastIndices.AsSpan().SequenceEqual(indices)) return;
        _lastIndices = [.. indices];
        _dirty = true;

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
    /// 커서는 방향만 정한다 — 커서 자리에 도착해서 멈추는 것도, 창 밖으로 나갔다고
    /// 서는 것도 아니다. 한 번 뱃머리를 잡으면 막힐 때까지 그 쪽으로 간다.
    /// </summary>
    private void Sail(double dt)
    {
        if (Paused) { _tickAccum = 0; return; }

        // 닻을 내렸으면 그 자리에 선다. 뱃머리도 그대로 둬서 닻을 올리면 가던 쪽으로 다시 간다.
        if (_anchored) { _tickAccum = 0; return; }

        // 커서가 창 밖으로 나가도 배는 가던 쪽으로 계속 간다. 커서는 방향을 바꿀 때만 쓴다.
        double dx = _targetX - _shipX, dy = _targetY - _shipY;
        if (_mouseInside && dx * dx + dy * dy > TurnDeadZoneCells * TurnDeadZoneCells)
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

    /// <summary>
    /// 게임 지형표. 칸을 지날 수 있는지는 이것이 가른다.
    /// </summary>
    private TerrainTable? _terrain;

    /// <summary>지금 모드에서 지날 수 있는 칸인지. 배는 물을, 말은 뭍을 간다.</summary>
    /// <remarks>
    /// 게임이 쓰는 표를 그대로 본다(<see cref="TerrainTable"/>) — 칸의 <b>첫 바이트</b>로
    /// 부류를 찾고, 0·1 이면 물, 2 이상이면 뭍이다.
    ///
    /// 예전에는 그림을 그리려고 재어 둔 육지 비율이 반을 넘으면 막았다. 그것은 색을 섞는
    /// 비율이지 통행 규칙이 아니어서, 런던 앞 하구처럼 육지가 50~55% 인 칸이 막혀
    /// 게임에서는 들어가지는 데를 못 들어갔다. 표를 못 읽을 때만 그 어림으로 물러선다.
    /// </remarks>
    private bool CanGo(double cellX, double cellY)
    {
        if (_terrain != null && _world != null)
        {
            byte low = _world[RawAt(cellX, cellY).Offset];
            return _onLand ? _terrain.CanWalk(low) : _terrain.CanSail(low);
        }

        double land = LandRatioAt(cellX, cellY);
        return _onLand ? land >= WalkMinLandRatio : land < SailMaxLandRatio;
    }

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
        if (_terrain != null && _world != null)
            return !_terrain.CanSail(_world[RawAt(cellX, cellY).Offset]);
        return LandRatioAt(cellX, cellY) >= SailMaxLandRatio;
    }

    /// <summary>그 칸의 육지 비율(0 이면 온통 바다, 1 이면 온통 육지). 지도 밖은 육지로 본다.</summary>
    private double LandRatioAt(double cellX, double cellY)
    {
        if (_world == null) return 0;
        if (cellY < 0 || cellY >= WorldMapRenderer.CellH) return 1;

        int off = RawAt(cellX, cellY).Offset;
        byte terrain = (byte)(_world[off] & 0x7F);
        if (terrain == 0) return 0;   // 바다
        if (terrain == 1) return 1;   // 육지
        return WorldMapRenderer.GetCoastLandRatio(terrain);   // 해안 칸
    }

    /// <summary>
    /// 칸 좌표가 WORLD.CDS 파일 안에서 놓인 자리. 파일은 2500바이트 행이 2500줄이고,
    /// 짝수 행이 지도의 왼쪽 절반, 홀수 행이 오른쪽 절반이다(한 칸 2바이트).
    /// </summary>
    private static (int CellX, int CellY, int Row, int Col, int Offset) RawAt(double cellX, double cellY)
    {
        int cx = (int)Math.Floor(cellX);
        int cy = (int)Math.Floor(cellY);
        cx -= (int)Math.Floor(cx / (double)WorldMapRenderer.UnfoldedW) * WorldMapRenderer.UnfoldedW;
        cy = Math.Clamp(cy, 0, WorldMapRenderer.CellH - 1);

        bool right = cx >= WorldMapRenderer.CellW;
        int col = right ? cx - WorldMapRenderer.CellW : cx;
        int row = cy * 2 + (right ? 1 : 0);
        return (cx, cy, row, col, row * WorldMapRenderer.RawStride + col * 2);
    }

    /// <summary>한 칸이 WORLD.CDS 안에서 어디에 어떻게 적혀 있는지. 좌표 겹쳐 보기에 쓴다.</summary>
    public readonly record struct CellInfo(
        double X, double Y, int CellX, int CellY,
        int Row, int Col, int Offset, byte Terrain, byte Attr, int Tile, double LandRatio);

    /// <summary>지금 배가 선 칸.</summary>
    public CellInfo? ShipCell => _shipKnown ? Describe(_shipX, _shipY) : null;

    /// <summary>커서가 가리키는 칸. 커서가 창 밖이면 null.</summary>
    public CellInfo? MouseCell => _mouseInside && _ready
        ? Describe(_lastOrigin.X + _mouse.X * _lastDpiX * _cellsPerPixel,
                   _lastOrigin.Y + _mouse.Y * _lastDpiY * _cellsPerPixel)
        : null;

    /// <summary>그 자리의 칸 정보를 모아 준다. 지도를 아직 안 읽었으면 값이 0 이다.</summary>
    private CellInfo? Describe(double cellX, double cellY)
    {
        if (_world == null) return null;
        var (cx, cy, row, col, off) = RawAt(cellX, cellY);
        byte terrain = (byte)(_world[off] & 0x7F);
        return new CellInfo(cellX, cellY, cx, cy, row, col, off,
                            terrain, _world[off + 1],
                            WorldMapRenderer.CellToTile(_world, off),
                            LandRatioAt(cellX, cellY));
    }

    /// <summary>
    /// 닻을 내리거나 올린다. 내리면 배가 그 자리에서 즉시 서고, 다시 올리면 가던 쪽으로 간다.
    /// 뭍에서는(말) 내릴 닻이 없고, 도시에 들어가 있는 동안도 받지 않는다
    /// (<see cref="SeaBlocked"/>).
    /// </summary>
    /// <returns>이제 정박 중이면 true.</returns>
    public bool ToggleAnchor()
    {
        if (SeaBlocked || _onLand) return false;
        _anchored = !_anchored;
        _tickAccum = 0;
        return _anchored;
    }

    /// <summary>
    /// 커서 자리를 알려 준다. 배는 이 쪽으로 나아간다. 도시에 들어가 있는 동안은
    /// 커서가 지도 위에 있어도 없는 셈 친다 — 도시 화면 옆을 스쳐도 뱃머리가 돌지 않게.
    /// </summary>
    public void SetMouse(Point p, bool inside)
    {
        _mouse = p;
        _mouseInside = inside && !SeaBlocked;
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
        if (!_ready || SeaBlocked) return;
        double cx = _lastOrigin.X + p.X * _lastDpiX * _cellsPerPixel;
        double cy = _lastOrigin.Y + p.Y * _lastDpiY * _cellsPerPixel;
        cy = Math.Clamp(cy, 0, WorldMapRenderer.CellH - 1);
        (cx, cy) = NearestWater(cx, cy);

        _shipX = _targetX = cx;
        _shipY = _targetY = cy;
        _shipKnown = true;
        _blocked = false;
        _anchored = false;
        _tickAccum = 0;
        if (_follow) { _centerX = cx; _centerY = cy; }
    }

    /// <summary>시작 칸. 혹시 뭍이면(WORLD.CDS 가 다르면) 가장 가까운 물칸으로 밀어 낸다.</summary>
    private (double X, double Y) LisbonStart() => NearestWater(StartCellX, StartCellY);

    /// <summary>
    /// 배를 그 도시 앞바다에 갖다 놓는다(불러오기에 쓴다). 도시 번호가 표에 없으면 false.
    /// </summary>
    public bool PlaceAtCity(int cityId)
    {
        if (!_ready) return false;
        if (!GameMapCoords.TryCityCell(cityId, out double cx, out double cy)) return false;

        (cx, cy) = NearestWater(cx, cy);   // 도시 칸은 뭍이라 앞바다로 밀어 낸다
        _shipX = _targetX = cx;
        _shipY = _targetY = cy;
        _shipKnown = true;
        _blocked = false;
        _anchored = false;
        _onLand = false;
        _tickAccum = 0;
        _dirX = _dirY = 0;                 // 뱃머리를 놓아 그 자리에 선다
        _centerX = cx;
        _centerY = cy;
        _follow = true;
        _dirty = true;
        return true;
    }

    /// <summary>배를 리스본 앞바다로 되돌린다.</summary>
    public void ResetToLisbon()
    {
        var (lx, ly) = LisbonStart();
        _shipX = _targetX = lx;
        _shipY = _targetY = ly;
        _shipKnown = true;
        _blocked = false;
        _anchored = false;
        _tickAccum = 0;
        _centerX = lx;
        _centerY = ly;
        _follow = true;
    }

    /// <summary>
    /// 배 둘레 <paramref name="radiusCells"/> 칸 안에 뭍이 있는지. 상륙할 수 있는 자리인지 볼 때 쓴다.
    /// </summary>
    public bool IsNearLand(int radiusCells = 2) => IsNear(true, radiusCells);

    /// <summary>배 둘레에 물이 있는지. 뭍에서 출항할 수 있는 자리인지 볼 때 쓴다.</summary>
    public bool IsNearWater(int radiusCells = 2) => IsNear(false, radiusCells);

    private bool IsNear(bool land, int radiusCells)
    {
        if (!_shipKnown) return false;
        for (int dy = -radiusCells; dy <= radiusCells; dy++)
            for (int dx = -radiusCells; dx <= radiusCells; dx++)
                if (IsLand(_shipX + dx, _shipY + dy) == land) return true;
        return false;
    }

    /// <summary>
    /// 상륙. 가장 가까운 뭍으로 한 칸 올라서고 말로 바뀐다. 지날 수 있는 칸도 뒤집힌다.
    /// </summary>
    public bool Land()
    {
        if (SeaBlocked || _onLand) return false;
        var spot = NearestCell(_shipX, _shipY, wantLand: true, maxRing: 3);
        if (spot == null) return false;

        // 배는 지금 자리에 대 둔다 — 뭍에 있는 동안 그 자리에 남아 어디로 상륙했는지 보인다.
        // 게임도 자리를 적어 두었다가 출항할 때 그대로 되돌린다(0x004936DE).
        _mooredX = _shipX;
        _mooredY = _shipY;
        _mooredHeading = _heading;
        _moored = true;

        (_shipX, _shipY) = spot.Value;
        _targetX = _shipX;
        _targetY = _shipY;
        _onLand = true;
        _blocked = false;
        _anchored = false;
        _tickAccum = 0;
        if (_follow) { _centerX = _shipX; _centerY = _shipY; }
        return true;
    }

    /// <summary>출항. 가장 가까운 물칸으로 내려가 배로 돌아간다.</summary>
    public bool Embark()
    {
        if (SeaBlocked || !_onLand) return false;

        // 대 둔 자리로 돌아간다 — 게임도 상륙할 때 적어 둔 자리를 그대로 되돌린다
        // (0x004936DE). 그 자리가 물이 아니게 됐으면(있을 수 없지만) 가까운 물칸으로 간다.
        var spot = _moored && !IsLand(_mooredX, _mooredY)
            ? (_mooredX, _mooredY)
            : NearestCell(_shipX, _shipY, wantLand: false, maxRing: 3);
        if (spot == null) return false;

        _moored = false;
        (_shipX, _shipY) = spot.Value;
        _targetX = _shipX;
        _targetY = _shipY;
        _onLand = false;
        _blocked = false;
        _anchored = false;
        _tickAccum = 0;
        if (_follow) { _centerX = _shipX; _centerY = _shipY; }
        return true;
    }

    /// <summary>둘레를 한 칸씩 넓혀 가며 원하는 쪽(뭍/물) 칸을 찾는다. 없으면 null.</summary>
    private (double X, double Y)? NearestCell(double cx, double cy, bool wantLand, int maxRing)
    {
        if (IsLand(cx, cy) == wantLand) return (cx, cy);
        for (int r = 1; r <= maxRing; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Abs(dx) != r && Math.Abs(dy) != r) continue;
                    double x = cx + dx, y = cy + dy;
                    if (y < 0 || y >= WorldMapRenderer.CellH) continue;
                    if (IsLand(x, y) == wantLand) return (x, y);
                }
        return null;
    }

    /// <summary>
    /// 접안으로 치는 거리. 배가 항구 칸에서 이만큼 안에 들어와야 입항을 묻는다.
    /// </summary>
    /// <remarks>
    /// 예전에는 도시 <b>중심</b>에서 6칸이었다. 도시 중심은 뭍이라 배가 닿을 수 없는 자리인데다
    /// 6칸이면 화면으로 200점 가까이 떨어진 먼바다여서, 접안하기 한참 전에 물음창이 떴다.
    /// 그래서 기준을 도시 앞 항구 칸으로 옮기고 거리도 배 한 척 길이만큼으로 줄였다.
    /// </remarks>
    private const double DockRadiusCells = 2.0;

    /// <summary>도시 중심이 이보다 멀면 항구 칸을 구해 볼 것도 없다. 거르는 데만 쓴다.</summary>
    private const double HarborSearchCells = 8;

    /// <summary>도시 ID -> 항구 칸(도시에서 가장 가까운 물칸). 한 번 구하면 들고 있는다.</summary>
    private readonly Dictionary<int, (double X, double Y)> _harbors = [];

    /// <summary>
    /// 배가 접안한 도시 ID. 없으면 -1. 자리는 게임 원본 도시 표에서 구한 항구 칸이다.
    /// </summary>
    public int NearestCity(double radiusCells = DockRadiusCells) => NearestDock(radiusCells).Id;

    /// <summary>뭍에서 도시 어귀로 치는 거리(칸). 도시 칸 자체를 재므로 짧다.</summary>
    private const double TownRadiusCells = 2.0;

    /// <summary>
    /// 말이 닿은 도시 ID. 없으면 -1. 배와 달리 <b>도시 칸</b>까지의 거리를 잰다 —
    /// 뭍에서는 항구 칸이 아니라 마을 자체로 들어가기 때문이다.
    /// </summary>
    public int NearestTown(double radiusCells = TownRadiusCells)
    {
        if (!_shipKnown) return -1;
        int best = -1;
        double bestD = radiusCells * radiusCells;
        for (int id = 0; id < GameMapCoords.CityCount; id++)
        {
            if (!GameMapCoords.TryCityCell(id, out double cx, out double cy)) continue;
            double d = DistanceSq(cx, cy);
            if (d < bestD) { bestD = d; best = id; }
        }
        return best;
    }

    /// <summary>
    /// 배에 가장 가까운 항구와 그 거리(칸). <paramref name="radiusCells"/> 밖이면 ID 가 -1 이다.
    /// 반지름은 꼭 적는다 — 넓을수록 도시마다 항구 칸을 찾아야 해서 무거워진다.
    /// </summary>
    public (int Id, double Cells) NearestDock(double radiusCells)
    {
        if (!_shipKnown) return (-1, double.NaN);
        int best = -1;
        double bestD = radiusCells * radiusCells;
        double coarse = radiusCells + HarborSearchCells;
        double coarseSq = coarse * coarse;

        for (int id = 0; id < GameMapCoords.CityCount; id++)
        {
            if (!GameMapCoords.TryCityCell(id, out double cx, out double cy)) continue;
            // 먼 도시는 항구 칸을 찾을 것도 없이 도시 중심만으로 거른다 — 항구 찾기가 무겁다.
            if (DistanceSq(cx, cy) > coarseSq) continue;

            var harbor = Harbor(id, cx, cy);
            double d = DistanceSq(harbor.X, harbor.Y);
            if (d < bestD) { bestD = d; best = id; }
        }
        return (best, best < 0 ? double.NaN : Math.Sqrt(bestD));
    }

    /// <summary>배가 닿을 수 있는 도시 앞 물칸. 도시 중심이 뭍이므로 한 칸씩 넓혀 가며 찾는다.</summary>
    private (double X, double Y) Harbor(int id, double cityX, double cityY)
    {
        if (_harbors.TryGetValue(id, out var cached)) return cached;
        var spot = NearestWater(cityX, cityY);
        _harbors[id] = spot;
        return spot;
    }

    /// <summary>배에서 그 칸까지 거리의 제곱. 가로가 이어져 있는 것을 셈에 넣는다.</summary>
    private double DistanceSq(double cellX, double cellY)
    {
        double dx = cellX - _shipX;
        if (dx > WorldMapRenderer.UnfoldedW / 2.0) dx -= WorldMapRenderer.UnfoldedW;
        if (dx < -WorldMapRenderer.UnfoldedW / 2.0) dx += WorldMapRenderer.UnfoldedW;
        double dy = cellY - _shipY;
        return dx * dx + dy * dy;
    }

    /// <summary>입항. 배를 세우고 알린다.</summary>
    public void EnterPort(string cityName)
    {
        _tickAccum = 0;
        _dirX = _dirY = 0;          // 뱃머리를 놓아 그 자리에 선다
        Status = $"[{cityName}] 입항 — {_shipX:F1}, {_shipY:F1} 칸";
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
