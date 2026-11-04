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
}
