using Alliance.Client.Features.Settings;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Alliance.Client.Features.Video;

public sealed class VideoViewModel : ObservableObject
{
    public VideoViewModel(IVideoStreamService videoStreamService, AppSettings settings)
    {
        Title = "Reserved Video Surface";
        StatusText = videoStreamService.StatusText;
        TransportHint = videoStreamService.TransportDescription;
        PlaceholderMessage = "The stage is live, but no UDP receiver, frame reassembler, or decoder is active in scaffold mode.";
        FrameCounterLabel = "FRAME 0000 | STATIC";
        SourceHint = $"LISTEN {settings.UdpVideo.ListenPort:D4}";
        Footnote = "Future work lands in Features/Video/Udp and Features/Video/Decode without changing the shell layout.";
    }

    public string Title { get; }

    public string StatusText { get; }

    public string TransportHint { get; }

    public string PlaceholderMessage { get; }

    public string FrameCounterLabel { get; }

    public string SourceHint { get; }

    public string Footnote { get; }
}
