using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DynamicIsland.Infrastructure;

/// <summary>
/// A captured frame of the virtual screen with fast pixel access, used by the
/// eyedropper overlay so the overlay itself never ends up in the sampled pixels.
/// </summary>
public sealed class ScreenPixels : IDisposable
{
    private readonly byte[] _pixels;
    private readonly int _stride;

    private ScreenPixels(byte[] pixels, int stride, int width, int height, System.Drawing.Rectangle bounds)
    {
        _pixels = pixels;
        _stride = stride;
        Width = width;
        Height = height;
        Bounds = bounds;
    }

    public int Width { get; }

    public int Height { get; }

    public System.Drawing.Rectangle Bounds { get; }

    public static ScreenPixels CaptureVirtualScreen()
    {
        var bounds = new System.Drawing.Rectangle(
            (int)SystemParameters.VirtualScreenLeft,
            (int)SystemParameters.VirtualScreenTop,
            Math.Max(1, (int)SystemParameters.VirtualScreenWidth),
            Math.Max(1, (int)SystemParameters.VirtualScreenHeight));

        using var bitmap = new System.Drawing.Bitmap(
            bounds.Width,
            bounds.Height,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);

        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(
                bounds.Left,
                bounds.Top,
                0,
                0,
                bounds.Size,
                System.Drawing.CopyPixelOperation.SourceCopy);
        }

        var data = bitmap.LockBits(
            new System.Drawing.Rectangle(0, 0, bounds.Width, bounds.Height),
            System.Drawing.Imaging.ImageLockMode.ReadOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);

        try
        {
            var stride = Math.Abs(data.Stride);
            var pixels = new byte[stride * bounds.Height];
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            return new ScreenPixels(pixels, stride, bounds.Width, bounds.Height, bounds);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    public Color GetPixel(int x, int y)
    {
        var cx = Math.Clamp(x, 0, Width - 1);
        var cy = Math.Clamp(y, 0, Height - 1);
        var index = (cy * _stride) + (cx * 4);

        // Captured as BGRA.
        return Color.FromRgb(_pixels[index + 2], _pixels[index + 1], _pixels[index]);
    }

    public BitmapSource CreateBitmapSource()
    {
        var source = BitmapSource.Create(Width, Height, 96, 96, PixelFormats.Bgra32, null, _pixels, _stride);
        source.Freeze();
        return source;
    }

    public void Dispose()
    {
        // Managed buffer only; nothing unmanaged to release.
    }
}
