using System.Runtime.InteropServices;

namespace AlexIsland.App.Native;

/// <summary>
/// Lee una sola fila de píxeles de la pantalla para saber de qué color es la barra de
/// la ventana que está debajo de la franja. Solo se calcula un color: los píxeles no
/// se guardan ni salen del equipo.
/// </summary>
internal static class ScreenRowSampler
{
    /// <summary>Fila de píxeles (0x00RRGGBB) en coordenadas físicas, o null si no se pudo leer.</summary>
    public static int[]? ReadRow(int x, int y, int width)
    {
        if (width <= 0) return null;

        var screen = GetDC(0);
        if (screen == 0) return null;
        var memory = CreateCompatibleDC(screen);
        nint bitmap = 0, previous = 0;
        try
        {
            if (memory == 0) return null;

            var info = new BitmapInfoHeader
            {
                biSize = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                biWidth = width,
                biHeight = -1, // de arriba hacia abajo
                biPlanes = 1,
                biBitCount = 32,
            };
            bitmap = CreateDIBSection(memory, ref info, 0, out var bits, 0, 0);
            if (bitmap == 0 || bits == 0) return null;

            previous = SelectObject(memory, bitmap);
            const uint SrcCopy = 0x00CC0020;
            if (!BitBlt(memory, 0, 0, width, 1, screen, x, y, SrcCopy)) return null;

            var row = new int[width];
            Marshal.Copy(bits, row, 0, width);
            for (var i = 0; i < row.Length; i++) row[i] &= 0xFFFFFF;
            return row;
        }
        finally
        {
            if (previous != 0) SelectObject(memory, previous);
            if (bitmap != 0) DeleteObject(bitmap);
            if (memory != 0) DeleteDC(memory);
            ReleaseDC(0, screen);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint hwnd, nint hdc);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleDC(nint hdc);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(nint hdc);

    [DllImport("gdi32.dll")]
    private static extern nint CreateDIBSection(nint hdc, ref BitmapInfoHeader info, uint usage, out nint bits, nint section, uint offset);

    [DllImport("gdi32.dll")]
    private static extern nint SelectObject(nint hdc, nint obj);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint obj);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BitBlt(nint dest, int x, int y, int width, int height, nint source, int sx, int sy, uint rop);
}
