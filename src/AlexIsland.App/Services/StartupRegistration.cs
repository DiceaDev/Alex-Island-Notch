using Microsoft.Win32;

namespace AlexIsland.App.Services;

/// <summary>
/// Inicio automático con Windows mediante la clave Run del usuario actual
/// (HKCU). No requiere administrador ni afecta a otros usuarios del equipo.
/// </summary>
internal static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AlexIsland";

    /// <summary>
    /// Deja el registro en el estado pedido. Si está activado, también corrige
    /// la ruta cuando la app se movió de carpeta. Devuelve false si falló.
    /// </summary>
    public static bool Apply(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key is null) return false;

            if (!enabled)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                return true;
            }

            var command = GetLaunchCommand();
            if (command is null) return false;

            if (!string.Equals(key.GetValue(ValueName) as string, command, StringComparison.OrdinalIgnoreCase))
            {
                key.SetValue(ValueName, command, RegistryValueKind.String);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Ruta del ejecutable entre comillas. Solo se registra un .exe real que
    /// exista en disco, nunca argumentos ni rutas armadas desde otros datos.
    /// </summary>
    private static string? GetLaunchCommand()
    {
        var path = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(path)
            || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            || !File.Exists(path)
            || path.Contains('"'))
        {
            return null;
        }

        return $"\"{path}\"";
    }
}
