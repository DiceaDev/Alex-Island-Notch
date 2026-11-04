using System.Runtime.InteropServices;

namespace AlexIsland.App.Native;

/// <summary>
/// Ícono en el área de notificación con menú contextual. WinUI 3 no trae esta
/// función, así que se usa Shell_NotifyIcon con una ventana de solo mensajes
/// propia (no se intercepta ni se modifica la ventana de la isla).
/// </summary>
/// <summary>Opción del menú de la bandeja. Un separador no tiene texto ni acción.</summary>
internal sealed record TrayMenuItem(int Id, string Text, bool Checked = false)
{
    public static TrayMenuItem Separator { get; } = new(0, string.Empty);
    public bool IsSeparator => Id == 0;
}

internal sealed class TrayIcon : IDisposable
{

    private const uint WmApp = 0x8000;
    private const uint WmTrayCallback = WmApp + 1;
    private const uint WmLButtonUp = 0x0202;
    private const uint WmRButtonUp = 0x0205;
    private const uint WmNull = 0x0000;

    private const uint NimAdd = 0x0;
    private const uint NimModify = 0x1;
    private const uint NimDelete = 0x2;
    private const uint NifMessage = 0x1;
    private const uint NifIcon = 0x2;
    private const uint NifTip = 0x4;

    private const uint MfString = 0x0;
    private const uint MfChecked = 0x8;
    private const uint MfSeparator = 0x800;
    private const uint TpmRightButton = 0x2;
    private const uint TpmReturnCmd = 0x100;
    private const uint TpmNoNotify = 0x80;

    private static readonly nint HwndMessage = new(-3);

    private readonly WndProcDelegate _wndProc; // se mantiene vivo mientras exista la ventana
    private readonly string _className = "AlexIslandTray_" + Environment.ProcessId;
    private readonly uint _taskbarCreatedMessage;
    private readonly nint _icon;
    private readonly bool _ownsIcon;
    private nint _hwnd;
    private string _tooltip;
    private bool _added;
    private bool _disposed;

    /// <summary>Clic izquierdo sobre el ícono.</summary>
    public event Action? Activated;

    /// <summary>Id de la opción elegida en el menú.</summary>
    public event Action<int>? CommandInvoked;

    /// <summary>
    /// Arma el menú cada vez que se abre, para que las marcas (✓) reflejen el
    /// estado actual de los ajustes.
    /// </summary>
    public Func<IReadOnlyList<TrayMenuItem>> MenuItems { get; set; } = () => [];

    public TrayIcon(string iconPath, string tooltip)
    {
        _tooltip = tooltip;
        _wndProc = WndProc;
        _taskbarCreatedMessage = RegisterWindowMessageW("TaskbarCreated");

        (_icon, _ownsIcon) = LoadTrayIcon(iconPath);

        var wc = new WndClassEx
        {
            cbSize = (uint)Marshal.SizeOf<WndClassEx>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = GetModuleHandleW(null),
            lpszClassName = _className,
        };

        if (RegisterClassExW(ref wc) == 0)
        {
            throw new InvalidOperationException("No se pudo registrar la ventana del ícono de bandeja.");
        }

        _hwnd = CreateWindowExW(0, _className, string.Empty, 0, 0, 0, 0, 0, HwndMessage, 0, wc.hInstance, 0);
        if (_hwnd == 0)
        {
            throw new InvalidOperationException("No se pudo crear la ventana del ícono de bandeja.");
        }

        AddIcon();
    }

    public void SetTooltip(string tooltip)
    {
        _tooltip = tooltip;
        if (_added)
        {
            var data = CreateData();
            Shell_NotifyIconW(NimModify, ref data);
        }
    }

    private void AddIcon()
    {
        var data = CreateData();
        _added = Shell_NotifyIconW(NimAdd, ref data);
    }

    private NotifyIconData CreateData() => new()
    {
        cbSize = (uint)Marshal.SizeOf<NotifyIconData>(),
        hWnd = _hwnd,
        uID = 1,
        uFlags = NifMessage | NifIcon | NifTip,
        uCallbackMessage = WmTrayCallback,
        hIcon = _icon,
        szTip = _tooltip.Length > 127 ? _tooltip[..127] : _tooltip,
        szInfo = string.Empty,
        szInfoTitle = string.Empty,
    };

    private nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == WmTrayCallback)
        {
            var mouseMessage = (uint)(lParam.ToInt64() & 0xFFFF);
            if (mouseMessage == WmLButtonUp)
            {
                Activated?.Invoke();
            }
            else if (mouseMessage == WmRButtonUp)
            {
                ShowMenu();
            }
            return 0;
        }

        // Si el Explorador se reinicia, el área de notificación se recrea vacía.
        if (msg == _taskbarCreatedMessage && _taskbarCreatedMessage != 0)
        {
            AddIcon();
            return 0;
        }

        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private void ShowMenu()
    {
        var menu = CreatePopupMenu();
        if (menu == 0) return;

        try
        {
            foreach (var item in MenuItems())
            {
                if (item.IsSeparator)
                {
                    AppendMenuW(menu, MfSeparator, 0, null);
                }
                else
                {
                    AppendMenuW(menu, MfString | (item.Checked ? MfChecked : 0), item.Id, item.Text);
                }
            }

            GetCursorPos(out var point);

            // Necesario para que el menú se cierre al hacer clic fuera de él.
            SetForegroundWindow(_hwnd);
            var command = TrackPopupMenuEx(menu, TpmRightButton | TpmReturnCmd | TpmNoNotify, point.X, point.Y, _hwnd, 0);
            PostMessageW(_hwnd, WmNull, 0, 0);

            if (command != 0)
            {
                CommandInvoked?.Invoke(command);
            }
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    private static (nint Icon, bool Owned) LoadTrayIcon(string path)
    {
        if (File.Exists(path))
        {
            var size = GetSystemMetrics(49); // SM_CXSMICON
            var icon = LoadImageW(0, path, 1 /* IMAGE_ICON */, size, size, 0x10 /* LR_LOADFROMFILE */);
            if (icon != 0) return (icon, true);
        }

        return (LoadIconW(0, 32512 /* IDI_APPLICATION */), false);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_added)
        {
            var data = CreateData();
            Shell_NotifyIconW(NimDelete, ref data);
            _added = false;
        }

        if (_hwnd != 0)
        {
            DestroyWindow(_hwnd);
            _hwnd = 0;
        }

        UnregisterClassW(_className, GetModuleHandleW(null));

        if (_ownsIcon && _icon != 0)
        {
            DestroyIcon(_icon);
        }
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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint cbSize;
        public nint hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public nint hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassExW(ref WndClassEx wc);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterClassW(string className, nint hInstance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowExW(
        uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint hwnd);

    [DllImport("user32.dll")]
    private static extern nint DefWindowProcW(nint hwnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessageW(string message);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandleW(string? moduleName);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIconW(uint message, ref NotifyIconData data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint LoadImageW(nint instance, string name, uint type, int cx, int cy, uint load);

    [DllImport("user32.dll")]
    private static extern nint LoadIconW(nint instance, nint iconName);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint icon);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern nint CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenuW(nint menu, uint flags, nint id, string? text);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(nint menu);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint hwnd, nint tpmParams);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(nint hwnd, uint msg, nint wParam, nint lParam);
}
