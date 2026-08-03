using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Alliance.Client.Shared.Controls;

public class GridOverlay : Control
{
    public static readonly StyledProperty<double> ImageAspectRatioProperty =
        AvaloniaProperty.Register<GridOverlay, double>(nameof(ImageAspectRatio), 4.0 / 3.0);

    public static readonly StyledProperty<int> GridDensityProperty =
        AvaloniaProperty.Register<GridOverlay, int>(nameof(GridDensity), 9);

    public static readonly StyledProperty<double> LineOpacityProperty =
        AvaloniaProperty.Register<GridOverlay, double>(nameof(LineOpacity), 0.35);

    public double ImageAspectRatio
    {
        get => GetValue(ImageAspectRatioProperty);
        set => SetValue(ImageAspectRatioProperty, value);
    }

    public int GridDensity
    {
        get => GetValue(GridDensityProperty);
        set => SetValue(GridDensityProperty, value);
    }

    public double LineOpacity
    {
        get => GetValue(LineOpacityProperty);
        set => SetValue(LineOpacityProperty, value);
    }

    static GridOverlay()
    {
        AffectsRender<GridOverlay>(
            ImageAspectRatioProperty,
            GridDensityProperty,
            LineOpacityProperty);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var density = GridDensity;
        if (density <= 0)
            return;

        if (double.IsNaN(Bounds.Width) || double.IsNaN(Bounds.Height) ||
            Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        var imageAspect = ImageAspectRatio;
        if (double.IsNaN(imageAspect) || imageAspect <= 0)
            return;

        var containerWidth = Bounds.Width;
        var containerHeight = Bounds.Height;
        var containerAspect = containerWidth / containerHeight;

        double drawWidth, drawHeight, drawX, drawY;

        if (containerAspect > imageAspect)
        {
            drawHeight = containerHeight;
            drawWidth = drawHeight * imageAspect;
            drawX = (containerWidth - drawWidth) / 2.0;
            drawY = 0;
        }
        else
        {
            drawWidth = containerWidth;
            drawHeight = drawWidth / imageAspect;
            drawX = 0;
            drawY = (containerHeight - drawHeight) / 2.0;
        }

        var lineColor = new Color(255, 42, 57, 68);
        var brush = new SolidColorBrush(lineColor, LineOpacity);
        var pen = new Pen(brush);

        double cellWidth = drawWidth / density;
        double cellHeight = drawHeight / density;

        for (int i = 0; i <= density; i++)
        {
            double x = drawX + i * cellWidth;
            context.DrawLine(pen, new Point(x, drawY), new Point(x, drawY + drawHeight));
        }

        for (int i = 0; i <= density; i++)
        {
            double y = drawY + i * cellHeight;
            context.DrawLine(pen, new Point(drawX, y), new Point(drawX + drawWidth, y));
        }
    }
}
