using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Alliance.Client.Shared.Utils;

public sealed class TeamHealthBrushConverter : IValueConverter
{
    public static readonly TeamHealthBrushConverter Instance = new();

    private static readonly SolidColorBrush AllyBrush = new(Color.Parse("#4A9EFF"));
    private static readonly SolidColorBrush EnemyBrush = new(Color.Parse("#FF4A4A"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isEnemy)
            return isEnemy ? EnemyBrush : AllyBrush;

        return AllyBrush;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
