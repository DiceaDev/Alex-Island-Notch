using System.Runtime.InteropServices;
using AlexIsland.Core.Display;

namespace AlexIsland.App.Native;

/// <summary>
/// Franja que la isla reserva arriba de un monitor. Se registra en Windows como una
/// barra de escritorio (igual que la barra de tareas), así las ventanas maximizadas
/// empiezan debajo y la isla no tapa sus pestañas. Se pinta del color de lo que tiene
/// debajo para que no quede un espacio vacío.
/// </summary>
internal sealed class TopBandWindow : IDisposable
{
    private const string ClassName = "AlexIslandTopBand";
    private const uint CallbackMessage = 0x8000 + 0x31; // WM_APP + 0x31

    private const uint AbmNew = 0, AbmRemove = 1, AbmQueryPos = 2, AbmSetPos = 3;
    private const uint AbeTop = 1;
    private const int AbnPosChanged = 1, AbnFullScreenApp = 2;

    private static WndProcDelegate? s_wndProc;
    private static readonly Dictionary<nint, TopBandWindow> s_windows = [];
    private static readonly object s_registeredLock = new();
    private static readonly HashSet<nint> s_registered = [];

    private nint _brush;
    private int _color = -1;
    private bool _registered;
    private bool _hiddenForFullscreen;
    private bool _disposed;
    private MonitorArea? _monitor;
    private int _height;

    public nint Hwnd { get; }

    /// <summary>Zona de pantalla donde quedó la franja (para leer el color de abajo).</summary>
    public (int X, int Y, int Width, int Height) Bounds { get; private set; }

    /// <summary>Oculta mientras una app ocupa el monitor en pantalla completa.</summary>
    public bool IsHidden => _hiddenForFullscreen || _disposed;

    /// <summary>La posición de la franja cambió (otra barra se movió o se volvió a ubicar).</summary>
    public event EventHandler? Repositioned;

    public TopBandWindow(int initialColor)
    {
        RegisterWindowClass();

        const uint WsExTopmost = 0x00000008, WsExToolWindow = 0x00000080, WsExNoActivate = 0x08000000;
        const uint WsPopup = 0x80000000;

        Hwnd = CreateWindowExW(WsExTopmost | WsExToolWindow | WsExNoActivate, ClassName, "Alex Island franja",
            WsPopup, 0, 0, 1, 1, 0, 0, GetModuleHandleW(null), 0);
        if (Hwnd == 0) throw new InvalidOperationException("No se pudo crear la franja superior: " + Marshal.GetLastWin32Error());

        s_windows[Hwnd] = this;
        SetColor(initialColor);

        var data = NewData();
        data.uCallbackMessage = CallbackMessage;
        _registered = SHAppBarMessage(AbmNew, ref data) != 0;
        App.Log($"Franja superior: registro en Windows {(_registered ? "aceptado" : "rechazado")}.");
        if (!_registered)
        {
            DestroyWindow(Hwnd);
            throw new InvalidOperationException("Windows no aceptó la franja superior.");
        }
        lock (s_registeredLock) s_registered.Add(Hwnd);
    }

    /// <summary>Ubica la franja arriba del monitor; si nada cambió, no molesta a las demás ventanas.</summary>
    public void Place(MonitorArea monitor, int height)
    {
        if (_disposed) return;
        if (_monitor == monitor && _height == height) return;
        _monitor = monitor;
        _height = height;
        Reposition(force: true);
    }

    private void Reposition(bool force)
    {
        if (_disposed || _monitor is null) return;

        var data = NewData();
        data.uEdge = AbeTop;
        data.rc = new Rect { Left = _monitor.X, Top = _monitor.Y, Right = _monitor.X + _monitor.Width, Bottom = _monitor.Y + _height };
        SHAppBarMessage(AbmQueryPos, ref data);
        data.rc.Bottom = data.rc.Top + _height;

        var bounds = (data.rc.Left, data.rc.Top, data.rc.Right - data.rc.Left, data.rc.Bottom - data.rc.Top);
        // Cada franja avisa a las demás al ubicarse: si esta no cambió, no se vuelve a
        // ubicar, o las franjas de varios monitores se avisarían entre sí sin fin.
        if (!force && bounds == Bounds) return;

        SHAppBarMessage(AbmSetPos, ref data);
        App.Log($"Franja superior: reservada en x={data.rc.Left} y={data.rc.Top} ancho={bounds.Item3} alto={bounds.Item4}.");
        Bounds = bounds;
        UpdateFullscreen();

        const uint SwpNoActivate = 0x0010, SwpShowWindow = 0x0040, SwpHideWindow = 0x0080;
        SetWindowPos(Hwnd, HwndTopmost, Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height,
            SwpNoActivate | (_hiddenForFullscreen ? SwpHideWindow : SwpShowWindow));
        Repositioned?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Cambia el color (0xRRGGBB). Se llama en el hilo de la interfaz.</summary>
    public void SetColor(int rgb)
    {
        if (_disposed || rgb == _color) return;
        _color = rgb;

        // GDI usa 0x00BBGGRR.
        var colorRef = (uint)(((rgb & 0xFF) << 16) | (rgb & 0xFF00) | ((rgb >> 16) & 0xFF));
        var old = _brush;
        _brush = CreateSolidBrush(colorRef);
        if (old != 0) DeleteObject(old);
        InvalidateRect(Hwnd, 0, true);
    }

    private void UpdateFullscreen()
    {
        _hiddenForFullscreen = NativeMethods.IsFullscreenAppActive(Hwnd);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Release(Hwnd);
        s_windows.Remove(Hwnd);
        DestroyWindow(Hwnd);
        if (_brush != 0) DeleteObject(_brush);
    }

    /// <summary>Devuelve a Windows el espacio de todas las franjas. Seguro de llamar al salir de la app.</summary>
    public static void ReleaseAll()
    {
        nint[] hwnds;
        lock (s_registeredLock) hwnds = [.. s_registered];
        foreach (var hwnd in hwnds) Release(hwnd);
    }

    private static void Release(nint hwnd)
    {
        lock (s_registeredLock)
        {
            if (!s_registered.Remove(hwnd)) return;
        }
        var data = new AppBarData { cbSize = (uint)Marshal.SizeOf<AppBarData>(), hWnd = hwnd };
        SHAppBarMessage(AbmRemove, ref data);
    }

    private AppBarData NewData() => new() { cbSize = (uint)Marshal.SizeOf<AppBarData>(), hWnd = Hwnd };

    private void OnAppBarNotification(int code, nint lParam)
    {
        switch (code)
        {
            case AbnPosChanged:
                Reposition(force: false);
                break;
            case AbnFullScreenApp:
                var hidden = NativeMethods.IsFullscreenAppActive(Hwnd);
                if (hidden == _hiddenForFullscreen) break;
                _hiddenForFullscreen = hidden;
                ShowWindow(Hwnd, hidden ? 0 /* SW_HIDE */ : 4 /* SW_SHOWNOACTIVATE */);
                Repositioned?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    private static void RegisterWindowClass()
    {
        if (s_wndProc is not null) return;
        s_wndProc = WndProc;
        var wc = new WndClassEx
        {
            cbSize = (uint)Marshal.SizeOf<WndClassEx>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(s_wndProc),
            hInstance = GetModuleHandleW(null),
            hCursor = LoadCursorW(0, 32512 /* IDC_ARROW */),
            lpszClassName = ClassName,
        };
        if (RegisterClassExW(ref wc) == 0)
        {
            s_wndProc = null;
            throw new InvalidOperationException("No se pudo registrar la franja superior: " + Marshal.GetLastWin32Error());
        }
    }

    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        const uint WmPaint = 0x000F, WmEraseBackground = 0x0014, WmMouseActivate = 0x0021;
        s_windows.TryGetValue(hwnd, out var band);

        switch (msg)
        {
            case WmEraseBackground:
                return 1;

            case WmPaint when band is not null:
                var hdc = BeginPaint(hwnd, out var ps);
                FillRect(hdc, ref ps.rcPaint, band._brush);
                EndPaint(hwnd, ref ps);
                return 0;

            case WmMouseActivate:
                return 3; // MA_NOACTIVATE: un clic en la franja no le quita el foco a nadie

            case CallbackMessage when band is not null:
                band.OnAppBarNotification((int)wParam, lParam);
                return 0;
        }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    // ---------------------------------------------------------------- Win32

    private static readonly nint HwndTopmost = -1;

    private delegate nint WndProcDelegate(nint hwnd, uint msg, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct AppBarData
    {
        public uint cbSize;
        public nint hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public Rect rc;
        public nint lParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PaintStruct
    {
        public nint hdc;
        public int fErase;
        public Rect rcPaint;
        public int fRestore;
        public int fIncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] rgbReserved;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassEx
    {
        public uint cbSize;
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public nint hIconSm;
    }

    [DllImport("shell32.dll")]
    private static extern nuint SHAppBarMessage(uint message, ref AppBarData data);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandleW(string? name);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassExW(ref WndClassEx wc);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowExW(uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [DllImport("user32.dll")]
    private static extern nint DefWindowProcW(nint hwnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern nint LoadCursorW(nint instance, nint cursorName);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint hwnd, int command);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InvalidateRect(nint hwnd, nint rect, [MarshalAs(UnmanagedType.Bool)] bool erase);

    [DllImport("user32.dll")]
    private static extern nint BeginPaint(nint hwnd, out PaintStruct paint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EndPaint(nint hwnd, ref PaintStruct paint);

    [DllImport("user32.dll")]
    private static extern int FillRect(nint hdc, ref Rect rect, nint brush);

    [DllImport("gdi32.dll")]
    private static extern nint CreateSolidBrush(uint color);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint obj);
}
