using System.Text.Json;
using AlexIsland.Core.Planner;

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

    [Fact]
    public void Has_five_days_and_one_shared_weekend_cell()
    {
        var cells = PlannerWeek.CellsOf(new DateOnly(2026, 11, 5));

        Assert.Equal(
            ["Lunes 2", "Martes 3", "Miércoles 4", "Jueves 5", "Viernes 6", "Sábado y domingo 7-8"],
            cells.Select(c => c.Label));
        Assert.True(cells[5].Contains(new DateOnly(2026, 11, 7)));
        Assert.True(cells[5].Contains(new DateOnly(2026, 11, 8)));
        Assert.False(cells[4].Contains(new DateOnly(2026, 11, 7)));
    }

    [Theory]
    [InlineData("2026-11-05", "2 al 8 de noviembre")]
    [InlineData("2026-12-01", "30 de noviembre al 6 de diciembre")]
    public void Describes_the_week(string date, string expected)
        => Assert.Equal(expected, PlannerWeek.Describe(DateOnly.Parse(date)));
}

public class PlannerBoardTests
{
    private const string Monday = "2026-11-02";

    [Fact]
    public void Adds_notes_to_a_day_and_cleans_the_text()
    {
        var board = new PlannerBoard();

        var note = board.AddNote(Monday, "  Llamar\n a  soporte ");

        Assert.Equal("Llamar a soporte", note!.Text);
        Assert.Single(board.NotesOf(Monday));
        Assert.Empty(board.NotesOf("2026-11-03"));
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
        board.AddNote(Monday, "Informe")!.Done = true;
        board.AddReminder("Renovar licencia");

        var loaded = JsonSerializer.Deserialize<PlannerBoard>(JsonSerializer.Serialize(board))!.Normalize();

        Assert.True(loaded.NotesOf(Monday)[0].Done);
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
