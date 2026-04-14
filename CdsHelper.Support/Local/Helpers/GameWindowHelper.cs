using System.Drawing;
using System.Runtime.InteropServices;

namespace CdsHelper.Support.Local.Helpers;

/// <summary>
/// Win32 P/Invoke 기반 게임 윈도우 탐색, 화면 캡처, 마우스 제어.
/// </summary>
public static class GameWindowHelper
{
    #region P/Invoke

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, char[] lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool LogicalToPhysicalPoint(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int width, int height,
        IntPtr hdcSrc, int xSrc, int ySrc, uint rop);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X, Y;
    }

    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const int SW_RESTORE = 9;
    private const uint SRCCOPY = 0x00CC0020;
    private const uint PW_CLIENTONLY = 0x1;
    private const uint WM_LBUTTONDOWN = 0x0201;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint MK_LBUTTON = 0x0001;
    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_KEYUP = 0x0101;

    // 숫자패드 Virtual Key Codes
    private const int VK_NUMPAD1 = 0x61;
    private const int VK_NUMPAD2 = 0x62;
    private const int VK_NUMPAD3 = 0x63;
    private const int VK_NUMPAD4 = 0x64;
    private const int VK_NUMPAD5 = 0x65;
    private const int VK_NUMPAD6 = 0x66;
    private const int VK_NUMPAD7 = 0x67;
    private const int VK_NUMPAD8 = 0x68;
    private const int VK_NUMPAD9 = 0x69;

    private const int VK_RETURN = 0x0D;
    private const int VK_UP     = 0x26;
    private const int VK_DOWN   = 0x28;

    #endregion

    /// <summary>
    /// 게임 윈도우 핸들을 찾는다. 타이틀에 partialTitle이 포함된 첫 번째 윈도우를 반환.
    /// </summary>
    public static IntPtr FindGameWindow(string partialTitle = "大航海時代")
    {
        IntPtr found = IntPtr.Zero;
        var buffer = new char[256];

        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd))
                return true;

            var len = GetWindowText(hWnd, buffer, buffer.Length);
            if (len <= 0)
                return true;

            var title = new string(buffer, 0, len);
            if (title.Contains(partialTitle, StringComparison.OrdinalIgnoreCase))
            {
                found = hWnd;
                return false; // stop enumeration
            }

            return true;
        }, IntPtr.Zero);

        return found;
    }

    /// <summary>
    /// 클라이언트 영역의 스크린 좌표 원점을 반환한다.
    /// </summary>
    public static (int x, int y) GetClientOrigin(IntPtr hWnd)
    {
        var pt = new POINT { X = 0, Y = 0 };
        ClientToScreen(hWnd, ref pt);
        return (pt.X, pt.Y);
    }

    /// <summary>
    /// 클라이언트 영역 크기를 반환한다.
    /// </summary>
    public static (int width, int height) GetClientSize(IntPtr hWnd)
    {
        GetClientRect(hWnd, out var rect);
        return (rect.Width, rect.Height);
    }

    /// <summary>
    /// 게임 윈도우 클라이언트 영역을 캡처하여 Bitmap으로 반환한다.
    /// </summary>
    public static Bitmap? CaptureClient(IntPtr hWnd)
    {
        GetClientRect(hWnd, out var clientRect);
        var width = clientRect.Width;
        var height = clientRect.Height;

        if (width <= 0 || height <= 0)
            return null;

        var bmp = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(bmp);
        var hdcBmp = g.GetHdc();

        // PrintWindow로 캡처 (최소화 상태에서도 동작)
        if (!PrintWindow(hWnd, hdcBmp, PW_CLIENTONLY))
        {
            // 실패 시 BitBlt fallback
            var hdcSrc = GetDC(hWnd);
            BitBlt(hdcBmp, 0, 0, width, height, hdcSrc, 0, 0, SRCCOPY);
            ReleaseDC(hWnd, hdcSrc);
        }

        g.ReleaseHdc(hdcBmp);
        return bmp;
    }

    /// <summary>
    /// 게임 윈도우를 전면으로 가져온다.
    /// </summary>
    public static void BringToFront(IntPtr hWnd)
    {
        ShowWindow(hWnd, SW_RESTORE);
        SetForegroundWindow(hWnd);
    }

    /// <summary>
    /// 스크린 절대좌표로 마우스를 이동한다.
    /// </summary>
    public static void MoveCursor(int screenX, int screenY)
    {
        SetCursorPos(screenX, screenY);
    }

    /// <summary>
    /// 윈도우 상대좌표(클라이언트 좌표)를 스크린 좌표로 변환하여 마우스 이동.
    /// </summary>
    public static void MoveCursorRelative(IntPtr hWnd, int clientX, int clientY)
    {
        var (ox, oy) = GetClientOrigin(hWnd);
        SetCursorPos(ox + clientX, oy + clientY);
    }

    /// <summary>
    /// 스크린 절대좌표에서 좌클릭을 수행한다.
    /// </summary>
    public static void SendClick(int screenX, int screenY)
    {
        SetCursorPos(screenX, screenY);
        Thread.Sleep(20);
        mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(20);
        mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
    }

    /// <summary>
    /// 윈도우 상대좌표(클라이언트 좌표)에서 좌클릭을 수행한다.
    /// LogicalToPhysicalPoint로 DPI 스케일링을 정확히 보정한다.
    /// </summary>
    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hMonitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    public static void SendClickRelative(IntPtr hWnd, int clientX, int clientY)
    {
        var (ox, oy) = GetClientOrigin(hWnd);

        // 모니터 실제 DPI로 스케일 계산
        var hMon = MonitorFromWindow(hWnd, 2); // MONITOR_DEFAULTTONEAREST
        GetDpiForMonitor(hMon, 0, out uint dpiX, out _); // MDT_EFFECTIVE_DPI
        double scale = dpiX / 96.0;

        // origin은 이미 물리 좌표, client는 가상 좌표 → 곱해서 물리 오프셋으로 변환
        int screenX = ox + (int)(clientX * scale);
        int screenY = oy + (int)(clientY * scale);
        SendClick(screenX, screenY);
    }

    /// <summary>
    /// PostMessage로 클라이언트 좌표에 좌클릭을 보낸다.
    /// DPI 스케일링과 무관하게 정확한 좌표에 클릭된다.
    /// </summary>
    public static void PostClickClient(IntPtr hWnd, int clientX, int clientY)
    {
        IntPtr lParam = (IntPtr)((clientY << 16) | (clientX & 0xFFFF));
        PostMessage(hWnd, WM_LBUTTONDOWN, (IntPtr)MK_LBUTTON, lParam);
        Thread.Sleep(20);
        PostMessage(hWnd, WM_LBUTTONUP, IntPtr.Zero, lParam);
    }

    /// <summary>
    /// 스크린 절대좌표에서 우클릭을 수행한다.
    /// </summary>
    public static void SendRightClick(int screenX, int screenY)
    {
        SetCursorPos(screenX, screenY);
        Thread.Sleep(20);
        mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(20);
        mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, UIntPtr.Zero);
    }

    /// <summary>
    /// 숫자패드 키(1~9)를 게임 윈도우에 전송한다.
    /// 7=NW 8=N 9=NE / 4=W 5=Stop 6=E / 1=SW 2=S 3=SE
    /// </summary>
    public static void SendNumpadKey(IntPtr hWnd, int numpadNumber)
    {
        if (numpadNumber < 1 || numpadNumber > 9) return;
        var vk = VK_NUMPAD1 + (numpadNumber - 1);
        PostMessage(hWnd, WM_KEYDOWN, (IntPtr)vk, IntPtr.Zero);
        Thread.Sleep(30);
        PostMessage(hWnd, WM_KEYUP, (IntPtr)vk, IntPtr.Zero);
    }

    /// <summary>
    /// 지정한 Virtual Key를 게임 윈도우에 전송한다.
    /// </summary>
    public static void SendKey(IntPtr hWnd, int vk)
    {
        PostMessage(hWnd, WM_KEYDOWN, (IntPtr)vk, IntPtr.Zero);
        Thread.Sleep(30);
        PostMessage(hWnd, WM_KEYUP, (IntPtr)vk, IntPtr.Zero);
    }

    /// <summary>Down 화살표 키 전송</summary>
    public static void SendDownKey(IntPtr hWnd) => SendKey(hWnd, VK_DOWN);

    /// <summary>Up 화살표 키 전송</summary>
    public static void SendUpKey(IntPtr hWnd) => SendKey(hWnd, VK_UP);

    /// <summary>Enter 키 전송</summary>
    public static void SendEnterKey(IntPtr hWnd) => SendKey(hWnd, VK_RETURN);

    /// <summary>
    /// 캡처된 화면에서 도시 안인지 판별한다.
    /// 도시 안이면 화면 중앙에 고정 크기의 장식 액자 프레임이 존재한다.
    /// 프레임의 좌/우 세로 테두리 위치에서 어두운 픽셀을 샘플링하여 판정.
    /// </summary>
    public static bool IsInCity(Bitmap bitmap)
    {
        int w = bitmap.Width;
        int h = bitmap.Height;
        if (w < 200 || h < 200) return false;

        // 프레임은 화면 중앙에 고정 크기로 위치
        // 좌 테두리: x ≈ 27%, 우 테두리: x ≈ 77%
        // 세로 범위: y 40%~75% (프레임 내부 영역)
        int leftX = w * 27 / 100;
        int rightX = w * 77 / 100;

        int leftDark = 0;
        int rightDark = 0;
        int sampleCount = 0;

        for (int pct = 40; pct <= 75; pct += 5)
        {
            int y = h * pct / 100;
            sampleCount++;

            // 좌 테두리: leftX 부근 ±3px 범위에서 어두운 픽셀 확인
            if (IsDarkAt(bitmap, leftX, y, w, h) ||
                IsDarkAt(bitmap, leftX - 2, y, w, h) ||
                IsDarkAt(bitmap, leftX + 2, y, w, h))
                leftDark++;

            // 우 테두리
            if (IsDarkAt(bitmap, rightX, y, w, h) ||
                IsDarkAt(bitmap, rightX - 2, y, w, h) ||
                IsDarkAt(bitmap, rightX + 2, y, w, h))
                rightDark++;
        }

        // 좌/우 테두리 모두 절반 이상 샘플에서 감지되면 도시 안
        return leftDark >= sampleCount / 2 && rightDark >= sampleCount / 2;
    }

    private static bool IsDarkAt(Bitmap bmp, int x, int y, int w, int h)
    {
        if (x < 0 || x >= w || y < 0 || y >= h) return false;
        var p = bmp.GetPixel(x, y);
        return p.R < 90 && p.G < 80 && p.B < 70;
    }

    /// <summary>
    /// 방위각(0~360)을 숫자패드 방향키로 변환.
    /// 0°=N(8), 45°=NE(9), 90°=E(6), 135°=SE(3), 180°=S(2), 225°=SW(1), 270°=W(4), 315°=NW(7)
    /// </summary>
    public static int BearingToNumpad(double bearing)
    {
        bearing = ((bearing % 360) + 360) % 360;
        // 8방향: 각 45° 구간, ±22.5° 범위
        return bearing switch
        {
            >= 337.5 or < 22.5   => 8, // N
            >= 22.5 and < 67.5   => 9, // NE
            >= 67.5 and < 112.5  => 6, // E
            >= 112.5 and < 157.5 => 3, // SE
            >= 157.5 and < 202.5 => 2, // S
            >= 202.5 and < 247.5 => 1, // SW
            >= 247.5 and < 292.5 => 4, // W
            _                    => 7  // NW
        };
    }
}
