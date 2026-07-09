using System.Globalization;
using Avalonia.Data.Converters;

namespace Alliance.Client.Shared.Utils;

public sealed class BarWidthConverter : IValueConverter
{
    public static readonly BarWidthConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double percent && parameter is not null)
        {
            if (TryParseMaxWidth(parameter, out var maxWidth))
            {
                return Math.Clamp(percent * maxWidth, 0, maxWidth);
            }
        }

        return 0.0;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    private static bool TryParseMaxWidth(object parameter, out double maxWidth)
    {
        if (parameter is double d)
        {
            maxWidth = d;
            return true;
        }

        if (parameter is int i)
        {
            maxWidth = i;
            return true;
        }

        if (parameter is string s &&
            double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            maxWidth = parsed;
            return true;
        }

        maxWidth = 0;
        return false;
    }
}
