using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

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

    private static readonly Regex MeetingUrlRegex = new(
        @"(https?://(meet\.google\.com|teams\.microsoft\.com|teams\.live\.com|[a-zA-Z0-9\-]+\.zoom\.us|[a-zA-Z0-9\-]+\.webex\.com)/[^\s""'>]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string? TryExtractMeetingUrl(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var match = MeetingUrlRegex.Match(text);
        return match.Success ? match.Value : null;
    }
}

