namespace AlexIsland.Core.Settings;
public sealed class IslandSettings
{
    public int WindowWidth { get; set; } = 420;
    public int WindowHeight { get; set; } = 82;
    public int TopOffset { get; set; } = 8;
    public bool StartWithWindows { get; set; }
    public bool EnableMediaModule { get; set; } = true;
    public bool EnableVolumeModule { get; set; } = true;
    public bool EnableNotificationsModule { get; set; } = true;
}
