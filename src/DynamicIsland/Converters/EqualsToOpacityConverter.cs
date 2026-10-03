using System.Globalization;
using System.Windows.Data;

namespace DynamicIsland.Converters;

/// <summary>
/// Returns full opacity when the bound value equals the converter parameter,
/// a dimmed opacity when another section is active, and a mid opacity when
/// nothing is selected (home/menu state).
/// </summary>
public sealed class EqualsToOpacityConverter : IValueConverter
{
    public double ActiveOpacity { get; set; } = 1.0;

    public double InactiveOpacity { get; set; } = 0.42;

    public double NeutralOpacity { get; set; } = 0.8;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var current = value?.ToString();
        if (string.IsNullOrEmpty(current))
        {
            return NeutralOpacity;
        }

        var equals = string.Equals(current, parameter?.ToString(), StringComparison.OrdinalIgnoreCase);
        return equals ? ActiveOpacity : InactiveOpacity;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
