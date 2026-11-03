using System.Text.Json;

namespace AlexIsland.App.Services;

internal static class JsonFilePersistence
{
    public static async Task<T> LoadAsync<T>(string path, Func<T> createDefault, JsonSerializerOptions options, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return createDefault();
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            return await JsonSerializer.DeserializeAsync<T>(stream, options, cancellationToken)
                ?? throw new JsonException("El JSON contiene un valor nulo.");
        }
        catch (JsonException ex)
        {
            var backup = $"{path}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmssfff}";
            File.Move(path, backup, overwrite: false);
            throw new InvalidDataException($"El archivo {Path.GetFileName(path)} estaba dañado. Se conservó una copia en {Path.GetFileName(backup)}.", ex);
        }
    }

    public static async Task SaveAsync(string path, ReadOnlyMemory<byte> json, CancellationToken cancellationToken)
    {
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await stream.WriteAsync(json, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            if (File.Exists(path)) File.Copy(path, path + ".bak", overwrite: true);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}