using System.Text.Json.Serialization;

namespace AlexIsland.Core.Habits;

/// <summary>Un hábito con su meta diaria y el registro de cuántas veces se cumplió cada día.</summary>
public sealed class Habit
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Veces al día para darlo por cumplido (1 = marcar/desmarcar; 8 = ocho vasos de agua).</summary>
    [JsonPropertyName("target")]
    public int Target { get; set; } = 1;

    /// <summary>Fecha (yyyy-MM-dd) → veces registradas ese día.</summary>
    [JsonPropertyName("log")]
    public Dictionary<string, int> Log { get; set; } = [];
}
