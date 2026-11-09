using System.IO;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace AlexIsland.App;

public partial class App : Application
{
    private MainWindow? _mainWindow;
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
        // Al salir, Windows debe recuperar el espacio reservado arriba de la pantalla.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => AlexIsland.App.Native.TopBandWindow.ReleaseAll();

        // La isla vive en una ventana propia (no es una Window de WinUI). Sin esto,
        // la app se cerraría al cerrar la última Window, por ejemplo la de Ajustes.
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            WriteLog("OnLaunched: iniciando MainWindow.");
            _mainWindow = new MainWindow();
            WriteLog("OnLaunched: MainWindow creada; mostrando la isla.");
            _mainWindow.Show();
            WriteLog("OnLaunched: isla visible.");
        }
        catch (Exception ex)
        {
            WriteLog("ERROR durante el arranque:", ex);
            var detail = $"{ex.Message}\n\nEn:\n{ex.StackTrace}";
            if (detail.Length > 800) detail = detail[..800] + "...";
            MessageBoxW(0,
                $"Alex Island no pudo iniciarse.\n\n{detail}\n\nRegistro: {LogPath}",
                "Error al iniciar Alex Island", 0x10);
        }
    }

    private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        // Un error de la interfaz no debe cerrar la isla: los datos se guardan aparte
        // de forma atómica, y lo ocurrido queda en el registro para poder corregirlo.
        e.Handled = true;

        string state;
        try
        {
            state = _mainWindow?.DescribeStateForLog() ?? "sin ventana";
        }
        catch (Exception ex)
        {
            state = $"estado no disponible ({ex.GetType().Name})";
        }
        WriteLog($"Excepción no controlada de WinUI; la isla sigue abierta. Estado: {state}.", e.Exception);
    }

    /// <summary>Registro de diagnóstico en %LocalAppData%\AlexIsland\logs\startup.log.</summary>
    internal static void Log(string message, Exception? exception = null) => WriteLog(message, exception);

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
