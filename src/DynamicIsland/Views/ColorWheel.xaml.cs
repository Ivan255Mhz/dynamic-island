using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DynamicIsland.Infrastructure;

namespace DynamicIsland.Views;

/// <summary>
/// Hue ring with a saturation/value square, similar to the reference picker.
/// </summary>
public partial class ColorWheel : UserControl
{
    private const double Size = 124;
    private const double OuterRadius = 58;
    private const double InnerRadius = 48;
    private const double SquareSize = 52;
    private const double MarkerRadius = (OuterRadius + InnerRadius) / 2;

    private static readonly Lazy<BitmapSource> Ring = new(CreateRing, isThreadSafe: true);

    private DragMode _mode;

    /// <summary>Builds the ring bitmap ahead of time so the first show is smooth.</summary>
    public static void Prewarm() => _ = Ring.Value;

    public ColorWheel()
    {
        InitializeComponent();

        RingImage.Source = Ring.Value;
        UpdateVisuals();

        Root.MouseLeftButtonDown += OnMouseDown;
        Root.MouseMove += OnMouseMove;
        Root.MouseLeftButtonUp += OnMouseUp;
    }

    public static readonly DependencyProperty HueProperty = DependencyProperty.Register(
        nameof(Hue),
        typeof(double),
        typeof(ColorWheel),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnVisualChanged));

    public static readonly DependencyProperty SaturationProperty = DependencyProperty.Register(
        nameof(Saturation),
        typeof(double),
        typeof(ColorWheel),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnVisualChanged));

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value),
        typeof(double),
        typeof(ColorWheel),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnVisualChanged));

    public double Hue
    {
        get => (double)GetValue(HueProperty);
        set => SetValue(HueProperty, value);
    }

    public double Saturation
    {
        get => (double)GetValue(SaturationProperty);
        set => SetValue(SaturationProperty, value);
    }

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    private static void OnVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((ColorWheel)d).UpdateVisuals();

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        var mode = HitTest(e.GetPosition(this));
        if (mode == DragMode.None)
        {
            return;
        }

        _mode = mode;
        Root.CaptureMouse();
        Apply(e.GetPosition(this));
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_mode == DragMode.None || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        Apply(e.GetPosition(this));
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_mode == DragMode.None)
        {
            return;
        }

        _mode = DragMode.None;
        Root.ReleaseMouseCapture();
    }

    private DragMode HitTest(Point point)
    {
        var dx = point.X - (Size / 2);
        var dy = point.Y - (Size / 2);
        var distance = Math.Sqrt((dx * dx) + (dy * dy));

        if (distance >= InnerRadius && distance <= OuterRadius + 4)
        {
            return DragMode.Hue;
        }

        var left = (Size - SquareSize) / 2;
        if (point.X >= left && point.X <= left + SquareSize &&
            point.Y >= left && point.Y <= left + SquareSize)
        {
            return DragMode.SaturationValue;
        }

        return DragMode.None;
    }

    private void Apply(Point point)
    {
        if (_mode == DragMode.Hue)
        {
            var dx = point.X - (Size / 2);
            var dy = point.Y - (Size / 2);
            Hue = ((Math.Atan2(dy, dx) * 180 / Math.PI) + 360) % 360;
            return;
        }

        var left = (Size - SquareSize) / 2;
        Saturation = Math.Clamp((point.X - left) / SquareSize, 0, 1);
        Value = Math.Clamp(1 - ((point.Y - left) / SquareSize), 0, 1);
    }

    private void UpdateVisuals()
    {
        SvBase.Fill = new SolidColorBrush(ColorMath.FromHsv(Hue, 1, 1));

        var center = Size / 2;
        var angle = Hue * Math.PI / 180;
        var hueX = center + (Math.Cos(angle) * MarkerRadius);
        var hueY = center + (Math.Sin(angle) * MarkerRadius);
        Canvas.SetLeft(HueMarker, hueX - (HueMarker.Width / 2));
        Canvas.SetTop(HueMarker, hueY - (HueMarker.Height / 2));

        var left = (Size - SquareSize) / 2;
        var svX = left + (Saturation * SquareSize);
        var svY = left + ((1 - Value) * SquareSize);
        Canvas.SetLeft(SvMarker, svX - (SvMarker.Width / 2));
        Canvas.SetTop(SvMarker, svY - (SvMarker.Height / 2));
    }

    private static BitmapSource CreateRing()
    {
        // Supersample 4x and downsample in code so the image is rendered 1:1
        // (no per-frame filtering) yet keeps smooth, anti-aliased edges.
        const int scale = 4;
        var src = (int)Size * scale;
        var srcStride = src * 4;
        var source = new byte[srcStride * src];
        var center = src / 2.0;
        var outer = OuterRadius * scale;
        var inner = InnerRadius * scale;

        for (var y = 0; y < src; y++)
        {
            for (var x = 0; x < src; x++)
            {
                var dx = x + 0.5 - center;
                var dy = y + 0.5 - center;
                var distance = Math.Sqrt((dx * dx) + (dy * dy));

                if (distance < inner || distance > outer)
                {
                    continue;
                }

                var hue = ((Math.Atan2(dy, dx) * 180 / Math.PI) + 360) % 360;
                var color = ColorMath.FromHsv(hue, 1, 1);
                var index = (y * srcStride) + (x * 4);
                source[index] = color.B;
                source[index + 1] = color.G;
                source[index + 2] = color.R;
                source[index + 3] = 255;
            }
        }

        var size = (int)Size;
        var stride = size * 4;
        var pixels = new byte[stride * size];
        var samples = scale * scale;

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                int r = 0, g = 0, b = 0, a = 0;

                for (var sy = 0; sy < scale; sy++)
                {
                    var row = ((y * scale) + sy) * srcStride;
                    for (var sx = 0; sx < scale; sx++)
                    {
                        var i = row + (((x * scale) + sx) * 4);
                        var alpha = source[i + 3];
                        b += source[i] * alpha;
                        g += source[i + 1] * alpha;
                        r += source[i + 2] * alpha;
                        a += alpha;
                    }
                }

                var o = (y * stride) + (x * 4);
                if (a > 0)
                {
                    pixels[o] = (byte)(b / a);
                    pixels[o + 1] = (byte)(g / a);
                    pixels[o + 2] = (byte)(r / a);
                    pixels[o + 3] = (byte)(a / samples);
                }
            }
        }

        var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        bitmap.Freeze();
        return bitmap;
    }

    private enum DragMode
    {
        None,
        Hue,
        SaturationValue,
    }
}
