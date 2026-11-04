using System.Text.Json;
using AlexIsland.Core.Habits;

namespace AlexIsland.App.Services;

/// <summary>Guarda los hábitos en %LocalAppData%\AlexIsland\habits.json (solo nombres y conteos por día).</summary>
internal sealed class HabitStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public HabitStore()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AlexIsland");
        Directory.CreateDirectory(directory);
        _filePath = Path.Combine(directory, "habits.json");
    }

    public async Task<List<Habit>> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_filePath)) return [];

            await using var stream = File.OpenRead(_filePath);
            var habits = await JsonSerializer.DeserializeAsync<List<Habit>>(stream, JsonOptions, cancellationToken);
            return HabitRules.Sanitize(habits, DateOnly.FromDateTime(DateTime.Today));
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

    public async Task SaveAsync(IEnumerable<Habit> habits, CancellationToken cancellationToken = default)
    {
        var snapshot = habits.ToList();
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var tempFile = _filePath + ".tmp";
            await using (var stream = File.Create(tempFile))
            {
                await JsonSerializer.SerializeAsync(stream, snapshot, JsonOptions, cancellationToken);
            }
            File.Move(tempFile, _filePath, overwrite: true);
        }
        finally
        {
            _lock.Release();
        }
    }
}
