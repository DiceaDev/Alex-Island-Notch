using AlexIsland.App.Services;
using AlexIsland.Core.Notifications;
using Microsoft.UI.Xaml.Controls;

namespace AlexIsland.App;

public sealed partial class MainWindow : UserControl
{
    // ==============================================================
    // SECCIÓN NOTIFICACIONES DE WINDOWS
    // ==============================================================

    /// <summary>Los mensajes se leen más despacio que un cambio de volumen.</summary>
    private static readonly TimeSpan NotificationPeekDuration = TimeSpan.FromSeconds(5.5);

    private const string OwnAppName = "Alex Island";

    private NotificationWatcher? _notificationWatcher;

    private void InitializeNotifications()
    {
        _notificationWatcher = new NotificationWatcher();
        _notificationWatcher.NotificationsArrived += batch =>
            DispatcherQueue.TryEnqueue(() => OnNotificationsArrived(batch));
    }

    /// <summary>Inicia o detiene la lectura según el ajuste; sin el ajuste no se consulta nada.</summary>
    private void ApplyNotificationSettings()
    {
        if (_notificationWatcher is null || _isDisposed) return;

        if (_settings.ShowNotificationPeeks) _notificationWatcher.Start();
        else _notificationWatcher.Stop();
    }

    private void StopNotifications()
    {
        _notificationWatcher?.Dispose();
        _notificationWatcher = null;
    }

    private void OnNotificationsArrived(IReadOnlyList<IslandNotification> batch)
    {
        if (!_settings.ShowNotificationPeeks || !CanShowAutomaticPeek) return;

        var muted = NotificationPresenter.ParseMutedApps(_settings.MutedNotificationApps);
        var visible = batch.Where(n => NotificationPresenter.ShouldShow(n, muted, OwnAppName)).ToList();
        if (visible.Count == 0) return;

        // Si llegan varias a la vez se muestra la más reciente con un contador,
        // en lugar de encadenar avisos que taparían la isla durante mucho rato.
        var latest = visible[^1];
        var (title, subtitle) = NotificationPresenter.Format(latest, _settings.ShowNotificationContent, visible.Count - 1);

        ShowHardwarePeek(
            HardwarePeekType.Notification,
            title,
            subtitle,
            percent01: 1.0,
            duration: NotificationPeekDuration);
    }
}
