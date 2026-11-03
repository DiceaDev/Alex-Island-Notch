using System.Text.Json;
using AlexIsland.Core.Models;
using AlexIsland.Core.Services;

namespace AlexIsland.App.Services;

public sealed class CalendarEventStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public CalendarEventStore()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AlexIsland");
        Directory.CreateDirectory(directory);
        _filePath = Path.Combine(directory, "calendar_events.json");
    }

    public async Task<List<CalendarEvent>> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try { return await JsonFilePersistence.LoadAsync(_filePath, static () => new List<CalendarEvent>(), JsonOptions, cancellationToken); }
        finally { _lock.Release(); }
    }

    public async Task SaveAsync(IEnumerable<CalendarEvent> events, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(events);
        var snapshot = events.ToList();
        var json = JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonOptions);
        await _lock.WaitAsync(cancellationToken);
        try { await JsonFilePersistence.SaveAsync(_filePath, json, cancellationToken); }
        finally { _lock.Release(); }
    }

    public static List<CalendarEvent> FindConflicts(CalendarEvent target, IEnumerable<CalendarEvent> allEvents)
        => allEvents.Where(e => target.HasConflictWith(e)).ToList();

    public async Task<int> ImportIcsAsync(string icsText, List<CalendarEvent> existingList)
    {
        var imported = IcsCalendarHelper.ParseIcs(icsText);
        var snapshot = existingList.ToList();
        var addedCount = 0;
        foreach (var ev in imported)
        {
            var exists = snapshot.Any(e =>
                (!string.IsNullOrWhiteSpace(e.Id) && !string.IsNullOrWhiteSpace(ev.Id) && e.Id == ev.Id) ||
                (e.Title.Equals(ev.Title, StringComparison.OrdinalIgnoreCase) && e.StartTime == ev.StartTime));
            if (exists) continue;
            existingList.Add(ev);
            snapshot.Add(ev);
            addedCount++;
        }
        if (addedCount > 0) await SaveAsync(snapshot);
        return addedCount;
    }
}