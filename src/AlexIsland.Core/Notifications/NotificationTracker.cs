namespace AlexIsland.Core.Notifications;

/// <summary>
/// Detecta qué notificaciones son nuevas entre una lectura y la siguiente.
/// La primera lectura solo registra las que ya estaban en el centro de
/// notificaciones, para no repetir avisos antiguos al abrir la app.
/// </summary>
public sealed class NotificationTracker
{
    private HashSet<uint> _seen = [];
    private bool _seeded;

    /// <summary>Devuelve las notificaciones que no estaban en la lectura anterior, de la más antigua a la más reciente.</summary>
    public IReadOnlyList<IslandNotification> Update(IEnumerable<IslandNotification> current)
    {
        ArgumentNullException.ThrowIfNull(current);

        var snapshot = current.ToList();
        var fresh = _seeded
            ? snapshot.Where(n => !_seen.Contains(n.Id)).OrderBy(n => n.Created).ThenBy(n => n.Id).ToList()
            : [];

        // Solo se recuerdan las que siguen en el centro de notificaciones, así el
        // conjunto no crece sin límite durante días de uso.
        _seen = snapshot.Select(n => n.Id).ToHashSet();
        _seeded = true;
        return fresh;
    }

    /// <summary>Olvida todo; la próxima lectura vuelve a ser solo de registro (por ejemplo, al reactivar el ajuste).</summary>
    public void Reset()
    {
        _seen = [];
        _seeded = false;
    }
}
