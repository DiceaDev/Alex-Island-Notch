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
            if (!File.Exists(_filePath))
            {
                var settings = new PomodoroSettings();
                await SaveInternalAsync(settings, cancellationToken);
                return settings;
            }

            await using var stream = File.OpenRead(_filePath);
            var loaded = await JsonSerializer.DeserializeAsync<PomodoroSettings>(stream, JsonOptions, cancellationToken);
            var result = loaded ?? new PomodoroSettings();
            result.CheckDateReset();
            return result;
        }
        catch
        {
            return new PomodoroSettings();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(PomodoroSettings settings, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            await SaveInternalAsync(settings, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task SaveInternalAsync(PomodoroSettings settings, CancellationToken cancellationToken)
    {
        var tempFile = _filePath + ".tmp";
        await using (var stream = File.Create(tempFile))
        {
            await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken);
        }
        File.Move(tempFile, _filePath, overwrite: true);
    }
}

