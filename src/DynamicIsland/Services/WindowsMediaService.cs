using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DynamicIsland.Models;
using Windows.Foundation;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace DynamicIsland.Services;

/// <summary>
/// Reads and controls the currently active media session through the Windows
/// Global System Media Transport Controls (GSMTC) API. Works with any source
/// exposing a media session, including Yandex Music playing inside a browser.
/// </summary>
public sealed class WindowsMediaService : IMediaService
{
    private const int MaxArtworkRetries = 8;
    private const int FirstArtworkDelayMs = 300;
    private const int RetryArtworkDelayMs = 400;

    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly Dispatcher _dispatcher;

    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private TrackInfo _current = TrackInfo.None;

    private string? _trackKey;
    private ImageSource? _lastArtwork;
    private int? _lastArtworkHash;
    private int _trackGeneration;
    private int _artworkRetries;

    public WindowsMediaService()
    {
        _dispatcher = Application.Current.Dispatcher;
    }

    public TrackInfo Current => _current;

    public event EventHandler<TrackInfo>? TrackChanged;

    public async Task StartAsync()
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            Infrastructure.Diag.MediaManagerReady = 1;
            _manager.CurrentSessionChanged += OnCurrentSessionChanged;
            await AttachToAsync(_manager.GetCurrentSession());
        }
        catch
        {
            Publish(TrackInfo.None);
        }
    }

    public Task TogglePlayPauseAsync() => InvokeSessionAsync(s => s.TryTogglePlayPauseAsync());

    public Task NextAsync() => InvokeSessionAsync(s => s.TrySkipNextAsync());

    public Task PreviousAsync() => InvokeSessionAsync(s => s.TrySkipPreviousAsync());

    private async Task InvokeSessionAsync(
        Func<GlobalSystemMediaTransportControlsSession, IAsyncOperation<bool>> action)
    {
        var session = _session ?? _manager?.GetCurrentSession();
        if (session is null)
        {
            return;
        }

        try
        {
            await action(session);
        }
        catch
        {
            // The session may have disappeared between the check and the call.
        }
    }

    private void OnCurrentSessionChanged(
        GlobalSystemMediaTransportControlsSessionManager sender,
        CurrentSessionChangedEventArgs args)
    {
        _ = AttachToAsync(sender.GetCurrentSession());
    }

    private async Task AttachToAsync(GlobalSystemMediaTransportControlsSession? session)
    {
        await _refreshGate.WaitAsync().ConfigureAwait(false);
        try
        {
            Detach();
            ResetCache();

            _session = session;
            if (_session is null)
            {
                Publish(TrackInfo.None);
                return;
            }

            _session.MediaPropertiesChanged += OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged += OnPlaybackInfoChanged;

            await RefreshCoreAsync(_session).ConfigureAwait(false);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private void Detach()
    {
        if (_session is null)
        {
            return;
        }

        _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
        _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
        _session = null;
    }

    private void ResetCache()
    {
        _trackKey = null;
        _lastArtwork = null;
        _lastArtworkHash = null;
        _artworkRetries = 0;
        _trackGeneration++;
    }

    private void OnMediaPropertiesChanged(
        GlobalSystemMediaTransportControlsSession sender,
        MediaPropertiesChangedEventArgs args)
    {
        _ = RefreshAsync(sender);
    }

    private void OnPlaybackInfoChanged(
        GlobalSystemMediaTransportControlsSession sender,
        PlaybackInfoChangedEventArgs args)
    {
        _ = RefreshAsync(sender);
    }

    private Task RefreshAsync(GlobalSystemMediaTransportControlsSession session)
    {
        return Task.Run(async () =>
        {
            await _refreshGate.WaitAsync().ConfigureAwait(false);
            try
            {
                await RefreshCoreAsync(session).ConfigureAwait(false);
            }
            finally
            {
                _refreshGate.Release();
            }
        });
    }

    private async Task RefreshCoreAsync(GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            Infrastructure.Diag.MediaRefreshes++;
            var props = await session.TryGetMediaPropertiesAsync();
            Infrastructure.Diag.MediaPropertyFetches++;

            if (!ReferenceEquals(session, _session))
            {
                return;
            }

            var status = session.GetPlaybackInfo().PlaybackStatus;
            var isPlaying = status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

            var title = props.Title ?? string.Empty;
            var artist = props.Artist ?? string.Empty;
            var album = props.AlbumTitle ?? string.Empty;
            var key = $"{title}\u001F{artist}\u001F{album}";

            if (!string.Equals(key, _trackKey, StringComparison.Ordinal))
            {
                BeginTrack(title, artist, album, key, isPlaying);
                return;
            }

            // Same track: keep whatever artwork is already resolved.
            Publish(new TrackInfo(title, artist, album, isPlaying, _lastArtwork));
        }
        catch
        {
            // Ignore transient failures while a session is switching.
        }
    }

    private void BeginTrack(string title, string artist, string album, string key, bool isPlaying)
    {
        if (_lastArtwork is null)
        {
            // Nothing was on screen for the previous track, so there is no
            // stale image to guard against.
            _lastArtworkHash = null;
        }

        _trackKey = key;
        _lastArtwork = null;
        _artworkRetries = 0;
        var generation = ++_trackGeneration;

        // Show the track right away with a placeholder cover; the real artwork
        // follows a moment later once the system has actually switched it.
        Publish(new TrackInfo(title, artist, album, isPlaying, null));

        ScheduleArtworkFetch(_session, generation, FirstArtworkDelayMs);
    }

    private void ScheduleArtworkFetch(
        GlobalSystemMediaTransportControlsSession? session,
        int generation,
        int delayMs)
    {
        if (session is null)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delayMs).ConfigureAwait(false);
                await FetchArtworkAsync(session, generation).ConfigureAwait(false);
            }
            catch
            {
                // Ignore; a later event or retry will fetch again.
            }
        });
    }

    private async Task FetchArtworkAsync(
        GlobalSystemMediaTransportControlsSession session,
        int generation)
    {
        ImageSource? image = null;
        var hash = 0;

        try
        {
            var props = await session.TryGetMediaPropertiesAsync();
            var loaded = await LoadArtworkAsync(props.Thumbnail).ConfigureAwait(false);
            if (loaded is not null)
            {
                image = loaded.Value.Image;
                hash = loaded.Value.Hash;
            }
        }
        catch
        {
            // Treated the same as a failed load below.
        }

        var scheduleRetry = false;

        await _refreshGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (generation != _trackGeneration || !ReferenceEquals(session, _session))
            {
                return;
            }

            var stale = _lastArtworkHash is int previousHash && hash == previousHash;

            if (image is not null && !stale)
            {
                _lastArtwork = image;
                _lastArtworkHash = hash;
                _artworkRetries = 0;
                Publish(_current with { Artwork = image });
                return;
            }

            if (_artworkRetries < MaxArtworkRetries)
            {
                _artworkRetries++;
                scheduleRetry = true;
            }
            else if (image is not null)
            {
                // Fallback: accept the image, it may genuinely match the
                // previous track (same album art).
                _lastArtwork = image;
                _lastArtworkHash = hash;
                Publish(_current with { Artwork = image });
                return;
            }
        }
        finally
        {
            _refreshGate.Release();
        }

        if (scheduleRetry)
        {
            ScheduleArtworkFetch(session, generation, RetryArtworkDelayMs);
        }
    }

    private static async Task<(ImageSource Image, int Hash)?> LoadArtworkAsync(
        IRandomAccessStreamReference? reference)
    {
        if (reference is null)
        {
            return null;
        }

        try
        {
            Infrastructure.Diag.MediaArtworkDecodes++;
            using var stream = await reference.OpenReadAsync();
            var size = (uint)stream.Size;
            if (size == 0)
            {
                return null;
            }

            using var reader = new DataReader(stream.GetInputStreamAt(0));
            await reader.LoadAsync(size);
            var bytes = new byte[size];
            reader.ReadBytes(bytes);

            return (BuildImage(bytes), ComputeHash(bytes));
        }
        catch
        {
            return null;
        }
    }

    private static int ComputeHash(byte[] bytes)
    {
        unchecked
        {
            var hash = (int)2166136261;
            foreach (var value in bytes)
            {
                hash = (hash ^ value) * 16777619;
            }

            return hash;
        }
    }

    private static ImageSource BuildImage(byte[] bytes)
    {
        using var memory = new MemoryStream(bytes);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = memory;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private void Publish(TrackInfo info)
    {
        if (_dispatcher.CheckAccess())
        {
            Apply(info);
        }
        else
        {
            _dispatcher.Invoke(() => Apply(info));
        }
    }

    private void Apply(TrackInfo info)
    {
        _current = info;
        TrackChanged?.Invoke(this, info);
    }

    public ValueTask DisposeAsync()
    {
        if (_manager is not null)
        {
            _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
        }

        Detach();
        _refreshGate.Dispose();
        return ValueTask.CompletedTask;
    }
}
