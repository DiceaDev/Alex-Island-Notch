using AlexIsland.Core.Models;
using AlexIsland.Core.Services;

namespace AlexIsland.Core.Tests;

public class IcsCalendarHelperTests
{
    private static string Calendar(params string[] eventLines)
    {
        string[] lines =
        [
            "BEGIN:VCALENDAR",
            "VERSION:2.0",
            "BEGIN:VEVENT",
            .. eventLines,
            "END:VEVENT",
            "END:VCALENDAR",
        ];
        return string.Join("\r\n", lines);
    }

    [Fact]
    public void Keeps_safe_meeting_url()
    {
        var events = IcsCalendarHelper.ParseIcs(Calendar(
            "SUMMARY:Daily",
            "DTSTART:20261104T130000Z",
            "URL:https://meet.google.com/abc-defg-hij"));

        var ev = Assert.Single(events);
        Assert.Equal("https://meet.google.com/abc-defg-hij", ev.MeetingUrl);
    }

    [Theory]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("ms-msdt:/id PCWDiagnostic")]
    [InlineData("search-ms:query=x")]
    [InlineData("http://meet.google.com/abc")]
    [InlineData("https://evil.example/meet")]
    public void Drops_unsafe_meeting_url(string url)
    {
        var events = IcsCalendarHelper.ParseIcs(Calendar(
            "SUMMARY:Reunión trampa",
            "DTSTART:20261104T130000Z",
            $"URL:{url}"));

        Assert.Null(Assert.Single(events).MeetingUrl);
    }

    [Fact]
    public void Falls_back_to_safe_link_in_description_when_url_is_unsafe()
    {
        var events = IcsCalendarHelper.ParseIcs(Calendar(
            "SUMMARY:Planning",
            "DTSTART:20261104T130000Z",
            "URL:file:///C:/x.exe",
            @"DESCRIPTION:Únete aquí: https://teams.microsoft.com/l/meetup-join/abc"));

        Assert.Equal("https://teams.microsoft.com/l/meetup-join/abc", Assert.Single(events).MeetingUrl);
    }

    [Fact]
    public void Export_omits_unsafe_meeting_url()
    {
        var ics = IcsCalendarHelper.ToIcs(
        [
            new CalendarEvent { Title = "A", MeetingUrl = "file:///C:/x.exe" },
            new CalendarEvent { Title = "B", MeetingUrl = "https://zoom.us/j/1" },
        ]);

        Assert.DoesNotContain("file:", ics);
        Assert.Contains("URL:https://zoom.us/j/1", ics);
    }

    [Fact]
    public void Rejects_calendars_over_size_limit()
    {
        var huge = Calendar("SUMMARY:x", "DESCRIPTION:" + new string('a', IcsCalendarHelper.MaxIcsLength));
        Assert.Throws<InvalidDataException>(() => IcsCalendarHelper.ParseIcs(huge));
    }

    [Fact]
    public void Truncates_very_long_titles()
    {
        var events = IcsCalendarHelper.ParseIcs(Calendar("SUMMARY:" + new string('t', 10_000)));
        Assert.Equal(300, Assert.Single(events).Title.Length);
    }

    [Fact]
    public void Unescapes_text_in_a_single_pass()
    {
        // "\\n" es una barra invertida literal seguida de "n", no un salto de línea.
        var events = IcsCalendarHelper.ParseIcs(Calendar(
            @"SUMMARY:Ruta C:\\new\, carpeta\; ok",
            @"DESCRIPTION:Línea 1\nLínea 2"));

        var ev = Assert.Single(events);
        Assert.Equal(@"Ruta C:\new, carpeta; ok", ev.Title);
        Assert.Equal("Línea 1\nLínea 2", ev.Description);
    }

    [Fact]
    public void Round_trips_title_with_special_characters()
    {
        var original = new CalendarEvent { Title = @"Revisión, fase 1; C:\temp\notes" };
        var parsed = IcsCalendarHelper.ParseIcs(IcsCalendarHelper.ToIcs([original]));
        Assert.Equal(original.Title, Assert.Single(parsed).Title);
    }

    private static readonly DateTimeOffset Today = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Expands_weekly_recurring_meetings_like_outlook_sends_them()
    {
        var events = IcsCalendarHelper.ParseIcs(Calendar(
            "UID:serie1",
            "SUMMARY:Daily IT",
            "DTSTART:20261102T120000Z",
            "DTEND:20261102T123000Z",
            "RRULE:FREQ=WEEKLY;BYDAY=MO,WE;COUNT=4"), now: Today);

        Assert.Equal(4, events.Count);
        Assert.Equal(4, events.Select(e => e.Id).Distinct().Count());
        Assert.All(events, e => Assert.Equal(TimeSpan.FromMinutes(30), e.EndTime - e.StartTime));
    }

    [Fact]
    public void Recurring_series_skips_exdate_and_replaced_occurrences()
    {
        var ics = string.Join("\r\n",
        [
            "BEGIN:VCALENDAR",
            "BEGIN:VEVENT", "UID:s2", "SUMMARY:Sync", "DTSTART:20261102T120000Z", "DTEND:20261102T130000Z",
            "RRULE:FREQ=DAILY;COUNT=5", "EXDATE:20261103T120000Z", "END:VEVENT",
            "BEGIN:VEVENT", "UID:s2", "RECURRENCE-ID:20261104T120000Z", "SUMMARY:Sync movida",
            "DTSTART:20261104T150000Z", "DTEND:20261104T160000Z", "END:VEVENT",
            "END:VCALENDAR",
        ]);

        var events = IcsCalendarHelper.ParseIcs(ics, now: Today);

        // 5 días - 1 excluido - 1 movido + 1 movido = 4
        Assert.Equal(4, events.Count);
        Assert.Contains(events, e => e.Title == "Sync movida");
        Assert.Equal(1, events.Count(e => e.Title == "Sync movida"));
    }

    [Fact]
    public void Cancelled_events_are_ignored()
    {
        var events = IcsCalendarHelper.ParseIcs(Calendar(
            "SUMMARY:Cancelada", "DTSTART:20261104T130000Z", "STATUS:CANCELLED"), now: Today);
        Assert.Empty(events);
    }

    [Fact]
    public void Uses_tzid_to_convert_to_local_time()
    {
        var events = IcsCalendarHelper.ParseIcs(Calendar(
            "SUMMARY:Con zona", "DTSTART;TZID=UTC:20261104T090000", "DTEND;TZID=UTC:20261104T100000"), now: Today);

        var ev = Assert.Single(events);
        Assert.Equal(new DateTimeOffset(2026, 11, 4, 9, 0, 0, TimeSpan.Zero), ev.StartTime);
    }

    [Fact]
    public void Uses_duration_when_there_is_no_end()
    {
        var events = IcsCalendarHelper.ParseIcs(Calendar(
            "SUMMARY:Con duración", "DTSTART:20261104T130000Z", "DURATION:PT1H30M"), now: Today);

        var ev = Assert.Single(events);
        Assert.Equal(TimeSpan.FromMinutes(90), ev.EndTime - ev.StartTime);
    }

    [Fact]
    public void Old_recurring_series_only_returns_occurrences_in_window()
    {
        var events = IcsCalendarHelper.ParseIcs(Calendar(
            "SUMMARY:Antigua", "DTSTART:20240101T120000Z", "DTEND:20240101T130000Z", "RRULE:FREQ=WEEKLY"), now: Today);

        Assert.NotEmpty(events);
        Assert.True(events.Count < 100);
        Assert.All(events, e => Assert.True(e.EndTime >= Today.AddDays(-31)));
        Assert.All(events, e => Assert.True(e.StartTime <= Today.AddDays(401)));
    }

    [Fact]
    public void Monthly_last_friday_is_supported()
    {
        var events = IcsCalendarHelper.ParseIcs(Calendar(
            "SUMMARY:Mensual", "DTSTART:20261030T120000Z", "DTEND:20261030T130000Z",
            "RRULE:FREQ=MONTHLY;BYDAY=-1FR;COUNT=3"), now: Today);

        Assert.Equal(3, events.Count);
    }
}
