using CommunityToolkit.Mvvm.ComponentModel;

namespace Alliance.Client.Features.RmcsImage;

public sealed class ImageWindowViewModel : ObservableObject
{
    private int _gridDensity = 9;
    private double _lineOpacity = 0.35;

    public ImageWindowViewModel(RmcsImageStore imageStore)
    {
        ImageStore = imageStore;
        imageStore.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(RmcsImageStore.ComposedImage))
                OnPropertyChanged(nameof(ImageAspectRatio));
        };
    }

    public RmcsImageStore ImageStore { get; }

    public int GridDensity
    {
        get => _gridDensity;
        set => SetProperty(ref _gridDensity, value);
    }

    public double LineOpacity
    {
        get => _lineOpacity;
        set => SetProperty(ref _lineOpacity, value);
    }

    public double ImageAspectRatio
    {
        get
        {
            var bmp = ImageStore.ComposedImage;
            if (bmp is null || bmp.PixelSize.Height <= 0)
                return 4.0 / 3.0;
            return (double)bmp.PixelSize.Width / bmp.PixelSize.Height;
        }
    }
}
