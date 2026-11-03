using AlexIsland.Core.Models;
using AlexIsland.Core.Services;
using Xunit;

namespace AlexIsland.Core.Tests;

public sealed class CalendarEventRegressionTests
{
    [Fact]
    public void HasConflictWith_AllDayAndTimedEventsOnSameDate_Conflicts()
    {
        var allDay = new CalendarEvent
        {
            Id = "all-day",
            IsAllDay = true,
            StartTime = new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.Zero),
            EndTime = new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.Zero)
        };
        var timed = new CalendarEvent
        {
            Id = "timed",
            StartTime = new DateTimeOffset(2026, 11, 3, 15, 0, 0, TimeSpan.Zero),
            EndTime = new DateTimeOffset(2026, 11, 3, 16, 0, 0, TimeSpan.Zero)
        };

        Assert.True(allDay.HasConflictWith(timed));
        Assert.True(timed.HasConflictWith(allDay));
    }

    [Fact]
    public void HasConflictWith_AllDayAndTimedEventsOnDifferentDates_DoNotConflict()
    {
        var allDay = new CalendarEvent
        {
            Id = "all-day",
            IsAllDay = true,
            StartTime = new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.Zero),
            EndTime = new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.Zero)
        };
        var timed = new CalendarEvent
        {
            Id = "timed",
            StartTime = new DateTimeOffset(2026, 11, 4, 9, 0, 0, TimeSpan.Zero),
            EndTime = new DateTimeOffset(2026, 11, 4, 10, 0, 0, TimeSpan.Zero)
        };

        Assert.False(allDay.HasConflictWith(timed));
        Assert.False(timed.HasConflictWith(allDay));
    }
}

public sealed class IcsCalendarRegressionTests
{
    [Fact]
    public void ParseIcs_OneDayAllDayEvent_UsesInclusiveInternalEnd()
    {
        const string ics = """
            BEGIN:VCALENDAR
            BEGIN:VEVENT
            UID:one-day
            DTSTART;VALUE=DATE:20261103
            DTEND;VALUE=DATE:20261104
            SUMMARY:Evento de un día
            END:VEVENT
            END:VCALENDAR
            """;

        var imported = Assert.Single(IcsCalendarHelper.ParseIcs(ics));

        Assert.True(imported.IsAllDay);
        Assert.Equal(new DateTime(2026, 11, 3), imported.StartTime.Date);
        Assert.Equal(new DateTime(2026, 11, 3), imported.EndTime.Date);
    }

    [Fact]
    public void ParseIcs_UnknownTimeZone_IgnoresEvent()
    {
        const string ics = """
            BEGIN:VCALENDAR
            BEGIN:VEVENT
            UID:unknown-zone
            DTSTART;TZID=Invalid/Unknown_Zone:20261103T100000
            DTEND;TZID=Invalid/Unknown_Zone:20261103T110000
            SUMMARY:Evento con zona desconocida
            END:VEVENT
            END:VCALENDAR
            """;

        Assert.Empty(IcsCalendarHelper.ParseIcs(ics));
    }

    [Fact]
    public void ParseIcs_AllDayRangeWithExclusiveEndBeforeStart_IgnoresEvent()
    {
        const string ics = """
            BEGIN:VCALENDAR
            BEGIN:VEVENT
            UID:invalid-all-day
            DTSTART;VALUE=DATE:20261104
            DTEND;VALUE=DATE:20261104
            SUMMARY:Rango inválido
            END:VEVENT
            END:VCALENDAR
            """;

        Assert.Empty(IcsCalendarHelper.ParseIcs(ics));
    }

    [Fact]
    public void ToIcsAndParseIcs_RoundTripEscapedText()
    {
        var original = new CalendarEvent
        {
            Id = "escaped",
            Title = "Revisión, fase 5; cierre",
            Description = "Primera línea\nsegunda línea; detalle, adicional \\ final",
            StartTime = new DateTimeOffset(2026, 11, 3, 13, 0, 0, TimeSpan.Zero),
            EndTime = new DateTimeOffset(2026, 11, 3, 14, 0, 0, TimeSpan.Zero)
        };

        var ics = IcsCalendarHelper.ToIcs([original]);
        var imported = Assert.Single(IcsCalendarHelper.ParseIcs(ics));

        Assert.Equal(original.Title, imported.Title);
        Assert.Equal(original.Description, imported.Description);
        Assert.Equal(original.StartTime, imported.StartTime);
        Assert.Equal(original.EndTime, imported.EndTime);
    }

    [Fact]
    public void ToIcs_NonHttpMeetingUrl_IsNotExported()
    {
        var calendarEvent = new CalendarEvent
        {
            Id = "unsafe-url",
            Title = "Evento",
            MeetingUrl = "javascript:alert(1)",
            StartTime = new DateTimeOffset(2026, 11, 3, 13, 0, 0, TimeSpan.Zero),
            EndTime = new DateTimeOffset(2026, 11, 3, 14, 0, 0, TimeSpan.Zero)
        };

        var ics = IcsCalendarHelper.ToIcs([calendarEvent]);

        Assert.DoesNotContain("URL:", ics);
    }
}
