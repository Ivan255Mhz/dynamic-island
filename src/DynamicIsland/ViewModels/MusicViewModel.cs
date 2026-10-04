using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DynamicIsland.Interop;
using DynamicIsland.Models;
using DynamicIsland.Services;

namespace DynamicIsland.ViewModels;

public sealed partial class MusicViewModel : ObservableObject, IDisposable
{
    private readonly IMediaService _media;

    [ObservableProperty]
    private TrackInfo _track = TrackInfo.None;

    [ObservableProperty]
    private bool _isMuted;

    [ObservableProperty]
    private double _volume;

    private bool _syncingVolume;

    public MusicViewModel(IMediaService media)
    {
        _media = media;
        _media.TrackChanged += OnTrackChanged;
        Track = _media.Current;
        IsMuted = SystemAudio.IsMuted();

        if (SystemAudio.TryGetVolume(out var volume))
        {
            _syncingVolume = true;
            Volume = volume;
            _syncingVolume = false;
        }
    }

    public string PlayPauseGlyph => Track.IsPlaying ? "⏸" : "▶";

    public string VolumePercent => $"{Math.Round(Volume * 100)}%";

    [RelayCommand]
    private Task TogglePlayPause() => _media.TogglePlayPauseAsync();

    [RelayCommand]
    private Task Next() => _media.NextAsync();

    [RelayCommand]
    private Task Previous() => _media.PreviousAsync();

    [RelayCommand]
    private void ToggleMute()
    {
        if (SystemAudio.TryToggleMute(out var muted))
        {
            IsMuted = muted;
        }
    }

    /// <summary>Re-reads the system volume so external changes (volume keys) show up.</summary>
    public void RefreshVolume()
    {
        if (SystemAudio.TryGetVolume(out var volume))
        {
            _syncingVolume = true;
            Volume = volume;
            _syncingVolume = false;
        }

        IsMuted = SystemAudio.IsMuted();
    }

    partial void OnVolumeChanged(double value)
    {
        OnPropertyChanged(nameof(VolumePercent));

        if (_syncingVolume)
        {
            return;
        }

        if (SystemAudio.TrySetVolume((float)value))
        {
            IsMuted = SystemAudio.IsMuted();
        }
    }

    private void OnTrackChanged(object? sender, TrackInfo track) => Track = track;

    partial void OnTrackChanged(TrackInfo value) => OnPropertyChanged(nameof(PlayPauseGlyph));

    public void Dispose() => _media.TrackChanged -= OnTrackChanged;
}
