using System.Globalization;
using System.Text;
using AlexIsland.Core.Models;
using AlexIsland.Core.Security;

namespace AlexIsland.Core.Services;

public static class IcsCalendarHelper
{
    /// <summary>Tamaño máximo (en caracteres) de un calendario que se acepta importar.</summary>
    public const int MaxIcsLength = 2 * 1024 * 1024;

    /// <summary>Cantidad máxima de eventos que se importan de un solo calendario.</summary>
    public const int MaxEvents = 5000;

    private const int MaxTitleLength = 300;
    private const int MaxTextLength = 4000;

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

            var safeUrl = MeetingUrlPolicy.Sanitize(ev.MeetingUrl);
            if (safeUrl is not null)
            {
                sb.AppendLine($"URL:{safeUrl}");
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

    /// <summary>Las reuniones recurrentes se expanden desde 30 días atrás hasta ~13 meses adelante.</summary>
    private const int WindowPastDays = 30;
    private const int WindowFutureDays = 400;
    private const int MaxOccurrencesPerSeries = 2000;
    private const int MaxPeriods = 20000;

    private sealed class RawEvent
    {
        public CalendarEvent Event { get; } = new();
        public string? RRule { get; set; }
        public string? Duration { get; set; }
        public bool HasEnd { get; set; }
        public bool Cancelled { get; set; }
        public DateTimeOffset? RecurrenceId { get; set; }
        public List<DateTimeOffset> ExDates { get; } = [];
    }

    /// <summary>
    /// Lee un .ics. Entiende zonas horarias (TZID), duraciones, reuniones recurrentes (RRULE con
    /// EXDATE y ocurrencias modificadas) y eventos cancelados, que es lo que publica Outlook.
    /// <paramref name="now"/> solo se usa para fijar la ventana de expansión (útil en pruebas).
    /// </summary>
    public static List<CalendarEvent> ParseIcs(string icsContent, string defaultSource = "ICAL", DateTimeOffset? now = null)
    {
        if (string.IsNullOrWhiteSpace(icsContent)) return [];
        if (icsContent.Length > MaxIcsLength)
        {
            throw new InvalidDataException(
                $"El calendario supera el tamaño máximo permitido ({MaxIcsLength / (1024 * 1024)} MB).");
        }

        var lines = UnfoldIcsLines(icsContent);
        var raws = new List<RawEvent>();
        RawEvent? current = null;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Equals("BEGIN:VEVENT", StringComparison.OrdinalIgnoreCase))
            {
                current = new RawEvent();
                current.Event.Source = defaultSource;
                continue;
            }

            if (line.Equals("END:VEVENT", StringComparison.OrdinalIgnoreCase))
            {
                if (current is not null && !string.IsNullOrWhiteSpace(current.Event.Title))
                {
                    var ev = current.Event;
                    ev.MeetingUrl ??= CalendarEvent.TryExtractMeetingUrl(ev.Description)
                                      ?? CalendarEvent.TryExtractMeetingUrl(ev.Location);

                    if (!current.HasEnd)
                    {
                        ev.EndTime = TryParseDuration(current.Duration, out var span)
                            ? ev.StartTime + span
                            : ev.IsAllDay ? ev.StartTime.AddDays(1) : ev.StartTime;
                    }

                    raws.Add(current);
                    if (raws.Count >= MaxEvents) break;
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
            var ev2 = current.Event;

            switch (propName)
            {
                case "UID":
                    if (!string.IsNullOrWhiteSpace(valuePart)) ev2.Id = valuePart.Replace("@alexisland.local", "");
                    break;
                case "SUMMARY":
                    ev2.Title = Truncate(UnescapeIcs(valuePart), MaxTitleLength);
                    break;
                case "DESCRIPTION":
                    ev2.Description = Truncate(UnescapeIcs(valuePart), MaxTextLength);
                    break;
                case "LOCATION":
                    ev2.Location = Truncate(UnescapeIcs(valuePart), MaxTitleLength);
                    break;
                case "URL":
                    // Un URL no permitido se descarta; al cerrar el evento se intenta
                    // extraer uno válido de la descripción o la ubicación.
                    ev2.MeetingUrl = MeetingUrlPolicy.Sanitize(valuePart);
                    break;
                case "CATEGORIES":
                    ev2.Category = Truncate(valuePart.Split(',')[0].Trim().ToUpperInvariant(), 32);
                    break;
                case "STATUS":
                    current.Cancelled = valuePart.Trim().Equals("CANCELLED", StringComparison.OrdinalIgnoreCase);
                    break;
                case "DTSTART":
                    ev2.StartTime = ParseIcsDateTime(valuePart, GetParam(keyPart, "TZID"), out var isAllDay);
                    ev2.IsAllDay = isAllDay;
                    break;
                case "DTEND":
                    ev2.EndTime = ParseIcsDateTime(valuePart, GetParam(keyPart, "TZID"), out _);
                    current.HasEnd = true;
                    break;
                case "DURATION":
                    current.Duration = valuePart.Trim();
                    break;
                case "RRULE":
                    current.RRule = valuePart.Trim();
                    break;
                case "RECURRENCE-ID":
                    current.RecurrenceId = ParseIcsDateTime(valuePart, GetParam(keyPart, "TZID"), out _);
                    break;
                case "EXDATE":
                    var tzid = GetParam(keyPart, "TZID");
                    foreach (var part in valuePart.Split(','))
                    {
                        if (current.ExDates.Count < 1000)
                        {
                            current.ExDates.Add(ParseIcsDateTime(part, tzid, out _));
                        }
                    }
                    break;
            }
        }

        return BuildEvents(raws, now ?? DateTimeOffset.Now);
    }

    private static List<CalendarEvent> BuildEvents(List<RawEvent> raws, DateTimeOffset now)
    {
        var windowStart = now.AddDays(-WindowPastDays);
        var windowEnd = now.AddDays(WindowFutureDays);

        // Ocurrencias de una serie que fueron movidas o canceladas: se omiten de la serie.
        var replaced = new HashSet<(string, long)>();
        foreach (var raw in raws)
        {
            if (raw.RecurrenceId is { } rid) replaced.Add((raw.Event.Id, rid.UtcDateTime.Ticks));
        }

        var results = new List<CalendarEvent>();
        foreach (var raw in raws)
        {
            if (raw.Cancelled) continue;
            var ev = raw.Event;

            if (raw.RecurrenceId is not null)
            {
                // Ocurrencia modificada: se agrega como evento propio.
                ev.Id = OccurrenceId(ev.Id, ev.StartTime);
                results.Add(ev);
            }
            else if (raw.RRule is null)
            {
                results.Add(ev);
            }
            else
            {
                foreach (var occurrence in ExpandSeries(raw, replaced, windowStart, windowEnd))
                {
                    results.Add(occurrence);
                    if (results.Count >= MaxEvents) break;
                }
            }

            if (results.Count >= MaxEvents) break;
        }

        return results;
    }

    private static string OccurrenceId(string baseId, DateTimeOffset start)
        => $"{baseId}_{start.UtcDateTime:yyyyMMddTHHmm}";

    private static IEnumerable<CalendarEvent> ExpandSeries(
        RawEvent raw, HashSet<(string, long)> replaced, DateTimeOffset windowStart, DateTimeOffset windowEnd)
    {
        var master = raw.Event;
        var rule = RecurrenceRule.Parse(raw.RRule!);
        if (rule is null)
        {
            // Regla que no se entiende: al menos se conserva la primera reunión.
            yield return master;
            yield break;
        }

        var duration = master.EndTime - master.StartTime;
        if (duration < TimeSpan.Zero) duration = TimeSpan.Zero;

        var timeOfDay = master.IsAllDay ? TimeSpan.Zero : master.StartTime.ToLocalTime().TimeOfDay;
        var firstDate = master.IsAllDay ? master.StartTime.Date : master.StartTime.ToLocalTime().Date;
        var limit = master.IsAllDay ? windowEnd.UtcDateTime.Date : windowEnd.ToLocalTime().Date;
        var excluded = raw.ExDates.Select(d => d.UtcDateTime.Ticks).ToHashSet();
        var emitted = 0;

        foreach (var date in GenerateDates(firstDate, rule, limit))
        {
            var start = master.IsAllDay ? new DateTimeOffset(date, TimeSpan.Zero) : MakeLocal(date + timeOfDay);
            var key = start.UtcDateTime.Ticks;
            if (excluded.Contains(key) || replaced.Contains((master.Id, key))) continue;
            if (start + duration < windowStart) continue;

            yield return new CalendarEvent
            {
                Id = OccurrenceId(master.Id, start),
                Title = master.Title,
                Description = master.Description,
                Location = master.Location,
                MeetingUrl = master.MeetingUrl,
                Category = master.Category,
                Source = master.Source,
                IsAllDay = master.IsAllDay,
                StartTime = start,
                EndTime = start + duration,
            };

            if (++emitted >= MaxOccurrencesPerSeries) yield break;
        }
    }

    private static DateTimeOffset MakeLocal(DateTime wallClock)
    {
        var unspecified = DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified);
        if (TimeZoneInfo.Local.IsInvalidTime(unspecified)) unspecified = unspecified.AddHours(1);
        return new DateTimeOffset(unspecified, TimeZoneInfo.Local.GetUtcOffset(unspecified));
    }

    private sealed class RecurrenceRule
    {
        public string Freq { get; private init; } = "";
        public int Interval { get; private init; } = 1;
        public int? Count { get; private init; }
        public DateTime? Until { get; private init; }
        public List<(int Ordinal, DayOfWeek Day)> ByDay { get; } = [];
        public List<int> ByMonthDay { get; } = [];
        public List<int> ByMonth { get; } = [];

        public static RecurrenceRule? Parse(string text)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in text.Split(';'))
            {
                var eq = part.IndexOf('=');
                if (eq > 0) map[part[..eq].Trim()] = part[(eq + 1)..].Trim();
            }

            if (!map.TryGetValue("FREQ", out var freq)) return null;
            freq = freq.ToUpperInvariant();
            if (freq is not ("DAILY" or "WEEKLY" or "MONTHLY" or "YEARLY")) return null;

            var interval = map.TryGetValue("INTERVAL", out var iv) && int.TryParse(iv, out var i) ? Math.Clamp(i, 1, 1000) : 1;
            int? count = map.TryGetValue("COUNT", out var c) && int.TryParse(c, out var n) && n > 0 ? n : null;

            DateTime? until = null;
            if (map.TryGetValue("UNTIL", out var u))
            {
                var parsed = ParseIcsDateTime(u, null, out var allDay);
                until = allDay ? parsed.Date : parsed.ToLocalTime().Date;
            }

            var rule = new RecurrenceRule { Freq = freq, Interval = interval, Count = count, Until = until };

            if (map.TryGetValue("BYDAY", out var byDay))
            {
                foreach (var token in byDay.Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    var t = token.Trim().ToUpperInvariant();
                    if (t.Length < 2) continue;
                    var day = t[^2..] switch
                    {
                        "MO" => DayOfWeek.Monday, "TU" => DayOfWeek.Tuesday, "WE" => DayOfWeek.Wednesday,
                        "TH" => DayOfWeek.Thursday, "FR" => DayOfWeek.Friday, "SA" => DayOfWeek.Saturday,
                        "SU" => DayOfWeek.Sunday, _ => (DayOfWeek?)null,
                    };
                    if (day is null) continue;
                    var ordinal = t.Length > 2 && int.TryParse(t[..^2], out var o) ? o : 0;
                    rule.ByDay.Add((ordinal, day.Value));
                }
            }

            AddInts(map, "BYMONTHDAY", rule.ByMonthDay);
            AddInts(map, "BYMONTH", rule.ByMonth);
            return rule;
        }

        private static void AddInts(Dictionary<string, string> map, string key, List<int> target)
        {
            if (!map.TryGetValue(key, out var value)) return;
            foreach (var token in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(token.Trim(), out var n) && n != 0) target.Add(n);
            }
        }
    }

    private static IEnumerable<DateTime> GenerateDates(DateTime start, RecurrenceRule rule, DateTime limit)
    {
        var produced = 0;
        for (var k = 0; k < MaxPeriods; k++)
        {
            foreach (var date in PeriodCandidates(start, rule, k))
            {
                if (date < start) continue;
                if (rule.Until is { } until && date > until) yield break;
                if (date > limit) yield break;

                yield return date;
                if (rule.Count is { } count && ++produced >= count) yield break;
            }
        }
    }

    private static List<DateTime> PeriodCandidates(DateTime start, RecurrenceRule rule, int k)
    {
        var result = new List<DateTime>();
        var step = (long)k * rule.Interval;
        if (step > 100_000) return result;

        switch (rule.Freq)
        {
            case "DAILY":
                result.Add(start.AddDays(step));
                break;

            case "WEEKLY":
            {
                var weekStart = start.AddDays(-(((int)start.DayOfWeek + 6) % 7)).AddDays(7 * step);
                IEnumerable<DayOfWeek> days = rule.ByDay.Count > 0 ? rule.ByDay.Select(d => d.Day).Distinct() : [start.DayOfWeek];
                result.AddRange(days.Select(d => weekStart.AddDays(((int)d + 6) % 7)));
                break;
            }

            case "MONTHLY":
            {
                if (step > 12_000) return result;
                var first = new DateTime(start.Year, start.Month, 1).AddMonths((int)step);
                result.AddRange(DaysOfMonth(first.Year, first.Month, rule, start.Day));
                break;
            }

            case "YEARLY":
            {
                if (step > 1_000) return result;
                var year = start.Year + (int)step;
                IEnumerable<int> months = rule.ByMonth.Count > 0 ? rule.ByMonth.Where(m => m is >= 1 and <= 12) : [start.Month];
                foreach (var month in months.Distinct().OrderBy(m => m))
                {
                    result.AddRange(DaysOfMonth(year, month, rule, start.Day));
                }
                break;
            }
        }

        result.Sort();
        return result;
    }

    private static IEnumerable<DateTime> DaysOfMonth(int year, int month, RecurrenceRule rule, int defaultDay)
    {
        var daysInMonth = DateTime.DaysInMonth(year, month);

        if (rule.ByMonthDay.Count > 0)
        {
            foreach (var md in rule.ByMonthDay)
            {
                var day = md > 0 ? md : daysInMonth + md + 1;
                if (day >= 1 && day <= daysInMonth) yield return new DateTime(year, month, day);
            }
            yield break;
        }

        if (rule.ByDay.Count > 0)
        {
            foreach (var (ordinal, weekday) in rule.ByDay)
            {
                var matches = Enumerable.Range(1, daysInMonth)
                    .Select(d => new DateTime(year, month, d))
                    .Where(d => d.DayOfWeek == weekday)
                    .ToList();

                if (ordinal == 0)
                {
                    foreach (var d in matches) yield return d;
                }
                else
                {
                    var index = ordinal > 0 ? ordinal - 1 : matches.Count + ordinal;
                    if (index >= 0 && index < matches.Count) yield return matches[index];
                }
            }
            yield break;
        }

        if (defaultDay <= daysInMonth) yield return new DateTime(year, month, defaultDay);
    }

    private static string? GetParam(string keyPart, string name)
    {
        foreach (var part in keyPart.Split(';').Skip(1))
        {
            var eq = part.IndexOf('=');
            if (eq > 0 && part[..eq].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return part[(eq + 1)..].Trim().Trim('"');
            }
        }

        return null;
    }

    private static bool TryParseDuration(string? text, out TimeSpan span)
    {
        span = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var match = System.Text.RegularExpressions.Regex.Match(
            text.Trim().ToUpperInvariant(),
            @"^([+-])?P(?:(\d+)W)?(?:(\d+)D)?(?:T(?:(\d+)H)?(?:(\d+)M)?(?:(\d+)S)?)?$",
            System.Text.RegularExpressions.RegexOptions.None,
            TimeSpan.FromSeconds(1));
        if (!match.Success) return false;

        static int Part(System.Text.RegularExpressions.Group g)
            => g.Success && int.TryParse(g.Value, out var v) ? Math.Min(v, 100_000) : 0;

        span = TimeSpan.FromDays(Part(match.Groups[2]) * 7 + Part(match.Groups[3]))
               + TimeSpan.FromHours(Part(match.Groups[4]))
               + TimeSpan.FromMinutes(Part(match.Groups[5]))
               + TimeSpan.FromSeconds(Part(match.Groups[6]));
        if (match.Groups[1].Value == "-") span = -span;
        return true;
    }

    private static bool TryResolveZone(string? tzid, out TimeZoneInfo zone)
    {
        zone = TimeZoneInfo.Utc;
        if (string.IsNullOrWhiteSpace(tzid)) return false;

        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(tzid);
            return true;
        }
        catch (Exception)
        {
            // Outlook también usa "tzone://Microsoft/Utc" y nombres personalizados.
            if (tzid.Contains("utc", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }

    private static DateTimeOffset ParseIcsDateTime(string value, string? tzid, out bool isAllDay)
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

        // Hora "flotante" con zona horaria (DTSTART;TZID=...): se convierte a la hora local del equipo.
        if (TryResolveZone(tzid, out var zone) &&
            DateTime.TryParseExact(clean, ["yyyyMMddTHHmmss", "yyyyMMddTHHmm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var wall))
        {
            var unspecified = DateTime.SpecifyKind(wall, DateTimeKind.Unspecified);
            if (zone.IsInvalidTime(unspecified)) unspecified = unspecified.AddHours(1);
            return new DateTimeOffset(unspecified, zone.GetUtcOffset(unspecified)).ToLocalTime();
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

    // Recorre el texto una sola vez: con Replace encadenados, una secuencia
    // como "\\n" (barra escapada seguida de "n") se convertía por error en salto de línea.
    private static string UnescapeIcs(string text)
    {
        if (text.IndexOf('\\') < 0) return text;

        var sb = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c != '\\' || i == text.Length - 1)
            {
                sb.Append(c);
                continue;
            }

            var next = text[++i];
            switch (next)
            {
                case 'n' or 'N':
                    sb.Append('\n');
                    break;
                case ',' or ';' or '\\':
                    sb.Append(next);
                    break;
                default:
                    sb.Append('\\').Append(next);
                    break;
            }
        }

        return sb.ToString();
    }

    private static string Truncate(string text, int maxLength)
        => text.Length <= maxLength ? text : text[..maxLength];
}

