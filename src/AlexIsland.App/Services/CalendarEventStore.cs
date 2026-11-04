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
        try
        {
            // Agenda vacía al empezar: las reuniones llegan desde Outlook, un .ics o "+ Evento".
            if (!File.Exists(_filePath)) return [];

            await using var readStream = File.OpenRead(_filePath);
            var items = await JsonSerializer.DeserializeAsync<List<CalendarEvent>>(readStream, JsonOptions, cancellationToken);
            return items ?? [];
        }
        catch
        {
            return [];
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(IEnumerable<CalendarEvent> events, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var tempFile = _filePath + ".tmp";
            await using (var stream = File.Create(tempFile))
            {
                await JsonSerializer.SerializeAsync(stream, events, JsonOptions, cancellationToken);
            }
            File.Move(tempFile, _filePath, overwrite: true);
        }
        finally
        {
            _lock.Release();
        }
    }

    public static List<CalendarEvent> FindConflicts(CalendarEvent target, IEnumerable<CalendarEvent> allEvents)
    {
        return allEvents.Where(e => target.HasConflictWith(e)).ToList();
    }

    public async Task<int> ImportIcsAsync(string icsText, List<CalendarEvent> existingList)
    {
        var imported = IcsCalendarHelper.ParseIcs(icsText);
        var addedCount = 0;

        foreach (var ev in imported)
        {
            var exists = existingList.Any(e =>
                (!string.IsNullOrWhiteSpace(e.Id) && !string.IsNullOrWhiteSpace(ev.Id) && e.Id == ev.Id) ||
                (e.Title.Equals(ev.Title, StringComparison.OrdinalIgnoreCase) && e.StartTime == ev.StartTime));

            if (!exists)
            {
                existingList.Add(ev);
                addedCount++;
            }
        }

        if (addedCount > 0)
        {
            await SaveAsync(existingList);
        }

        return addedCount;
    }
}
