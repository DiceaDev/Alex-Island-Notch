using AlexIsland.App.Services;
using AlexIsland.Core.Calendar;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AlexIsland.App;

public sealed partial class MainWindow : UserControl
{
    // ==============================================================
    // SECCIÓN AGENDA: IMPORTAR DESDE OUTLOOK (solo Outlook → isla)
    // ==============================================================

    /// <summary>Cada cuánto se vuelve a leer Outlook mientras la sincronización está activa.</summary>
    private static readonly TimeSpan OutlookSyncInterval = TimeSpan.FromMinutes(15);

    /// <summary>Tiempo que se deja sincronizar a Outlook con el servidor cuando hubo que abrirlo.</summary>
    private static readonly TimeSpan OutlookServerSyncWait = TimeSpan.FromSeconds(30);

    /// <summary>Primera lectura poco después de arrancar, para no competir con el inicio de Windows.</summary>
    private static readonly TimeSpan OutlookFirstSyncDelay = TimeSpan.FromSeconds(20);

    private DispatcherQueueTimer? _outlookSyncTimer;
    private CancellationTokenSource? _outlookSyncCts;
    private Task _calendarLoadTask = Task.CompletedTask;
    private bool _outlookSyncRunning;

    /// <summary>Activa o detiene la sincronización periódica según el ajuste.</summary>
    private void ApplyOutlookSyncSettings()
    {
        if (_isDisposed) return;

        if (!_settings.SyncOutlookCalendar || !OutlookCalendarReader.IsAvailable)
        {
            _outlookSyncTimer?.Stop();
            return;
        }

        if (_outlookSyncTimer is null)
        {
            _outlookSyncTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
            _outlookSyncTimer.IsRepeating = true;
            _outlookSyncTimer.Tick += (_, _) =>
            {
                // Tras la primera vuelta corta, sigue con el intervalo normal.
                _outlookSyncTimer.Interval = OutlookSyncInterval;
                _ = SyncOutlookAsync(userInitiated: false);
            };
        }

        if (!_outlookSyncTimer.IsRunning)
        {
            _outlookSyncTimer.Interval = OutlookFirstSyncDelay;
            _outlookSyncTimer.Start();
        }
    }

    private void StopOutlookSync()
    {
        _outlookSyncTimer?.Stop();
        _outlookSyncCts?.Cancel();
    }

    private async void OutlookImportBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!OutlookCalendarReader.IsAvailable)
        {
            ShowSyncStatus("No se encontró Outlook clásico en este equipo. Usa \"Cargar archivo .ics\".", isError: true);
            return;
        }

        // Importar a mano deja activada la sincronización automática.
        if (!_settings.SyncOutlookCalendar)
        {
            var updated = _settings.Clone();
            updated.SyncOutlookCalendar = true;
            OnSettingsChanged(updated);
            _settingsWindow?.Refresh(_settings);
        }

        await SyncOutlookAsync(userInitiated: true);
    }

    private async Task SyncOutlookAsync(bool userInitiated)
    {
        if (_outlookSyncRunning || _isDisposed) return;
        _outlookSyncRunning = true;

        if (userInitiated)
        {
            OutlookImportLabel.Text = "Leyendo Outlook…";
            ShowSyncStatus("Leyendo el calendario de Outlook…", isError: false);
        }

        var windowStart = DateTime.Today.AddDays(-1);
        var windowEnd = DateTime.Today.AddDays(45);
        var first = true;

        _outlookSyncCts = new CancellationTokenSource();
        try
        {
            await _calendarLoadTask;

            await OutlookCalendarReader.ReadAsync(
                windowStart,
                windowEnd,
                OutlookServerSyncWait,
                appointments =>
                {
                    var isFirst = first;
                    first = false;
                    DispatcherQueue.TryEnqueue(() => _ = ApplyOutlookAppointmentsAsync(
                        appointments, windowStart, windowEnd, showResult: userInitiated && isFirst));
                },
                _outlookSyncCts.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            App.Log("Agenda: no se pudo leer el calendario de Outlook.", ex);
            if (userInitiated && !_isDisposed)
            {
                ShowSyncStatus("No se pudo leer Outlook. Revisa que el Outlook clásico tenga tu cuenta configurada.", isError: true);
            }
        }
        finally
        {
            _outlookSyncCts?.Dispose();
            _outlookSyncCts = null;
            _outlookSyncRunning = false;
            if (!_isDisposed) OutlookImportLabel.Text = "Importar de Outlook";
        }
    }

    private async Task ApplyOutlookAppointmentsAsync(
        IReadOnlyList<OutlookAppointment> appointments,
        DateTime windowStart,
        DateTime windowEnd,
        bool showResult)
    {
        if (_isDisposed) return;

        try
        {
            var result = OutlookCalendarSync.Apply(_events, appointments, windowStart, windowEnd);

            if (result.HasChanges)
            {
                await _calendarStore.SaveAsync(_events);
                UpdateCalendarBadges();
                CheckUpcomingMeetings();
                RenderCalendarView();
            }

            if (showResult)
            {
                var meetings = appointments.Count(a => !OutlookCalendarSync.IsCancelled(a.MeetingStatus));
                ShowSyncStatus(result.HasChanges
                    ? $"✓ Outlook: {meetings} reunión(es) · {result.Added} nueva(s), {result.Updated} actualizada(s), {result.Removed} quitada(s)"
                    : $"✓ Outlook: {meetings} reunión(es), todo al día", isError: false);
            }
        }
        catch (Exception ex)
        {
            App.Log("Agenda: error al aplicar la lectura de Outlook.", ex);
            if (showResult) ShowSyncStatus($"Error al importar de Outlook: {ex.Message}", isError: true);
        }
    }
}
