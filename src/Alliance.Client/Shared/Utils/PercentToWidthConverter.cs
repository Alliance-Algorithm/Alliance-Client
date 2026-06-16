using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Alliance.Client.Shared.Utils;

public sealed class PercentToWidthConverter : IValueConverter
{
    public static readonly PercentToWidthConverter Instance = new();

    private const double MaxBarWidth = 168.0;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double percent)
        {
            return Math.Clamp(percent * MaxBarWidth, 0, MaxBarWidth);
        }

        if (value is int intPercent)
        {
            return Math.Clamp(intPercent * MaxBarWidth / 100.0, 0, MaxBarWidth);
        }

        return 0.0;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
