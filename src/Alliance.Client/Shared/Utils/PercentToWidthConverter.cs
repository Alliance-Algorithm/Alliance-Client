using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Alliance.Client.Shared.Utils;

/// <summary>
/// Converts a 0..1 percent to a pixel width.
/// Single-binding mode: uses DefaultMaxWidth.
/// Multi-binding mode: second value is the parent bar's actual width.
/// </summary>
public sealed class PercentToWidthConverter : IValueConverter, IMultiValueConverter
{
    public static readonly PercentToWidthConverter Instance = new();

    private const double DefaultMaxWidth = 168.0;

    // IMultiValueConverter — used by CurrentRobotPanel multi-binding
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 1)
            return 0.0;

        var percent = values[0] switch
        {
            double d => d,
            int i => i / 100.0,
            _ => 0.0
        };

        var maxWidth = DefaultMaxWidth;
        if (values.Count >= 2 && values[1] is double parentWidth && parentWidth > 0)
            maxWidth = parentWidth;

        return Math.Clamp(percent * maxWidth, 0, maxWidth);
    }

    // IValueConverter — used by RobotStatusBar single-binding
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double percent)
            return Math.Clamp(percent * DefaultMaxWidth, 0, DefaultMaxWidth);

        if (value is int intPercent)
            return Math.Clamp(intPercent * DefaultMaxWidth / 100.0, 0, DefaultMaxWidth);

        return 0.0;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
