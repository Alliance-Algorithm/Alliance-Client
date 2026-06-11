using Alliance.Client.Features.Settings;
using Alliance.Client.Shared.Models;

namespace Alliance.Client.Features.Video;

public sealed class DesignTimeVideoStreamService : IVideoStreamService
{
    public DesignTimeVideoStreamService(AppSettings settings)
    {
        TransportDescription = $"UDP {settings.UdpVideo.ListenPort} / {settings.UdpVideo.Codec.ToUpperInvariant()} reserved";
    }

    public ConnectionState State => ConnectionState.NotConnected;

    public string StatusText => "No Stream";

    public string TransportDescription { get; }
}
