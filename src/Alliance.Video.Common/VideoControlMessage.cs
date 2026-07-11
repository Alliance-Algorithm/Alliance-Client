namespace Alliance.Video.Common;

public sealed class VideoControlMessage
{
    public string SharedMemoryPath { get; set; } = string.Empty;

    public string StatusPipeName { get; set; } = string.Empty;

    public int UdpPort { get; set; } = 3334;

    public int FrameWidth { get; set; } = 1920;

    public int FrameHeight { get; set; } = 1080;

    public int ExpectedFps { get; set; } = 60;

    public int PresentFps { get; set; } = 60;

    public int FrameAssemblyTimeoutMs { get; set; } = 50;

    public int HeartbeatIntervalMs { get; set; } = 250;

    public int SignalLostAfterMs { get; set; } = 500;

    public int ClearFrameAfterMs { get; set; } = 2000;
}
