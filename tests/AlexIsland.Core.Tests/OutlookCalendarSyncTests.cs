using AlexIsland.Core.Calendar;
using AlexIsland.Core.Models;

namespace AlexIsland.Core.Tests;

public class OutlookCalendarSyncTests
{
    // Chile continental en noviembre: UTC-3.
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("Test-3", TimeSpan.FromHours(-3), "Test-3", "Test-3");
    private static readonly DateTimeOffset WindowStart = new(2026, 11, 4, 0, 0, 0, TimeSpan.FromHours(-3));
    private static readonly DateTimeOffset WindowEnd = WindowStart.AddDays(30);

    private static OutlookAppointment A(
        string globalId = "040000008200E00074C5B7101A82E008AAA",
        string subject = "Reunión Semanal",
        int hourUtc = 18,
        int day = 5,
        int status = 1,
        string? body = null,
        string? location = null,
        int? reminder = 15) =>
        new(globalId, subject,
            new DateTime(2026, 11, day, hourUtc, 30, 0, DateTimeKind.Utc),
            new DateTime(2026, 11, day, hourUtc + 1, 30, 0, DateTimeKind.Utc),
            false, location, body, status, reminder);

    [Fact]
    public void Converts_to_local_time_with_outlook_source()
    {
        var ev = OutlookCalendarSync.ToEvent(A(), Zone)!;

        Assert.Equal(new DateTimeOffset(2026, 11, 5, 15, 30, 0, TimeSpan.FromHours(-3)), ev.StartTime);
        Assert.Equal(TimeSpan.FromHours(-3), ev.StartTime.Offset);
        Assert.Equal("OUTLOOK", ev.Source);
        Assert.StartsWith(OutlookCalendarSync.IdPrefix, ev.Id);
        Assert.Equal(15, ev.ReminderMinutes);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(7)]
    public void Cancelled_meetings_are_skipped(int status)
        => Assert.Null(OutlookCalendarSync.ToEvent(A(status: status), Zone));

    [Fact]
    public void Extracts_teams_link_from_body_but_does_not_store_body()
    {
        var ev = OutlookCalendarSync.ToEvent(A(
            body: "Unirse a la reunión <https://teams.microsoft.com/l/meetup-join/19%3ameeting_abc%40thread.v2/0> Contraseña secreta",
            location: "Reunión de Microsoft Teams"), Zone)!;

        Assert.StartsWith("https://teams.microsoft.com/l/meetup-join/", ev.MeetingUrl);
        Assert.Null(ev.Description);
        Assert.Equal("Reunión de Microsoft Teams", ev.Location);
    }

    [Fact]
    public void Unsafe_links_are_ignored()
    {
        var ev = OutlookCalendarSync.ToEvent(A(body: "https://phishing.example.com/login"), Zone)!;

        Assert.Null(ev.MeetingUrl);
    }

    [Fact]
    public void Each_occurrence_of_a_recurring_meeting_gets_its_own_stable_id()
    {
        var monday = OutlookCalendarSync.ToEvent(A(day: 9), Zone)!;
        var tuesday = OutlookCalendarSync.ToEvent(A(day: 10), Zone)!;

        Assert.NotEqual(monday.Id, tuesday.Id);
        Assert.Equal(monday.Id, OutlookCalendarSync.ToEvent(A(day: 9), Zone)!.Id);
    }

    [Fact]
    public void First_sync_adds_meetings_and_keeps_local_events()
    {
        var local = new CalendarEvent { Title = "Mi tarea", Source = "LOCAL", StartTime = WindowStart.AddHours(9), EndTime = WindowStart.AddHours(10) };
        var events = new List<CalendarEvent> { local };

        var result = OutlookCalendarSync.Apply(events, [A(), A(globalId: "B", day: 6)], WindowStart, WindowEnd, Zone);

        Assert.Equal(new OutlookSyncResult(2, 0, 0), result);
        Assert.Contains(local, events);
        Assert.Equal(3, events.Count);
    }

    [Fact]
    public void Repeating_the_same_sync_changes_nothing()
    {
        var events = new List<CalendarEvent>();
        OutlookCalendarSync.Apply(events, [A()], WindowStart, WindowEnd, Zone);

        var result = OutlookCalendarSync.Apply(events, [A()], WindowStart, WindowEnd, Zone);

        Assert.False(result.HasChanges);
        Assert.Single(events);
    }

    [Fact]
    public void Deleted_or_cancelled_meetings_disappear_from_the_island()
    {
        var events = new List<CalendarEvent>();
        OutlookCalendarSync.Apply(events, [A(), A(globalId: "B", day: 6)], WindowStart, WindowEnd, Zone);

        var result = OutlookCalendarSync.Apply(events, [A(), A(globalId: "B", day: 6, status: 7)], WindowStart, WindowEnd, Zone);

        Assert.Equal(1, result.Removed);
        Assert.Single(events);
    }

    [Fact]
    public void Past_meetings_outside_the_window_are_kept_as_history()
    {
        var events = new List<CalendarEvent>();
        OutlookCalendarSync.Apply(events, [A(day: 2)], WindowStart.AddDays(-7), WindowEnd, Zone);

        OutlookCalendarSync.Apply(events, [], WindowStart, WindowEnd, Zone);

        Assert.Single(events);
    }

    [Fact]
    public void Renamed_meeting_is_updated_and_keeps_linked_task()
    {
        var events = new List<CalendarEvent>();
        OutlookCalendarSync.Apply(events, [A()], WindowStart, WindowEnd, Zone);
        events[0].LinkedTaskId = "t1";
        events[0].LinkedTaskTitle = "Preparar informe";

        var result = OutlookCalendarSync.Apply(events, [A(subject: "Reunión Semanal (nueva sala)")], WindowStart, WindowEnd, Zone);

        Assert.Equal(1, result.Updated);
        Assert.Equal("Reunión Semanal (nueva sala)", events[0].Title);
        Assert.Equal("t1", events[0].LinkedTaskId);
    }

    [Fact]
    public void Replaces_copies_imported_before_from_ics()
    {
        const string uid = "040000008200E00074C5B7101A82E008AAA";
        var byUid = new CalendarEvent { Id = uid, Title = "Otra cosa", Source = "ICAL" };
        var byTitle = new CalendarEvent
        {
            Title = "reunión semanal",
            Source = "ICAL",
            StartTime = new DateTimeOffset(2026, 11, 5, 15, 30, 0, TimeSpan.FromHours(-3)),
        };
        var localTwin = new CalendarEvent { Title = "Reunión Semanal", Source = "LOCAL", StartTime = byTitle.StartTime };
        var events = new List<CalendarEvent> { byUid, byTitle, localTwin };

        OutlookCalendarSync.Apply(events, [A(globalId: uid)], WindowStart, WindowEnd, Zone);

        Assert.DoesNotContain(byUid, events);
        Assert.DoesNotContain(byTitle, events);
        Assert.Contains(localTwin, events);
        Assert.Single(events, e => e.Source == "OUTLOOK");
    }

    [Fact]
    public void Missing_subject_gets_a_placeholder()
        => Assert.Equal("(Sin asunto)", OutlookCalendarSync.ToEvent(A(subject: "  "), Zone)!.Title);
}

public class OutlookResponseTests
{
    private static OutlookAppointment A(int meetingStatus, int responseStatus) =>
        new("G", "Revisión HUs",
            new DateTime(2026, 11, 6, 13, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 11, 6, 14, 0, 0, DateTimeKind.Utc),
            false, null, null, meetingStatus, 10, responseStatus);

    [Theory]
    [InlineData(3, 5, true)]   // invitación recibida, sin responder
    [InlineData(3, 3, false)]  // aceptada
    [InlineData(3, 2, false)]  // provisional
    [InlineData(1, 1, false)]  // reunión organizada por el usuario
    [InlineData(0, 0, false)]  // cita sin asistentes
    public void Marks_only_unanswered_invitations(int meetingStatus, int responseStatus, bool expected)
        => Assert.Equal(expected, OutlookCalendarSync.ToEvent(A(meetingStatus, responseStatus))!.NeedsResponse);

    [Fact]
    public void Answering_an_invitation_updates_the_event()
    {
        var start = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        var events = new List<AlexIsland.Core.Models.CalendarEvent>();
        OutlookCalendarSync.Apply(events, [A(3, 5)], start, start.AddDays(30));

        var result = OutlookCalendarSync.Apply(events, [A(3, 3)], start, start.AddDays(30));

        Assert.Equal(1, result.Updated);
        Assert.False(events[0].NeedsResponse);
    }
}
