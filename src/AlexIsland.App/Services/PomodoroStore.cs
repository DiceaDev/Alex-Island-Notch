using System.Text.Json;
using AlexIsland.Core.Models;

namespace AlexIsland.App.Services;

public sealed class PomodoroStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public PomodoroStore()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AlexIsland");
        Directory.CreateDirectory(directory);
        _filePath = Path.Combine(directory, "pomodoro_settings.json");
    }

    public async Task<PomodoroSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var settings = await JsonFilePersistence.LoadAsync(_filePath, static () => new PomodoroSettings(), JsonOptions, cancellationToken);
            Normalize(settings);
            settings.CheckDateReset();
            return settings;
        }
        finally { _lock.Release(); }
    }

    public async Task SaveAsync(PomodoroSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var snapshot = JsonSerializer.Deserialize<PomodoroSettings>(JsonSerializer.Serialize(settings, JsonOptions), JsonOptions)
            ?? throw new JsonException("No fue posible crear una copia de la configuración Pomodoro.");
        Normalize(snapshot);
        var json = JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonOptions);
        await _lock.WaitAsync(cancellationToken);
        try { await JsonFilePersistence.SaveAsync(_filePath, json, cancellationToken); }
        finally { _lock.Release(); }
    }

    private static void Normalize(PomodoroSettings settings)
    {
        settings.WorkMinutes = Math.Clamp(settings.WorkMinutes, 1, 180);
        settings.ShortBreakMinutes = Math.Clamp(settings.ShortBreakMinutes, 1, 60);
        settings.LongBreakMinutes = Math.Clamp(settings.LongBreakMinutes, 1, 120);
        settings.LongBreakInterval = Math.Clamp(settings.LongBreakInterval, 1, 12);
        settings.CompletedToday = Math.Max(0, settings.CompletedToday);
    }
}