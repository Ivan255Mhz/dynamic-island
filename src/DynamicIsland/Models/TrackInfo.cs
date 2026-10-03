using System.Windows.Media;

namespace DynamicIsland.Models;

public sealed record TrackInfo(
    string Title,
    string Artist,
    string Album,
    bool IsPlaying,
    ImageSource? Artwork)
{
    public static readonly TrackInfo None = new(
        Title: "Нет активного медиа",
        Artist: string.Empty,
        Album: string.Empty,
        IsPlaying: false,
        Artwork: null);

    public bool HasTrack => !ReferenceEquals(this, None) && !string.IsNullOrWhiteSpace(Title);

    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? "Неизвестный трек" : Title;

    public string DisplayArtist => string.IsNullOrWhiteSpace(Artist) ? "—" : Artist;
}
