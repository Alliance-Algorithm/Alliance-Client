using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace Alliance.Client.Features.Video;

public sealed class VideoFeedControl : global::Avalonia.Controls.Control
{
    public static readonly StyledProperty<VideoStreamStore?> StoreProperty =
        AvaloniaProperty.Register<VideoFeedControl, VideoStreamStore?>(nameof(Store));

    private VideoStreamStore? _subscribedStore;

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
            Subscribe(change.GetNewValue<VideoStreamStore?>());
            InvalidateVisual();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Subscribe(null);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Subscribe(Store);
    }

    private void Subscribe(VideoStreamStore? store)
    {
        if (ReferenceEquals(_subscribedStore, store))
        {
            return;
        }

        if (_subscribedStore is not null)
        {
            _subscribedStore.FrameUpdated -= OnFrameUpdated;
        }

        _subscribedStore = store;

        if (_subscribedStore is not null)
        {
            _subscribedStore.FrameUpdated += OnFrameUpdated;
        }
    }

    private void OnFrameUpdated()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            InvalidateVisual();
        }
        else
        {
            Dispatcher.UIThread.Post(InvalidateVisual);
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
