using DynamicIsland.Models;

namespace DynamicIsland.Services;

public interface IMediaService : IAsyncDisposable
{
    TrackInfo Current { get; }

    event EventHandler<TrackInfo>? TrackChanged;

    Task StartAsync();

    Task TogglePlayPauseAsync();

    Task NextAsync();

    Task PreviousAsync();
}
