using System.Text.Json;
using AlexIsland.Core.Settings;

namespace AlexIsland.App.Services;

/// <summary>
/// Guarda los ajustes en %LocalAppData%\AlexIsland\settings.json.
/// Un archivo dañado nunca impide arrancar: se respalda y se usan valores por defecto.
/// </summary>
internal sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _settingsPath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public SettingsStore()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AlexIsland");
        Directory.CreateDirectory(directory);
        _settingsPath = Path.Combine(directory, "settings.json");
    }

    public async Task<IslandSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_settingsPath)) return new IslandSettings();

            await using var stream = File.OpenRead(_settingsPath);
            var loaded = await JsonSerializer.DeserializeAsync<IslandSettings>(stream, JsonOptions, cancellationToken);
            return (loaded ?? new IslandSettings()).Normalize();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            TryBackupCorruptFile();
            return new IslandSettings();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(IslandSettings settings, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var temporaryPath = _settingsPath + ".tmp";
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, settings.Clone().Normalize(), JsonOptions, cancellationToken);
            }
            File.Move(temporaryPath, _settingsPath, overwrite: true);
        }
        finally
        {
            _lock.Release();
        }
    }

    private void TryBackupCorruptFile()
    {
        try
        {
            File.Copy(_settingsPath, _settingsPath + ".corrupt", overwrite: true);
        }
        catch
        {
            // Si ni siquiera se puede respaldar, se sigue con los valores por defecto.
        }
    }
}
