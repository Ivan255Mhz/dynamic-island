using System.Windows;
using System.Windows.Controls;
using DynamicIsland.Models;

namespace DynamicIsland.Animations;

/// <summary>
/// Attached animatable radius. WPF has no built-in CornerRadiusAnimation, so we
/// animate a plain double and push it into <see cref="Border.CornerRadius"/>.
/// The rounded corners follow the docked screen edge (flat side against it).
/// </summary>
public static class BorderEx
{
    public static IslandDock Dock { get; set; } = IslandDock.Top;

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

    public static void Apply(Border border, double radius)
    {
        border.CornerRadius = Dock == IslandDock.Bottom
            ? new CornerRadius(radius, radius, 0, 0)
            : new CornerRadius(0, 0, radius, radius);
    }

    private static void OnAnimatedRadiusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Border border)
        {
            Apply(border, (double)e.NewValue);
        }
    }
}
