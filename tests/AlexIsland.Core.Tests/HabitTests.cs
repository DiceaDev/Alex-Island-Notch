using AlexIsland.Core.Habits;

namespace AlexIsland.Core.Tests;

public class HabitRulesTests
{
    private static readonly DateOnly Today = new(2026, 11, 4);

    [Theory]
    [InlineData("Leer", "Leer", 1)]
    [InlineData("  Agua x8 ", "Agua", 8)]
    [InlineData("Agua X 3", "Agua", 3)]
    [InlineData("Estirar ×2", "Estirar", 2)]
    [InlineData("Café x999", "Café", HabitRules.MaxTarget)]
    [InlineData("Mix", "Mix", 1)]
    [InlineData("   ", "", 1)]
    [InlineData(null, "", 1)]
    public void Parses_name_and_daily_target(string? input, string name, int target)
        => Assert.Equal((name, target), HabitRules.ParseInput(input));

    [Fact]
    public void Add_rejects_empty_duplicates_and_overflow()
    {
        var habits = new List<Habit>();
        Assert.True(HabitRules.Add(habits, "Leer"));
        Assert.False(HabitRules.Add(habits, "leer"));
        Assert.False(HabitRules.Add(habits, "  "));

        for (var i = 0; habits.Count < HabitRules.MaxHabits; i++) HabitRules.Add(habits, "H" + i);
        Assert.False(HabitRules.Add(habits, "Uno más"));
    }

    [Fact]
    public void Name_is_trimmed_and_stripped_of_control_characters()
    {
        var (name, _) = HabitRules.ParseInput("Le\u0001er " + new string('a', 100));
        Assert.True(name.Length <= HabitRules.MaxNameLength);
        Assert.DoesNotContain('\u0001', name);
    }

    [Fact]
    public void Tap_counts_up_and_wraps_to_zero()
    {
        var habit = new Habit { Name = "Agua", Target = 2 };
        Assert.Equal(1, HabitRules.Tap(habit, Today));
        Assert.False(HabitRules.IsDone(habit, Today));
        Assert.Equal(2, HabitRules.Tap(habit, Today));
        Assert.True(HabitRules.IsDone(habit, Today));
        Assert.Equal(0, HabitRules.Tap(habit, Today));
        Assert.Empty(habit.Log);
    }

    [Fact]
    public void Single_target_habit_toggles()
    {
        var habit = new Habit { Name = "Leer" };
        Assert.Equal(1, HabitRules.Tap(habit, Today));
        Assert.True(HabitRules.IsDone(habit, Today));
        Assert.Equal(0, HabitRules.Tap(habit, Today));
        Assert.False(HabitRules.IsDone(habit, Today));
    }

    [Fact]
    public void Streak_counts_consecutive_days_and_survives_an_undone_today()
    {
        var habit = new Habit { Name = "Leer" };
        for (var i = 1; i <= 3; i++) HabitRules.SetCount(habit, Today.AddDays(-i), 1);

        Assert.Equal(3, HabitRules.Streak(habit, Today)); // hoy aún no se cumple: no se rompe

        HabitRules.SetCount(habit, Today, 1);
        Assert.Equal(4, HabitRules.Streak(habit, Today));

        HabitRules.SetCount(habit, Today.AddDays(-2), 0); // un hueco corta la racha
        Assert.Equal(2, HabitRules.Streak(habit, Today));
    }

    [Fact]
    public void Streak_is_zero_after_a_missed_day()
    {
        var habit = new Habit { Name = "Leer" };
        HabitRules.SetCount(habit, Today.AddDays(-2), 1);
        Assert.Equal(0, HabitRules.Streak(habit, Today));
    }

    [Fact]
    public void Last_days_runs_oldest_to_today()
    {
        var habit = new Habit { Name = "Leer" };
        HabitRules.SetCount(habit, Today, 1);
        HabitRules.SetCount(habit, Today.AddDays(-6), 1);

        var week = HabitRules.LastDays(habit, Today);
        Assert.Equal(7, week.Length);
        Assert.True(week[0]);
        Assert.True(week[6]);
        Assert.False(week[3]);
    }

    [Fact]
    public void Sanitize_cleans_data_loaded_from_disk()
    {
        var loaded = new List<Habit?>
        {
            new() { Name = "Ok", Target = 500, Log = new() { ["2026-11-03"] = 99, ["basura"] = 1, ["2010-01-01"] = 1, ["2026-11-02"] = 0 } },
            new() { Name = "   " },
            null,
            new() { Name = "Sin id", Id = "" },
        };

        var result = HabitRules.Sanitize(loaded, Today);

        Assert.Equal(2, result.Count);
        Assert.Equal(HabitRules.MaxTarget, result[0].Target);
        var entry = Assert.Single(result[0].Log);
        Assert.Equal("2026-11-03", entry.Key);
        Assert.Equal(HabitRules.MaxTarget, entry.Value);
        Assert.False(string.IsNullOrEmpty(result[1].Id));
    }

    [Fact]
    public void Done_today_counts_completed_habits()
    {
        var a = new Habit { Name = "A" };
        var b = new Habit { Name = "B", Target = 2 };
        HabitRules.SetCount(a, Today, 1);
        HabitRules.SetCount(b, Today, 1);
        Assert.Equal(1, HabitRules.DoneToday([a, b], Today));
    }
}
