namespace Alliance.Video.Common;

public static class VideoConstants
{
    public const int SharedBufferSlots = 3;
    public const int FrameHeaderSize = 64;
    public const int SharedHeaderSize = 128;
    public const int MaxUdpPayloadBytes = 1392;
    public const int Magic = 0x56494430; // VID0
    public const int PixelFormatBgra32 = 1;
}
