using System.Globalization;
using Avalonia.Data.Converters;

namespace Alliance.Client.Shared.Utils;

public sealed class ScaledDoubleConverter : IValueConverter
{
    public static readonly ScaledDoubleConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var scale = System.Convert.ToDouble(value ?? 1d, CultureInfo.InvariantCulture);
        var baseValue = System.Convert.ToDouble(parameter ?? 0d, CultureInfo.InvariantCulture);
        return Math.Round(baseValue * scale, 2);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
