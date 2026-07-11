using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Alliance.Video.Common;
using FFmpeg.AutoGen;
using Microsoft.Extensions.Logging;

namespace Alliance.VideoWorker;

internal sealed class WorkerRuntime : IDisposable
{
    private static readonly TimeSpan PipeConnectTimeout = TimeSpan.FromSeconds(5);

    private readonly VideoControlMessage _control;
    private readonly ILogger<WorkerRuntime> _logger;
    private readonly CancellationTokenSource _cts = new();
    private readonly SharedFrameLayout _layout;
    private readonly HevcFrameAssembler _assembler;
    private readonly VideoStatusTracker _status;
    private readonly FfmpegHevcDecoder _decoder;
    private readonly MemoryMappedFile _memoryMappedFile;
    private readonly MemoryMappedViewAccessor _accessor;

    public WorkerRuntime(VideoControlMessage control, ILogger<WorkerRuntime> logger)
    {
        _control = control;
        _logger = logger;
        _layout = new SharedFrameLayout(control.FrameWidth, control.FrameHeight);
        _assembler = new HevcFrameAssembler(control.FrameAssemblyTimeoutMs);
        _status = new VideoStatusTracker();
        _decoder = new FfmpegHevcDecoder(control.FrameWidth, control.FrameHeight);

        var backingStream = new FileStream(control.SharedMemoryPath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
        _memoryMappedFile = MemoryMappedFile.CreateFromFile(
            backingStream,
            null,
            _layout.TotalBytes,
            MemoryMappedFileAccess.ReadWrite,
            HandleInheritability.None,
            leaveOpen: false);
        _accessor = _memoryMappedFile.CreateViewAccessor(0, _layout.TotalBytes, MemoryMappedFileAccess.ReadWrite);

        var header = new byte[VideoConstants.SharedHeaderSize];
        SharedFrameLayout.WriteSharedHeader(header, control.FrameWidth, control.FrameHeight, _layout.Stride);
        _accessor.WriteArray(0, header, 0, header.Length);
    }

    public async Task RunAsync()
    {
        _status.UpdateState("Connecting", "Video worker starting");

        using var pipe = new NamedPipeClientStream(
            ".",
            _control.StatusPipeName,
            PipeDirection.Out,
            PipeOptions.Asynchronous);

        using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token))
        {
            connectCts.CancelAfter(PipeConnectTimeout);
            try
            {
                await pipe.ConnectAsync(connectCts.Token);
            }
            catch (OperationCanceledException) when (!_cts.IsCancellationRequested)
            {
                throw new TimeoutException($"Video worker failed to connect to status pipe '{_control.StatusPipeName}' within {PipeConnectTimeout.TotalSeconds:F0} seconds.");
            }
        }

        using var writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true };

        var heartbeatTask = Task.Run(() => HeartbeatLoopAsync(writer, _cts.Token), _cts.Token);
        var receiveTask = Task.Run(() => ReceiveLoopAsync(_cts.Token), _cts.Token);

        var completed = await Task.WhenAny(heartbeatTask, receiveTask);
        _cts.Cancel();

        try
        {
            await Task.WhenAll(heartbeatTask, receiveTask);
        }
        catch (OperationCanceledException)
        {
        }

        if (completed.IsFaulted)
        {
            await completed;
        }
    }

    private async Task HeartbeatLoopAsync(StreamWriter writer, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_control.HeartbeatIntervalMs));
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            var status = _status.CreateMessage();
            var line = JsonSerializer.Serialize(status, WorkerProtocol.JsonOptions);
            await writer.WriteLineAsync(line);
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(IPAddress.Any, _control.UdpPort));
        socket.ReceiveBufferSize = 4 * 1024 * 1024;

        _status.UpdateState("Ready", $"Listening UDP:{_control.UdpPort}");

        var packetBuffer = ArrayPool<byte>.Shared.Rent(2048);
        long frameNumber = 0;
        var lastDiagnosticsAt = DateTimeOffset.UtcNow;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var received = await socket.ReceiveAsync(packetBuffer, SocketFlags.None, cancellationToken);
                if (received <= 8)
                {
                    continue;
                }

                _status.MarkPacketReceived();

                if (_assembler.TryAddPacket(packetBuffer.AsSpan(0, received), out var frameBytes))
                {
                    _status.MarkFrameAssembled();
                    try
                    {
                        var outputFrames = new List<DecodedFrame>();
                        _decoder.Decode(frameBytes, outputFrames);
                        foreach (var decoded in outputFrames)
                        {
                            frameNumber++;
                            PublishFrame(decoded.Buffer, frameNumber, decoded.TimestampUnixMs);
                            _status.MarkFrameDecoded();
                            _status.MarkFramePresented(decoded.TimestampUnixMs);
                        }
                    }
                    catch (Exception ex)
                    {
                        _status.MarkDecodeError(ex.Message);
                        _logger.LogWarning(ex, "Video decode failed for assembled HEVC frame.");
                    }
                }

                var now = DateTimeOffset.UtcNow;
                if ((now - lastDiagnosticsAt).TotalMilliseconds >= 1000)
                {
                    lastDiagnosticsAt = now;
                    var d = _assembler.Diagnostics;
                    _logger.LogInformation(
                        "[assembler-diag] lastLen={LastLen} lastFrameId={FrameId} lastFragIdx={FragIdx} lastTotalBytes={TotalBytes} lastExpectedFrags={ExpFrags} pending={Pending} | drops: tooShort={TooShort} badTotal={BadTotal} fragOOR={FragOor} totalMismatch={TotalMismatch} offsetOOR={OffsetOor} expired={Expired} completed={Completed}",
                        d.LastPacketLength, d.LastFrameId, d.LastFragmentIndex, d.LastTotalBytes, d.LastExpectedFragments, d.PendingAssemblies,
                        d.DropTooShort, d.DropBadTotalBytes, d.DropFragmentOutOfRange, d.DropTotalMismatch, d.DropOffsetOutOfRange, d.ExpiredAssemblies, d.CompletedFrames);
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(packetBuffer);
        }
    }

    private void PublishFrame(ReadOnlySpan<byte> frame, long frameNumber, long timestampUnixMs)
    {
        var slotIndex = (int)(frameNumber % VideoConstants.SharedBufferSlots);
        var slotHeader = new byte[VideoConstants.FrameHeaderSize];
        var slotOffset = _layout.GetSlotOffset(slotIndex);
        var dataOffset = _layout.GetFrameDataOffset(slotIndex);
        var writeVersion = (frameNumber * 2) + 1;
        var stableVersion = writeVersion + 1;

        SharedFrameLayout.WriteSlotHeader(
            slotHeader,
            writeVersion,
            frameNumber,
            _control.FrameWidth,
            _control.FrameHeight,
            _layout.Stride,
            _layout.FrameBytes,
            timestampUnixMs,
            stable: false);
        _accessor.WriteArray(slotOffset, slotHeader, 0, slotHeader.Length);

        var buffer = frame.ToArray();
        _accessor.WriteArray(dataOffset, buffer, 0, buffer.Length);

        SharedFrameLayout.WriteSlotHeader(
            slotHeader,
            stableVersion,
            frameNumber,
            _control.FrameWidth,
            _control.FrameHeight,
            _layout.Stride,
            _layout.FrameBytes,
            timestampUnixMs,
            stable: true);
        _accessor.WriteArray(slotOffset, slotHeader, 0, slotHeader.Length);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _decoder.Dispose();
        _accessor.Dispose();
        _memoryMappedFile.Dispose();
        _cts.Dispose();
    }
}

internal sealed class VideoStatusTracker
{
    private readonly object _gate = new();
    private string _state = "NotConnected";
    private string _note = "Starting";
    private DateTimeOffset? _lastPacketAt;
    private DateTimeOffset? _lastFrameAt;
    private long _packetCount;
    private long _assembledFrameCount;
    private long _decodedFrameCount;
    private long _presentedFrameCount;
    private long _decodeErrorCount;
    private DateTimeOffset _presentWindowStart = DateTimeOffset.UtcNow;
    private long _presentWindowFrames;
    private double _presentFps;

    public void UpdateState(string state, string note)
    {
        lock (_gate)
        {
            _state = state;
            _note = note;
        }
    }

    public void MarkPacketReceived()
    {
        lock (_gate)
        {
            _packetCount++;
            _lastPacketAt = DateTimeOffset.UtcNow;
        }
    }

    public void MarkFrameAssembled()
    {
        lock (_gate)
        {
            _assembledFrameCount++;
        }
    }

    public void MarkFrameDecoded()
    {
        lock (_gate)
        {
            _decodedFrameCount++;
        }
    }

    public void MarkFramePresented(long timestampUnixMs)
    {
        lock (_gate)
        {
            _presentedFrameCount++;
            _lastFrameAt = DateTimeOffset.FromUnixTimeMilliseconds(timestampUnixMs);
            _presentWindowFrames++;

            var now = DateTimeOffset.UtcNow;
            var elapsedSeconds = (now - _presentWindowStart).TotalSeconds;
            if (elapsedSeconds >= 1)
            {
                _presentFps = _presentWindowFrames / elapsedSeconds;
                _presentWindowFrames = 0;
                _presentWindowStart = now;
            }
        }
    }

    public void MarkDecodeError(string note)
    {
        lock (_gate)
        {
            _decodeErrorCount++;
            _note = note;
            _state = "Degraded";
        }
    }

    public VideoStatusMessage CreateMessage()
    {
        lock (_gate)
        {
            return new VideoStatusMessage
            {
                Pid = Environment.ProcessId,
                StreamState = _state,
                SentAt = DateTimeOffset.UtcNow,
                LastPacketAt = _lastPacketAt,
                LastFrameAt = _lastFrameAt,
                PacketCount = _packetCount,
                AssembledFrameCount = _assembledFrameCount,
                DecodedFrameCount = _decodedFrameCount,
                PresentedFrameCount = _presentedFrameCount,
                DecodeErrorCount = _decodeErrorCount,
                PresentFps = _presentFps,
                Note = _note
            };
        }
    }
}

internal sealed class HevcFrameAssembler
{
    private readonly int _frameAssemblyTimeoutMs;
    private readonly Dictionary<ushort, FrameAssembly> _assemblies = [];
    private readonly AssemblerDiagnostics _diagnostics = new();

    public HevcFrameAssembler(int frameAssemblyTimeoutMs)
    {
        _frameAssemblyTimeoutMs = frameAssemblyTimeoutMs;
    }

    public AssemblerDiagnostics Diagnostics => _diagnostics;

    public bool TryAddPacket(ReadOnlySpan<byte> packet, out byte[] frameBytes)
    {
        frameBytes = Array.Empty<byte>();
        _diagnostics.LastPacketLength = packet.Length;
        if (packet.Length <= 8)
        {
            _diagnostics.DropTooShort++;
            return false;
        }

        CleanupExpired();

        var frameId = (ushort)((packet[0] << 8) | packet[1]);
        var fragmentIndex = (ushort)((packet[2] << 8) | packet[3]);
        var totalBytes = ((uint)packet[4] << 24) | ((uint)packet[5] << 16) | ((uint)packet[6] << 8) | packet[7];
        _diagnostics.LastFrameId = frameId;
        _diagnostics.LastFragmentIndex = fragmentIndex;
        _diagnostics.LastTotalBytes = totalBytes;
        if (totalBytes == 0 || totalBytes > 8 * 1024 * 1024)
        {
            _diagnostics.DropBadTotalBytes++;
            return false;
        }

        var payload = packet[8..];
        var expectedFragments = (int)Math.Ceiling(totalBytes / (double)VideoConstants.MaxUdpPayloadBytes);
        _diagnostics.LastExpectedFragments = expectedFragments;
        if (fragmentIndex >= expectedFragments)
        {
            _diagnostics.DropFragmentOutOfRange++;
            return false;
        }

        if (!_assemblies.TryGetValue(frameId, out var assembly))
        {
            assembly = new FrameAssembly((int)totalBytes, expectedFragments);
            _assemblies[frameId] = assembly;
        }

        if (assembly.TotalBytes != (int)totalBytes)
        {
            _assemblies.Remove(frameId);
            _diagnostics.DropTotalMismatch++;
            return false;
        }

        var offset = fragmentIndex * VideoConstants.MaxUdpPayloadBytes;
        if (offset + payload.Length > assembly.TotalBytes)
        {
            _assemblies.Remove(frameId);
            _diagnostics.DropOffsetOutOfRange++;
            return false;
        }

        if (!assembly.Received[fragmentIndex])
        {
            payload.CopyTo(assembly.Buffer.AsSpan(offset, payload.Length));
            assembly.Received[fragmentIndex] = true;
            assembly.ReceivedCount++;
        }

        _diagnostics.PendingAssemblies = _assemblies.Count;

        if (assembly.ReceivedCount == assembly.ExpectedFragments)
        {
            frameBytes = assembly.Buffer;
            _assemblies.Remove(frameId);
            _diagnostics.CompletedFrames++;
            _diagnostics.PendingAssemblies = _assemblies.Count;
            return true;
        }

        return false;
    }

    private void CleanupExpired()
    {
        if (_assemblies.Count == 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var expired = _assemblies
            .Where(pair => (now - pair.Value.StartedAt).TotalMilliseconds > _frameAssemblyTimeoutMs)
            .Select(pair => pair.Key)
            .ToArray();

        foreach (var key in expired)
        {
            _assemblies.Remove(key);
            _diagnostics.ExpiredAssemblies++;
        }
    }

    private sealed class FrameAssembly
    {
        public FrameAssembly(int totalBytes, int expectedFragments)
        {
            TotalBytes = totalBytes;
            ExpectedFragments = expectedFragments;
            Buffer = new byte[totalBytes];
            Received = new bool[expectedFragments];
            StartedAt = DateTimeOffset.UtcNow;
        }

        public int TotalBytes { get; }

        public int ExpectedFragments { get; }

        public byte[] Buffer { get; }

        public bool[] Received { get; }

        public int ReceivedCount { get; set; }

        public DateTimeOffset StartedAt { get; }
    }
}

internal sealed class AssemblerDiagnostics
{
    public int LastPacketLength;
    public ushort LastFrameId;
    public ushort LastFragmentIndex;
    public uint LastTotalBytes;
    public int LastExpectedFragments;
    public int PendingAssemblies;

    public long DropTooShort;
    public long DropBadTotalBytes;
    public long DropFragmentOutOfRange;
    public long DropTotalMismatch;
    public long DropOffsetOutOfRange;
    public long ExpiredAssemblies;
    public long CompletedFrames;
}

internal static class FfmpegLoader
{
    private const int ExpectedAvcodecMajor = 62;
    private static readonly object Gate = new();
    private static bool _initialized;

    private static readonly string[] CandidateDirectories =
    {
        "/usr/lib",
        "/usr/lib/x86_64-linux-gnu",
        "/usr/lib64",
        "/usr/local/lib"
    };

    public static void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        lock (Gate)
        {
            if (_initialized)
            {
                return;
            }

            var root = ResolveFfmpegRoot();
            ffmpeg.RootPath = root;
            DynamicallyLoadedBindings.Initialize();
            _initialized = true;
        }
    }

    private static string ResolveFfmpegRoot()
    {
        var searched = new List<string>();

        var overridePath = Environment.GetEnvironmentVariable("ALLIANCE_FFMPEG_ROOT");
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            searched.Add(overridePath);
            if (HasMatchingAvcodec(overridePath))
            {
                return overridePath;
            }
        }

        foreach (var dir in CandidateDirectories)
        {
            searched.Add(dir);
            if (HasMatchingAvcodec(dir))
            {
                return dir;
            }
        }

        throw new InvalidOperationException(
            $"FFmpeg runtime not found. Expected libavcodec.so.{ExpectedAvcodecMajor} " +
            $"(FFmpeg 8.x, matching FFmpeg.AutoGen 8.1.0). Searched: {string.Join(", ", searched)}. " +
            $"Found instead: {DescribeFoundVersions()}. " +
            $"Install FFmpeg 8.x or set ALLIANCE_FFMPEG_ROOT to the directory containing libavcodec.so.{ExpectedAvcodecMajor}.");
    }

    private static bool HasMatchingAvcodec(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return false;
        }

        return File.Exists(Path.Combine(directory, $"libavcodec.so.{ExpectedAvcodecMajor}"));
    }

    private static string DescribeFoundVersions()
    {
        var found = new List<string>();
        foreach (var dir in CandidateDirectories)
        {
            if (!Directory.Exists(dir))
            {
                continue;
            }

            foreach (var path in Directory.EnumerateFiles(dir, "libavcodec.so.*"))
            {
                found.Add(Path.GetFileName(path));
            }
        }

        return found.Count == 0 ? "none" : string.Join(", ", found);
    }
}

internal unsafe sealed class FfmpegHevcDecoder : IDisposable
{
    private const int InputBufferPaddingSize = 64;

    private readonly int _width;
    private readonly int _height;
    private readonly AVCodecContext* _codecContext;
    private readonly AVCodecParserContext* _parserContext;
    private readonly AVFrame* _decodedFrame;
    private readonly AVFrame* _convertedFrame;
    private readonly AVPacket* _packet;
    private SwsContext* _scaleContext;
    private AVPixelFormat _currentPixelFormat = AVPixelFormat.AV_PIX_FMT_NONE;
    private readonly byte[] _bgraBuffer;
    private readonly GCHandle _bgraBufferHandle;
    private readonly byte_ptrArray4 _dstData;
    private readonly int_array4 _dstLineSize;

    public FfmpegHevcDecoder(int width, int height)
    {
        _width = width;
        _height = height;
        FfmpegLoader.EnsureInitialized();
        var codec = ffmpeg.avcodec_find_decoder(AVCodecID.AV_CODEC_ID_HEVC);
        if (codec is null)
        {
            throw new InvalidOperationException("HEVC decoder not available in FFmpeg runtime.");
        }

        _codecContext = ffmpeg.avcodec_alloc_context3(codec);
        if (_codecContext is null)
        {
            throw new InvalidOperationException("Failed to allocate FFmpeg codec context.");
        }

        _codecContext->thread_count = 0;
        _codecContext->thread_type = ffmpeg.FF_THREAD_FRAME;
        ThrowIfFailed(ffmpeg.avcodec_open2(_codecContext, codec, null), "Failed to open HEVC decoder.");

        _parserContext = ffmpeg.av_parser_init((int)AVCodecID.AV_CODEC_ID_HEVC);
        if (_parserContext is null)
        {
            throw new InvalidOperationException("Failed to initialize HEVC parser.");
        }

        _decodedFrame = ffmpeg.av_frame_alloc();
        _convertedFrame = ffmpeg.av_frame_alloc();
        _packet = ffmpeg.av_packet_alloc();
        if (_decodedFrame is null || _convertedFrame is null || _packet is null)
        {
            throw new InvalidOperationException("Failed to allocate FFmpeg packet/frame structures.");
        }

        _bgraBuffer = new byte[width * height * 4];
        _bgraBufferHandle = GCHandle.Alloc(_bgraBuffer, GCHandleType.Pinned);

        fixed (byte_ptrArray4* dstData = &_dstData)
        fixed (int_array4* dstLineSize = &_dstLineSize)
        {
            ThrowIfFailed(
                ffmpeg.av_image_fill_arrays(
                    ref _dstData,
                    ref _dstLineSize,
                    (byte*)_bgraBufferHandle.AddrOfPinnedObject(),
                    AVPixelFormat.AV_PIX_FMT_BGRA,
                    width,
                    height,
                    1),
                "Failed to prepare BGRA image buffers.");
        }
    }

    public void Decode(byte[] frameBytes, List<DecodedFrame> outputFrames)
    {
        var inputSize = frameBytes.Length + InputBufferPaddingSize;
        var inputBuffer = new byte[inputSize];
        frameBytes.CopyTo(inputBuffer.AsSpan(0, frameBytes.Length));

        fixed (byte* inputPtr = inputBuffer)
        {
            var consumed = 0;
            while (consumed < frameBytes.Length)
            {
                var remaining = frameBytes.Length - consumed;
                byte* poutbuf = null;
                var poutbufSize = 0;
                var parsed = ffmpeg.av_parser_parse2(
                    _parserContext,
                    _codecContext,
                    &poutbuf,
                    &poutbufSize,
                    inputPtr + consumed,
                    remaining,
                    0,
                    0,
                    0);

                if (parsed < 0)
                {
                    break;
                }

                consumed += parsed;

                if (poutbufSize <= 0)
                {
                    continue;
                }

                ThrowIfFailed(ffmpeg.av_new_packet(_packet, poutbufSize), "Failed to allocate packet.");
                Buffer.MemoryCopy(poutbuf, (void*)_packet->data, poutbufSize, poutbufSize);
                _packet->size = poutbufSize;

                ThrowIfFailed(ffmpeg.avcodec_send_packet(_codecContext, _packet), "Failed to send HEVC packet to decoder.");
                ffmpeg.av_packet_unref(_packet);

                ReceiveFrames(outputFrames);
            }
        }
    }

    private void ReceiveFrames(List<DecodedFrame> outputFrames)
    {
        while (true)
        {
            var result = ffmpeg.avcodec_receive_frame(_codecContext, _decodedFrame);
            if (result == ffmpeg.AVERROR(ffmpeg.EAGAIN) || result == ffmpeg.AVERROR_EOF)
            {
                return;
            }

            ThrowIfFailed(result, "Failed to receive decoded frame from FFmpeg.");

            EnsureScaleContext((AVPixelFormat)_decodedFrame->format);

            ffmpeg.sws_scale(
                _scaleContext,
                _decodedFrame->data,
                _decodedFrame->linesize,
                0,
                _decodedFrame->height,
                _dstData,
                _dstLineSize);

            outputFrames.Add(new DecodedFrame(_bgraBuffer.ToArray(), DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
        }
    }

    private void EnsureScaleContext(AVPixelFormat pixelFormat)
    {
        if (_currentPixelFormat == pixelFormat && _scaleContext is not null)
        {
            return;
        }

        if (_scaleContext is not null)
        {
            var old = _scaleContext;
            ffmpeg.sws_freeContext(old);
        }

        _currentPixelFormat = pixelFormat;
        _scaleContext = ffmpeg.sws_getContext(
            _width,
            _height,
            pixelFormat,
            _width,
            _height,
            AVPixelFormat.AV_PIX_FMT_BGRA,
            (int)SwsFlags.SWS_BILINEAR,
            null,
            null,
            null);

        if (_scaleContext is null)
        {
            throw new InvalidOperationException("Failed to create FFmpeg swscale context.");
        }
    }

    public void Dispose()
    {
        if (_bgraBufferHandle.IsAllocated)
        {
            _bgraBufferHandle.Free();
        }

        var packet = _packet;
        ffmpeg.av_packet_free(&packet);
        var decodedFrame = _decodedFrame;
        ffmpeg.av_frame_free(&decodedFrame);
        var convertedFrame = _convertedFrame;
        ffmpeg.av_frame_free(&convertedFrame);
        var scaleContext = _scaleContext;
        ffmpeg.sws_freeContext(scaleContext);
        var parserContext = _parserContext;
        ffmpeg.av_parser_close(parserContext);
        var codecContext = _codecContext;
        ffmpeg.avcodec_free_context(&codecContext);
    }

    private static void ThrowIfFailed(int errorCode, string message)
    {
        if (errorCode >= 0)
        {
            return;
        }

        var bufferSize = 1024;
        var buffer = stackalloc byte[bufferSize];
        ffmpeg.av_strerror(errorCode, buffer, (ulong)bufferSize);
        var detail = Marshal.PtrToStringAnsi((IntPtr)buffer) ?? $"ffmpeg error {errorCode}";
        throw new InvalidOperationException($"{message} {detail}");
    }
}

internal readonly record struct DecodedFrame(byte[] Buffer, long TimestampUnixMs);
