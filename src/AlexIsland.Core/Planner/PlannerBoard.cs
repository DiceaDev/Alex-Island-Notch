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
}

/// <summary>Un casillero de la semana. Sábado y domingo comparten uno, como en un planner de papel.</summary>
public sealed record PlannerCell(string Key, string Label, DateOnly First, DateOnly Last)
{
    public bool Contains(DateOnly date) => date >= First && date <= Last;
}

/// <summary>Reparte las fechas en los seis casilleros de una semana que empieza el lunes.</summary>
public static class PlannerWeek
{
    private static readonly string[] DayNames = ["Lunes", "Martes", "Miércoles", "Jueves", "Viernes"];

    public static DateOnly StartOf(DateOnly date)
    {
        var daysFromMonday = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-daysFromMonday);
    }

    public static string KeyOf(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static IReadOnlyList<PlannerCell> CellsOf(DateOnly anyDayOfWeek)
    {
        var monday = StartOf(anyDayOfWeek);
        var cells = new List<PlannerCell>(6);

        for (var i = 0; i < DayNames.Length; i++)
        {
            var day = monday.AddDays(i);
            cells.Add(new PlannerCell(KeyOf(day), $"{DayNames[i]} {day.Day}", day, day));
        }

        var saturday = monday.AddDays(5);
        var sunday = monday.AddDays(6);
        cells.Add(new PlannerCell(KeyOf(saturday), $"Sáb y dom {saturday.Day}-{sunday.Day}", saturday, sunday));
        return cells;
    }

    /// <summary>"2 al 8 de noviembre" o, si la semana cruza de mes, "30 de noviembre al 6 de diciembre".</summary>
    public static string Describe(DateOnly anyDayOfWeek)
    {
        var culture = CultureInfo.GetCultureInfo("es-CL");
        var monday = StartOf(anyDayOfWeek);
        var sunday = monday.AddDays(6);
        return monday.Month == sunday.Month
            ? $"{monday.Day} al {sunday.Day} de {sunday.ToString("MMMM", culture)}"
            : $"{monday.Day} de {monday.ToString("MMMM", culture)} al {sunday.Day} de {sunday.ToString("MMMM", culture)}";
    }
}

/// <summary>Notas por casillero de la semana y la lista "Recuerda", que no depende de la fecha.</summary>
public sealed class PlannerBoard
{
    [JsonPropertyName("days")]
    public Dictionary<string, List<PlannerNote>> Days { get; set; } = [];

    [JsonPropertyName("reminders")]
    public List<PlannerNote> Reminders { get; set; } = [];

    public IReadOnlyList<PlannerNote> NotesOf(string key) =>
        Days.TryGetValue(key, out var notes) ? notes : [];

    /// <summary>Agrega una nota al casillero; devuelve null si el texto está vacío.</summary>
    public PlannerNote? AddNote(string key, string? text)
    {
        var note = Create(text);
        if (note is null) return null;

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
