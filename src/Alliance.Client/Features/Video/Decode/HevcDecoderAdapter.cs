using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using FFmpeg.AutoGen;

namespace Alliance.Client.Features.Video.Decode;

internal unsafe sealed class HevcDecoderAdapter : IDisposable
{
    private AVCodecContext* _codecContext;
    private AVFrame* _decodedFrame;
    private AVPacket* _packet;
    private SwsContext* _scaleContext;
    private byte[]? _bgraBuffer;
    private int _bgraStride;
    private bool _disposed;

    public HevcDecoderAdapter()
    {
        ffmpeg.av_log_set_level(ffmpeg.AV_LOG_QUIET);

        var codec = ffmpeg.avcodec_find_decoder(AVCodecID.AV_CODEC_ID_HEVC);
        if (codec is null)
        {
            throw new InvalidOperationException("FFmpeg HEVC decoder is not available.");
        }

        _codecContext = ffmpeg.avcodec_alloc_context3(codec);
        if (_codecContext is null)
        {
            throw new InvalidOperationException("Unable to allocate FFmpeg codec context.");
        }

        ThrowIfNegative(ffmpeg.avcodec_open2(_codecContext, codec, null), "avcodec_open2");

        _decodedFrame = ffmpeg.av_frame_alloc();
        _packet = ffmpeg.av_packet_alloc();
        if (_decodedFrame is null || _packet is null)
        {
            throw new InvalidOperationException("Unable to allocate FFmpeg frame buffers.");
        }
    }

    public bool TryDecode(byte[] hevcFrame, out WriteableBitmap? bitmap, out string statusText)
    {
        bitmap = null;
        statusText = "Awaiting stream";

        ffmpeg.av_packet_unref(_packet);
        ffmpeg.av_frame_unref(_decodedFrame);

        ThrowIfNegative(ffmpeg.av_new_packet(_packet, hevcFrame.Length), "av_new_packet");
        fixed (byte* sourcePtr = hevcFrame)
        {
            Buffer.MemoryCopy(sourcePtr, _packet->data, hevcFrame.Length, hevcFrame.Length);
        }

        var sendResult = ffmpeg.avcodec_send_packet(_codecContext, _packet);
        if (sendResult < 0)
        {
            statusText = $"Decoder rejected packet ({sendResult})";
            return false;
        }

        var receiveResult = ffmpeg.avcodec_receive_frame(_codecContext, _decodedFrame);
        if (receiveResult == ffmpeg.AVERROR(ffmpeg.EAGAIN) || receiveResult == ffmpeg.AVERROR_EOF)
        {
            statusText = "Decoder warming up";
            return false;
        }

        if (receiveResult < 0)
        {
            statusText = $"Decoder receive failed ({receiveResult})";
            return false;
        }

        bitmap = ConvertFrameToBitmap(_decodedFrame);
        statusText = $"Streaming {_decodedFrame->width}x{_decodedFrame->height}";
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_scaleContext is not null)
        {
            ffmpeg.sws_freeContext(_scaleContext);
            _scaleContext = null;
        }

        if (_decodedFrame is not null)
        {
            var decodedFrame = _decodedFrame;
            ffmpeg.av_frame_free(&decodedFrame);
            _decodedFrame = null;
        }

        if (_packet is not null)
        {
            var packet = _packet;
            ffmpeg.av_packet_free(&packet);
            _packet = null;
        }

        if (_codecContext is not null)
        {
            var codecContext = _codecContext;
            ffmpeg.avcodec_free_context(&codecContext);
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
        {
            _bgraBuffer = new byte[bufferLength];
        }

        fixed (byte* destination = _bgraBuffer)
        {
            byte_ptrArray4 destinationData = default;
            int_array4 destinationLinesize = default;
            destinationData[0] = destination;
            destinationLinesize[0] = _bgraStride;

            _scaleContext = ffmpeg.sws_getCachedContext(
                _scaleContext,
                width,
                height,
                (AVPixelFormat)frame->format,
                width,
                height,
                AVPixelFormat.AV_PIX_FMT_BGRA,
                ffmpeg.SWS_FAST_BILINEAR,
                null,
                null,
                null);

            if (_scaleContext is null)
            {
                throw new InvalidOperationException("Unable to create FFmpeg scaling context.");
            }

            ffmpeg.sws_scale(
                _scaleContext,
                frame->data,
                frame->linesize,
                0,
                height,
                destinationData,
                destinationLinesize);
        }

        var bitmap = new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            Avalonia.Platform.PixelFormat.Bgra8888,
            Avalonia.Platform.AlphaFormat.Opaque);

        using var lockedFrame = bitmap.Lock();
        Marshal.Copy(_bgraBuffer, 0, lockedFrame.Address, _bgraBuffer.Length);
        return bitmap;
    }

    private static void ThrowIfNegative(int result, string operation)
    {
        if (result < 0)
        {
            throw new InvalidOperationException($"{operation} failed with error code {result}.");
        }
    }
}
