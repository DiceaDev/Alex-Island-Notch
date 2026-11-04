using System.Text.Json;
using System.Text.Json.Serialization;
using AlexIsland.Core.Shelf;

namespace AlexIsland.App.Services;

/// <summary>Guarda el shelf en %LocalAppData%\AlexIsland\shelf.json (solo rutas y textos).</summary>
internal sealed class ShelfStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public ShelfStore()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AlexIsland");
        Directory.CreateDirectory(directory);
        _filePath = Path.Combine(directory, "shelf.json");
    }

    public async Task<List<ShelfItem>> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_filePath)) return [];

            await using var stream = File.OpenRead(_filePath);
            var items = await JsonSerializer.DeserializeAsync<List<ShelfItem>>(stream, JsonOptions, cancellationToken);
            return ShelfRules.Sanitize(items);
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

    public async Task SaveAsync(IEnumerable<ShelfItem> items, CancellationToken cancellationToken = default)
    {
        var snapshot = items.ToList();
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
