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
    /// </summary>
    public static void SendClickRelative(IntPtr hWnd, int clientX, int clientY)
    {
        var (ox, oy) = GetClientOrigin(hWnd);
        SendClick(ox + clientX, oy + clientY);
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
}
