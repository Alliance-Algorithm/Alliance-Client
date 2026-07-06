using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;

namespace Alliance.Client.Features.Video.Decode;

internal unsafe sealed class HevcDecoderAdapter : IDisposable
{
    private const int ExpectedCodecContextExtradataOffset = 72;
    private const int ExpectedCodecContextExtradataSizeOffset = 80;
    private const uint SupportedAvcodecMajorVersion = 62;
    private AVCodecContext* _codecContext;
    private AVFrame* _decodedFrame;
    private AVPacket* _packet;
    private SwsContext* _scaleContext;
    private byte[]? _bgraBuffer;
    private int _bgraStride;
    private bool _disposed;
    private bool _firstFrameLogged;
    private bool _firstDecodedLogged;
    private int _decodedCount;

    private readonly Queue<byte[]> _pendingFrames = new();
    private byte[]? _vps, _sps, _pps;
    private bool _paramsReady;
    private bool _decoderOpened;
    private bool _needIdr = true;
    private int _discardedCount;
    private AVCodec* _codec;

    private const int AV_LOG_QUIET = -8;
    private const int AVERROR_EAGAIN = -11;
    private const int AVERROR_EOF = -541478725;
    private const int SWS_FAST_BILINEAR = 1;
    private const int AV_PIX_FMT_BGRA = 30;
    private const int AV_INPUT_BUFFER_PADDING_SIZE = 64;

    public HevcDecoderAdapter()
    {
        try { av_log_set_level(AV_LOG_QUIET); } catch { }

        VerifyInteropCompatibility();

        _codec = avcodec_find_decoder_by_name("hevc");
        if (_codec is null)
            throw new InvalidOperationException("FFmpeg HEVC decoder is not available.");

        _codecContext = avcodec_alloc_context3(_codec);
        if (_codecContext is null)
            throw new InvalidOperationException("Unable to allocate FFmpeg codec context.");

        _decodedFrame = av_frame_alloc();
        _packet = av_packet_alloc();
        if (_decodedFrame is null || _packet is null)
            throw new InvalidOperationException("Unable to allocate FFmpeg frame buffers.");
    }

    private void EnsureDecoderOpened()
    {
        if (_decoderOpened) return;

        var extradataBuffer = BuildCodecExtradata(_vps!, _sps!, _pps!);
        var extradataSize = extradataBuffer.Length - AV_INPUT_BUFFER_PADDING_SIZE;

        var extradata = av_malloc((ulong)extradataBuffer.Length);
        if (extradata is null)
            throw new InvalidOperationException("Unable to allocate FFmpeg extradata buffer.");

        fixed (byte* p = extradataBuffer)
        {
            Buffer.MemoryCopy(p, extradata, extradataBuffer.Length, extradataBuffer.Length);
        }

        var ctx = (AVCodecContextExt*)_codecContext;
        ctx->extradata = (byte*)extradata;
        ctx->extradata_size = extradataSize;

        ThrowIfNegative(avcodec_open2(_codecContext, _codec, null), "avcodec_open2");
        _decoderOpened = true;
        Console.WriteLine(
            $"[HEVC] Decoder opened with extradata ({extradataSize} bytes + {AV_INPUT_BUFFER_PADDING_SIZE} padding, off={ExpectedCodecContextExtradataOffset}/{ExpectedCodecContextExtradataSizeOffset})");
    }

    public bool TryDecode(byte[] hevcFrame, out WriteableBitmap? bitmap, out string statusText)
    {
        bitmap = null;
        statusText = "Awaiting parameter sets";

        if (!_firstFrameLogged)
        {
            _firstFrameLogged = true;
            Console.WriteLine($"[HEVC] First frame: {hevcFrame.Length} bytes");
        }

        if (!_paramsReady)
        {
            ScanParameterSets(hevcFrame);

            if (_vps is null || _sps is null || _pps is null)
            {
                _pendingFrames.Enqueue(hevcFrame);
                statusText = $"Waiting SPS/PPS (vps={_vps != null}, sps={_sps != null}, pps={_pps != null})";
                return false;
            }

            Console.WriteLine("[HEVC] Parameter sets collected, opening decoder");
            _paramsReady = true;
            EnsureDecoderOpened();
            PrimeDecoderWithPendingFrames();
        }

        if (_needIdr)
        {
            if (ContainsIdr(hevcFrame))
            {
                Console.WriteLine($"[HEVC] IDR found ({_discardedCount} frames discarded before IDR)");
                _needIdr = false;
            }
            else
            {
                _discardedCount++;
                statusText = $"Waiting IDR ({_discardedCount} discarded)";
                return false;
            }
        }

        return DecodeFrame(hevcFrame, out bitmap, out statusText);
    }

    internal static byte[] BuildCodecExtradata(byte[] vps, byte[] sps, byte[] pps)
    {
        var buffer = new byte[vps.Length + sps.Length + pps.Length + AV_INPUT_BUFFER_PADDING_SIZE];
        var offset = 0;

        Array.Copy(vps, 0, buffer, offset, vps.Length);
        offset += vps.Length;
        Array.Copy(sps, 0, buffer, offset, sps.Length);
        offset += sps.Length;
        Array.Copy(pps, 0, buffer, offset, pps.Length);

        return buffer;
    }

    internal static IReadOnlyList<byte[]> SelectFramesStartingFromFirstIdr(IEnumerable<byte[]> frames, out int discardedBeforeIdr)
    {
        var selectedFrames = new List<byte[]>();
        var foundIdr = false;
        discardedBeforeIdr = 0;

        foreach (var frame in frames)
        {
            if (!foundIdr)
            {
                if (!ContainsIdr(frame))
                {
                    discardedBeforeIdr++;
                    continue;
                }

                foundIdr = true;
            }

            selectedFrames.Add(frame);
        }

        return selectedFrames;
    }

    private void ScanParameterSets(byte[] frame)
    {
        ScanForNalType(frame, 32, ref _vps);
        ScanForNalType(frame, 33, ref _sps);
        ScanForNalType(frame, 34, ref _pps);
    }

    private static void ScanForNalType(byte[] data, int targetType, ref byte[]? storage)
    {
        if (storage is not null) return;

        var i = 0;
        while (i <= data.Length - 6)
        {
            if (data[i] == 0 && data[i + 1] == 0 && data[i + 2] == 0 && data[i + 3] == 1)
            {
                var nalType = (data[i + 4] >> 1) & 0x3F;
                if (nalType == targetType)
                {
                    var nalStart = i;
                    var nalEnd = data.Length;
                    for (var j = i + 4; j <= data.Length - 4; j++)
                    {
                        if (data[j] == 0 && data[j + 1] == 0 && data[j + 2] == 0 && data[j + 3] == 1)
                        {
                            nalEnd = j;
                            break;
                        }
                    }

                    var nalUnit = new byte[nalEnd - nalStart];
                    Array.Copy(data, nalStart, nalUnit, 0, nalUnit.Length);
                    Console.WriteLine($"[HEVC] Extracted NAL type {nalType} ({nalUnit.Length} bytes)");
                    storage = nalUnit;
                    return;
                }

                i += 4 + 2;
            }
            else
            {
                i++;
            }
        }
    }

    private static bool ContainsIdr(byte[] data)
    {
        for (var i = 0; i <= data.Length - 6; i++)
        {
            if (data[i] == 0 && data[i + 1] == 0 && data[i + 2] == 0 && data[i + 3] == 1)
            {
                var nalType = (data[i + 4] >> 1) & 0x3F;
                if (nalType == 19 || nalType == 20)
                    return true;
                i += 4;
            }
        }
        return false;
    }

    private void SendFrame(byte[] frame)
    {
        av_packet_unref(_packet);
        av_frame_unref(_decodedFrame);

        ThrowIfNegative(av_new_packet(_packet, frame.Length), "av_new_packet");
        fixed (byte* p = frame)
        {
            Buffer.MemoryCopy(p, _packet->data, frame.Length, frame.Length);
        }
        _packet->stream_index = 0;

        var sendResult = avcodec_send_packet(_codecContext, _packet);
        if (sendResult < 0)
            return;

        while (true)
        {
            var result = avcodec_receive_frame(_codecContext, _decodedFrame);
            if (result == AVERROR_EAGAIN || result == AVERROR_EOF)
                break;
            if (result >= 0)
                _decodedCount++;
        }
    }

    private void PrimeDecoderWithPendingFrames()
    {
        var framesToPrime = SelectFramesStartingFromFirstIdr(_pendingFrames, out var discardedBeforeIdr);
        _pendingFrames.Clear();
        _discardedCount += discardedBeforeIdr;

        if (framesToPrime.Count == 0)
        {
            return;
        }

        if (_needIdr)
        {
            Console.WriteLine($"[HEVC] IDR found in pending frame ({_discardedCount} discarded before IDR)");
            _needIdr = false;
        }

        foreach (var frame in framesToPrime)
        {
            SendFrame(frame);
        }
    }

    private bool DecodeFrame(byte[] hevcFrame, out WriteableBitmap? bitmap, out string statusText)
    {
        bitmap = null;
        statusText = "Awaiting stream";

        av_packet_unref(_packet);
        av_frame_unref(_decodedFrame);

        ThrowIfNegative(av_new_packet(_packet, hevcFrame.Length), "av_new_packet");
        fixed (byte* sourcePtr = hevcFrame)
        {
            Buffer.MemoryCopy(sourcePtr, _packet->data, hevcFrame.Length, hevcFrame.Length);
        }
        _packet->stream_index = 0;

        var sendResult = avcodec_send_packet(_codecContext, _packet);
        if (sendResult < 0)
        {
            statusText = $"Decoder rejected packet (code={sendResult})";
            return false;
        }

        while (true)
        {
            var receiveResult = avcodec_receive_frame(_codecContext, _decodedFrame);
            if (receiveResult == AVERROR_EAGAIN || receiveResult == AVERROR_EOF)
            {
                statusText = "Decoder warming up";
                return false;
            }

            if (receiveResult < 0)
            {
                statusText = $"Decoder receive failed (code={receiveResult})";
                return false;
            }

            _decodedCount++;
            if (!_firstDecodedLogged)
            {
                _firstDecodedLogged = true;
                Console.WriteLine($"[HEVC] First decoded: {_decodedFrame->width}x{_decodedFrame->height}, pixfmt={_decodedFrame->format}");
            }

            bitmap = ConvertFrameToBitmap(_decodedFrame);
            statusText = $"Streaming {_decodedFrame->width}x{_decodedFrame->height}";
            return true;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_scaleContext is not null)
        {
            sws_freeContext(_scaleContext);
            _scaleContext = null;
        }

        if (_decodedFrame is not null)
        {
            var f = _decodedFrame;
            av_frame_free(&f);
            _decodedFrame = null;
        }

        if (_packet is not null)
        {
            var p = _packet;
            av_packet_free(&p);
            _packet = null;
        }

        if (_codecContext is not null)
        {
            var c = _codecContext;
            avcodec_free_context(&c);
            _codecContext = null;
        }
    }

    private WriteableBitmap ConvertFrameToBitmap(AVFrame* frame)
    {
        var width = frame->width;
        var height = frame->height;
        _bgraStride = width * 4;
        var bufferLength = _bgraStride * height;

        if (_bgraBuffer is null || _bgraBuffer.Length != bufferLength)
            _bgraBuffer = new byte[bufferLength];

        fixed (byte* destination = _bgraBuffer)
        {
            byte** dstData = stackalloc byte*[1];
            int* dstStride = stackalloc int[1];
            dstData[0] = destination;
            dstStride[0] = _bgraStride;

            _scaleContext = sws_getCachedContext(
                _scaleContext,
                width, height, frame->format,
                width, height, AV_PIX_FMT_BGRA,
                SWS_FAST_BILINEAR,
                null, null, null);

            if (_scaleContext is null)
                throw new InvalidOperationException("Unable to create FFmpeg scaling context.");

            sws_scale(
                _scaleContext,
                &frame->data0, &frame->linesize0,
                0, height,
                dstData, dstStride);
        }

        var bitmap = new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            Avalonia.Platform.PixelFormat.Bgra8888,
            Avalonia.Platform.AlphaFormat.Opaque);

        using var locked = bitmap.Lock();
        Marshal.Copy(_bgraBuffer, 0, locked.Address, _bgraBuffer.Length);
        return bitmap;
    }

    private static void ThrowIfNegative(int result, string operation)
    {
        if (result < 0)
            throw new InvalidOperationException($"{operation} failed with error code {result}.");
    }

    private static void VerifyInteropCompatibility()
    {
        var codecVersion = avcodec_version();
        var codecMajorVersion = codecVersion >> 16;
        if (codecMajorVersion != SupportedAvcodecMajorVersion)
            throw new InvalidOperationException(
                $"Unsupported libavcodec major version {codecMajorVersion}. Expected {SupportedAvcodecMajorVersion} for the current interop layout.");

        var extradataOff = Marshal.OffsetOf<AVCodecContextExt>(nameof(AVCodecContextExt.extradata)).ToInt64();
        var extradataSizeOff = Marshal.OffsetOf<AVCodecContextExt>(nameof(AVCodecContextExt.extradata_size)).ToInt64();
        if (extradataOff != ExpectedCodecContextExtradataOffset || extradataSizeOff != ExpectedCodecContextExtradataSizeOffset)
            throw new InvalidOperationException(
                $"AVCodecContext overlay mismatch: expected extradata@{ExpectedCodecContextExtradataOffset} extradata_size@{ExpectedCodecContextExtradataSizeOffset}, got extradata@{extradataOff} extradata_size@{extradataSizeOff}");

        var pkt = av_packet_alloc();
        try
        {
            var dataOff = (long)(&pkt->data) - (long)pkt;
            var sizeOff = (long)(&pkt->size) - (long)pkt;
            if (dataOff != 24 || sizeOff != 32)
                throw new InvalidOperationException(
                    $"AVPacket layout mismatch: expected data@24 size@32, got data@{dataOff} size@{sizeOff}");
        }
        finally
        {
            av_packet_free(&pkt);
        }

        var frame = av_frame_alloc();
        try
        {
            var wOff = (long)(&frame->width) - (long)frame;
            var fOff = (long)(&frame->format) - (long)frame;
            if (wOff != 104 || fOff != 116)
                throw new InvalidOperationException(
                    $"AVFrame layout mismatch: expected width@104 format@116, got width@{wOff} format@{fOff}");
        }
        finally
        {
            av_frame_free(&frame);
        }
    }

#pragma warning disable CS0649
    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct AVFrame
    {
        public byte* data0, data1, data2, data3, data4, data5, data6, data7;
        public int linesize0, linesize1, linesize2, linesize3, linesize4, linesize5, linesize6, linesize7;
        public byte** extended_data;
        public int width, height;
        public int nb_samples;
        public int format;
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct AVPacket
    {
        private nint _buf;
        private long _pts;
        private long _dts;
        public byte* data;
        public int size;
        public int stream_index;
        private int _flags;
        private nint _side_data;
        private int _side_data_elems;
        private long _duration;
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct AVCodecContextExt
    {
        // Verified against libavcodec major 62. This overlay is only used to set
        // extradata before avcodec_open2.
        private fixed byte _pad[72];
        public byte* extradata;
        public int extradata_size;
    }
#pragma warning restore CS0649

#pragma warning disable IDE1006
    [DllImport("avcodec", EntryPoint = "avcodec_find_decoder_by_name")]
    private static extern AVCodec* _avcodec_find_decoder_by_name(byte* name);
    private static AVCodec* avcodec_find_decoder_by_name(string name)
    {
        fixed (byte* p = System.Text.Encoding.UTF8.GetBytes(name + "\0"))
            return _avcodec_find_decoder_by_name(p);
    }

    [DllImport("avcodec")] private static extern AVCodecContext* avcodec_alloc_context3(AVCodec* codec);
    [DllImport("avcodec")] private static extern uint avcodec_version();
    [DllImport("avcodec")] private static extern int avcodec_open2(AVCodecContext* ctx, AVCodec* codec, void* options);
    [DllImport("avcodec")] private static extern int avcodec_send_packet(AVCodecContext* ctx, AVPacket* pkt);
    [DllImport("avcodec")] private static extern int avcodec_receive_frame(AVCodecContext* ctx, AVFrame* frame);
    [DllImport("avcodec")] private static extern void avcodec_free_context(AVCodecContext** ctx);

    [DllImport("avutil")] private static extern AVFrame* av_frame_alloc();
    [DllImport("avutil")] private static extern void av_frame_free(AVFrame** frame);
    [DllImport("avutil")] private static extern void av_frame_unref(AVFrame* frame);
    [DllImport("avutil")] private static extern void* av_malloc(ulong size);
    [DllImport("avutil")] private static extern void av_log_set_level(int level);

    [DllImport("avcodec")] private static extern AVPacket* av_packet_alloc();
    [DllImport("avcodec")] private static extern void av_packet_free(AVPacket** pkt);
    [DllImport("avcodec")] private static extern void av_packet_unref(AVPacket* pkt);
    [DllImport("avcodec")] private static extern int av_new_packet(AVPacket* pkt, int size);

    [DllImport("swscale")] private static extern SwsContext* sws_getCachedContext(SwsContext* ctx, int srcW, int srcH, int srcFormat, int dstW, int dstH, int dstFormat, int flags, void* srcFilter, void* dstFilter, double* param);
    [DllImport("swscale")] private static extern int sws_scale(SwsContext* ctx, byte** srcSlice, int* srcStride, int srcSliceY, int srcSliceH, byte** dst, int* dstStride);
    [DllImport("swscale")] private static extern void sws_freeContext(SwsContext* ctx);
#pragma warning restore IDE1006

    private struct AVCodec { }
    private struct AVCodecContext { }
    private struct SwsContext { }
}
