using System.IO.MemoryMappedFiles;
using Alliance.Video.Common;

namespace Alliance.Client.Features.Video;

public sealed class SharedFrameReader : IDisposable
{
    private readonly SharedFrameLayout _layout;
    private readonly MemoryMappedFile _memoryMappedFile;
    private readonly MemoryMappedViewAccessor _accessor;
    private readonly byte[] _slotHeaderBuffer = new byte[VideoConstants.FrameHeaderSize];
    private readonly byte[] _frameBuffer;

    public SharedFrameReader(string sharedMemoryPath, int width, int height)
    {
        _layout = new SharedFrameLayout(width, height);
        _frameBuffer = new byte[_layout.FrameBytes];
        var stream = new FileStream(sharedMemoryPath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
        _memoryMappedFile = MemoryMappedFile.CreateFromFile(stream, null, _layout.TotalBytes, MemoryMappedFileAccess.ReadWrite, HandleInheritability.None, leaveOpen: false);
        _accessor = _memoryMappedFile.CreateViewAccessor(0, _layout.TotalBytes, MemoryMappedFileAccess.Read);
    }

    public bool TryReadLatestFrame(out ReadOnlyMemory<byte> frame, out long version, out long timestampUnixMs)
    {
        frame = default;
        version = 0;
        timestampUnixMs = 0;

        if (!TryGetLatestFrameInfo(out var info))
        {
            return false;
        }

        return TryReadFrame(info, out frame, out version, out timestampUnixMs);
    }

    internal bool TryGetLatestFrameInfo(out SharedFrameInfo info)
    {
        info = default;

        SharedFrameSlotHeader? latest = null;
        var latestSlotIndex = -1;
        for (var slotIndex = 0; slotIndex < VideoConstants.SharedBufferSlots; slotIndex++)
        {
            _accessor.ReadArray(_layout.GetSlotOffset(slotIndex), _slotHeaderBuffer, 0, _slotHeaderBuffer.Length);
            var header = SharedFrameLayout.ReadSlotHeader(_slotHeaderBuffer);
            if (!IsReadableHeader(header))
            {
                continue;
            }

            if (latest is null || header.FrameNumber > latest.Value.FrameNumber)
            {
                latest = header;
                latestSlotIndex = slotIndex;
            }
        }

        if (latest is null || latestSlotIndex < 0)
        {
            return false;
        }

        info = new SharedFrameInfo(
            latestSlotIndex,
            latest.Value.Version,
            latest.Value.FrameNumber,
            latest.Value.FrameBytes,
            latest.Value.TimestampUnixMs);
        return true;
    }

    internal bool TryReadFrame(
        SharedFrameInfo info,
        out ReadOnlyMemory<byte> frame,
        out long version,
        out long timestampUnixMs)
    {
        frame = default;
        version = 0;
        timestampUnixMs = 0;

        if (info.SlotIndex < 0 || info.SlotIndex >= VideoConstants.SharedBufferSlots ||
            info.FrameBytes <= 0 || info.FrameBytes > _layout.FrameBytes)
        {
            return false;
        }

        _accessor.ReadArray(_layout.GetFrameDataOffset(info.SlotIndex), _frameBuffer, 0, info.FrameBytes);
        _accessor.ReadArray(_layout.GetSlotOffset(info.SlotIndex), _slotHeaderBuffer, 0, _slotHeaderBuffer.Length);
        var confirm = SharedFrameLayout.ReadSlotHeader(_slotHeaderBuffer);
        if (!IsReadableHeader(confirm) ||
            confirm.Version != info.Version ||
            confirm.FrameNumber != info.FrameNumber ||
            confirm.FrameBytes != info.FrameBytes)
        {
            return false;
        }

        frame = _frameBuffer.AsMemory(0, confirm.FrameBytes);
        version = confirm.Version;
        timestampUnixMs = confirm.TimestampUnixMs;
        return true;
    }

    private bool IsReadableHeader(SharedFrameSlotHeader header)
    {
        return header.Stable &&
               header.Width == _layout.Width &&
               header.Height == _layout.Height &&
               header.Stride == _layout.Stride &&
               header.PixelFormat == VideoConstants.PixelFormatBgra32 &&
               header.FrameBytes > 0 &&
               header.FrameBytes <= _layout.FrameBytes;
    }

    public void Dispose()
    {
        _accessor.Dispose();
        _memoryMappedFile.Dispose();
    }
}

internal readonly record struct SharedFrameInfo(
    int SlotIndex,
    long Version,
    long FrameNumber,
    int FrameBytes,
    long TimestampUnixMs);
