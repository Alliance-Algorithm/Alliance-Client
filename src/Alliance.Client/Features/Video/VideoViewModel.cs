using System.ComponentModel;
using Avalonia.Media;
using Alliance.Client.Features.Settings;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Alliance.Client.Features.Video;

public sealed class VideoViewModel : ObservableObject
{
    private readonly IVideoStreamService _videoStreamService;
    private IImage? _currentFrame;
    private string _statusText;
    private string _transportHint;
    private string _placeholderMessage;
    private string _frameCounterLabel;

    public VideoViewModel(IVideoStreamService videoStreamService, AppSettings settings)
    {
        _videoStreamService = videoStreamService;
        _videoStreamService.PropertyChanged += HandleVideoStreamChanged;

        Title = "Live Video";
        SourceHint = $"LISTEN {settings.UdpVideo.ListenPort:D4}";
        Footnote = $"HEVC decode path active for UDP {settings.UdpVideo.ListenPort:D4}.";

        _currentFrame = _videoStreamService.CurrentFrame;
        _statusText = _videoStreamService.StatusText;
        _transportHint = _videoStreamService.TransportDescription;
        _placeholderMessage = BuildPlaceholderMessage();
        _frameCounterLabel = _videoStreamService.FrameCounterLabel;
    }

    public string Title { get; }

    public IImage? CurrentFrame
    {
        get => _currentFrame;
        private set => SetProperty(ref _currentFrame, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string TransportHint
    {
        get => _transportHint;
        private set => SetProperty(ref _transportHint, value);
    }

    public string PlaceholderMessage
    {
        get => _placeholderMessage;
        private set => SetProperty(ref _placeholderMessage, value);
    }

    public string FrameCounterLabel
    {
        get => _frameCounterLabel;
        private set => SetProperty(ref _frameCounterLabel, value);
    }

    public string SourceHint { get; }

    public string Footnote { get; }

    private void HandleVideoStreamChanged(object? sender, PropertyChangedEventArgs args)
    {
        CurrentFrame = _videoStreamService.CurrentFrame;
        StatusText = _videoStreamService.StatusText;
        TransportHint = _videoStreamService.TransportDescription;
        FrameCounterLabel = _videoStreamService.FrameCounterLabel;
        PlaceholderMessage = BuildPlaceholderMessage();
    }

    private string BuildPlaceholderMessage()
    {
        return CurrentFrame is null
            ? _videoStreamService.StatusText
            : string.Empty;
    }
}
