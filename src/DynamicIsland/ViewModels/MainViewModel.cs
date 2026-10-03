using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DynamicIsland.ViewModels;

public enum IslandSection
{
    Music,
    Clipboard,
    Screenshots,
    Translator,
}

public sealed partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private IslandSection _selectedSection = IslandSection.Music;

    public MainViewModel(
        MusicViewModel music,
        ClipboardViewModel clipboard,
        ScreenshotsViewModel screenshots,
        TranslatorViewModel translator)
    {
        Music = music;
        Clipboard = clipboard;
        Screenshots = screenshots;
        Translator = translator;

        Music.PropertyChanged += OnMusicPropertyChanged;
    }

    public MusicViewModel Music { get; }

    public ClipboardViewModel Clipboard { get; }

    public ScreenshotsViewModel Screenshots { get; }

    public TranslatorViewModel Translator { get; }

    public bool ShowMiniPlayer => Music.Track.HasTrack;

    public bool ShowRail => !ShowMiniPlayer;

    [RelayCommand]
    private void OpenSection(string? section)
    {
        if (Enum.TryParse<IslandSection>(section, out var parsed))
        {
            SelectedSection = parsed;
        }
    }

    private void OnMusicPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MusicViewModel.Track))
        {
            OnPropertyChanged(nameof(ShowMiniPlayer));
            OnPropertyChanged(nameof(ShowRail));
        }
    }
}
