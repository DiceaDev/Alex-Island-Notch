namespace AlexIsland.Core.SystemInfo;

/// <summary>
/// Decide cuándo avisar que un accesorio Bluetooth tiene poca batería: una sola vez
/// al bajar del umbral, y de nuevo solo si se cargó por encima del margen o si se
/// volvió a conectar. Así el aviso no se repite cada minuto mientras sigue bajo.
/// </summary>
public sealed class BluetoothLowBatteryAlerts
{
    public const int Threshold = 20;

    /// <summary>Margen para no avisar dos veces si el nivel oscila entre 20 y 21 %.</summary>
    public const int RearmAbove = 25;

    private readonly HashSet<string> _warned = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Accesorios con batería baja que todavía no se han avisado.</summary>
    public IReadOnlyList<BluetoothBatteryInfo> Pending(IReadOnlyList<BluetoothBatteryInfo> devices)
    {
        // Olvida los desconectados y los que ya se cargaron: si vuelven bajos, se avisa otra vez.
        _warned.RemoveWhere(name => devices.FirstOrDefault(d => SameName(d.Name, name)) is not { } device
            || device.Percent > RearmAbove);

        return devices
            .Where(d => d.Percent <= Threshold && !_warned.Contains(d.Name))
            .OrderBy(d => d.Percent)
            .ToList();
    }

    /// <summary>Se llama solo cuando el aviso se mostró de verdad.</summary>
    public void MarkWarned(BluetoothBatteryInfo device) => _warned.Add(device.Name);

    private static bool SameName(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
