using DynamicIsland.Models;

namespace DynamicIsland.Services;

public interface IClipboardService : IDisposable
{
    ClipboardEntry Current { get; }

    IReadOnlyList<ClipboardEntry> History { get; }

    event EventHandler<ClipboardEntry>? ClipboardChanged;

    void Start();

    void SetText(string text);
}
