using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;

namespace AlexIsland.App.Native;

/// <summary>
/// Ventana propia de la isla: un popup Win32 compuesto con transparencia por
/// píxel (WS_EX_NOREDIRECTIONBITMAP) que aloja el XAML con DesktopWindowXamlSource.
///
/// Una ventana normal de WinUI pinta su propio fondo y obligaba a recortarla con
/// una región, que no tiene suavizado: las curvas del notch se veían en escalera.
/// Aquí no hay fondo ni región, así que el antialiasing de XAML se mezcla con el
/// escritorio, igual que en Nusa (que usa DirectComposition directamente).
/// </summary>
internal sealed class IslandHostWindow : IDisposable
{
    private const string ClassName = "AlexIslandHost";

    private static WindowsXamlManager? s_xamlManager;
    private static WndProcDelegate? s_wndProc;
    private static readonly Dictionary<nint, IslandHostWindow> s_windows = [];

    private readonly DesktopWindowXamlSource _xamlSource;
    private bool _closed;

    public nint Hwnd { get; }

    public WindowId WindowId => Win32Interop.GetWindowIdFromWindow(Hwnd);

    /// <summary>La ventana se destruyó (por Close o porque Windows la cerró).</summary>
    public event EventHandler? Closed;

    /// <summary>Se conectó, desconectó o reordenó alguna pantalla.</summary>
    public event EventHandler? DisplayChanged;

    public IslandHostWindow(UIElement content, int width, int height)
    {
        // Necesario antes de crear DesktopWindowXamlSource en este hilo.
        s_xamlManager ??= WindowsXamlManager.InitializeForCurrentThread();

        RegisterWindowClass();

        const uint WsExNoRedirectionBitmap = 0x00200000;
        const uint WsExTopmost = 0x00000008;
        const uint WsExToolWindow = 0x00000080;
        const uint WsPopup = 0x80000000;

        Hwnd = CreateWindowExW(
            WsExNoRedirectionBitmap | WsExTopmost | WsExToolWindow,
            ClassName, "Alex Island", WsPopup,
            0, 0, Math.Max(1, width), Math.Max(1, height),
            0, 0, GetModuleHandleW(null), 0);
        if (Hwnd == 0)
        {
            throw new InvalidOperationException("No se pudo crear la ventana de la isla: " + Marshal.GetLastWin32Error());
        }

        s_windows[Hwnd] = this;

        _xamlSource = new DesktopWindowXamlSource();
        _xamlSource.Initialize(WindowId);
        _xamlSource.SiteBridge.MoveAndResize(new Windows.Graphics.RectInt32(0, 0, width, height));
        _xamlSource.Content = content;
    }

    /// <summary>Muestra la ventana sin quitarle el foco a la aplicación activa.</summary>
    public void Show()
    {
        const int SwShowNoActivate = 4;
        ShowWindow(Hwnd, SwShowNoActivate);
        _xamlSource.SiteBridge.Show();
    }

    /// <summary>Tamaño en píxeles físicos.</summary>
    public void Resize(int width, int height)
    {
        if (_closed) return;
        width = Math.Max(1, width);
        height = Math.Max(1, height);

        const uint SwpNoMove = 0x0002, SwpNoZOrder = 0x0004, SwpNoActivate = 0x0010;
        SetWindowPos(Hwnd, 0, 0, 0, width, height, SwpNoMove | SwpNoZOrder | SwpNoActivate);
        _xamlSource.SiteBridge.MoveAndResize(new Windows.Graphics.RectInt32(0, 0, width, height));
    }

    /// <summary>Posición en píxeles físicos de pantalla.</summary>
    public void Move(int x, int y)
    {
        if (_closed) return;
        const uint SwpNoSize = 0x0001, SwpNoZOrder = 0x0004, SwpNoActivate = 0x0010;
        SetWindowPos(Hwnd, 0, x, y, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
    }

    public void Close()
    {
        if (_closed) return;
        DestroyWindow(Hwnd);
    }

    public void Dispose() => Close();

    private void OnDestroyed()
    {
        if (_closed) return;
        _closed = true;
        s_windows.Remove(Hwnd);

        try { _xamlSource.Dispose(); } catch { }
        Closed?.Invoke(this, EventArgs.Empty);
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
            lpszClassName = ClassName,
        };

        if (RegisterClassExW(ref wc) == 0)
        {
            throw new InvalidOperationException("No se pudo registrar la ventana de la isla: " + Marshal.GetLastWin32Error());
        }
    }

    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        const uint WmDestroy = 0x0002;
        const uint WmClose = 0x0010;
        const uint WmEraseBackground = 0x0014;
        const uint WmDisplayChange = 0x007E;

        switch (msg)
        {
            case WmEraseBackground:
                // Nada que borrar: el contenido lo compone XAML con transparencia.
                return 1;

            case WmDisplayChange:
                if (s_windows.TryGetValue(hwnd, out var changed)) changed.DisplayChanged?.Invoke(changed, EventArgs.Empty);
                break;

            case WmClose:
                DestroyWindow(hwnd);
                return 0;

            case WmDestroy:
                if (s_windows.TryGetValue(hwnd, out var window)) window.OnDestroyed();
                return 0;
        }

        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    // ---------------------------------------------------------------- Win32

    private delegate nint WndProcDelegate(nint hwnd, uint msg, nint wParam, nint lParam);

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

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandleW(string? name);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassExW(ref WndClassEx wc);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowExW(uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [DllImport("user32.dll")]
    private static extern nint DefWindowProcW(nint hwnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint hwnd, int command);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int cx, int cy, uint flags);
}
