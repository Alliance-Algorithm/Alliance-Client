using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Alliance.Client.Shared.Utils;

public sealed class HealthBarBrushConverter : IValueConverter
{
    public static readonly HealthBarBrushConverter Instance = new();

    private static readonly SolidColorBrush HealthyBrush = new(Color.Parse("#57D7C7"));
    private static readonly SolidColorBrush DamagedBrush = new(Color.Parse("#F1BF5B"));
    private static readonly SolidColorBrush CriticalBrush = new(Color.Parse("#FF8766"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return (value as string) switch
        {
            "healthy" => HealthyBrush,
            "damaged" => DamagedBrush,
            "critical" => CriticalBrush,
            _ => HealthyBrush
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
