using System.Globalization;
using System.Text.Json.Serialization;
using AlexIsland.Core.Notifications;

namespace AlexIsland.Core.Planner;

/// <summary>Una nota libre del planner.</summary>
public sealed class PlannerNote
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("done")]
    public bool Done { get; set; }

    /// <summary>Hora del bloque (9 = de 09:00 a 10:00). Las notas sin hora van al primer bloque del día.</summary>
    [JsonPropertyName("hour")]
    public int? Hour { get; set; }
}

/// <summary>Un día laboral de la semana.</summary>
public sealed record PlannerDay(string Key, string Label, DateOnly Date);

/// <summary>Reparte las fechas en los días laborales de una semana (lunes a viernes).</summary>
public static class PlannerWeek
{
    private static readonly string[] DayNames = ["Lun", "Mar", "Mié", "Jue", "Vie"];

    public static DateOnly StartOf(DateOnly date)
    {
        var daysFromMonday = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-daysFromMonday);
    }

    /// <summary>Semana laboral que corresponde mostrar: en fin de semana ya interesa la que viene.</summary>
    public static DateOnly CurrentWorkWeek(DateOnly today) =>
        today.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday
            ? StartOf(today).AddDays(7)
            : StartOf(today);

    public static string KeyOf(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static IReadOnlyList<PlannerDay> DaysOf(DateOnly anyDayOfWeek)
    {
        var monday = StartOf(anyDayOfWeek);
        return DayNames
            .Select((name, i) =>
            {
                var day = monday.AddDays(i);
                return new PlannerDay(KeyOf(day), $"{name} {day.Day}", day);
            })
            .ToList();
    }

    /// <summary>"2 al 6 de noviembre" o, si la semana cruza de mes, "30 de noviembre al 4 de diciembre".</summary>
    public static string Describe(DateOnly anyDayOfWeek)
    {
        var culture = CultureInfo.GetCultureInfo("es-CL");
        var monday = StartOf(anyDayOfWeek);
        var friday = monday.AddDays(4);
        return monday.Month == friday.Month
            ? $"{monday.Day} al {friday.Day} de {friday.ToString("MMMM", culture)}"
            : $"{monday.Day} de {monday.ToString("MMMM", culture)} al {friday.Day} de {friday.ToString("MMMM", culture)}";
    }
}

/// <summary>Bloques de una hora dentro del horario laboral.</summary>
public static class PlannerHours
{
    /// <summary>Horas de inicio de cada bloque: de 9 a 18 son 9, 10, ... 17.</summary>
    public static IReadOnlyList<int> Blocks(int startHour, int endHour) =>
        Enumerable.Range(startHour, Math.Max(0, endHour - startHour)).ToList();

    /// <summary>
    /// Bloque donde se ve una nota: sin hora va al primero, y si quedó fuera
    /// del horario (porque se cambió en Ajustes) va al más cercano. Nunca se oculta.
    /// </summary>
    public static int BlockOf(PlannerNote note, int startHour, int endHour)
    {
        ArgumentNullException.ThrowIfNull(note);
        return Math.Clamp(note.Hour ?? startHour, startHour, Math.Max(startHour, endHour - 1));
    }

    /// <summary>Indica si una reunión ocupa parte del bloque de esa hora de ese día.</summary>
    public static bool Overlaps(DateTime meetingStart, DateTime meetingEnd, DateOnly date, int hour)
    {
        var blockStart = date.ToDateTime(new TimeOnly(hour, 0));
        var blockEnd = blockStart.AddHours(1);
        // Una reunión sin duración se muestra en el bloque donde empieza.
        return meetingEnd <= meetingStart
            ? meetingStart >= blockStart && meetingStart < blockEnd
            : meetingStart < blockEnd && meetingEnd > blockStart;
    }
}

/// <summary>Notas por día y la lista "Recuerda", que no depende de la fecha.</summary>
public sealed class PlannerBoard
{
    [JsonPropertyName("days")]
    public Dictionary<string, List<PlannerNote>> Days { get; set; } = [];

    [JsonPropertyName("reminders")]
    public List<PlannerNote> Reminders { get; set; } = [];

    public IReadOnlyList<PlannerNote> NotesOf(string key) =>
        Days.TryGetValue(key, out var notes) ? notes : [];

    /// <summary>Notas que se ven en un bloque del horario.</summary>
    public IReadOnlyList<PlannerNote> NotesAt(string key, int hour, int startHour, int endHour) =>
        NotesOf(key).Where(n => PlannerHours.BlockOf(n, startHour, endHour) == hour).ToList();

    /// <summary>Agrega una nota al día, en el bloque indicado; devuelve null si el texto está vacío.</summary>
    public PlannerNote? AddNote(string key, string? text, int? hour = null)
    {
        var note = Create(text);
        if (note is null) return null;
        note.Hour = hour;

        if (!Days.TryGetValue(key, out var notes))
        {
            notes = [];
            Days[key] = notes;
        }
        notes.Add(note);
        return note;
    }

    /// <summary>Agrega una nota a "Recuerda"; devuelve null si el texto está vacío.</summary>
    public PlannerNote? AddReminder(string? text)
    {
        var note = Create(text);
        if (note is not null) Reminders.Add(note);
        return note;
    }

    /// <summary>Marca o desmarca una nota, esté en un día o en "Recuerda".</summary>
    public bool Toggle(string id)
    {
        var note = AllNotes().FirstOrDefault(n => n.Id == id);
        if (note is null) return false;
        note.Done = !note.Done;
        return true;
    }

    /// <summary>Borra una nota; los días que quedan vacíos se quitan para no acumular datos.</summary>
    public bool Remove(string id)
    {
        if (Reminders.RemoveAll(n => n.Id == id) > 0) return true;

        foreach (var (key, notes) in Days)
        {
            if (notes.RemoveAll(n => n.Id == id) == 0) continue;
            if (notes.Count == 0) Days.Remove(key);
            return true;
        }
        return false;
    }

    /// <summary>Corrige lo que pudo venir vacío en un archivo editado a mano o dañado.</summary>
    public PlannerBoard Normalize()
    {
        Days ??= [];
        Reminders ??= [];
        foreach (var key in Days.Keys.ToList())
        {
            if (Days[key] is null) Days.Remove(key);
        }
        return this;
    }

    private IEnumerable<PlannerNote> AllNotes() => Reminders.Concat(Days.Values.SelectMany(n => n));

    private static PlannerNote? Create(string? text)
    {
        var clean = NotificationPresenter.Clean(text);
        return clean.Length == 0 ? null : new PlannerNote { Text = clean };
    }
}
