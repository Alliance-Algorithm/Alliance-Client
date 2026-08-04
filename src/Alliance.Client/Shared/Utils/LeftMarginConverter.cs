using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace Alliance.Client.Shared.Utils;

public sealed class LeftMarginConverter : IMultiValueConverter
{
    public static readonly LeftMarginConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2)
            return new Thickness(0);

        var percent = values[0] switch
        {
            double d => d,
            int i => i / 100.0,
            _ => 0.0
        };

        var maxWidth = 0.0;
        if (values[1] is double w && w > 0)
            maxWidth = w;

        return new Thickness(percent * maxWidth, 0, 0, 0);
    }
}
