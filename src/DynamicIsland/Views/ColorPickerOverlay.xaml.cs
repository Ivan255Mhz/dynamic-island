using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DynamicIsland.Infrastructure;

namespace DynamicIsland.Views;

/// <summary>
/// Full-screen eyedropper: shows the captured frame with a magnifier and
/// returns the pixel under the cursor on click.
/// </summary>
public partial class ColorPickerOverlay : Window
{
    private const int MagnifierCells = 13;
    private const int MagnifierRadius = 6;

    private readonly ScreenPixels _pixels;
    private readonly WriteableBitmap _magnifier;
    private readonly byte[] _magnifierBuffer = new byte[MagnifierCells * MagnifierCells * 4];

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

        _magnifier = new WriteableBitmap(
            MagnifierCells,
            MagnifierCells,
            96,
            96,
            PixelFormats.Bgra32,
            null);
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

        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        Update(center);
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

        HLine.X1 = 0;
        HLine.X2 = ActualWidth;
        HLine.Y1 = HLine.Y2 = cursor.Y;
        VLine.Y1 = 0;
        VLine.Y2 = ActualHeight;
        VLine.X1 = VLine.X2 = cursor.X;

        if (imageX != _lastImageX || imageY != _lastImageY)
        {
            _lastImageX = imageX;
            _lastImageY = imageY;
            UpdateMagnifier(imageX, imageY);

            var color = _pixels.GetPixel(imageX, imageY);
            HexLabel.Text = ColorMath.ToHex(color);
            RgbLabel.Text = $"RGB {ColorMath.ToRgbText(color)}";
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
        for (var dy = -MagnifierRadius; dy <= MagnifierRadius; dy++)
        {
            for (var dx = -MagnifierRadius; dx <= MagnifierRadius; dx++)
            {
                var color = _pixels.GetPixel(imageX + dx, imageY + dy);
                var index = (((dy + MagnifierRadius) * MagnifierCells) + (dx + MagnifierRadius)) * 4;
                _magnifierBuffer[index] = color.B;
                _magnifierBuffer[index + 1] = color.G;
                _magnifierBuffer[index + 2] = color.R;
                _magnifierBuffer[index + 3] = 255;
            }
        }

        _magnifier.WritePixels(
            new Int32Rect(0, 0, MagnifierCells, MagnifierCells),
            _magnifierBuffer,
            MagnifierCells * 4,
            0);
    }

    private void PositionPanels(Point cursor)
    {
        const double panel = 156;
        const double infoHeight = 74;

        var left = cursor.X + 26;
        var top = cursor.Y + 26;

        if (left + panel > ActualWidth)
        {
            left = cursor.X - panel - 26;
        }

        if (top + panel + infoHeight > ActualHeight)
        {
            top = cursor.Y - panel - infoHeight - 26;
        }

        Canvas.SetLeft(Magnifier, Math.Max(0, left));
        Canvas.SetTop(Magnifier, Math.Max(0, top));
        Canvas.SetLeft(Info, Math.Max(0, left));
        Canvas.SetTop(Info, Math.Max(0, top + panel + 8));
    }
}
