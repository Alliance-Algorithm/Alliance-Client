using System.Buffers.Binary;
using System.Text;
using Alliance.Client.Features.Video.Udp;

namespace Alliance.Client.Tests;

public sealed class VideoAssemblerTests
{
    [Fact]
    public void HevcFrameAssembler_Reassembles_OutOfOrder_Segments()
    {
        var assembler = new HevcFrameAssembler();
        var totalSize = 10u;
        var firstSegment = BuildPacket(7, 1, totalSize, "WORLD");
        var secondSegment = BuildPacket(7, 0, totalSize, "HELLO");

        Assert.False(assembler.TryAddPacket(firstSegment, out var pendingFrame, out _));
        Assert.Null(pendingFrame);

        Assert.True(assembler.TryAddPacket(secondSegment, out var completedFrame, out _));
        Assert.NotNull(completedFrame);
        Assert.Equal("HELLOWORLD", Encoding.ASCII.GetString(completedFrame!));
    }

    [Fact]
    public void HevcFrameAssembler_Rejects_Oversized_Frames()
    {
        var assembler = new HevcFrameAssembler();
        var packet = new byte[16];
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(0, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2, 2), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4, 4), 4_194_305);

        Assert.False(assembler.TryAddPacket(packet, out _, out var note));
        Assert.Contains("oversized", note, StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] BuildPacket(ushort frameId, ushort segmentIndex, uint totalSize, string payload)
    {
        var data = Encoding.ASCII.GetBytes(payload);
        var packet = new byte[8 + data.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(0, 2), frameId);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2, 2), segmentIndex);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4, 4), totalSize);
        Buffer.BlockCopy(data, 0, packet, 8, data.Length);
        return packet;
    }
}
