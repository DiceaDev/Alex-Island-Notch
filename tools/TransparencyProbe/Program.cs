// Prueba de referencia: dibuja un notch con Windows.UI.Composition (el motor
// sobre el que trabaja DirectComposition, como hace Nusa) en una ventana con
// transparencia por píxel, para comparar con Alex Island en el mismo equipo.
//
// Uso: TransparencyProbe.exe [segundos]

using System.Numerics;
using System.Runtime.InteropServices;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;
using WinRT;

internal static unsafe class Program
{
    private const int Width = 300;
    private const int Height = 72;

    [STAThread]
    private static int Main(string[] args)
    {
        var seconds = args.Length > 0 && int.TryParse(args[0], out var s) ? s : 30;

        // Windows.UI.Composition necesita una cola de Windows.System en el hilo.
        var options = new DispatcherQueueOptions { Size = sizeof(DispatcherQueueOptions), ThreadType = 2, ApartmentType = 2 };
        Check(CreateDispatcherQueueController(options, out _), "CreateDispatcherQueueController");

        var hwnd = CreateProbeWindow();
        Console.WriteLine($"Ventana: 0x{hwnd:X}");

        var compositor = new Compositor();
        var target = CreateDesktopTarget(compositor, hwnd);

        var root = compositor.CreateContainerVisual();
        root.Size = new Vector2(Width, Height);
        target.Root = root;

        var shapes = compositor.CreateShapeVisual();
        shapes.Size = new Vector2(Width, Height);

        // Cuerpo: rectángulo redondeado que empieza por encima de la ventana, para
        // que solo se vean redondeadas las esquinas inferiores (como un notch).
        var body = compositor.CreateRoundedRectangleGeometry();
        body.Offset = new Vector2(24, -40);
        body.Size = new Vector2(Width - 48, Height + 40 - 8);
        body.CornerRadius = new Vector2(26, 26);
        var bodyShape = compositor.CreateSpriteShape(body);
        bodyShape.FillBrush = compositor.CreateColorBrush(Windows.UI.Color.FromArgb(255, 0, 0, 0));
        shapes.Shapes.Add(bodyShape);

        // Detalle: un punto verde y un círculo grande para ver el suavizado.
        var dot = compositor.CreateEllipseGeometry();
        dot.Center = new Vector2(58, 32);
        dot.Radius = new Vector2(7, 7);
        var dotShape = compositor.CreateSpriteShape(dot);
        dotShape.FillBrush = compositor.CreateColorBrush(Windows.UI.Color.FromArgb(255, 0, 230, 118));
        shapes.Shapes.Add(dotShape);

        var ring = compositor.CreateEllipseGeometry();
        ring.Center = new Vector2(Width - 58, 32);
        ring.Radius = new Vector2(14, 14);
        var ringShape = compositor.CreateSpriteShape(ring);
        ringShape.StrokeBrush = compositor.CreateColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255));
        ringShape.StrokeThickness = 2;
        shapes.Shapes.Add(ringShape);

        root.Children.InsertAtTop(shapes);
        Console.WriteLine("Composición lista.");

        var end = Environment.TickCount64 + seconds * 1000L;
        while (Environment.TickCount64 < end)
        {
            while (PeekMessageW(out var msg, 0, 0, 0, 1))
            {
                TranslateMessage(ref msg);
                DispatchMessageW(ref msg);
            }
            Thread.Sleep(15);
        }

        GC.KeepAlive(target);
        return 0;
    }

    private static nint CreateProbeWindow()
    {
        var instance = GetModuleHandleW(null);
        s_wndProc = (h, m, w, l) => DefWindowProcW(h, m, w, l);

        var wc = new WndClassEx
        {
            cbSize = (uint)Marshal.SizeOf<WndClassEx>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(s_wndProc),
            hInstance = instance,
            lpszClassName = "TransparencyProbe",
        };
        if (RegisterClassExW(ref wc) == 0) throw new InvalidOperationException("RegisterClassEx");

        const uint WsExNoRedirectionBitmap = 0x00200000;
        const uint WsExTopmost = 0x00000008;
        const uint WsExToolWindow = 0x00000080;
        const uint WsExNoActivate = 0x08000000;
        const uint WsPopup = 0x80000000;
        const uint WsVisible = 0x10000000;

        var x = (GetSystemMetrics(0) - Width) / 2;
        var hwnd = CreateWindowExW(
            WsExNoRedirectionBitmap | WsExTopmost | WsExToolWindow | WsExNoActivate,
            "TransparencyProbe", "Transparency probe", WsPopup | WsVisible,
            x, 0, Width, Height, 0, 0, instance, 0);
        if (hwnd == 0) throw new InvalidOperationException("CreateWindowEx: " + Marshal.GetLastWin32Error());
        return hwnd;
    }

    /// <summary>ICompositorDesktopInterop::CreateDesktopWindowTarget(hwnd, isTopmost, out target).</summary>
    private static DesktopWindowTarget CreateDesktopTarget(Compositor compositor, nint hwnd)
    {
        var unknown = ((IWinRTObject)compositor).NativeObject.ThisPtr;
        var iid = new Guid("29E691FA-4567-4DCA-B319-D0F207EB6807");
        Check(Marshal.QueryInterface(unknown, ref iid, out var interop), "QueryInterface(ICompositorDesktopInterop)");
        try
        {
            var vtable = *(nint**)interop;
            var create = (delegate* unmanaged[Stdcall]<nint, nint, int, nint*, int>)vtable[3];
            nint targetPtr;
            Check(create(interop, hwnd, 1, &targetPtr), "CreateDesktopWindowTarget");
            return DesktopWindowTarget.FromAbi(targetPtr);
        }
        finally
        {
            Marshal.Release(interop);
        }
    }

    private static void Check(int hr, string what)
    {
        if (hr < 0) throw new InvalidOperationException($"{what} falló: 0x{hr:X8}");
    }

    private static WndProc? s_wndProc;

    private delegate nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct DispatcherQueueOptions
    {
        public int Size;
        public int ThreadType;
        public int ApartmentType;
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

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public nint hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    [DllImport("CoreMessaging.dll")]
    private static extern int CreateDispatcherQueueController(DispatcherQueueOptions options, out nint controller);

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
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessageW(out Msg msg, nint hwnd, uint min, uint max, uint remove);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref Msg msg);

    [DllImport("user32.dll")]
    private static extern nint DispatchMessageW(ref Msg msg);
}
