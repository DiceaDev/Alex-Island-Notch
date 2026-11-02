using System.Text.Json;
using AlexIsland.Core.Settings;
namespace AlexIsland.App.Services;
internal sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _settingsPath;
    public SettingsStore()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AlexIsland");
        Directory.CreateDirectory(directory);
        _settingsPath = Path.Combine(directory, "settings.json");
    }
    public async Task<IslandSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_settingsPath)) return new IslandSettings();
        await using var stream = File.OpenRead(_settingsPath);
        return await JsonSerializer.DeserializeAsync<IslandSettings>(stream, JsonOptions, cancellationToken)
            ?? new IslandSettings();
    }
    public async Task SaveAsync(IslandSettings settings, CancellationToken cancellationToken = default)
    {
        var temporaryPath = _settingsPath + ".tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken);
        }
        File.Move(temporaryPath, _settingsPath, overwrite: true);
    }
}
