namespace AlexIsland.Core.Modules;
public interface IIslandModule : IAsyncDisposable
{
    string Id { get; }
    string DisplayName { get; }
    ValueTask InitializeAsync(CancellationToken cancellationToken);
    ValueTask StartAsync(CancellationToken cancellationToken);
    ValueTask StopAsync(CancellationToken cancellationToken);
}
