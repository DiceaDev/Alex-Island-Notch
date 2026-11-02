namespace AlexIsland.Core.Services;
public sealed class SystemClock : IClock { public DateTimeOffset Now => DateTimeOffset.Now; }
