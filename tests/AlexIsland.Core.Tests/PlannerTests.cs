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
    [InlineData("2026-11-02", 0)] // lunes: primera columna del mes
    [InlineData("2026-11-05", 3)]
    [InlineData("2026-11-01", 6)] // domingo: última columna
    public void Monday_is_the_first_day_of_the_week(string date, int expected)
        => Assert.Equal(expected, PlannerWeek.DaysFromMonday(DateOnly.Parse(date)));

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

    [Fact]
    public void Merged_notes_occupy_several_blocks()
    {
        var note = new PlannerNote { Hour = 10, Span = 2 };

        var blocks = PlannerHours.LayoutDay([note], 9, 13);

        Assert.Equal([(9, 1), (10, 2), (12, 1)], blocks.Select(b => (b.Hour, b.Span)));
        Assert.Same(note, blocks[1].Notes.Single());
    }

    [Fact]
    public void Merging_over_another_note_keeps_both()
    {
        var merged = new PlannerNote { Text = "Combinada", Hour = 10, Span = 3 };
        var inside = new PlannerNote { Text = "Dentro", Hour = 11 };
        var chained = new PlannerNote { Text = "Sigue", Hour = 12, Span = 2 };

        var block = PlannerHours.LayoutDay([merged, inside, chained], 9, 18).Single(b => b.Hour == 10);

        Assert.Equal(4, block.Span); // 10 a 14: la tercera lo alarga
        Assert.Equal(3, block.Notes.Count);
    }

    [Fact]
    public void Merged_block_stops_at_the_end_of_the_schedule()
    {
        var blocks = PlannerHours.LayoutDay([new PlannerNote { Hour = 17, Span = 5 }], 9, 18);

        Assert.Equal(1, blocks[^1].Span);
        Assert.Equal(17, blocks[^1].Hour);
        Assert.Equal(9, blocks.Sum(b => b.Span));
    }

    [Fact]
    public void Span_can_be_changed_and_never_goes_below_one()
    {
        var board = new PlannerBoard();
        var note = board.AddNote("2026-11-02", "Taller", hour: 10, span: 2)!;
        Assert.Equal(2, note.Span);

        Assert.True(board.SetSpan(note.Id, 3));
        Assert.Equal(3, note.Span);
        Assert.True(board.SetSpan(note.Id, 0));
        Assert.Equal(1, note.Span);
        Assert.False(board.SetSpan("no-existe", 2));
    }

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
    public void Default_schedule_is_half_past_eight_to_six()
    {
        var settings = new IslandSettings();

        Assert.Equal(8, settings.PlannerStartHour);
        Assert.Equal(30, settings.PlannerStartMinute);
        Assert.Equal(18, settings.PlannerEndHour);
    }

    [Theory]
    [InlineData(30, 30)]
    [InlineData(0, 0)]
    [InlineData(15, 0)] // solo horas en punto o medias horas
    public void Start_minute_is_on_the_hour_or_half_hour(int minute, int expected)
        => Assert.Equal(expected, new IslandSettings { PlannerStartMinute = minute }.Normalize().PlannerStartMinute);

    [Fact]
    public void First_row_can_start_at_half_past()
    {
        Assert.Equal("08:30", PlannerHours.LabelOf(8, 8, 30));
        Assert.Equal("09:00", PlannerHours.LabelOf(9, 8, 30));
        Assert.Equal("09:00", PlannerHours.LabelOf(9, 9, 0));
    }

    [Theory]
    [InlineData(8, 0, 8, 30, false)] // termina justo cuando empieza el horario
    [InlineData(8, 30, 9, 0, true)]
    [InlineData(8, 15, 8, 45, true)]
    [InlineData(8, 45, 9, 30, true)]
    [InlineData(9, 0, 10, 0, false)] // ya es del bloque siguiente
    public void Half_hour_block_only_covers_from_its_start(int sh, int sm, int eh, int em, bool expected)
        => Assert.Equal(expected, PlannerHours.Overlaps(At(sh, sm), At(eh, em), Thursday, 8, fromMinute: 30));

    [Fact]
    public void Merged_notes_work_from_the_half_hour_row()
    {
        var blocks = PlannerHours.LayoutDay([new PlannerNote { Hour = 8, Span = 2 }], 8, 18);

        Assert.Equal((8, 2), (blocks[0].Hour, blocks[0].Span));
        Assert.Equal(10, blocks.Sum(b => b.Span));
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
        var blocks = PlannerHours.LayoutDay(board.NotesOf(Monday), 9, 18);
        Assert.Single(blocks.Single(b => b.Hour == 11).Notes);
        Assert.Empty(blocks.Single(b => b.Hour == 12).Notes);
        Assert.Empty(board.NotesOf("2026-11-03"));
    }

    [Fact]
    public void Notes_saved_before_the_schedule_appear_in_the_first_block()
    {
        var old = JsonSerializer.Deserialize<PlannerBoard>(
            """{"days":{"2026-11-02":[{"id":"a","text":"Nota vieja","done":true}]},"reminders":[]}""")!.Normalize();

        var first = PlannerHours.LayoutDay(old.NotesOf(Monday), 9, 18)[0];
        Assert.Equal(1, first.Span);
        Assert.True(first.Notes.Single().Done);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Ignores_empty_notes(string? text)
    {
        var board = new PlannerBoard();

        Assert.Null(board.AddNote(Monday, text));
        Assert.Empty(board.Days);
    }

    [Fact]
    public void Toggles_notes()
    {
        var board = new PlannerBoard();
        var day = board.AddNote(Monday, "Informe")!;

        Assert.True(board.Toggle(day.Id));
        Assert.True(day.Done);

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
        board.SetNotepad("Renovar licencia\nLlamar a soporte");

        var loaded = JsonSerializer.Deserialize<PlannerBoard>(JsonSerializer.Serialize(board))!.Normalize();

        Assert.True(loaded.NotesOf(Monday)[0].Done);
        Assert.Equal(15, loaded.NotesOf(Monday)[0].Hour);
        Assert.Equal("Renovar licencia\nLlamar a soporte", loaded.Notepad);
    }

    [Fact]
    public void Normalize_repairs_a_damaged_file()
    {
        var loaded = JsonSerializer.Deserialize<PlannerBoard>("""{"days":{"2026-11-02":null},"reminders":null}""")!.Normalize();

        Assert.Empty(loaded.Days);
        Assert.Empty(loaded.Reminders);
        Assert.Equal(string.Empty, loaded.Notepad);
    }

    [Fact]
    public void Old_reminders_move_to_the_notepad_without_losing_anything()
    {
        var loaded = JsonSerializer.Deserialize<PlannerBoard>(
            """{"reminders":[{"id":"a","text":"nuevas aperturas para Alvi"},{"id":"b","text":"Pagar licencia","done":true}]}""")!.Normalize();

        Assert.Equal("nuevas aperturas para Alvi\n✓ Pagar licencia", loaded.Notepad);
        Assert.Empty(loaded.Reminders);

        loaded.Normalize(); // cargar de nuevo no las duplica
        Assert.Equal("nuevas aperturas para Alvi\n✓ Pagar licencia", loaded.Notepad);
    }

    [Fact]
    public void Old_reminders_are_added_after_the_existing_notepad_text()
    {
        var loaded = JsonSerializer.Deserialize<PlannerBoard>(
            """{"notepad":"Ya escrito","reminders":[{"id":"a","text":"Antigua"}]}""")!.Normalize();

        Assert.Equal("Ya escrito\nAntigua", loaded.Notepad);
    }

    [Fact]
    public void Notepad_keeps_long_text_and_line_breaks()
    {
        var board = new PlannerBoard();
        var longLine = new string('x', 5000);

        Assert.True(board.SetNotepad($"Uno\rDos\r\n\r\n  {longLine}"));
        Assert.Equal($"Uno\nDos\n\n  {longLine}", board.Notepad);
    }

    [Fact]
    public void Notepad_reports_when_nothing_changed()
    {
        var board = new PlannerBoard();
        board.SetNotepad("Uno\nDos");

        Assert.False(board.SetNotepad("Uno\rDos")); // el TextBox devuelve \r: es el mismo texto
        Assert.True(board.SetNotepad(null));
        Assert.Equal(string.Empty, board.Notepad);
    }
}
