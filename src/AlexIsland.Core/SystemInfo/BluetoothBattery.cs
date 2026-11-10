namespace AlexIsland.Core.SystemInfo;

/// <summary>Tipo de accesorio, para elegir el icono.</summary>
public enum BluetoothDeviceKind
{
    Other,
    Audio,
    Mouse,
    Keyboard,
}

/// <summary>
/// Un nodo de dispositivo tal como lo entrega Windows. Un mismo accesorio aparece
/// como varios nodos (el dispositivo, sus perfiles y servicios) que comparten contenedor.
/// </summary>
public sealed record BluetoothNode(
    string InstanceId,
    Guid ContainerId,
    string? Name,
    bool? IsConnected,
    int? BatteryPercent);

/// <summary>Accesorio conectado con su nivel de batería.</summary>
public sealed record BluetoothBatteryInfo(string Name, int Percent, BluetoothDeviceKind Kind);

public static class BluetoothBattery
{
    // Perfiles por los que se reconoce el tipo de accesorio.
    private const string HidService = "{00001812-";
    private const string ClassicHid = "{00001124-";
    private const string HandsFree = "{0000111E-";
    private const string AudioSink = "{0000110B-";

    /// <summary>
    /// Agrupa los nodos por accesorio y devuelve solo los conectados que informan batería.
    /// Windows conserva el último nivel de los accesorios desconectados, por eso se descartan.
    /// </summary>
    public static IReadOnlyList<BluetoothBatteryInfo> Combine(IEnumerable<BluetoothNode> nodes)
    {
        var result = new List<BluetoothBatteryInfo>();

        foreach (var group in nodes.Where(n => n.ContainerId != Guid.Empty).GroupBy(n => n.ContainerId))
        {
            var devices = group.Where(n => IsDeviceNode(n.InstanceId)).ToList();
            if (devices.Count == 0 || !devices.Any(n => n.IsConnected == true)) continue;

            // Primero el nivel del propio dispositivo; si no, el de un perfil (manos libres).
            var battery = devices.Select(n => n.BatteryPercent).FirstOrDefault(b => b is not null)
                ?? group.Select(n => n.BatteryPercent).FirstOrDefault(b => b is not null);
            if (battery is not int percent) continue;

            var name = devices.Select(n => n.Name).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n))?.Trim()
                ?? "Dispositivo Bluetooth";

            result.Add(new BluetoothBatteryInfo(name, Math.Clamp(percent, 0, 100), KindOf(name, group)));
        }

        return result.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static bool IsDeviceNode(string id)
        => id.StartsWith(@"BTHENUM\DEV_", StringComparison.OrdinalIgnoreCase)
        || id.StartsWith(@"BTHLE\DEV_", StringComparison.OrdinalIgnoreCase);

    private static BluetoothDeviceKind KindOf(string name, IEnumerable<BluetoothNode> nodes)
    {
        var ids = nodes.Select(n => n.InstanceId).ToList();
        bool Has(string service) => ids.Any(id => id.Contains(service, StringComparison.OrdinalIgnoreCase));

        if (Has(HandsFree) || Has(AudioSink)) return BluetoothDeviceKind.Audio;
        if (Has(HidService) || Has(ClassicHid))
        {
            return name.Contains("keyboard", StringComparison.OrdinalIgnoreCase)
                || name.Contains("teclado", StringComparison.OrdinalIgnoreCase)
                || name.Contains("keys", StringComparison.OrdinalIgnoreCase)
                ? BluetoothDeviceKind.Keyboard
                : BluetoothDeviceKind.Mouse;
        }
        return BluetoothDeviceKind.Other;
    }
}
