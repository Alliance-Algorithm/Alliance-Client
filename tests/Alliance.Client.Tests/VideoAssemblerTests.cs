using System.Buffers.Binary;
using System.Text;
using Alliance.Client.Features.Video.Decode;
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
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(0, 2), 1);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(2, 2), 0);
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(4, 4), 4_194_305);

        Assert.False(assembler.TryAddPacket(packet, out _, out var note));
        Assert.Contains("oversized", note, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HevcDecoderAdapter_BuildCodecExtradata_Appends_ZeroPadding()
    {
        var vps = BuildNal(32, 0xAA, 0xBB);
        var sps = BuildNal(33, 0xCC, 0xDD, 0xEE);
        var pps = BuildNal(34, 0xF0);

        var extradata = HevcDecoderAdapter.BuildCodecExtradata(vps, sps, pps);
        var payloadLength = vps.Length + sps.Length + pps.Length;

        Assert.Equal(payloadLength + 64, extradata.Length);
        Assert.Equal(vps, extradata[..vps.Length]);
        Assert.Equal(sps, extradata[vps.Length..(vps.Length + sps.Length)]);
        Assert.Equal(pps, extradata[(vps.Length + sps.Length)..payloadLength]);
        Assert.All(extradata[payloadLength..], b => Assert.Equal(0, b));
    }

    [Fact]
    public void HevcDecoderAdapter_SelectFramesStartingFromFirstIdr_Skips_Leading_NonIdr_Frames()
    {
        var leading = BuildNal(1, 0x10, 0x11);
        var idr = BuildNal(19, 0x20, 0x21);
        var trailing = BuildNal(1, 0x30, 0x31);

        var selected = HevcDecoderAdapter.SelectFramesStartingFromFirstIdr([leading, idr, trailing], out var discardedBeforeIdr);

        Assert.Equal(1, discardedBeforeIdr);
        Assert.Equal(2, selected.Count);
        Assert.Same(idr, selected[0]);
        Assert.Same(trailing, selected[1]);
    }

    private static byte[] BuildPacket(ushort frameId, ushort segmentIndex, uint totalSize, string payload)
    {
        var data = Encoding.ASCII.GetBytes(payload);
        var packet = new byte[8 + data.Length];
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(0, 2), frameId);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(2, 2), segmentIndex);
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(4, 4), totalSize);
        Buffer.BlockCopy(data, 0, packet, 8, data.Length);
        return packet;
    }

    private static byte[] BuildNal(int nalType, params byte[] payload)
    {
        var nal = new byte[6 + payload.Length];
        nal[3] = 1;
        nal[4] = (byte)(nalType << 1);
        nal[5] = 1;
        Buffer.BlockCopy(payload, 0, nal, 6, payload.Length);
        return nal;
    }
}
