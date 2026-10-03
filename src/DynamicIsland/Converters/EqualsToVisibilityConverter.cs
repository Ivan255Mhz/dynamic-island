using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DynamicIsland.Converters;

public sealed class EqualsToVisibilityConverter : IValueConverter
{
    public bool UseHidden { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var equals = string.Equals(
            value?.ToString(),
            parameter?.ToString(),
            StringComparison.OrdinalIgnoreCase);

        if (equals)
        {
            return Visibility.Visible;
        }

        return UseHidden ? Visibility.Hidden : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
