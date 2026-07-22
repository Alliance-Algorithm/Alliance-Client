using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Alliance.Client.Shared.Utils;

public sealed class TeamHealthBrushConverter : IValueConverter
{
    public static readonly TeamHealthBrushConverter Instance = new();

    private static readonly SolidColorBrush BlueBrush = new(Color.Parse("#4A9EFF"));
    private static readonly SolidColorBrush RedBrush = new(Color.Parse("#FF4A4A"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isBlue)
            return isBlue ? BlueBrush : RedBrush;

        return BlueBrush;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
