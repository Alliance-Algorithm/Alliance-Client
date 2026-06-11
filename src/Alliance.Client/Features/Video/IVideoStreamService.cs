using Alliance.Client.Shared.Models;

namespace Alliance.Client.Features.Video;

public interface IVideoStreamService
{
    ConnectionState State { get; }

    string StatusText { get; }

    string TransportDescription { get; }
}
