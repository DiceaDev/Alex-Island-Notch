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

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern nint CreateRectRgn(int left, int top, int right, int bottom);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern nint CreateRoundRectRgn(int left, int top, int right, int bottom, int widthEllipse, int heightEllipse);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int CombineRgn(nint destination, nint source1, nint source2, int mode);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowRgn(nint hWnd, nint hRgn, [MarshalAs(UnmanagedType.Bool)] bool bRedraw);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint hObject);

    /// <summary>
    /// Región de la ventana (zona que recibe el mouse) un poco más holgada que la
    /// silueta del notch. Las regiones de Windows no tienen suavizado; por eso su
    /// borde se deja en la zona transparente, donde no se ve, y el contorno
    /// visible lo dibuja XAML con antialiasing. Todas las medidas en píxeles físicos.
    /// </summary>
    internal static void SetWindowNotchRegion(nint hwnd, int width, int height, int ear, int bottomRadius, int margin)
    {
        if (hwnd == 0 || width <= 0 || height <= 0) return;

        // Cuerpo con las esquinas inferiores redondeadas. Empieza por encima de la
        // ventana para que sus esquinas superiores (también redondeadas) queden fuera.
        var radius = Math.Max(1, bottomRadius + margin);
        var body = CreateRoundRectRgn(
            ear - margin, -2 * radius,
            width - ear + margin + 1, height + 1,
            2 * radius, 2 * radius);

        // Franja superior que cubre las "orejas" cóncavas que unen el notch al borde.
        var ears = CreateRectRgn(0, 0, width, Math.Min(height, ear + margin));

        if (body == 0 || ears == 0)
        {
            if (body != 0) DeleteObject(body);
            if (ears != 0) DeleteObject(ears);
            return;
        }

        const int RgnOr = 2;
        _ = CombineRgn(body, body, ears, RgnOr);
        DeleteObject(ears);

        // Si SetWindowRgn tiene éxito, Windows pasa a ser dueño de la región.
        if (!SetWindowRgn(hwnd, body, true))
        {
            DeleteObject(body);
        }
    }

    // ------------------------------------------------------------ Transparencia

    [StructLayout(LayoutKind.Sequential)]
    private struct DwmBlurBehind
    {
        public uint Flags;
        public int Enable;
        public nint BlurRegion;
        public int TransitionOnMaximized;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmEnableBlurBehindWindow(nint hwnd, ref DwmBlurBehind blurBehind);

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(nint hwnd, ref Margins margins);

    [DllImport("user32.dll", EntryPoint = "SetClassLongPtrW")]
    private static extern nint SetClassLongPtr(nint hwnd, int index, nint value);

    [DllImport("gdi32.dll")]
    private static extern nint GetStockObject(int index);

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint hwnd, nint hdc);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint hwnd, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern int FillRect(nint hdc, ref NativeRect rect, nint brush);

    /// <summary>
    /// Pide a DWM que respete el canal alfa de la ventana. Se usa "blur behind"
    /// con una región fuera de la ventana: activa la transparencia por píxel sin
    /// aplicar ningún desenfoque. Devuelve false si DWM no lo aceptó.
    /// </summary>
    internal static bool EnablePerPixelTransparency(nint hwnd)
    {
        if (hwnd == 0) return false;

        var region = CreateRectRgn(-2, -2, -1, -1);
        try
        {
            var margins = new Margins();
            _ = DwmExtendFrameIntoClientArea(hwnd, ref margins);

            var blur = new DwmBlurBehind
            {
                Flags = 0x1 | 0x2, // DWM_BB_ENABLE | DWM_BB_BLURREGION
                Enable = 1,
                BlurRegion = region,
            };
            var ok = DwmEnableBlurBehindWindow(hwnd, ref blur) == 0;

            // Con el canal alfa activo, el negro GDI cuenta como transparente. Windows
            // pintaba el fondo de la ventana con el pincel de su clase (blanco con el
            // tema claro): se cambia a negro y se limpia lo ya pintado.
            const int GclpHbrBackground = -10;
            const int BlackBrush = 4;
            var black = GetStockObject(BlackBrush);
            _ = SetClassLongPtr(hwnd, GclpHbrBackground, black);

            var hdc = GetDC(hwnd);
            if (hdc != 0)
            {
                if (GetClientRect(hwnd, out var rect)) _ = FillRect(hdc, ref rect, black);
                _ = ReleaseDC(hwnd, hdc);
            }

            return ok;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (region != 0) DeleteObject(region);
        }
    }

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

        EnablePerPixelTransparency(hwnd);

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
