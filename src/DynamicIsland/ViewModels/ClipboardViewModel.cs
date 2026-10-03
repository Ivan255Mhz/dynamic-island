using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DynamicIsland.Models;
using DynamicIsland.Services;

namespace DynamicIsland.ViewModels;

public sealed partial class ClipboardViewModel : ObservableObject, IDisposable
{
    private readonly IClipboardService _clipboard;

    [ObservableProperty]
    private ClipboardEntry _current;

    public ClipboardViewModel(IClipboardService clipboard)
    {
        _clipboard = clipboard;
        _current = clipboard.Current;
        _clipboard.ClipboardChanged += OnClipboardChanged;

        Rebuild();
    }

    public ObservableCollection<ClipboardEntry> History { get; } = new();

    [RelayCommand]
    private void Activate(ClipboardEntry? entry)
    {
        if (entry is null)
        {
            return;
        }

        if (entry.HasText)
        {
            _clipboard.SetText(entry.Text!);
            return;
        }

        if (entry.IsImage && entry.Image is System.Windows.Media.Imaging.BitmapSource bitmap)
        {
            try
            {
                Clipboard.SetImage(bitmap);
            }
            catch
            {
                // Clipboard may be locked; ignore for the prototype.
            }
        }
    }

    private void OnClipboardChanged(object? sender, ClipboardEntry entry)
    {
        Current = entry;
        Rebuild();
    }

    private void Rebuild()
    {
        History.Clear();
        foreach (var item in _clipboard.History)
        {
            History.Add(item);
        }
    }

    public void Dispose() => _clipboard.ClipboardChanged -= OnClipboardChanged;
}
