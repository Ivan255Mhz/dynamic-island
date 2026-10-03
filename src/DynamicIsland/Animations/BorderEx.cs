using System.Windows;
using System.Windows.Controls;

namespace DynamicIsland.Animations;

/// <summary>
/// Attached animatable radius. WPF has no built-in CornerRadiusAnimation, so we
/// animate a plain double and push it into <see cref="Border.CornerRadius"/>
/// (top corners stay square to keep the notch flush with the screen edge).
/// </summary>
public static class BorderEx
{
    public static readonly DependencyProperty AnimatedRadiusProperty =
        DependencyProperty.RegisterAttached(
            "AnimatedRadius",
            typeof(double),
            typeof(BorderEx),
            new PropertyMetadata(20.0, OnAnimatedRadiusChanged));

    public static double GetAnimatedRadius(DependencyObject element)
        => (double)element.GetValue(AnimatedRadiusProperty);

    public static void SetAnimatedRadius(DependencyObject element, double value)
        => element.SetValue(AnimatedRadiusProperty, value);

    private static void OnAnimatedRadiusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Border border)
        {
            border.CornerRadius = new CornerRadius(0, 0, (double)e.NewValue, (double)e.NewValue);
        }
    }
}
