namespace AlexIsland.Core.Display;

/// <summary>Un monitor conectado, en píxeles físicos de pantalla.</summary>
/// <param name="Id">Identificador del monitor (HMONITOR); puede cambiar al conectar o desconectar pantallas.</param>
public sealed record MonitorArea(nint Id, int X, int Y, int Width, int Height, bool IsPrimary);

/// <summary>
/// Reglas de la isla con varios monitores: hay una sola isla completa, en el monitor
/// activo, y un notch liviano ("satélite") en cada uno de los demás.
/// </summary>
public static class MonitorLayout
{
    /// <summary>Coordenada X para centrar una ventana del ancho indicado en el monitor.</summary>
    public static int CenteredX(MonitorArea monitor, int windowWidth)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        return monitor.X + ((monitor.Width - windowWidth) / 2);
    }

    /// <summary>Monitores que llevan un notch satélite: todos menos el de la isla completa.</summary>
    public static IReadOnlyList<MonitorArea> SatelliteMonitors(IEnumerable<MonitorArea> monitors, nint activeMonitor)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        return monitors.Where(m => m.Id != activeMonitor).ToList();
    }

    /// <summary>
    /// La isla completa va al monitor del cursor, salvo que esté en uso: abierta,
    /// fijada o mostrando un aviso. Al terminar, se mueve en la siguiente comprobación.
    /// </summary>
    public static bool ShouldFollowCursor(
        bool showOnAllMonitors,
        bool isExpanded,
        bool isPinned,
        bool isPeekActive,
        nint cursorMonitor,
        nint activeMonitor)
        => showOnAllMonitors
           && !isExpanded
           && !isPinned
           && !isPeekActive
           && cursorMonitor != 0
           && cursorMonitor != activeMonitor;
}
