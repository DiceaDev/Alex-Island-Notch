using System.Text.Json.Serialization;

namespace AlexIsland.Core.Models;

public sealed class DevTask
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; set; } = "TODO";

    [JsonPropertyName("is_completed")]
    public bool IsCompleted { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public static (string Category, string CleanTitle) ParseInput(string rawInput)
    {
        if (string.IsNullOrWhiteSpace(rawInput))
            return ("TODO", string.Empty);

        var trimmed = rawInput.Trim();
        var colonIndex = trimmed.IndexOf(':');

        if (colonIndex > 0 && colonIndex <= 10)
        {
            var prefix = trimmed[..colonIndex].Trim().ToUpperInvariant();
            var body = trimmed[(colonIndex + 1)..].Trim();

            var normalizedCategory = prefix switch
            {
                "FEAT" or "FEATURE" => "FEAT",
                "FIX" or "BUG" or "HOTFIX" => "FIX",
                "REFACTOR" or "CLEAN" => "REFACTOR",
                "DOCS" or "DOC" => "DOCS",
                "TEST" or "TESTS" => "TEST",
                "CHORE" => "CHORE",
                _ => string.Empty
            };

            if (!string.IsNullOrEmpty(normalizedCategory) && !string.IsNullOrWhiteSpace(body))
            {
                return (normalizedCategory, body);
            }
        }

        return ("TODO", trimmed);
    }
}

