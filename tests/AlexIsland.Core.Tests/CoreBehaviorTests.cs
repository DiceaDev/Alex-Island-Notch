using AlexIsland.Core.Models;
using AlexIsland.Core.Services;
using Xunit;

namespace AlexIsland.Core.Tests;

public sealed class CalendarEventTests
{
    [Fact]
    public void HasConflictWith_TimedEventsThatTouchAtBoundary_DoNotConflict()
    {
        var first = new CalendarEvent
        {
            Id = "first",
            StartTime = new DateTimeOffset(2026, 11, 3, 10, 0, 0, TimeSpan.Zero),
            EndTime = new DateTimeOffset(2026, 11, 3, 11, 0, 0, TimeSpan.Zero)
        };
        var second = new CalendarEvent
        {
            Id = "second",
            StartTime = first.EndTime,
            EndTime = first.EndTime.AddHours(1)
        };

        Assert.False(first.HasConflictWith(second));
        Assert.False(second.HasConflictWith(first));
    }

    [Fact]
    public void HasConflictWith_OverlappingTimedEvents_Conflicts()
    {
        var first = new CalendarEvent
        {
            Id = "first",
            StartTime = new DateTimeOffset(2026, 11, 3, 10, 0, 0, TimeSpan.Zero),
            EndTime = new DateTimeOffset(2026, 11, 3, 11, 0, 0, TimeSpan.Zero)
        };
        var second = new CalendarEvent
        {
            Id = "second",
            StartTime = new DateTimeOffset(2026, 11, 3, 10, 30, 0, TimeSpan.Zero),
            EndTime = new DateTimeOffset(2026, 11, 3, 11, 30, 0, TimeSpan.Zero)
        };

        Assert.True(first.HasConflictWith(second));
    }

    [Fact]
    public void HasConflictWith_AllDayMultiDayEvents_UsesInclusiveDates()
    {
        var first = new CalendarEvent
        {
            Id = "first",
            IsAllDay = true,
            StartTime = new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.Zero),
            EndTime = new DateTimeOffset(2026, 11, 5, 0, 0, 0, TimeSpan.Zero)
        };
        var second = new CalendarEvent
        {
            Id = "second",
            IsAllDay = true,
            StartTime = new DateTimeOffset(2026, 11, 5, 0, 0, 0, TimeSpan.Zero),
            EndTime = new DateTimeOffset(2026, 11, 6, 0, 0, 0, TimeSpan.Zero)
        };

        Assert.True(first.HasConflictWith(second));
    }

    [Fact]
    public void HasConflictWith_SameEventId_DoesNotConflict()
    {
        var first = new CalendarEvent { Id = "same" };
        var copy = new CalendarEvent { Id = "same" };

        Assert.False(first.HasConflictWith(copy));
    }
}

public sealed class IcsCalendarHelperTests
{
    [Fact]
    public void ParseIcs_AllDayEvent_ConvertsExclusiveDtEndToInclusiveEnd()
    {
        const string ics = """
            BEGIN:VCALENDAR
            BEGIN:VEVENT
            UID:all-day
            DTSTART;VALUE=DATE:20261103
            DTEND;VALUE=DATE:20261106
            SUMMARY:Trabajo de proyecto
            END:VEVENT
            END:VCALENDAR
            """;

        var events = IcsCalendarHelper.ParseIcs(ics);

        var imported = Assert.Single(events);
        Assert.True(imported.IsAllDay);
        Assert.Equal(new DateTime(2026, 11, 3), imported.StartTime.Date);
        Assert.Equal(new DateTime(2026, 11, 5), imported.EndTime.Date);
    }

    [Fact]
    public void ToIcs_AllDayEvent_ExportsExclusiveDtEnd()
    {
        var calendarEvent = new CalendarEvent
        {
            Id = "all-day",
            Title = "Día completo",
            IsAllDay = true,
            StartTime = new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.Zero),
            EndTime = new DateTimeOffset(2026, 11, 5, 0, 0, 0, TimeSpan.Zero)
        };

        var ics = IcsCalendarHelper.ToIcs([calendarEvent]);

        Assert.Contains("DTSTART;VALUE=DATE:20261103", ics);
        Assert.Contains("DTEND;VALUE=DATE:20261106", ics);
    }

    [Fact]
    public void ParseIcs_InvalidTimedRange_IsIgnored()
    {
        const string ics = """
            BEGIN:VCALENDAR
            BEGIN:VEVENT
            UID:invalid
            DTSTART:20261103T110000Z
            DTEND:20261103T100000Z
            SUMMARY:Intervalo inválido
            END:VEVENT
            END:VCALENDAR
            """;

        Assert.Empty(IcsCalendarHelper.ParseIcs(ics));
    }

    [Fact]
    public void ParseIcs_UtcEvent_PreservesInstant()
    {
        const string ics = """
            BEGIN:VCALENDAR
            BEGIN:VEVENT
            UID:utc
            DTSTART:20261103T130000Z
            DTEND:20261103T140000Z
            SUMMARY:Reunión UTC
            END:VEVENT
            END:VCALENDAR
            """;

        var imported = Assert.Single(IcsCalendarHelper.ParseIcs(ics));

        Assert.Equal(TimeSpan.Zero, imported.StartTime.Offset);
        Assert.Equal(new DateTimeOffset(2026, 11, 3, 13, 0, 0, TimeSpan.Zero), imported.StartTime);
    }

    [Fact]
    public void ParseIcs_FoldsLongLines_AndUnescapesText()
    {
        const string ics = "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nUID:folded\r\nDTSTART:20261103T130000Z\r\nDTEND:20261103T140000Z\r\nSUMMARY:Revisión\\, fase 5\r\nDESCRIPTION:Primera línea\\nsegunda línea\r\nEND:VEVENT\r\nEND:VCALENDAR";

        var imported = Assert.Single(IcsCalendarHelper.ParseIcs(ics));

        Assert.Equal("Revisión, fase 5", imported.Title);
        Assert.Equal("Primera línea\nsegunda línea", imported.Description);
    }
}

public sealed class DevTaskTests
{
    [Theory]
    [InlineData("FEAT: Agregar pruebas", "FEAT", "Agregar pruebas")]
    [InlineData("bug: Corregir error", "FIX", "Corregir error")]
    [InlineData("DOC: Documentar API", "DOCS", "Documentar API")]
    [InlineData("TEST: Cubrir calendario", "TEST", "Cubrir calendario")]
    public void ParseInput_RecognizedPrefix_ReturnsNormalizedCategory(
        string input, string expectedCategory, string expectedTitle)
    {
        var parsed = DevTask.ParseInput(input);

        Assert.Equal(expectedCategory, parsed.Category);
        Assert.Equal(expectedTitle, parsed.CleanTitle);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseInput_EmptyInput_ReturnsEmptyTodo(string input)
    {
        var parsed = DevTask.ParseInput(input);

        Assert.Equal("TODO", parsed.Category);
        Assert.Equal(string.Empty, parsed.CleanTitle);
    }

    [Fact]
    public void ParseInput_UnknownPrefix_KeepsInputAsTodo()
    {
        var parsed = DevTask.ParseInput("OPS: revisar logs");

        Assert.Equal("TODO", parsed.Category);
        Assert.Equal("OPS: revisar logs", parsed.CleanTitle);
    }
}
