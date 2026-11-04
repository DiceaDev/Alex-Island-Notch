using AlexIsland.Core.Notifications;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace AlexIsland.App.Services;

/// <summary>
/// Lee las notificaciones del centro de notificaciones de Windows con
/// <see cref="UserNotificationListener"/>.
///
/// El evento NotificationChanged solo funciona en apps empaquetadas (MSIX); Alex
/// Island no lo está, así que se consulta la lista cada pocos segundos. La consulta
/// es local, solo corre mientras el ajuste está activo y se salta si no hay permiso.
/// Nada de lo leído se guarda en disco ni se envía a Internet.
/// </summary>
internal sealed class NotificationWatcher : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);

    private readonly NotificationTracker _tracker = new();
    private CancellationTokenSource? _cts;

    /// <summary>Notificaciones nuevas (de la más antigua a la más reciente). Se dispara en un hilo de fondo.</summary>
    public event Action<IReadOnlyList<IslandNotification>>? NotificationsArrived;

    public bool IsRunning => _cts is not null;

    /// <summary>Estado del permiso sin mostrar nada al usuario. Devuelve null si la API no existe en este equipo.</summary>
    public static UserNotificationListenerAccessStatus? GetAccessStatus()
    {
        try
        {
            return UserNotificationListener.Current.GetAccessStatus();
        }
        catch (Exception ex)
        {
            App.Log("Notificaciones: no se pudo consultar el permiso.", ex);
            return null;
        }
    }

    /// <summary>Pide el permiso a Windows. Debe llamarse desde el hilo de interfaz.</summary>
    public static async Task<UserNotificationListenerAccessStatus?> RequestAccessAsync()
    {
        try
        {
            return await UserNotificationListener.Current.RequestAccessAsync();
        }
        catch (Exception ex)
        {
            App.Log("Notificaciones: no se pudo pedir el permiso.", ex);
            return null;
        }
    }

    public void Start()
    {
        if (_cts is not null) return;

        _tracker.Reset();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        // En un hilo de fondo: si se llamara desde la interfaz, cada vuelta del
        // bucle (y la lectura de todas las notificaciones) correría en ese hilo.
        _ = Task.Run(() => PollLoopAsync(token), token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    public void Dispose() => Stop();

    private async Task PollLoopAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(PollInterval);
        var loggedAccessProblem = false;
        var consecutiveErrors = 0;

        // Notificaciones ya convertidas, por Id. Leer el nombre de la app y el texto
        // de cada una cuesta llamadas a otros procesos; así solo se hace con las nuevas.
        // Es propia de este bucle, así que no se comparte entre hilos.
        var converted = new Dictionary<uint, IslandNotification>();

        try
        {
            do
            {
                var status = GetAccessStatus();
                if (status != UserNotificationListenerAccessStatus.Allowed)
                {
                    // Sin permiso no se lee nada. Si el usuario lo concede después en
                    // Configuración, la siguiente vuelta lo detecta sin reiniciar.
                    if (!loggedAccessProblem)
                    {
                        App.Log($"Notificaciones: permiso no concedido ({status?.ToString() ?? "API no disponible"}).");
                        loggedAccessProblem = true;
                    }
                    _tracker.Reset();
                    continue;
                }
                loggedAccessProblem = false;

                try
                {
                    var current = await ReadCurrentAsync(converted).WaitAsync(token).ConfigureAwait(false);
                    consecutiveErrors = 0;

                    var fresh = _tracker.Update(current);
                    if (fresh.Count > 0) NotificationsArrived?.Invoke(fresh);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // Un error aislado (por ejemplo, el servicio de notificaciones
                    // reiniciándose) no detiene la escucha; solo se registra el primero.
                    if (consecutiveErrors++ == 0) App.Log("Notificaciones: error al leer la lista.", ex);
                }
            }
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            // Stop() o cierre de la app.
        }
    }

    private static async Task<List<IslandNotification>> ReadCurrentAsync(Dictionary<uint, IslandNotification> converted)
    {
        var raw = await UserNotificationListener.Current.GetNotificationsAsync(NotificationKinds.Toast).AsTask().ConfigureAwait(false);
        var result = new List<IslandNotification>(raw.Count);
        var present = new HashSet<uint>();

        foreach (var item in raw)
        {
            present.Add(item.Id);
            if (converted.TryGetValue(item.Id, out var known))
            {
                result.Add(known);
                continue;
            }

            try
            {
                var notification = Convert(item);
                converted[item.Id] = notification;
                result.Add(notification);
            }
            catch
            {
                // Una notificación con formato inesperado no impide leer las demás.
            }
        }

        // Se olvidan las que ya no están en el centro de notificaciones.
        foreach (var id in converted.Keys.Where(id => !present.Contains(id)).ToList())
        {
            converted.Remove(id);
        }

        return result;
    }

    private static IslandNotification Convert(UserNotification item)
    {
        string appName;
        try
        {
            appName = item.AppInfo?.DisplayInfo?.DisplayName ?? string.Empty;
        }
        catch
        {
            // Algunas apps de escritorio no exponen su nombre visible.
            appName = string.Empty;
        }

        var texts = new List<string>();
        var binding = item.Notification?.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric);
        if (binding is not null)
        {
            foreach (var element in binding.GetTextElements())
            {
                if (!string.IsNullOrWhiteSpace(element.Text)) texts.Add(element.Text);
            }
        }

        var title = texts.Count > 0 ? texts[0] : string.Empty;
        var body = texts.Count > 1 ? string.Join(" · ", texts.Skip(1)) : string.Empty;
        return new IslandNotification(item.Id, appName, title, body, item.CreationTime);
    }
}
