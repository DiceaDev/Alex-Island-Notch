using System.Runtime.InteropServices;

namespace AlexIsland.App.Native;

internal static class NativeMethods
{
    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;

    private const long WsCaption = 0x00C00000L;
    private const long WsThickFrame = 0x00040000L;
    private const long WsSysMenu = 0x00080000L;
    private const long WsMinimizeBox = 0x00020000L;
    private const long WsMaximizeBox = 0x00010000L;
    private const long WsBorder = 0x00800000L;
    private const long WsDlgFrame = 0x00400000L;
    private const long WsPopup = unchecked((long)0x80000000);

    private const long WsExToolWindow = 0x00000080L;
    private const long WsExWindowEdge = 0x00000100L;
    private const long WsExClientEdge = 0x00000200L;

    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpShowWindow = 0x0040;

    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaBorderColor = 34;
    private const int DwmwcpDoNotRound = 1;
    private const int DwmColorNone = unchecked((int)0xFFFFFFFE);

    private static readonly nint HwndTopmost = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetDpiForWindow(nint hwnd);

    internal static double GetScaleFactor(nint hwnd)
    {
        if (hwnd == 0) return 1.0;
        var dpi = GetDpiForWindow(hwnd);
        return dpi >= 48 ? dpi / 96.0 : 1.0;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hwnd, out NativeRect rect);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        nint hwnd, int attribute, ref int value, int valueSize);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern nint CreatePolygonRgn(NativePoint[] lppt, int cPoints, int fnPolyFillMode);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowRgn(nint hWnd, nint hRgn, [MarshalAs(UnmanagedType.Bool)] bool bRedraw);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint hObject);

    internal static void SetWindowPolygonRegion(nint hwnd, NativePoint[] points)
    {
        if (hwnd == 0 || points == null || points.Length < 3) return;
        var rgn = CreatePolygonRgn(points, points.Length, 2); // 2 = WINDING
        if (rgn != 0)
        {
            if (!SetWindowRgn(hwnd, rgn, true))
            {
                DeleteObject(rgn);
            }
        }
    }

    internal static bool IsCursorWithinWindow(nint hwnd)
    {
        if (!GetCursorPos(out var cursor) || !GetWindowRect(hwnd, out var rect))
        {
            return false;
        }

        return cursor.X >= rect.Left && cursor.X < rect.Right
            && cursor.Y >= rect.Top && cursor.Y < rect.Bottom;
    }

    internal static void ConfigureIslandWindow(nint hwnd)
    {
        // El HWND debe ser una superficie popup sin decoración. Si quedan
        // WS_BORDER/WS_EX_WINDOWEDGE, Windows dibuja un contorno rectangular
        // que rompe la ilusión de un notch integrado en el borde del monitor.
        var style = GetWindowLongPtr(hwnd, GwlStyle).ToInt64();
        style &= ~(WsCaption | WsThickFrame | WsSysMenu | WsMinimizeBox
            | WsMaximizeBox | WsBorder | WsDlgFrame);
        style |= WsPopup;
        _ = SetWindowLongPtr(hwnd, GwlStyle, new nint(style));

        var extendedStyle = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        extendedStyle &= ~(WsExWindowEdge | WsExClientEdge);
        extendedStyle |= WsExToolWindow;
        _ = SetWindowLongPtr(hwnd, GwlExStyle, new nint(extendedStyle));

        // El contorno DWM se desactiva explícitamente. Las esquinas visibles
        // pertenecen al Border XAML, que puede suavizarlas sin recortar el HWND.
        var cornerPreference = DwmwcpDoNotRound;
        _ = DwmSetWindowAttribute(
            hwnd, DwmwaWindowCornerPreference, ref cornerPreference, sizeof(int));

        var borderColor = DwmColorNone;
        _ = DwmSetWindowAttribute(
            hwnd, DwmwaBorderColor, ref borderColor, sizeof(int));

        _ = SetWindowPos(
            hwnd,
            HwndTopmost,
            0,
            0,
            0,
            0,
            SwpNoMove | SwpNoSize | SwpNoActivate | SwpFrameChanged | SwpShowWindow);
    }
}
