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

    private IDXGISwapChain1? _swapChain;
    private ID3D11RenderTargetView? _backBufferView;
    private IntPtr _hwnd;
    private int _pixelW, _pixelH;
    private bool _ready;

    /// <summary>화면 한 점이 나아가는 칸 수. 작을수록 확대.</summary>
    private double _cellsPerPixel = 1.0 / 16;   // 칸당 16점 = 타일 원본 해상도

    /// <summary>화면 한가운데가 가리키는 칸 좌표.</summary>
    private double _centerX = 1185, _centerY = 357;

    private bool _follow = true;
    private bool _dragging;
    private Point _dragStart;
    private double _dragCx, _dragCy;

    // 좌표 표본은 띄엄띄엄 오므로 그 사이를 채워 부드럽게 움직인다.
    private double _shipX, _shipY;      // 지금 그리는 자리
    private double _targetX, _targetY;  // 마지막으로 읽은 자리
    private bool _shipKnown;

    public string Status { get; private set; } = "";

    /// <summary>Present 까지 마친 프레임 수. 진짜 그려지고 있는지 밖에서 볼 때 쓴다.</summary>
    public long FrameCount { get; private set; }

    /// <summary>스왑체인을 걸다 난 문제. 없으면 빈 문자열.</summary>
    public string SwapChainError { get; private set; } = "";

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
        var ocean = OceanTiles.LoadFromDirectory(gameDir);
        if (ocean == null) { Status = $"OCEAN.CDS 를 읽지 못했습니다 ({OceanTiles.LastError})"; return false; }

        _renderer.Initialize(world, ocean);
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

        UpdateShip();

        // 배를 따라간다 — 화면 한가운데에 둔다.
        if (_follow && _shipKnown) { _centerX = _shipX; _centerY = _shipY; }

        var origin = (_centerX - w / 2.0 * _cellsPerPixel, _centerY - h / 2.0 * _cellsPerPixel);

        var rect = (0f, 0f, 0f, 0f);
        if (_shipKnown)
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

    private void UpdateShip()
    {
        var cell = _ship.TryReadCell();
        if (cell != null)
        {
            _targetX = cell.Value.CellX;
            _targetY = cell.Value.CellY;
            if (!_shipKnown) { _shipX = _targetX; _shipY = _targetY; _shipKnown = true; }
            var spr = _ship.TryReadSprite();
            if (spr != null) _renderer.SetSprite(spr);
            Status = $"함대 {_targetX:F1}, {_targetY:F1} 칸";
        }
        else
        {
            Status = _ship.IsAttached ? "게임이 아직 항해 중이 아닙니다" : "게임(cds_95)이 떠 있지 않습니다";
        }

        if (!_shipKnown) return;
        // 읽은 자리로 조금씩 따라붙는다. 표본이 띄엄띄엄 와도 화면에서는 이어져 보인다.
        const double k = 0.15;
        _shipX += (_targetX - _shipX) * k;
        _shipY += (_targetY - _shipY) * k;
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

    /// <summary>배가 있는 자리로 되돌아가 다시 따라다닌다.</summary>
    public void RecenterOnShip()
    {
        if (!_shipKnown) return;
        _centerX = _shipX;
        _centerY = _shipY;
        _follow = true;
    }
}
