using Alliance.Client.Shared.Models;

namespace Alliance.Client.Features.Video;

public sealed record VideoStatusSnapshot(
    ConnectionState State,
    string StatusText,
    string MetricsText,
    string ResolutionText,
    DateTimeOffset? LastPacketAt,
    DateTimeOffset? LastFrameAt,
    long FrameVersion)
{
    public static VideoStatusSnapshot Empty(int width, int height)
    {
        return new VideoStatusSnapshot(
            ConnectionState.NotConnected,
            "WAITING FOR STREAM",
            "0.0 fps | 0 frames",
            $"{width}x{height}",
            null,
            null,
            0);
    }
}
