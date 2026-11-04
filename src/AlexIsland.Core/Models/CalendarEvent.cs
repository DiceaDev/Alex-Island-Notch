using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AlexIsland.Core.Security;

namespace AlexIsland.Core.Models;

public sealed partial class CalendarEvent
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("start_time")]
    public DateTimeOffset StartTime { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("end_time")]
    public DateTimeOffset EndTime { get; set; } = DateTimeOffset.UtcNow.AddMinutes(30);

    [JsonPropertyName("location")]
    public string? Location { get; set; }

    [JsonPropertyName("meeting_url")]
    public string? MeetingUrl { get; set; }

    [JsonPropertyName("linked_task_id")]
    public string? LinkedTaskId { get; set; }

    [JsonPropertyName("linked_task_title")]
    public string? LinkedTaskTitle { get; set; }

    [JsonPropertyName("reminder_minutes")]
    public int ReminderMinutes { get; set; } = 10;

    [JsonPropertyName("is_all_day")]
    public bool IsAllDay { get; set; }

    [JsonPropertyName("category")]
    public string Category { get; set; } = "MEETING"; // MEETING, DEV, REVIEW, DEADLINE, GENERAL

    [JsonPropertyName("source")]
    public string Source { get; set; } = "LOCAL"; // LOCAL, GOOGLE, OUTLOOK, ICAL

    public bool HasConflictWith(CalendarEvent other)
    {
        if (Id == other.Id) return false;

        if (IsAllDay || other.IsAllDay)
        {
            return StartTime.Date == other.StartTime.Date;
        }

        return StartTime < other.EndTime && EndTime > other.StartTime;
    }

    public bool IsHappeningNow()
    {
        var now = DateTimeOffset.Now;
        return now >= StartTime && now <= EndTime;
    }

    public bool IsStartingSoon(TimeSpan threshold)
    {
        var now = DateTimeOffset.Now;
        return StartTime > now && (StartTime - now) <= threshold;
    }

    // Localiza candidatos dentro de texto libre. La validación real la hace
    // MeetingUrlPolicy: este patrón solo busca, nunca decide qué es seguro.
    private static readonly Regex MeetingUrlRegex = new(
        @"https://[^\s""'<>]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(250));

    /// <summary>
    /// Busca en el texto el primer enlace de reunión que cumpla
    /// <see cref="MeetingUrlPolicy"/>. Devuelve <c>null</c> si no hay ninguno.
    /// </summary>
    public static string? TryExtractMeetingUrl(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        try
        {
            foreach (Match match in MeetingUrlRegex.Matches(text))
            {
                var safe = MeetingUrlPolicy.Sanitize(match.Value.TrimEnd('.', ',', ';', ')', ']'));
                if (safe is not null) return safe;
            }
        }
        catch (RegexMatchTimeoutException)
        {
            // Texto anómalo o malicioso: se trata como si no hubiera enlace.
        }

        return null;
    }
}

