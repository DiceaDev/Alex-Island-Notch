using System.Globalization;
using System.Text.RegularExpressions;

namespace AlexIsland.Core.Habits;

/// <summary>Reglas del seguimiento de hábitos: límites, rachas y semana. No toca disco ni UI.</summary>
public static partial class HabitRules
{
    public const int MaxHabits = 12;
    public const int MaxNameLength = 40;
    public const int MaxTarget = 20;

    /// <summary>Se conservan solo los últimos días del registro para que el archivo no crezca sin fin.</summary>
    public const int KeepDays = 400;

    public static string DayKey(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>
    /// Interpreta lo escrito por la persona: "Leer" → meta 1; "Agua x8" → meta 8.
    /// Devuelve nombre vacío si no hay nada utilizable.
    /// </summary>
    public static (string Name, int Target) ParseInput(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return (string.Empty, 1);

        var text = input.Trim();
        var target = 1;

        var match = TargetSuffix().Match(text);
        if (match.Success)
        {
            if (int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
            {
                target = Math.Clamp(n, 1, MaxTarget);
            }
            text = text[..match.Index].Trim();
        }

        text = CleanName(text);
        return (text, target);
    }

    [GeneratedRegex(@"\s+[xX×]\s*(\d{1,3})$", RegexOptions.None, matchTimeoutMilliseconds: 100)]
    private static partial Regex TargetSuffix();

    private static string CleanName(string name)
    {
        var cleaned = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return cleaned.Length > MaxNameLength ? cleaned[..MaxNameLength].TrimEnd() : cleaned;
    }

    /// <summary>Agrega un hábito nuevo. Falso si el nombre está vacío, repetido o se llegó al máximo.</summary>
    public static bool Add(List<Habit> habits, string? input)
    {
        var (name, target) = ParseInput(input);
        if (name.Length == 0) return false;
        if (habits.Count >= MaxHabits) return false;
        if (habits.Any(h => h.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) return false;

        habits.Add(new Habit { Name = name, Target = target });
        return true;
    }

    public static int CountOn(Habit habit, DateOnly day)
        => habit.Log.TryGetValue(DayKey(day), out var count) ? count : 0;

    public static bool IsDone(Habit habit, DateOnly day) => CountOn(habit, day) >= Math.Max(1, habit.Target);

    /// <summary>
    /// Un toque: suma una vez; al pasar la meta vuelve a cero (así se corrige un error sin otro botón).
    /// Devuelve el nuevo conteo.
    /// </summary>
    public static int Tap(Habit habit, DateOnly day)
    {
        var target = Math.Max(1, habit.Target);
        var next = CountOn(habit, day) + 1;
        if (next > target) next = 0;

        SetCount(habit, day, next);
        return next;
    }

    public static void SetCount(Habit habit, DateOnly day, int count)
    {
        var key = DayKey(day);
        if (count <= 0) habit.Log.Remove(key);
        else habit.Log[key] = Math.Min(count, MaxTarget);
    }

    /// <summary>
    /// Días seguidos cumplidos. Si hoy todavía no se cumple, la racha no se rompe: cuenta desde ayer.
    /// </summary>
    public static int Streak(Habit habit, DateOnly today)
    {
        var day = IsDone(habit, today) ? today : today.AddDays(-1);
        var streak = 0;
        while (streak < KeepDays && IsDone(habit, day))
        {
            streak++;
            day = day.AddDays(-1);
        }
        return streak;
    }

    /// <summary>Cumplimiento de los últimos <paramref name="days"/> días, del más antiguo al de hoy.</summary>
    public static bool[] LastDays(Habit habit, DateOnly today, int days = 7)
        => Enumerable.Range(0, days).Select(i => IsDone(habit, today.AddDays(i - (days - 1)))).ToArray();

    public static int DoneToday(IEnumerable<Habit> habits, DateOnly today) => habits.Count(h => IsDone(h, today));

    /// <summary>Limpia lo leído de disco: descarta entradas inválidas, normaliza metas y recorta el historial viejo.</summary>
    public static List<Habit> Sanitize(IEnumerable<Habit?>? loaded, DateOnly today)
    {
        var result = new List<Habit>();
        if (loaded is null) return result;

        var oldest = today.AddDays(-KeepDays);
        foreach (var habit in loaded)
        {
            if (habit is null) continue;
            var name = CleanName(habit.Name ?? string.Empty);
            if (name.Length == 0) continue;
            if (result.Count >= MaxHabits) break;

            var clean = new Habit
            {
                Id = string.IsNullOrWhiteSpace(habit.Id) ? Guid.NewGuid().ToString("N") : habit.Id,
                Name = name,
                Target = Math.Clamp(habit.Target, 1, MaxTarget),
            };

            foreach (var (key, count) in habit.Log ?? [])
            {
                if (count <= 0) continue;
                if (!DateOnly.TryParseExact(key, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)) continue;
                if (day < oldest || day > today.AddDays(1)) continue;
                clean.Log[DayKey(day)] = Math.Min(count, MaxTarget);
            }

            result.Add(clean);
        }

        return result;
    }
}
