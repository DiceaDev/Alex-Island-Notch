using System.Globalization;
using System.Text;
using AlexIsland.Core.Models;

namespace AlexIsland.Core.Services;

public static class IcsCalendarHelper
{
    public static string ToIcs(IEnumerable<CalendarEvent> events, string calendarName = "Alex Island Dev Calendar")
    {
        var sb = new StringBuilder();
        sb.AppendLine("BEGIN:VCALENDAR");
        sb.AppendLine("VERSION:2.0");
        sb.AppendLine("PRODID:-//AlexIsland//NotchCalendar//ES");
        sb.AppendLine($"X-WR-CALNAME:{EscapeIcs(calendarName)}");
        sb.AppendLine("CALSCALE:GREGORIAN");
        sb.AppendLine("METHOD:PUBLISH");

        foreach (var ev in events)
        {
            sb.AppendLine("BEGIN:VEVENT");
            sb.AppendLine($"UID:{EscapeIcs(ev.Id)}@alexisland.local");
            sb.AppendLine($"DTSTAMP:{DateTime.UtcNow:yyyyMMddTHHmmssZ}");

            if (ev.IsAllDay)
            {
                sb.AppendLine($"DTSTART;VALUE=DATE:{ev.StartTime:yyyyMMdd}");
                sb.AppendLine($"DTEND;VALUE=DATE:{ev.EndTime.AddDays(1):yyyyMMdd}");
            }
            else
            {
                sb.AppendLine($"DTSTART:{ev.StartTime.ToUniversalTime():yyyyMMddTHHmmssZ}");
                sb.AppendLine($"DTEND:{ev.EndTime.ToUniversalTime():yyyyMMddTHHmmssZ}");
            }

            sb.AppendLine($"SUMMARY:{EscapeIcs(ev.Title)}");
            if (!string.IsNullOrWhiteSpace(ev.Description)) sb.AppendLine($"DESCRIPTION:{EscapeIcs(ev.Description)}");
            if (!string.IsNullOrWhiteSpace(ev.Location)) sb.AppendLine($"LOCATION:{EscapeIcs(ev.Location)}");
            if (!string.IsNullOrWhiteSpace(ev.MeetingUrl)) sb.AppendLine($"URL:{ev.MeetingUrl}");
            if (!string.IsNullOrWhiteSpace(ev.Category)) sb.AppendLine($"CATEGORIES:{EscapeIcs(ev.Category)}");
            sb.AppendLine("END:VEVENT");
        }

        sb.AppendLine("END:VCALENDAR");
        return sb.ToString();
    }

    public static List<CalendarEvent> ParseIcs(string icsContent, string defaultSource = "ICAL")
    {
        var results = new List<CalendarEvent>();
        if (string.IsNullOrWhiteSpace(icsContent)) return results;

        CalendarEvent? current = null;
        var hasStart = false;
        var hasEnd = false;
        var invalidDate = false;

        foreach (var rawLine in UnfoldIcsLines(icsContent))
        {
            var line = rawLine.Trim();
            if (line.Equals("BEGIN:VEVENT", StringComparison.OrdinalIgnoreCase))
            {
                current = new CalendarEvent { Source = defaultSource };
                hasStart = false;
                hasEnd = false;
                invalidDate = false;
                continue;
            }

            if (line.Equals("END:VEVENT", StringComparison.OrdinalIgnoreCase))
            {
                if (current is not null &&
                    !invalidDate &&
                    hasStart &&
                    hasEnd &&
                    !string.IsNullOrWhiteSpace(current.Title) &&
                    current.EndTime > current.StartTime)
                {
                    current.MeetingUrl ??= CalendarEvent.TryExtractMeetingUrl(current.Description)
                                           ?? CalendarEvent.TryExtractMeetingUrl(current.Location);
                    results.Add(current);
                }

                current = null;
                continue;
            }

            if (current is null) continue;

            var colonIdx = line.IndexOf(':');
            if (colonIdx <= 0) continue;

            var keyPart = line[..colonIdx];
            var valuePart = line[(colonIdx + 1)..];
            var segments = keyPart.Split(';');
            var propName = segments[0].ToUpperInvariant();
            var tzid = segments
                .Skip(1)
                .FirstOrDefault(p => p.StartsWith("TZID=", StringComparison.OrdinalIgnoreCase))?
                .Substring(5)
                .Trim('"');

            switch (propName)
            {
                case "UID":
                    if (!string.IsNullOrWhiteSpace(valuePart))
                        current.Id = valuePart.EndsWith("@alexisland.local", StringComparison.Ordinal)
                            ? valuePart[..(valuePart.Length - "@alexisland.local".Length)]
                            : valuePart;
                    break;
                case "SUMMARY":
                    current.Title = UnescapeIcs(valuePart);
                    break;
                case "DESCRIPTION":
                    current.Description = UnescapeIcs(valuePart);
                    break;
                case "LOCATION":
                    current.Location = UnescapeIcs(valuePart);
                    break;
                case "URL":
                    current.MeetingUrl = valuePart.Trim();
                    break;
                case "CATEGORIES":
                    current.Category = UnescapeIcs(valuePart.Split(',')[0]).Trim().ToUpperInvariant();
                    break;
                case "DTSTART":
                case "DTEND":
                    if (!TryParseIcsDateTime(valuePart, tzid, out var parsed, out var isAllDay))
                    {
                        invalidDate = true;
                        break;
                    }

                    if (propName == "DTSTART")
                    {
                        current.StartTime = parsed;
                        current.IsAllDay = isAllDay;
                        hasStart = true;
                    }
                    else
                    {
                        current.EndTime = parsed;
                        hasEnd = true;
                    }
                    break;
            }
        }

        return results;
    }

    private static bool TryParseIcsDateTime(
        string value,
        string? timeZoneId,
        out DateTimeOffset result,
        out bool isAllDay)
    {
        result = default;
        isAllDay = false;
        var clean = value.Trim();

        if (DateTime.TryParseExact(clean, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateOnly))
        {
            isAllDay = true;
            result = new DateTimeOffset(DateTime.SpecifyKind(dateOnly.Date, DateTimeKind.Unspecified), TimeSpan.Zero);
            return true;
        }

        var formats = new[] { "yyyyMMdd'T'HHmmss'Z'", "yyyyMMdd'T'HHmm'Z'", "yyyyMMdd'T'HHmmss", "yyyyMMdd'T'HHmm" };
        if (!DateTime.TryParseExact(clean, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return false;

        if (clean.EndsWith('Z'))
        {
            result = new DateTimeOffset(DateTime.SpecifyKind(parsed, DateTimeKind.Utc));
            return true;
        }

        parsed = DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified);
        TimeZoneInfo zone;
        try
        {
            zone = string.IsNullOrWhiteSpace(timeZoneId)
                ? TimeZoneInfo.Local
                : TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }

        if (zone.IsInvalidTime(parsed)) return false;

        var offset = zone.GetUtcOffset(parsed);
        result = new DateTimeOffset(parsed, offset);
        return true;
    }

    private static List<string> UnfoldIcsLines(string ics)
    {
        var rawLines = ics.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var unfolded = new List<string>();

        foreach (var line in rawLines)
        {
            if (line.StartsWith(' ') || line.StartsWith('\t'))
            {
                if (unfolded.Count > 0) unfolded[^1] += line[1..];
            }
            else
            {
                unfolded.Add(line);
            }
        }

        return unfolded;
    }

    private static string EscapeIcs(string text)
    {
        return text
            .Replace(@"\", @"\\")
            .Replace(";", @"\;")
            .Replace(",", @"\,")
            .Replace("\n", @"\n")
            .Replace("\r", "");
    }

    private static string UnescapeIcs(string text)
    {
        return text
            .Replace(@"\N", "\n")
            .Replace(@"\n", "\n")
            .Replace(@"\r", "")
            .Replace(@"\,", ",")
            .Replace(@"\;", ";")
            .Replace(@"\\", @"\");
    }
}
