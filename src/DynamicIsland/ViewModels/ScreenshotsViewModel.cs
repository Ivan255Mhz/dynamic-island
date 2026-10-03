using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.Input;
using DynamicIsland.Models;
using DynamicIsland.Services;

namespace DynamicIsland.ViewModels;

public sealed partial class ScreenshotsViewModel : IDisposable
{
    private readonly IScreenshotService _screenshots;

    public ScreenshotsViewModel(IScreenshotService screenshots)
    {
        _screenshots = screenshots;
        _screenshots.ItemsChanged += OnItemsChanged;
        Replace(screenshots.Items);
    }

    public ObservableCollection<ScreenshotItem> Items { get; } = new();

    [RelayCommand]
    private void Open(ScreenshotItem? item)
    {
        if (item is null || !System.IO.File.Exists(item.FilePath))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{item.FilePath}\"",
            UseShellExecute = true,
        });
    }

    [RelayCommand]
    private void OpenFolder()
    {
        var folder = _screenshots.FolderPath;
        if (string.IsNullOrWhiteSpace(folder) || !System.IO.Directory.Exists(folder))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{folder}\"",
            UseShellExecute = true,
        });
    }

    private void OnItemsChanged(object? sender, IReadOnlyList<ScreenshotItem> items) => Replace(items);

    private void Replace(IReadOnlyList<ScreenshotItem> items)
    {
        Items.Clear();
        foreach (var item in items)
        {
            Items.Add(item);
        }
    }

    public void Dispose() => _screenshots.ItemsChanged -= OnItemsChanged;
}
