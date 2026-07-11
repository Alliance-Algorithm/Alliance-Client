namespace Alliance.Video.Common;

public sealed class VideoStatusMessage
{
    public int Pid { get; set; }

    public string StreamState { get; set; } = "NotConnected";

    public DateTimeOffset SentAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastPacketAt { get; set; }

    public DateTimeOffset? LastFrameAt { get; set; }

    public long PacketCount { get; set; }

    public long AssembledFrameCount { get; set; }

    public long DecodedFrameCount { get; set; }

    public long PresentedFrameCount { get; set; }

    public long DecodeErrorCount { get; set; }

    public double PresentFps { get; set; }

    public string Note { get; set; } = string.Empty;
}
