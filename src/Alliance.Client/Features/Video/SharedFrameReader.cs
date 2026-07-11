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

        SharedFrameSlotHeader? latest = null;
        var latestSlotIndex = -1;
        for (var slotIndex = 0; slotIndex < VideoConstants.SharedBufferSlots; slotIndex++)
        {
            _accessor.ReadArray(_layout.GetSlotOffset(slotIndex), _slotHeaderBuffer, 0, _slotHeaderBuffer.Length);
            var header = SharedFrameLayout.ReadSlotHeader(_slotHeaderBuffer);
            if (!header.Stable || header.FrameBytes <= 0 || header.FrameBytes > _layout.FrameBytes)
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

        _accessor.ReadArray(_layout.GetFrameDataOffset(latestSlotIndex), _frameBuffer, 0, latest.Value.FrameBytes);
        _accessor.ReadArray(_layout.GetSlotOffset(latestSlotIndex), _slotHeaderBuffer, 0, _slotHeaderBuffer.Length);
        var confirm = SharedFrameLayout.ReadSlotHeader(_slotHeaderBuffer);
        if (!confirm.Stable || confirm.Version != latest.Value.Version || confirm.FrameNumber != latest.Value.FrameNumber)
        {
            return false;
        }

        frame = _frameBuffer.AsMemory(0, confirm.FrameBytes);
        version = confirm.Version;
        timestampUnixMs = confirm.TimestampUnixMs;
        return true;
    }

    public void Dispose()
    {
        _accessor.Dispose();
        _memoryMappedFile.Dispose();
    }
}
