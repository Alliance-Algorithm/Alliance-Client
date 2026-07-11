using System.Buffers.Binary;

namespace Alliance.Video.Common;

public sealed class SharedFrameLayout
{
    public SharedFrameLayout(int width, int height)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "Frame width must be greater than 0.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), height, "Frame height must be greater than 0.");
        }

        Width = width;
        Height = height;
        Stride = checked(width * 4);
        FrameBytes = checked(Stride * height);
        SlotSize = checked(VideoConstants.FrameHeaderSize + FrameBytes);
        TotalBytes = checked(VideoConstants.SharedHeaderSize + (SlotSize * VideoConstants.SharedBufferSlots));
    }

    public int Width { get; }

    public int Height { get; }

    public int Stride { get; }

    public int FrameBytes { get; }

    public int SlotSize { get; }

    public int TotalBytes { get; }

    public int GetSlotOffset(int slotIndex)
    {
        return VideoConstants.SharedHeaderSize + (slotIndex * SlotSize);
    }

    public int GetFrameDataOffset(int slotIndex)
    {
        return GetSlotOffset(slotIndex) + VideoConstants.FrameHeaderSize;
    }

    public static void WriteSharedHeader(Span<byte> span, int width, int height, int stride)
    {
        BinaryPrimitives.WriteInt32LittleEndian(span[0..4], VideoConstants.Magic);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..8], width);
        BinaryPrimitives.WriteInt32LittleEndian(span[8..12], height);
        BinaryPrimitives.WriteInt32LittleEndian(span[12..16], stride);
        BinaryPrimitives.WriteInt32LittleEndian(span[16..20], VideoConstants.PixelFormatBgra32);
        BinaryPrimitives.WriteInt32LittleEndian(span[20..24], VideoConstants.SharedBufferSlots);
    }

    public static bool TryReadSharedHeader(ReadOnlySpan<byte> span, out int width, out int height, out int stride)
    {
        width = 0;
        height = 0;
        stride = 0;

        if (span.Length < VideoConstants.SharedHeaderSize)
        {
            return false;
        }

        if (BinaryPrimitives.ReadInt32LittleEndian(span[0..4]) != VideoConstants.Magic)
        {
            return false;
        }

        width = BinaryPrimitives.ReadInt32LittleEndian(span[4..8]);
        height = BinaryPrimitives.ReadInt32LittleEndian(span[8..12]);
        stride = BinaryPrimitives.ReadInt32LittleEndian(span[12..16]);
        return width > 0 && height > 0 && stride > 0;
    }

    public static void WriteSlotHeader(
        Span<byte> span,
        long version,
        long frameNumber,
        int width,
        int height,
        int stride,
        int frameBytes,
        long timestampUnixMs,
        bool stable)
    {
        BinaryPrimitives.WriteInt64LittleEndian(span[0..8], version);
        BinaryPrimitives.WriteInt64LittleEndian(span[8..16], frameNumber);
        BinaryPrimitives.WriteInt32LittleEndian(span[16..20], width);
        BinaryPrimitives.WriteInt32LittleEndian(span[20..24], height);
        BinaryPrimitives.WriteInt32LittleEndian(span[24..28], stride);
        BinaryPrimitives.WriteInt32LittleEndian(span[28..32], frameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(span[32..40], timestampUnixMs);
        BinaryPrimitives.WriteInt32LittleEndian(span[40..44], stable ? 1 : 0);
        BinaryPrimitives.WriteInt32LittleEndian(span[44..48], VideoConstants.PixelFormatBgra32);
    }

    public static SharedFrameSlotHeader ReadSlotHeader(ReadOnlySpan<byte> span)
    {
        return new SharedFrameSlotHeader(
            BinaryPrimitives.ReadInt64LittleEndian(span[0..8]),
            BinaryPrimitives.ReadInt64LittleEndian(span[8..16]),
            BinaryPrimitives.ReadInt32LittleEndian(span[16..20]),
            BinaryPrimitives.ReadInt32LittleEndian(span[20..24]),
            BinaryPrimitives.ReadInt32LittleEndian(span[24..28]),
            BinaryPrimitives.ReadInt32LittleEndian(span[28..32]),
            BinaryPrimitives.ReadInt64LittleEndian(span[32..40]),
            BinaryPrimitives.ReadInt32LittleEndian(span[40..44]) == 1,
            BinaryPrimitives.ReadInt32LittleEndian(span[44..48]));
    }
}

public readonly record struct SharedFrameSlotHeader(
    long Version,
    long FrameNumber,
    int Width,
    int Height,
    int Stride,
    int FrameBytes,
    long TimestampUnixMs,
    bool Stable,
    int PixelFormat);
