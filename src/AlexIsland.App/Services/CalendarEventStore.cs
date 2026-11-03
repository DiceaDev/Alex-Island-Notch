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
            if (!File.Exists(_filePath))
            {
                var initial = GetInitialDevEvents();
                var tempFile = _filePath + ".tmp";
                await using (var stream = File.Create(tempFile))
                {
                    await JsonSerializer.SerializeAsync(stream, initial, JsonOptions, cancellationToken);
                }
                File.Move(tempFile, _filePath, overwrite: true);
                return initial;
            }

            await using var readStream = File.OpenRead(_filePath);
            var items = await JsonSerializer.DeserializeAsync<List<CalendarEvent>>(readStream, JsonOptions, cancellationToken);
            return items ?? GetInitialDevEvents();
        }
        catch
        {
            return GetInitialDevEvents();
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

    private static List<CalendarEvent> GetInitialDevEvents()
    {
        var today = DateTime.Today;
        return
        [
            new CalendarEvent
            {
                Title = "Daily Standup - Equipo Dev",
                Description = "Revisión rápida de backlog y bloqueos diarios.",
                StartTime = new DateTimeOffset(today.AddHours(10), DateTimeOffset.Now.Offset),
                EndTime = new DateTimeOffset(today.AddHours(10).AddMinutes(20), DateTimeOffset.Now.Offset),
                MeetingUrl = "https://meet.google.com/abc-defg-hij",
                Category = "MEETING",
                Source = "GOOGLE"
            },
            new CalendarEvent
            {
                Title = "Code Review: Módulo Notch Island",
                Description = "Revisión de pull request para optimización de renderizado.",
                StartTime = new DateTimeOffset(today.AddHours(15), DateTimeOffset.Now.Offset),
                EndTime = new DateTimeOffset(today.AddHours(16), DateTimeOffset.Now.Offset),
                MeetingUrl = "https://teams.microsoft.com/l/meetup-join/alexisland-review",
                Category = "REVIEW",
                Source = "OUTLOOK"
            },
            new CalendarEvent
            {
                Title = "Sprint Planning & Refinamiento",
                Description = "Estimación de backlog e historias de usuario de informática.",
                StartTime = new DateTimeOffset(today.AddDays(1).AddHours(11), DateTimeOffset.Now.Offset),
                EndTime = new DateTimeOffset(today.AddDays(1).AddHours(12), DateTimeOffset.Now.Offset),
                MeetingUrl = "https://meet.google.com/xyz-uvwx-rst",
                Category = "PLANNING",
                Source = "GOOGLE"
            }
        ];
    }
}

