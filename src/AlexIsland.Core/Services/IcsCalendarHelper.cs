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
        sb.AppendLine($"X-WR-CALNAME:{calendarName}");
        sb.AppendLine("CALSCALE:GREGORIAN");
        sb.AppendLine("METHOD:PUBLISH");

        foreach (var ev in events)
        {
            sb.AppendLine("BEGIN:VEVENT");
            sb.AppendLine($"UID:{ev.Id}@alexisland.local");
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

            if (!string.IsNullOrWhiteSpace(ev.Description))
            {
                sb.AppendLine($"DESCRIPTION:{EscapeIcs(ev.Description)}");
            }

            if (!string.IsNullOrWhiteSpace(ev.Location))
            {
                sb.AppendLine($"LOCATION:{EscapeIcs(ev.Location)}");
            }

            if (!string.IsNullOrWhiteSpace(ev.MeetingUrl))
            {
                sb.AppendLine($"URL:{ev.MeetingUrl}");
            }

            if (!string.IsNullOrWhiteSpace(ev.Category))
            {
                sb.AppendLine($"CATEGORIES:{ev.Category}");
            }

            sb.AppendLine("END:VEVENT");
        }

        sb.AppendLine("END:VCALENDAR");
        return sb.ToString();
    }

    public static List<CalendarEvent> ParseIcs(string icsContent, string defaultSource = "ICAL")
    {
        var results = new List<CalendarEvent>();
        if (string.IsNullOrWhiteSpace(icsContent)) return results;

        var lines = UnfoldIcsLines(icsContent);
        CalendarEvent? current = null;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Equals("BEGIN:VEVENT", StringComparison.OrdinalIgnoreCase))
            {
                current = new CalendarEvent { Source = defaultSource };
                continue;
            }

            if (line.Equals("END:VEVENT", StringComparison.OrdinalIgnoreCase))
            {
                if (current is not null && !string.IsNullOrWhiteSpace(current.Title))
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

            var paramIdx = keyPart.IndexOf(';');
            var propName = (paramIdx > 0 ? keyPart[..paramIdx] : keyPart).ToUpperInvariant();

            switch (propName)
            {
                case "UID":
                    if (!string.IsNullOrWhiteSpace(valuePart)) current.Id = valuePart.Replace("@alexisland.local", "");
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
                    current.Category = valuePart.Split(',')[0].Trim().ToUpperInvariant();
                    break;
                case "DTSTART":
                    current.StartTime = ParseIcsDateTime(valuePart, out var isAllDay);
                    current.IsAllDay = isAllDay;
                    break;
                case "DTEND":
                    current.EndTime = ParseIcsDateTime(valuePart, out _);
                    break;
            }
        }

        return results;
    }

    private static DateTimeOffset ParseIcsDateTime(string value, out bool isAllDay)
    {
        isAllDay = false;
        var clean = value.Trim();

        if (clean.Length == 8 && DateTime.TryParseExact(clean, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateOnly))
        {
            isAllDay = true;
            return new DateTimeOffset(dateOnly, TimeSpan.Zero);
        }

        if (DateTimeOffset.TryParseExact(clean, "yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dtUtc))
        {
            return dtUtc.ToLocalTime();
        }

        if (DateTimeOffset.TryParseExact(clean, "yyyyMMddTHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dtLocal))
        {
            return dtLocal;
        }

        if (DateTimeOffset.TryParseExact(clean, "yyyyMMddTHHmmZ", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dtUtcShort))
        {
            return dtUtcShort.ToLocalTime();
        }

        if (DateTimeOffset.TryParseExact(clean, "yyyyMMddTHHmm", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dtLocalShort))
        {
            return dtLocalShort;
        }

        if (DateTimeOffset.TryParse(clean, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dtParsed))
        {
            return dtParsed;
        }

        return DateTimeOffset.Now;
    }

    private static List<string> UnfoldIcsLines(string ics)
    {
        var rawLines = ics.Replace("\r\n", "\n").Split('\n');
        var unfolded = new List<string>();

        foreach (var line in rawLines)
        {
            if (line.StartsWith(' ') || line.StartsWith('\t'))
            {
                if (unfolded.Count > 0)
                {
                    unfolded[^1] += line[1..];
                }
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

