using System.Globalization;
using Avalonia.Media;
using Avalonia.Data.Converters;

namespace Alliance.Client.Features.RmcsImage;

public sealed class StageColorConverter : IValueConverter
{
    private static readonly IBrush DoneBrush = new SolidColorBrush(0xFF58A6FF);
    private static readonly IBrush PendingBrush = new SolidColorBrush(0xFF333333);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? DoneBrush : PendingBrush;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
