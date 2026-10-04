using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DynamicIsland.Infrastructure;

namespace DynamicIsland.Views;

/// <summary>
/// Full-screen eyedropper: shows a Figma-like zoom grid above a swatch + hex
/// label and returns the pixel under the cursor on click.
/// </summary>
public partial class ColorPickerOverlay : Window
{
    private const int Cells = 13;
    private const int Radius = 6;

    private readonly ScreenPixels _pixels;
    private readonly WriteableBitmap _magnifier;
    private readonly byte[] _magnifierBuffer = new byte[Cells * Cells * 4];

    private int _lastImageX = -1;
    private int _lastImageY = -1;

    public ColorPickerOverlay(ScreenPixels pixels)
    {
        InitializeComponent();

        _pixels = pixels;

        Left = pixels.Bounds.Left;
        Top = pixels.Bounds.Top;
        Width = pixels.Width;
        Height = pixels.Height;

        ScreenImage.Source = pixels.CreateBitmapSource();

        _magnifier = new WriteableBitmap(Cells, Cells, 96, 96, PixelFormats.Bgra32, null);
        MagnifierImage.Source = _magnifier;

        Loaded += OnLoaded;
        MouseMove += OnMouseMove;
        MouseLeftButtonDown += OnLeftButtonDown;
        MouseRightButtonDown += OnCancel;
        KeyDown += OnKeyDown;
    }

    public Color? PickedColor { get; private set; }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Activate();
        Focus();

        Update(new Point(ActualWidth / 2, ActualHeight / 2));
    }

    private void OnMouseMove(object sender, MouseEventArgs e) => Update(e.GetPosition(this));

    private void OnLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var (x, y) = ToImage(e.GetPosition(this));
        PickedColor = _pixels.GetPixel(x, y);
        Close();
    }

    private void OnCancel(object sender, MouseButtonEventArgs e)
    {
        PickedColor = null;
        Close();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            PickedColor = null;
            Close();
        }
    }

    private void Update(Point cursor)
    {
        var (imageX, imageY) = ToImage(cursor);

        if (imageX != _lastImageX || imageY != _lastImageY)
        {
            _lastImageX = imageX;
            _lastImageY = imageY;

            UpdateMagnifier(imageX, imageY);

            var color = _pixels.GetPixel(imageX, imageY);
            HexLabel.Text = ColorMath.ToHex(color);
            Preview.Background = new SolidColorBrush(color);
        }

        PositionPanels(cursor);
    }

    private (int X, int Y) ToImage(Point cursor)
    {
        var scaleX = ActualWidth <= 0 ? 1 : _pixels.Width / ActualWidth;
        var scaleY = ActualHeight <= 0 ? 1 : _pixels.Height / ActualHeight;

        return ((int)Math.Round(cursor.X * scaleX), (int)Math.Round(cursor.Y * scaleY));
    }

    private void UpdateMagnifier(int imageX, int imageY)
    {
        for (var dy = -Radius; dy <= Radius; dy++)
        {
            for (var dx = -Radius; dx <= Radius; dx++)
            {
                var color = _pixels.GetPixel(imageX + dx, imageY + dy);
                var index = (((dy + Radius) * Cells) + (dx + Radius)) * 4;
                _magnifierBuffer[index] = color.B;
                _magnifierBuffer[index + 1] = color.G;
                _magnifierBuffer[index + 2] = color.R;
                _magnifierBuffer[index + 3] = 255;
            }
        }

        _magnifier.WritePixels(
            new Int32Rect(0, 0, Cells, Cells),
            _magnifierBuffer,
            Cells * 4,
            0);
    }

    private void PositionPanels(Point cursor)
    {
        const double gap = 18;
        const double spacing = 8;

        var magWidth = Magnifier.Width;
        var magHeight = Magnifier.Height;
        var infoWidth = Info.ActualWidth > 0 ? Info.ActualWidth : magWidth;
        var infoHeight = Info.ActualHeight > 0 ? Info.ActualHeight : 52;

        var width = Math.Max(magWidth, infoWidth);
        var height = magHeight + spacing + infoHeight;

        var left = cursor.X + gap;
        var top = cursor.Y + gap;

        if (left + width > ActualWidth)
        {
            left = cursor.X - width - gap;
        }

        if (top + height > ActualHeight)
        {
            top = cursor.Y - height - gap;
        }

        left = Math.Max(0, left);
        top = Math.Max(0, top);

        Canvas.SetLeft(Magnifier, left);
        Canvas.SetTop(Magnifier, top);
        Canvas.SetLeft(Info, left + ((magWidth - infoWidth) / 2));
        Canvas.SetTop(Info, top + magHeight + spacing);
    }
}
