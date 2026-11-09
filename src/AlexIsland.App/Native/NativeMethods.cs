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





    // ------------------------------------------------------------ DPI

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);

    /// <summary>Factor de escala del monitor donde está la ventana (1.0 = 96 DPI).</summary>
    internal static double GetDpiScale(nint hwnd)
    {
        if (hwnd == 0) return 1.0;
        try
        {
            var dpi = GetDpiForWindow(hwnd);
            return dpi > 0 ? dpi / 96.0 : 1.0;
        }
        catch
        {
            return 1.0;
        }
    }

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(nint monitor, int dpiType, out uint dpiX, out uint dpiY);

    /// <summary>Factor de escala de un monitor (1.0 = 96 DPI).</summary>
    internal static double GetMonitorDpiScale(nint monitor)
    {
        try
        {
            return GetDpiForMonitor(monitor, 0 /* MDT_EFFECTIVE_DPI */, out var dpi, out _) == 0 && dpi > 0 ? dpi / 96.0 : 1.0;
        }
        catch
        {
            return 1.0;
        }
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowPos")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPosRaw(nint hwnd, nint insertAfter, int x, int y, int cx, int cy, uint flags);

    /// <summary>Pone la ventana encima de las demás "siempre visibles", sin moverla ni activarla.</summary>
    internal static void BringToTopmostFront(nint hwnd)
    {
        const uint SwpNoSize = 0x0001, SwpNoMove = 0x0002, SwpNoActivate = 0x0010;
        if (hwnd != 0) SetWindowPosRaw(hwnd, -1 /* HWND_TOPMOST */, 0, 0, 0, 0, SwpNoSize | SwpNoMove | SwpNoActivate);
    }

    // ------------------------------------------------------------ Mostrar / ocultar

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint hwnd, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint hwnd);

    /// <summary>Muestra u oculta la isla sin quitarle el foco a la aplicación activa.</summary>
    internal static void SetIslandVisible(nint hwnd, bool visible)
    {
        if (hwnd == 0 || IsWindowVisible(hwnd) == visible) return;

        _ = ShowWindow(hwnd, visible ? 4 /* SW_SHOWNOACTIVATE */ : 0 /* SW_HIDE */);
        if (visible)
        {
            _ = SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
        }
    }

    // ------------------------------------------------------------ Pantalla completa

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);

    // ------------------------------------------------------------ Monitores

    private delegate bool MonitorEnumProc(nint monitor, nint hdc, nint rect, nint data);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(nint hdc, nint clip, MonitorEnumProc callback, nint data);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromPoint(NativePoint point, uint flags);

    /// <summary>Monitores conectados, en píxeles físicos.</summary>
    internal static IReadOnlyList<AlexIsland.Core.Display.MonitorArea> GetMonitors()
    {
        const uint MonitorInfoPrimary = 1;
        var monitors = new List<AlexIsland.Core.Display.MonitorArea>();

        EnumDisplayMonitors(0, 0, (monitor, _, _, _) =>
        {
            var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfoW(monitor, ref info))
            {
                monitors.Add(new AlexIsland.Core.Display.MonitorArea(
                    monitor,
                    info.Monitor.Left,
                    info.Monitor.Top,
                    info.Monitor.Right - info.Monitor.Left,
                    info.Monitor.Bottom - info.Monitor.Top,
                    (info.Flags & MonitorInfoPrimary) != 0));
            }
            return true;
        }, 0);

        return monitors;
    }

    /// <summary>Monitor donde está la ventana.</summary>
    internal static nint GetWindowMonitor(nint hwnd) => MonitorFromWindow(hwnd, 2 /* el más cercano */);

    /// <summary>Monitor bajo el cursor, o 0 si no se puede saber.</summary>
    internal static nint GetCursorMonitor() =>
        GetCursorPos(out var point) ? MonitorFromPoint(point, 2 /* el más cercano */) : 0;

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern nint GetShellWindow();

    [DllImport("user32.dll")]
    private static extern nint GetDesktopWindow();

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfoW(nint monitor, ref MonitorInfo info);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(nint hwnd, System.Text.StringBuilder className, int maxCount);

    /// <summary>
    /// Indica si hay una aplicación en pantalla completa (juego, video,
    /// presentación) en el mismo monitor que la isla.
    /// </summary>
    internal static bool IsFullscreenAppActive(nint islandHwnd)
    {
        try
        {
            // Juegos Direct3D y modo presentación los informa Windows directamente.
            if (SHQueryUserNotificationState(out var state) == 0 && state is 3 or 4)
            {
                return true;
            }

            var foreground = GetForegroundWindow();
            if (foreground == 0 || foreground == islandHwnd
                || foreground == GetShellWindow() || foreground == GetDesktopWindow())
            {
                return false;
            }

            var className = new System.Text.StringBuilder(64);
            _ = GetClassNameW(foreground, className, className.Capacity);
            var name = className.ToString();
            if (name is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
            {
                return false; // escritorio o barra de tareas
            }

            const uint MonitorDefaultToNearest = 2;
            var monitor = MonitorFromWindow(foreground, MonitorDefaultToNearest);
            if (monitor == 0 || monitor != MonitorFromWindow(islandHwnd, MonitorDefaultToNearest))
            {
                return false;
            }

            var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfoW(monitor, ref info) || !GetWindowRect(foreground, out var rect))
            {
                return false;
            }

            return rect.Left <= info.Monitor.Left && rect.Top <= info.Monitor.Top
                && rect.Right >= info.Monitor.Right && rect.Bottom >= info.Monitor.Bottom;
        }
        catch
        {
            return false;
        }
    }

    // ------------------------------------------------------------ Idioma del teclado

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern nint GetKeyboardLayout(uint threadId);

    /// <summary>
    /// Ventana activa y distribución de teclado (HKL) de su hilo. En Windows cada
    /// aplicación puede tener su propio idioma, por eso se informan juntos.
    /// </summary>
    internal static (nint Window, nint Layout) GetForegroundKeyboardLayout()
    {
        try
        {
            var window = GetForegroundWindow();
            if (window == 0) return (0, 0);
            var thread = GetWindowThreadProcessId(window, out _);
            return (window, thread == 0 ? 0 : GetKeyboardLayout(thread));
        }
        catch
        {
            return (0, 0);
        }
    }

    /// <summary>
    /// Indica si el cursor está sobre la ventana, descontando márgenes (en píxeles
    /// físicos) que no cuentan como parte de la isla, como la zona de su sombra.
    /// </summary>
    internal static bool IsCursorWithinWindow(nint hwnd, int insetLeft = 0, int insetRight = 0, int insetBottom = 0)
    {
        if (!GetCursorPos(out var cursor) || !GetWindowRect(hwnd, out var rect))
        {
            return false;
        }

        return cursor.X >= rect.Left + insetLeft && cursor.X < rect.Right - insetRight
            && cursor.Y >= rect.Top && cursor.Y < rect.Bottom - insetBottom;
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
