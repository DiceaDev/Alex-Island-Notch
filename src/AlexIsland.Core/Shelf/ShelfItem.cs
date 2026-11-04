using System.Text.Json.Serialization;

namespace AlexIsland.Core.Shelf;

public enum ShelfItemKind { File, Folder, Text }

/// <summary>
/// Elemento del shelf. Para archivos y carpetas se guarda solo la ruta (nunca
/// una copia); para textos, el texto mismo.
/// </summary>
public sealed class ShelfItem
{
    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [JsonPropertyName("kind")] public ShelfItemKind Kind { get; set; }
    [JsonPropertyName("path")] public string? Path { get; set; }
    [JsonPropertyName("text")] public string? Text { get; set; }
    [JsonPropertyName("added_at")] public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonIgnore]
    public string DisplayName => Kind == ShelfItemKind.Text
        ? ShelfRules.Preview(Text)
        : System.IO.Path.GetFileName(Path?.TrimEnd('\\', '/')) is { Length: > 0 } name ? name : Path ?? "?";
}

public static class ShelfRules
{
    public const int MaxItems = 70;
    public const int MaxTextLength = 10_000;
    private const int MaxPathLength = 32_767;

    /// <summary>
    /// Agrega rutas al principio del shelf. Ignora rutas inválidas o repetidas y
    /// respeta el máximo de elementos. Devuelve cuántas se agregaron.
    /// </summary>
    public static int AddPaths(List<ShelfItem> items, IEnumerable<(string Path, bool IsFolder)> paths)
    {
        var added = 0;
        foreach (var (path, isFolder) in paths)
        {
            if (items.Count >= MaxItems) break;
            if (!IsAcceptablePath(path)) continue;
            if (items.Any(i => i.Kind != ShelfItemKind.Text && string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase))) continue;

            items.Insert(0, new ShelfItem { Kind = isFolder ? ShelfItemKind.Folder : ShelfItemKind.File, Path = path });
            added++;
        }
        return added;
    }

    /// <summary>Agrega una tarjeta de texto (recortada al máximo permitido).</summary>
    public static bool AddText(List<ShelfItem> items, string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || items.Count >= MaxItems) return false;

        var value = text.Length > MaxTextLength ? text[..MaxTextLength] : text;
        if (items.Any(i => i.Kind == ShelfItemKind.Text && i.Text == value)) return false;

        items.Insert(0, new ShelfItem { Kind = ShelfItemKind.Text, Text = value });
        return true;
    }

    /// <summary>
    /// Limpia lo cargado desde disco: descarta entradas dañadas o editadas a mano
    /// que no cumplan las reglas, y recorta al máximo.
    /// </summary>
    public static List<ShelfItem> Sanitize(IEnumerable<ShelfItem>? loaded)
    {
        var result = new List<ShelfItem>();
        if (loaded is null) return result;

        foreach (var item in loaded)
        {
            if (result.Count >= MaxItems) break;
            if (item is null) continue;

            var valid = item.Kind switch
            {
                ShelfItemKind.Text => !string.IsNullOrWhiteSpace(item.Text),
                ShelfItemKind.File or ShelfItemKind.Folder => IsAcceptablePath(item.Path),
                _ => false,
            };
            if (!valid) continue;

            if (item.Kind == ShelfItemKind.Text && item.Text!.Length > MaxTextLength)
            {
                item.Text = item.Text[..MaxTextLength];
            }
            if (string.IsNullOrWhiteSpace(item.Id)) item.Id = Guid.NewGuid().ToString("N");
            result.Add(item);
        }

        return result;
    }

    /// <summary>
    /// Solo rutas absolutas normales (C:\..., \\servidor\...). Se rechazan rutas
    /// relativas, de dispositivo (\\?\, \\.\) y con caracteres inválidos.
    /// </summary>
    public static bool IsAcceptablePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > MaxPathLength) return false;
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\\.\", StringComparison.Ordinal)) return false;
        if (path.IndexOfAny(System.IO.Path.GetInvalidPathChars()) >= 0 || path.Any(char.IsControl)) return false;

        var isDrive = path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] is '\\' or '/';
        var isUnc = path.StartsWith(@"\\", StringComparison.Ordinal) && path.Length > 2 && path[2] != '\\';
        return isDrive || isUnc;
    }

    /// <summary>Primera línea del texto, acortada, para mostrar en la tarjeta.</summary>
    public static string Preview(string? text, int maxLength = 60)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var firstLine = text.Trim().Split('\n')[0].Trim();
        return firstLine.Length <= maxLength ? firstLine : firstLine[..(maxLength - 1)] + "…";
    }
}
