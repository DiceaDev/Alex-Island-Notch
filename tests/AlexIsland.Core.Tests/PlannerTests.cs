using System.Text.Json;
using AlexIsland.Core.Planner;
using AlexIsland.Core.Settings;

namespace AlexIsland.Core.Tests;

public class PlannerWeekTests
{
    [Theory]
    [InlineData("2026-11-02", "2026-11-02")] // lunes
    [InlineData("2026-11-05", "2026-11-02")] // jueves
    [InlineData("2026-11-08", "2026-11-02")] // domingo: sigue siendo la misma semana
    [InlineData("2026-11-09", "2026-11-09")]
    public void Week_starts_on_monday(string date, string expected)
        => Assert.Equal(DateOnly.Parse(expected), PlannerWeek.StartOf(DateOnly.Parse(date)));

    [Theory]
    [InlineData("2026-11-05", "2026-11-02")] // jueves: esta semana
    [InlineData("2026-11-07", "2026-11-09")] // sábado: ya interesa la que viene
    [InlineData("2026-11-08", "2026-11-09")] // domingo
    public void Weekend_shows_the_next_work_week(string today, string expected)
        => Assert.Equal(DateOnly.Parse(expected), PlannerWeek.CurrentWorkWeek(DateOnly.Parse(today)));

    [Fact]
    public void Only_has_monday_to_friday()
    {
        var days = PlannerWeek.DaysOf(new DateOnly(2026, 11, 5));

        Assert.Equal(["Lun 2", "Mar 3", "Mié 4", "Jue 5", "Vie 6"], days.Select(d => d.Label));
        Assert.Equal("2026-11-06", days[^1].Key);
    }

    [Theory]
    [InlineData("2026-11-05", "2 al 6 de noviembre")]
    [InlineData("2026-12-01", "30 de noviembre al 4 de diciembre")]
    public void Describes_the_work_week(string date, string expected)
        => Assert.Equal(expected, PlannerWeek.Describe(DateOnly.Parse(date)));
}

public class PlannerHoursTests
{
    private static readonly DateOnly Thursday = new(2026, 11, 5);

    private static DateTime At(int hour, int minute = 0) => Thursday.ToDateTime(new TimeOnly(hour, minute));

    [Fact]
    public void Blocks_cover_the_working_hours()
        => Assert.Equal([9, 10, 11, 12, 13, 14, 15, 16, 17], PlannerHours.Blocks(9, 18));

    [Theory]
    [InlineData(null, 9)]  // nota antigua, sin hora: primer bloque
    [InlineData(11, 11)]
    [InlineData(7, 9)]     // antes del horario: primer bloque
    [InlineData(20, 17)]   // después del horario: último bloque
    public void Notes_are_never_hidden_by_the_schedule(int? hour, int expected)
        => Assert.Equal(expected, PlannerHours.BlockOf(new PlannerNote { Hour = hour }, 9, 18));

    [Theory]
    [InlineData(15, 30, 16, 30, 15, true)]  // empieza dentro del bloque
    [InlineData(15, 30, 16, 30, 16, true)]  // sigue en el bloque siguiente
    [InlineData(15, 30, 16, 30, 14, false)]
    [InlineData(15, 0, 16, 0, 16, false)]   // termina justo cuando empieza el bloque
    [InlineData(10, 0, 10, 0, 10, true)]    // sin duración: en su bloque
    [InlineData(10, 0, 10, 0, 11, false)]
    public void Meetings_fill_the_blocks_they_touch(int sh, int sm, int eh, int em, int block, bool expected)
        => Assert.Equal(expected, PlannerHours.Overlaps(At(sh, sm), At(eh, em), Thursday, block));

    [Fact]
    public void A_meeting_on_another_day_does_not_count()
        => Assert.False(PlannerHours.Overlaps(At(10), At(11), Thursday.AddDays(1), 10));

    [Theory]
    [InlineData(9, 18, 9, 18)]
    [InlineData(18, 9, 18, 19)]  // al revés: una hora como mínimo
    [InlineData(1, 40, 6, 22)]   // fuera de rango
    public void Settings_keep_a_valid_schedule(int start, int end, int expectedStart, int expectedEnd)
    {
        var settings = new IslandSettings { PlannerStartHour = start, PlannerEndHour = end }.Normalize();

        Assert.Equal(expectedStart, settings.PlannerStartHour);
        Assert.Equal(expectedEnd, settings.PlannerEndHour);
    }

    [Fact]
    public void Default_schedule_is_nine_to_six()
    {
        var settings = new IslandSettings();

        Assert.Equal(9, settings.PlannerStartHour);
        Assert.Equal(18, settings.PlannerEndHour);
    }
}

public class PlannerBoardTests
{
    private const string Monday = "2026-11-02";

    [Fact]
    public void Adds_notes_to_an_hour_and_cleans_the_text()
    {
        var board = new PlannerBoard();

        var note = board.AddNote(Monday, "  Llamar\n a  soporte ", hour: 11);

        Assert.Equal("Llamar a soporte", note!.Text);
        Assert.Single(board.NotesAt(Monday, 11, 9, 18));
        Assert.Empty(board.NotesAt(Monday, 12, 9, 18));
        Assert.Empty(board.NotesOf("2026-11-03"));
    }

    [Fact]
    public void Notes_saved_before_the_schedule_appear_in_the_first_block()
    {
        var old = JsonSerializer.Deserialize<PlannerBoard>(
            """{"days":{"2026-11-02":[{"id":"a","text":"Nota vieja","done":true}]},"reminders":[]}""")!.Normalize();

        Assert.Single(old.NotesAt(Monday, 9, 9, 18));
        Assert.True(old.NotesAt(Monday, 9, 9, 18)[0].Done);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Ignores_empty_notes(string? text)
    {
        var board = new PlannerBoard();

        Assert.Null(board.AddNote(Monday, text));
        Assert.Null(board.AddReminder(text));
        Assert.Empty(board.Days);
        Assert.Empty(board.Reminders);
    }

    [Fact]
    public void Toggles_notes_in_days_and_reminders()
    {
        var board = new PlannerBoard();
        var day = board.AddNote(Monday, "Informe")!;
        var reminder = board.AddReminder("Renovar licencia")!;

        Assert.True(board.Toggle(day.Id));
        Assert.True(board.Toggle(reminder.Id));
        Assert.True(day.Done);
        Assert.True(reminder.Done);

        board.Toggle(day.Id);
        Assert.False(day.Done);
        Assert.False(board.Toggle("no-existe"));
    }

    [Fact]
    public void Removing_the_last_note_drops_the_empty_day()
    {
        var board = new PlannerBoard();
        var first = board.AddNote(Monday, "Uno")!;
        var second = board.AddNote(Monday, "Dos")!;

        Assert.True(board.Remove(first.Id));
        Assert.Single(board.NotesOf(Monday));

        Assert.True(board.Remove(second.Id));
        Assert.False(board.Days.ContainsKey(Monday));
        Assert.False(board.Remove("no-existe"));
    }

    [Fact]
    public void Survives_saving_and_loading()
    {
        var board = new PlannerBoard();
        board.AddNote(Monday, "Informe", hour: 15)!.Done = true;
        board.AddReminder("Renovar licencia");

        var loaded = JsonSerializer.Deserialize<PlannerBoard>(JsonSerializer.Serialize(board))!.Normalize();

        Assert.True(loaded.NotesOf(Monday)[0].Done);
        Assert.Equal(15, loaded.NotesOf(Monday)[0].Hour);
        Assert.Equal("Renovar licencia", loaded.Reminders[0].Text);
    }

    [Fact]
    public void Normalize_repairs_a_damaged_file()
    {
        var loaded = JsonSerializer.Deserialize<PlannerBoard>("""{"days":{"2026-11-02":null},"reminders":null}""")!.Normalize();

        Assert.Empty(loaded.Days);
        Assert.Empty(loaded.Reminders);
    }
}
