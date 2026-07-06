using System.Buffers.Binary;

namespace Alliance.Client.Features.Video.Udp;

internal sealed class HevcFrameAssembler
{
    private const int HeaderSize = 8;
    private const int MaxFrameSizeBytes = 4 * 1024 * 1024;
    private static readonly TimeSpan FrameTimeout = TimeSpan.FromSeconds(1);

    private readonly Dictionary<ushort, PartialFrame> _frames = new();

    public bool TryAddPacket(
        ReadOnlySpan<byte> datagram,
        out byte[]? completedFrame,
        out string? note)
    {
        completedFrame = null;
        note = null;

        PurgeExpiredFrames(DateTimeOffset.UtcNow);

        if (datagram.Length <= HeaderSize)
        {
            note = "Ignoring short UDP packet";
            return false;
        }

        var frameId = BinaryPrimitives.ReadUInt16BigEndian(datagram);
        var segmentIndex = BinaryPrimitives.ReadUInt16BigEndian(datagram[2..]);
        var totalFrameBytes = BinaryPrimitives.ReadUInt32BigEndian(datagram[4..]);
        var payload = datagram[HeaderSize..].ToArray();

        if (totalFrameBytes == 0 || totalFrameBytes > MaxFrameSizeBytes)
        {
            note = $"Dropping oversized frame {frameId}";
            _frames.Remove(frameId);
            return false;
        }

        if (!_frames.TryGetValue(frameId, out var partialFrame))
        {
            partialFrame = new PartialFrame(totalFrameBytes);
            _frames[frameId] = partialFrame;
        }

        if (partialFrame.TotalFrameBytes != totalFrameBytes)
        {
            _frames.Remove(frameId);
            note = $"Reset frame {frameId} because total size changed";
            return false;
        }

        if (!partialFrame.Segments.TryAdd(segmentIndex, payload))
        {
            note = $"Duplicate segment {segmentIndex} for frame {frameId}";
            return false;
        }

        partialFrame.TotalPayloadBytes += payload.Length;
        partialFrame.LastUpdatedUtc = DateTimeOffset.UtcNow;

        if (partialFrame.TotalPayloadBytes < partialFrame.TotalFrameBytes)
        {
            return false;
        }

        var orderedSegments = partialFrame.Segments.OrderBy(pair => pair.Key).ToArray();
        if (orderedSegments.Length == 0 || orderedSegments[0].Key != 0)
        {
            _frames.Remove(frameId);
            note = $"Dropping frame {frameId} because segment 0 is missing";
            return false;
        }

        for (var index = 1; index < orderedSegments.Length; index++)
        {
            if (orderedSegments[index].Key != orderedSegments[index - 1].Key + 1)
            {
                return false;
            }
        }

        if (partialFrame.TotalPayloadBytes != partialFrame.TotalFrameBytes)
        {
            _frames.Remove(frameId);
            note = $"Dropping frame {frameId} because payload length mismatched";
            return false;
        }

        completedFrame = new byte[partialFrame.TotalFrameBytes];
        var offset = 0;
        foreach (var segment in orderedSegments)
        {
            Buffer.BlockCopy(segment.Value, 0, completedFrame, offset, segment.Value.Length);
            offset += segment.Value.Length;
        }

        _frames.Remove(frameId);
        return true;
    }

    private void PurgeExpiredFrames(DateTimeOffset now)
    {
        var expired = _frames
            .Where(pair => now - pair.Value.LastUpdatedUtc > FrameTimeout)
            .Select(pair => pair.Key)
            .ToArray();

        foreach (var frameId in expired)
        {
            _frames.Remove(frameId);
        }
    }

    private sealed class PartialFrame
    {
        public PartialFrame(uint totalFrameBytes)
        {
            TotalFrameBytes = totalFrameBytes;
            LastUpdatedUtc = DateTimeOffset.UtcNow;
        }

        public uint TotalFrameBytes { get; }

        public int TotalPayloadBytes { get; set; }

        public DateTimeOffset LastUpdatedUtc { get; set; }

        public Dictionary<ushort, byte[]> Segments { get; } = new();
    }
}
