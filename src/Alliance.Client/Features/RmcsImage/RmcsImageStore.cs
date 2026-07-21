using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Alliance.Client.Features.RmcsImage;

public sealed class RmcsImageStore : ObservableObject
{
    private WriteableBitmap? _composedImage;
    private readonly RmcsPipelineProgress _pipelineProgress = new();

    public RmcsImageStore()
    {
    }

    public WriteableBitmap? ComposedImage
    {
        get => _composedImage;
        set => SetProperty(ref _composedImage, value);
    }

    public RmcsPipelineProgress PipelineProgress => _pipelineProgress;
}
