using System.Runtime.InteropServices;
using System.Text;

namespace AlexIsland.App.Native;

/// <summary>
/// Ventanas del Outlook clásico que la isla abre en segundo plano para leer el
/// calendario. Al abrirse, Outlook muestra su ventana de recordatorios; como
/// ese Outlook se cierra a los pocos segundos, se oculta para no interrumpir.
/// Solo se toca la ventana de recordatorios: cualquier otro diálogo (por
/// ejemplo, una petición de contraseña) queda a la vista del usuario.
/// </summary>
internal static class OutlookWindows
{
    // Títulos de la ventana de recordatorios según el idioma de Office:
    // "1 aviso(s)", "3 Reminder(s)", "2 recordatorio(s)"...
    private static readonly string[] ReminderTitleWords = ["aviso", "reminder", "recordatorio", "erinnerung", "rappel", "lembrete"];

    /// <summary>Oculta las ventanas de recordatorios visibles del proceso indicado. Devuelve cuántas ocultó.</summary>
    public static int HideReminderWindows(int processId)
    {
        var hidden = 0;
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid != processId || !IsWindowVisible(hwnd)) return true;

            var title = GetTitle(hwnd);
            if (ReminderTitleWords.Any(w => title.Contains(w, StringComparison.OrdinalIgnoreCase)))
            {
                ShowWindow(hwnd, SwHide);
                hidden++;
            }
            return true;
        }, 0);
        return hidden;
    }

    private static string GetTitle(nint hwnd)
    {
        var length = GetWindowTextLengthW(hwnd);
        if (length <= 0) return string.Empty;
        var buffer = new StringBuilder(length + 1);
        GetWindowTextW(hwnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    private const int SwHide = 0;

    private delegate bool EnumWindowsProc(nint hwnd, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hwnd, out int processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLengthW(nint hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(nint hwnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hwnd, int command);
}
