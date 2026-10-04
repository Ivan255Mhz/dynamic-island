using System.Windows.Media;
using DynamicIsland.Infrastructure;

namespace DynamicIsland.Services;

/// <summary>
/// Captures the screen and shows an overlay with a magnifier so the user can
/// pick any pixel color.
/// </summary>
public sealed class ScreenColorPickerService : IScreenColorPicker
{
    public event EventHandler? PickStarted;

    public event EventHandler? PickFinished;

    public async Task<Color?> PickAsync(CancellationToken cancellationToken = default)
    {
        PickStarted?.Invoke(this, EventArgs.Empty);

        try
        {
            // Give the island a moment to hide so it is not part of the frame.
            await Task.Delay(150, cancellationToken).ConfigureAwait(true);

            using var pixels = ScreenPixels.CaptureVirtualScreen();
            var overlay = new Views.ColorPickerOverlay(pixels);
            overlay.ShowDialog();
            return overlay.PickedColor;
        }
        catch
        {
            return null;
        }
        finally
        {
            PickFinished?.Invoke(this, EventArgs.Empty);
        }
    }
}
