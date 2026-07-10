using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Alliance.Client.Features.Video;

public sealed class VideoFeedControl : global::Avalonia.Controls.Control
{
    public static readonly StyledProperty<VideoStreamStore?> StoreProperty =
        AvaloniaProperty.Register<VideoFeedControl, VideoStreamStore?>(nameof(Store));

    public VideoStreamStore? Store
    {
        get => GetValue(StoreProperty);
        set => SetValue(StoreProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == StoreProperty)
        {
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = Bounds;
        context.FillRectangle(new SolidColorBrush(Color.Parse("#060A0D")), bounds);

        if (Store?.Surface is null)
        {
            return;
        }

        context.DrawImage(Store.Surface, bounds);
    }
}
