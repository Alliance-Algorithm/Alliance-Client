using CommunityToolkit.Mvvm.ComponentModel;

namespace Alliance.Client.Features.RmcsImage;

public sealed class ImageWindowViewModel : ObservableObject
{
    public ImageWindowViewModel(RmcsImageStore imageStore)
    {
        ImageStore = imageStore;
    }

    public RmcsImageStore ImageStore { get; }
}
