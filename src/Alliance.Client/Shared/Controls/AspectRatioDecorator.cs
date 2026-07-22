using Avalonia;
using Avalonia.Controls;

namespace Alliance.Client.Shared.Controls;

public sealed class AspectRatioDecorator : Decorator
{
    public static readonly StyledProperty<double> AspectRatioProperty =
        AvaloniaProperty.Register<AspectRatioDecorator, double>(nameof(AspectRatio), 16d / 9d);

    public double AspectRatio
    {
        get => GetValue(AspectRatioProperty);
        set => SetValue(AspectRatioProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var targetSize = ConstrainToAspectRatio(availableSize);
        Child?.Measure(targetSize);
        return targetSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Child is null)
        {
            return finalSize;
        }

        var targetSize = ConstrainToAspectRatio(finalSize);
        var x = Math.Max(0, (finalSize.Width - targetSize.Width) / 2);
        var y = Math.Max(0, (finalSize.Height - targetSize.Height) / 2);
        Child.Arrange(new Rect(new Point(x, y), targetSize));
        return finalSize;
    }

    private Size ConstrainToAspectRatio(Size availableSize)
    {
        var aspectRatio = AspectRatio;
        if (double.IsNaN(aspectRatio) || aspectRatio <= 0)
        {
            return availableSize;
        }

        var hasWidth = !double.IsInfinity(availableSize.Width) && !double.IsNaN(availableSize.Width);
        var hasHeight = !double.IsInfinity(availableSize.Height) && !double.IsNaN(availableSize.Height);

        if (hasWidth && hasHeight)
        {
            var width = availableSize.Width;
            var height = availableSize.Height;
            if (width / height > aspectRatio)
            {
                width = height * aspectRatio;
            }
            else
            {
                height = width / aspectRatio;
            }

            return new Size(width, height);
        }

        if (hasWidth)
        {
            return new Size(availableSize.Width, availableSize.Width / aspectRatio);
        }

        if (hasHeight)
        {
            return new Size(availableSize.Height * aspectRatio, availableSize.Height);
        }

        return default;
    }
}
