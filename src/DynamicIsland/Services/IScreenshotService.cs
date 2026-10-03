using DynamicIsland.Models;

namespace DynamicIsland.Services;

public interface IScreenshotService : IDisposable
{
    IReadOnlyList<ScreenshotItem> Items { get; }

    string? FolderPath { get; }

    event EventHandler<IReadOnlyList<ScreenshotItem>>? ItemsChanged;

    void Start();
}
