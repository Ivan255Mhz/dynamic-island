using System.Windows.Media;

namespace DynamicIsland.Services;

/// <summary>Lets the user pick a color from anywhere on the screen.</summary>
public interface IScreenColorPicker
{
    /// <summary>Raised right before the picking overlay appears.</summary>
    event EventHandler? PickStarted;

    /// <summary>Raised after picking finished (picked or cancelled).</summary>
    event EventHandler? PickFinished;

    /// <summary>Shows the full-screen picker; returns the chosen color or null.</summary>
    Task<Color?> PickAsync(CancellationToken cancellationToken = default);
}
