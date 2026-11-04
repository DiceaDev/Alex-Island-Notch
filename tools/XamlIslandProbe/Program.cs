// Prueba de referencia: XAML de WinUI incrustado con DesktopWindowXamlSource en
// una ventana propia con transparencia por píxel (WS_EX_NOREDIRECTIONBITMAP).
// Si el notch se ve suave y sin fondo, la app puede conservar todo su XAML y
// tener los bordes suavizados.
//
// Uso: XamlIslandProbe.exe [segundos]

using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

internal static class Program
{
    private const int Width = 300;
    private const int Height = 72;

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var seconds = args.Length > 0 && int.TryParse(args[0], out var s) ? s : 30;

            var dispatcher = DispatcherQueueController.CreateOnCurrentThread();
            var xamlManager = WindowsXamlManager.InitializeForCurrentThread();
            Console.WriteLine("XAML inicializado.");

            var hwnd = CreateProbeWindow();
            Console.WriteLine($"Ventana: 0x{hwnd:X}");

            var source = new DesktopWindowXamlSource();
            source.Initialize(Win32Interop.GetWindowIdFromWindow(hwnd));
            source.SiteBridge.MoveAndResize(new Windows.Graphics.RectInt32(0, 0, Width, Height));
            source.SiteBridge.Show();

            source.Content = BuildContent();
            Console.WriteLine("Contenido XAML asignado.");

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

            GC.KeepAlive(source);
            GC.KeepAlive(xamlManager);
            GC.KeepAlive(dispatcher);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    /// <summary>Notch en XAML puro: fondo transparente y un Border negro redondeado abajo.</summary>
    private static UIElement BuildContent()
    {
        var row = new Grid { Margin = new Thickness(22, 0, 22, 0), VerticalAlignment = VerticalAlignment.Center };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var dot = new Ellipse { Width = 14, Height = 14, Fill = new SolidColorBrush(ColorHelper.FromArgb(255, 0, 230, 118)) };
        var text = new TextBlock
        {
            Text = "XAML · 12:34",
            Foreground = new SolidColorBrush(Colors.White),
            FontSize = 15,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        var ring = new Ellipse { Width = 28, Height = 28, Stroke = new SolidColorBrush(Colors.White), StrokeThickness = 2 };
        Grid.SetColumn(text, 1);
        Grid.SetColumn(ring, 2);
        row.Children.Add(dot);
        row.Children.Add(text);
        row.Children.Add(ring);

        var notch = new Border
        {
            Background = new SolidColorBrush(Colors.Black),
            CornerRadius = new CornerRadius(0, 0, 26, 26),
            Margin = new Thickness(24, 0, 24, 8),
            Child = row,
        };

        return new Grid { Background = new SolidColorBrush(Colors.Transparent), Children = { notch } };
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
            lpszClassName = "XamlIslandProbe",
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
            "XamlIslandProbe", "XAML island probe", WsPopup | WsVisible,
            x, 0, Width, Height, 0, 0, instance, 0);
        if (hwnd == 0) throw new InvalidOperationException("CreateWindowEx: " + Marshal.GetLastWin32Error());
        return hwnd;
    }

    private static WndProc? s_wndProc;

    private delegate nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam);

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
