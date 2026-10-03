using System.Windows.Media;

namespace DynamicIsland.Models;

public sealed record ScreenshotItem(
    string FilePath,
    DateTimeOffset CapturedAt,
    ImageSource? Thumbnail);
