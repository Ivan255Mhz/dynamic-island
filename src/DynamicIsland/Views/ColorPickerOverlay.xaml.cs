using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DynamicIsland.Infrastructure;

namespace DynamicIsland.Views;

/// <summary>
/// Full-screen eyedropper: shows the captured frame with a small floating
/// swatch + hex label, and returns the pixel under the cursor on click.
/// </summary>
public partial class ColorPickerOverlay : Window
{
    private readonly ScreenPixels _pixels;

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

            var color = _pixels.GetPixel(imageX, imageY);
            HexLabel.Text = ColorMath.ToHex(color);
            Preview.Background = new SolidColorBrush(color);
        }

        PositionPanel(cursor);
    }

    private (int X, int Y) ToImage(Point cursor)
    {
        var scaleX = ActualWidth <= 0 ? 1 : _pixels.Width / ActualWidth;
        var scaleY = ActualHeight <= 0 ? 1 : _pixels.Height / ActualHeight;

        return ((int)Math.Round(cursor.X * scaleX), (int)Math.Round(cursor.Y * scaleY));
    }

    private void PositionPanel(Point cursor)
    {
        var width = Info.ActualWidth > 0 ? Info.ActualWidth : 130;
        var height = Info.ActualHeight > 0 ? Info.ActualHeight : 52;
        const double gap = 20;

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

        Canvas.SetLeft(Info, Math.Max(0, left));
        Canvas.SetTop(Info, Math.Max(0, top));
    }
}
