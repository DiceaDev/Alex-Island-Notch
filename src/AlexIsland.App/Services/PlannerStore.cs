using System.Text.Json;
using AlexIsland.Core.Planner;

namespace AlexIsland.App.Services;

/// <summary>
/// Guarda el planner en %LocalAppData%\AlexIsland\planner.json. Un archivo dañado
/// se respalda antes de empezar con uno vacío: nunca se pierden notas sin copia.
/// </summary>
internal sealed class PlannerStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public PlannerStore()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AlexIsland");
        Directory.CreateDirectory(directory);
        _filePath = Path.Combine(directory, "planner.json");
    }

    public async Task<PlannerBoard> LoadAsync()
    {
        await _lock.WaitAsync();
        try
        {
            if (!File.Exists(_filePath)) return new PlannerBoard();

            await using var stream = File.OpenRead(_filePath);
            var board = await JsonSerializer.DeserializeAsync<PlannerBoard>(stream, JsonOptions);
            return (board ?? new PlannerBoard()).Normalize();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            App.Log("Planner: no se pudo leer el archivo; se respalda y se empieza vacío.", ex);
            try
            {
                File.Copy(_filePath, _filePath + ".corrupt", overwrite: true);
            }
            catch
            {
                // Si tampoco se puede respaldar, se sigue con un planner vacío.
            }
            return new PlannerBoard();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(PlannerBoard board)
    {
        // Se serializa antes de esperar: así el guardado refleja este momento
        // aunque el usuario siga agregando notas mientras se escribe el archivo.
        var json = JsonSerializer.Serialize(board, JsonOptions);

        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            var temporaryPath = _filePath + ".tmp";
            await File.WriteAllTextAsync(temporaryPath, json).ConfigureAwait(false);
            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Guardado síncrono para el cierre de la app, cuando ya no se puede esperar.</summary>
    public void SaveNow(PlannerBoard board)
    {
        var json = JsonSerializer.Serialize(board, JsonOptions);

        if (!_lock.Wait(TimeSpan.FromSeconds(2))) throw new TimeoutException("Hay otro guardado en curso.");
        try
        {
            var temporaryPath = _filePath + ".tmp";
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        finally
        {
            _lock.Release();
        }
    }
}
