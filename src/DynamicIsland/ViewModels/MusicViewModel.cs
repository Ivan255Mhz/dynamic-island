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

    public MusicViewModel(IMediaService media)
    {
        _media = media;
        _media.TrackChanged += OnTrackChanged;
        Track = _media.Current;
        IsMuted = SystemAudio.IsMuted();
    }

    public string PlayPauseGlyph => Track.IsPlaying ? "⏸" : "▶";

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

    private void OnTrackChanged(object? sender, TrackInfo track) => Track = track;

    partial void OnTrackChanged(TrackInfo value) => OnPropertyChanged(nameof(PlayPauseGlyph));

    public void Dispose() => _media.TrackChanged -= OnTrackChanged;
}
