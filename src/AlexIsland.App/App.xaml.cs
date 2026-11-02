using System.IO;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace AlexIsland.App;

public partial class App : Application
{
    private Window? _mainWindow;
    private static readonly string LogDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AlexIsland", "logs");
    private static readonly string LogPath = Path.Combine(LogDirectory, "startup.log");

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBoxW(nint hWnd, string text, string caption, uint type);

    public App()
    {
        InitializeComponent();
        UnhandledException += App_UnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            WriteLog("OnLaunched: iniciando MainWindow.");
            _mainWindow = new MainWindow();
            WriteLog("OnLaunched: MainWindow creada; activando ventana.");
            _mainWindow.Activate();
            WriteLog("OnLaunched: ventana activada.");
        }
        catch (Exception ex)
        {
            WriteLog("ERROR durante el arranque:", ex);
            MessageBoxW(0,
                $"Alex Island no pudo iniciarse.\n\n{ex.Message}\n\nRegistro: {LogPath}",
                "Error al iniciar Alex Island", 0x10);
        }
    }

    private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        WriteLog("Excepción no controlada de WinUI:", e.Exception);
    }

    private static void WriteLog(string message, Exception? exception = null)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            var entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
            if (exception is not null)
                entry += Environment.NewLine + exception;
            File.AppendAllText(LogPath, entry + Environment.NewLine + Environment.NewLine);
        }
        catch
        {
            // El registro nunca debe provocar un segundo fallo durante el arranque.
        }
    }
}
