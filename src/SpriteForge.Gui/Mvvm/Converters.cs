using System.Globalization;
using System.Windows.Data;

namespace SpriteForge.Gui.Mvvm;

/// <summary>Uppercases a bound string (used for panel/group headers per the design's style spec).</summary>
public sealed class UpperCaseConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value?.ToString()?.ToUpperInvariant() ?? string.Empty;

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value?.ToString() ?? string.Empty;
}

/// <summary>
/// Inverts a bool for Visibility bindings (e.g. show the 3D preview when sheet mode is OFF).
/// Collapsed when <c>true</c>, Visible when <c>false</c>.
/// </summary>
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && b ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is System.Windows.Visibility v && v == System.Windows.Visibility.Collapsed;
}
