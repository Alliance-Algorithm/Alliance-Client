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
            return Task.CompletedTask;
        }

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
            await RunOnUiThreadAsync(() => ApplyState(ConnectionState.NotConnected, "Video stopped"));
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RunOnUiThreadAsync(() => ApplyState(ConnectionState.Connecting, "Binding UDP video socket"));

            using var udpClient = new UdpClient(_settings.UdpVideo.ListenPort);
            using var decoder = new HevcDecoderAdapter();
            var assembler = new HevcFrameAssembler();
            long frameCounter = 0;

            await RunOnUiThreadAsync(() => ApplyState(ConnectionState.Ready, "Waiting for HEVC frames"));

            while (!cancellationToken.IsCancellationRequested)
            {
                var result = await udpClient.ReceiveAsync(cancellationToken);
                if (!assembler.TryAddPacket(result.Buffer, out var frameData, out var note))
                {
                    if (!string.IsNullOrWhiteSpace(note) && note.StartsWith("Dropped", StringComparison.Ordinal))
                    {
                        await RunOnUiThreadAsync(() => ApplyState(ConnectionState.Degraded, note));
                    }

                    continue;
                }

                frameCounter++;

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
                            CurrentFrame = bitmap;
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
        }
        catch (SocketException ex)
        {
            _logger.LogWarning(ex, "UDP video socket failed.");
            await RunOnUiThreadAsync(() => ApplyState(ConnectionState.NotConnected, $"UDP error: {ex.Message}"));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Video stream service failed.");
            await RunOnUiThreadAsync(() => ApplyState(ConnectionState.NotConnected, $"Video error: {ex.Message}"));
        }
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
