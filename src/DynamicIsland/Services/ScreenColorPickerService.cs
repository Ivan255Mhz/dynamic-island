using System.Windows.Media;

namespace DynamicIsland.Services;

/// <summary>
/// Shows a live overlay with a magnifier so the user can pick any pixel color.
/// Pixel data is read straight from the screen while the overlay is open.
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
            // Give the island a moment to hide so it does not sit under the cursor.
            await Task.Delay(150, cancellationToken).ConfigureAwait(true);

            var overlay = new Views.ColorPickerOverlay();
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
