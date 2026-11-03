using System.Text.Json.Serialization;

namespace AlexIsland.Core.Models;

public enum PomodoroMode
{
    Work,
    ShortBreak,
    LongBreak
}

public enum PomodoroState
{
    Stopped,
    Running,
    Paused
}

public sealed class PomodoroSettings
{
    [JsonPropertyName("work_minutes")]
    public int WorkMinutes { get; set; } = 25;

    [JsonPropertyName("short_break_minutes")]
    public int ShortBreakMinutes { get; set; } = 5;

    [JsonPropertyName("long_break_minutes")]
    public int LongBreakMinutes { get; set; } = 15;

    [JsonPropertyName("long_break_interval")]
    public int LongBreakInterval { get; set; } = 4;

    [JsonPropertyName("completed_today")]
    public int CompletedToday { get; set; }

    [JsonPropertyName("last_active_date")]
    public DateTime LastActiveDate { get; set; } = DateTime.Today;

    public void CheckDateReset()
    {
        if (DateTime.Today > LastActiveDate)
        {
            CompletedToday = 0;
            LastActiveDate = DateTime.Today;
        }
    }
}

