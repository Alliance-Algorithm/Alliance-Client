using System.Net.Sockets;
using Avalonia.Media;
using Avalonia.Threading;
using Alliance.Client.Features.Settings;
using Alliance.Client.Features.Telemetry;
using Alliance.Client.Features.Video.Decode;
using Alliance.Client.Features.Video.Udp;
using Alliance.Client.Shared.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace Alliance.Client.Features.Video;

public sealed class UdpHevcVideoStreamService : ObservableObject, IVideoStreamService
{
    private readonly AppSettings _settings;
    private readonly TelemetryStore _telemetryStore;
    private readonly ILogger<UdpHevcVideoStreamService> _logger;

    private CancellationTokenSource? _runtimeCts;
    private Task? _receiveTask;
    private ConnectionState _state = ConnectionState.NotConnected;
    private string _statusText = "No Stream";
    private string _frameCounterLabel = "FRAME 000000 | IDLE";
    private IImage? _currentFrame;

    public UdpHevcVideoStreamService(
        AppSettings settings,
        TelemetryStore telemetryStore,
        ILogger<UdpHevcVideoStreamService> logger)
    {
        _settings = settings;
        _telemetryStore = telemetryStore;
        _logger = logger;
        TransportDescription = $"UDP {_settings.UdpVideo.ListenPort:D4} / {_settings.UdpVideo.Codec.ToUpperInvariant()}";
    }

    public ConnectionState State
    {
        get => _state;
        private set => SetProperty(ref _state, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string TransportDescription { get; }

    public string FrameCounterLabel
    {
        get => _frameCounterLabel;
        private set => SetProperty(ref _frameCounterLabel, value);
    }

    public IImage? CurrentFrame
    {
        get => _currentFrame;
        private set => SetProperty(ref _currentFrame, value);
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_runtimeCts is not null)
        {
            _logger.LogInformation("Video stream service already running, skipping start");
            return Task.CompletedTask;
        }

        _logger.LogInformation(
            "Starting video stream on UDP port {Port}, codec={Codec}",
            _settings.UdpVideo.ListenPort,
            _settings.UdpVideo.Codec.ToUpperInvariant());
        _runtimeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _receiveTask = Task.Run(() => RunAsync(_runtimeCts.Token), _runtimeCts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_runtimeCts is null)
        {
            return;
        }

        _logger.LogInformation("Stopping video stream service");
        await _runtimeCts.CancelAsync();
        try
        {
            if (_receiveTask is not null)
            {
                await _receiveTask.WaitAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _runtimeCts.Dispose();
            _runtimeCts = null;
            _receiveTask = null;
            await RunOnUiThreadAsync(() =>
            {
                ClearCurrentFrame();
                ApplyState(ConnectionState.NotConnected, "Video stopped");
            });
            _logger.LogInformation("Video stream service stopped");
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Binding UDP socket on port {Port}", _settings.UdpVideo.ListenPort);
            await RunOnUiThreadAsync(() => ApplyState(ConnectionState.Connecting, "Binding UDP video socket"));

            using var udpClient = new UdpClient(_settings.UdpVideo.ListenPort);
            _logger.LogInformation("UDP socket bound to port {Port}", _settings.UdpVideo.ListenPort);

            _logger.LogInformation("Initializing FFmpeg HEVC decoder");
            using var decoder = new HevcDecoderAdapter();
            _logger.LogInformation("FFmpeg HEVC decoder initialized");
            var assembler = new HevcFrameAssembler();
            long frameCounter = 0;
            long packetCounter = 0;
            long completedFrames = 0;
            long skippedPackets = 0;
            var firstFrameLogged = false;
            var firstPacketLogged = false;
            var nextStatsAt = DateTimeOffset.UtcNow.AddSeconds(5);

            await RunOnUiThreadAsync(() => ApplyState(ConnectionState.Ready, "Waiting for HEVC frames"));
            _logger.LogInformation("Video stream ready, waiting for HEVC frames on port {Port}", _settings.UdpVideo.ListenPort);

            while (!cancellationToken.IsCancellationRequested)
            {
                var result = await udpClient.ReceiveAsync(cancellationToken);
                packetCounter++;

                if (!firstPacketLogged)
                {
                    _logger.LogInformation(
                        "Received first UDP packet on port {Port} ({Bytes} bytes): header={HeaderHex}",
                        _settings.UdpVideo.ListenPort,
                        result.Buffer.Length,
                        Convert.ToHexString(result.Buffer.AsSpan(0, Math.Min(16, result.Buffer.Length))));
                    firstPacketLogged = true;
                }
                else if (packetCounter % 300 == 0)
                {
                    _logger.LogDebug("UDP packet #{Count} received ({Bytes} bytes)", packetCounter, result.Buffer.Length);
                }

                if (!assembler.TryAddPacket(result.Buffer, out var frameData, out var note))
                {
                    skippedPackets++;
                    if (!string.IsNullOrWhiteSpace(note))
                    {
                        _logger.LogDebug("Frame assembler: {Note}", note);
                        if (note.StartsWith("Dropped", StringComparison.Ordinal))
                        {
                            await RunOnUiThreadAsync(() => ApplyState(ConnectionState.Degraded, note));
                        }
                    }

                    continue;
                }

                frameCounter++;
                completedFrames++;
                if (!firstFrameLogged)
                {
                    _logger.LogInformation("Received first complete HEVC frame ({Bytes} bytes)", frameData?.Length ?? 0);
                    firstFrameLogged = true;
                }

                if (DateTimeOffset.UtcNow >= nextStatsAt)
                {
                    _logger.LogInformation(
                        "Frame stats: completed={Completed}, skipped={Skipped}, totalPkts={TotalPkts}",
                        completedFrames, skippedPackets, packetCounter);
                    completedFrames = 0;
                    skippedPackets = 0;
                    packetCounter = 0;
                    nextStatsAt = DateTimeOffset.UtcNow.AddSeconds(5);
                }

                try
                {
                    if (frameData is null)
                    {
                        continue;
                    }

                    if (decoder.TryDecode(frameData, out var bitmap, out var statusText) && bitmap is not null)
                    {
                        await RunOnUiThreadAsync(() =>
                        {
                            SetCurrentFrame(bitmap);
                            FrameCounterLabel = $"FRAME {frameCounter:D6} | LIVE";
                            ApplyState(ConnectionState.Ready, statusText);
                        });
                    }
                    else
                    {
                        await RunOnUiThreadAsync(() => ApplyState(ConnectionState.Degraded, statusText));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to decode HEVC frame {FrameCounter}", frameCounter);
                    await RunOnUiThreadAsync(() => ApplyState(ConnectionState.Degraded, $"Decode error: {ex.Message}"));
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Video stream receive loop cancelled");
        }
        catch (SocketException ex)
        {
            _logger.LogError(ex,
                "UDP socket error on port {Port}: {Message} (ErrorCode={ErrorCode})",
                _settings.UdpVideo.ListenPort,
                ex.Message,
                ex.ErrorCode);
            await RunOnUiThreadAsync(() => ApplyState(ConnectionState.NotConnected, $"UDP error: {ex.Message}"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Video stream service failed: {Message} (Type={ExceptionType})",
                ex.Message,
                ex.GetType().Name);
            await RunOnUiThreadAsync(() => ApplyState(ConnectionState.NotConnected, $"Video error: {ex.Message}"));
        }
        finally
        {
            await RunOnUiThreadAsync(ClearCurrentFrame);
        }
    }

    private void SetCurrentFrame(IImage nextFrame)
    {
        var previousFrame = _currentFrame;
        CurrentFrame = nextFrame;
        DisposeFrameLater(previousFrame, nextFrame);
    }

    private void ClearCurrentFrame()
    {
        var previousFrame = _currentFrame;
        CurrentFrame = null;
        DisposeFrameLater(previousFrame, null);
    }

    private static void DisposeFrameLater(IImage? frame, IImage? nextFrame)
    {
        if (frame is null || ReferenceEquals(frame, nextFrame))
        {
            return;
        }

        if (frame is not IDisposable disposable)
        {
            return;
        }

        Dispatcher.UIThread.Post(disposable.Dispose, DispatcherPriority.Background);
    }

    private void ApplyState(ConnectionState state, string statusText)
    {
        State = state;
        StatusText = statusText;
        _telemetryStore.SetVideoState(state, statusText);
    }

    private static async Task RunOnUiThreadAsync(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(action);
    }
}
