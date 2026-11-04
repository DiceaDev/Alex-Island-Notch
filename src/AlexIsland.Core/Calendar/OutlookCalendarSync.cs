using System.Security.Cryptography;
using System.Text;
using AlexIsland.Core.Models;

namespace AlexIsland.Core.Calendar;

/// <summary>Cita leída del calendario de Outlook, sin depender de COM.</summary>
/// <param name="GlobalId">GlobalAppointmentID: igual al UID que Outlook pone en los .ics.</param>
/// <param name="Subject">Asunto.</param>
/// <param name="StartUtc">Inicio en UTC.</param>
/// <param name="EndUtc">Fin en UTC.</param>
/// <param name="IsAllDay">Evento de día completo.</param>
/// <param name="Location">Ubicación (a veces contiene el enlace de la reunión).</param>
/// <param name="Body">Cuerpo; solo se usa para buscar el enlace de la reunión y no se guarda.</param>
/// <param name="MeetingStatus">OlMeetingStatus de Outlook (5 y 7 = cancelada).</param>
/// <param name="ReminderMinutes">Minutos de aviso configurados en Outlook, o null si no tiene recordatorio.</param>
public sealed record OutlookAppointment(
    string GlobalId,
    string Subject,
    DateTime StartUtc,
    DateTime EndUtc,
    bool IsAllDay,
    string? Location,
    string? Body,
    int MeetingStatus,
    int? ReminderMinutes);

/// <summary>Resultado de aplicar una lectura de Outlook a la agenda.</summary>
public sealed record OutlookSyncResult(int Added, int Removed, int Updated)
{
    public bool HasChanges => Added + Removed + Updated > 0;
}

/// <summary>
/// Importación en un solo sentido: Outlook → isla. La agenda refleja lo que hay en
/// Outlook dentro de la ventana leída (lo nuevo se agrega, lo movido se actualiza
/// y lo borrado o cancelado desaparece). Nunca se escribe nada en Outlook y los
/// eventos creados en la isla no se tocan.
/// </summary>
public static class OutlookCalendarSync
{
    public const string IdPrefix = "outlook-";
    public const string SourceName = "OUTLOOK";

    private const int OlMeetingCanceled = 5;
    private const int OlMeetingReceivedAndCanceled = 7;

    public static bool IsCancelled(int meetingStatus) =>
        meetingStatus is OlMeetingCanceled or OlMeetingReceivedAndCanceled;

    /// <summary>Id estable por ocurrencia: las reuniones recurrentes comparten GlobalId, así que se combina con el inicio.</summary>
    public static string BuildId(string globalId, DateTime startUtc)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{globalId}|{startUtc.Ticks}"));
        return IdPrefix + Convert.ToHexString(bytes, 0, 10).ToLowerInvariant();
    }

    /// <summary>Convierte una cita en evento de la isla, o null si está cancelada o no tiene datos útiles.</summary>
    public static CalendarEvent? ToEvent(OutlookAppointment appointment, TimeZoneInfo? localZone = null)
    {
        ArgumentNullException.ThrowIfNull(appointment);
        if (IsCancelled(appointment.MeetingStatus)) return null;
        if (appointment.EndUtc < appointment.StartUtc) return null;

        var zone = localZone ?? TimeZoneInfo.Local;
        var startUtc = DateTime.SpecifyKind(appointment.StartUtc, DateTimeKind.Utc);
        var endUtc = DateTime.SpecifyKind(appointment.EndUtc, DateTimeKind.Utc);

        var title = Notifications.NotificationPresenter.Clean(appointment.Subject);
        var location = Notifications.NotificationPresenter.Clean(appointment.Location);

        return new CalendarEvent
        {
            Id = BuildId(appointment.GlobalId, startUtc),
            Title = title.Length > 0 ? title : "(Sin asunto)",
            StartTime = TimeZoneInfo.ConvertTime(new DateTimeOffset(startUtc), zone),
            EndTime = TimeZoneInfo.ConvertTime(new DateTimeOffset(endUtc), zone),
            IsAllDay = appointment.IsAllDay,
            Location = location.Length > 0 ? location : null,
            // Primero la ubicación (Zoom suele ir ahí) y luego el cuerpo (Teams).
            MeetingUrl = CalendarEvent.TryExtractMeetingUrl(appointment.Location)
                         ?? CalendarEvent.TryExtractMeetingUrl(appointment.Body),
            ReminderMinutes = appointment.ReminderMinutes is int m and >= 0 and <= 7 * 24 * 60 ? m : 10,
            Category = "MEETING",
            Source = SourceName,
        };
    }

    /// <summary>
    /// Aplica una lectura completa de Outlook para la ventana [<paramref name="windowStart"/>, <paramref name="windowEnd"/>].
    /// Conserva la tarea vinculada de cada reunión que ya existía y reemplaza
    /// las copias importadas antes por .ics de esas mismas reuniones.
    /// </summary>
    public static OutlookSyncResult Apply(
        List<CalendarEvent> events,
        IReadOnlyList<OutlookAppointment> appointments,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        TimeZoneInfo? localZone = null)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(appointments);

        var fresh = new Dictionary<string, CalendarEvent>(StringComparer.Ordinal);
        var globalIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var appointment in appointments)
        {
            if (!string.IsNullOrWhiteSpace(appointment.GlobalId)) globalIds.Add(appointment.GlobalId);
            var ev = ToEvent(appointment, localZone);
            if (ev is not null) fresh[ev.Id] = ev;
        }

        var previous = events
            .Where(e => e.Id.StartsWith(IdPrefix, StringComparison.Ordinal))
            .GroupBy(e => e.Id)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        bool InWindow(CalendarEvent e) => e.EndTime > windowStart && e.StartTime < windowEnd;

        // Copias de las mismas reuniones traídas antes por .ics (mismo UID, o
        // mismo título e inicio). Los eventos creados en la isla nunca se borran.
        bool IsIcsDuplicate(CalendarEvent e) =>
            !e.Id.StartsWith(IdPrefix, StringComparison.Ordinal)
            && !string.Equals(e.Source, "LOCAL", StringComparison.OrdinalIgnoreCase)
            && (globalIds.Contains(e.Id)
                || fresh.Values.Any(f => f.StartTime == e.StartTime
                                         && string.Equals(f.Title, e.Title, StringComparison.OrdinalIgnoreCase)));

        var removed = 0;
        var updated = 0;
        var added = 0;

        removed += events.RemoveAll(e =>
            (e.Id.StartsWith(IdPrefix, StringComparison.Ordinal) && InWindow(e) && !fresh.ContainsKey(e.Id))
            || IsIcsDuplicate(e));

        foreach (var ev in fresh.Values)
        {
            if (previous.TryGetValue(ev.Id, out var old) && events.Contains(old))
            {
                // Lo que el usuario agregó en la isla (tarea vinculada) se mantiene.
                ev.LinkedTaskId = old.LinkedTaskId;
                ev.LinkedTaskTitle = old.LinkedTaskTitle;

                if (!SameContent(old, ev))
                {
                    events[events.IndexOf(old)] = ev;
                    updated++;
                }
                continue;
            }

            events.Add(ev);
            added++;
        }

        return new OutlookSyncResult(added, removed, updated);
    }

    private static bool SameContent(CalendarEvent a, CalendarEvent b) =>
        a.Title == b.Title
        && a.StartTime == b.StartTime
        && a.EndTime == b.EndTime
        && a.IsAllDay == b.IsAllDay
        && a.Location == b.Location
        && a.MeetingUrl == b.MeetingUrl
        && a.ReminderMinutes == b.ReminderMinutes;
}
