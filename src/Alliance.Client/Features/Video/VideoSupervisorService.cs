using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using Alliance.Client.Features.Settings;
using Alliance.Client.Shared.Models;
using Alliance.Video.Common;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;

namespace Alliance.Client.Features.Video;

public sealed class VideoSupervisorService : IVideoSupervisorService
{
    private readonly AppSettings _settings;
    private readonly VideoStreamStore _store;
    private readonly ILogger<VideoSupervisorService> _logger;
    private CancellationTokenSource? _runtimeCts;
    private Task? _runTask;

    public VideoSupervisorService(
        AppSettings settings,
        VideoStreamStore store,
        ILogger<VideoSupervisorService> logger)
    {
        _settings = settings;
        _store = store;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (!_settings.Video.Enabled || _runtimeCts is not null)
        {
            return Task.CompletedTask;
        }

        _runtimeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _runTask = Task.Run(() => RunAsync(_runtimeCts.Token), _runtimeCts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_runtimeCts is null)
        {
            return;
        }

        await _runtimeCts.CancelAsync();
        if (_runTask is not null)
        {
            await _runTask.WaitAsync(cancellationToken);
        }

        _runtimeCts.Dispose();
        _runtimeCts = null;
        _runTask = null;
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var restartDelay = TimeSpan.FromMilliseconds(_settings.Video.RestartInitialDelayMs);
        while (!cancellationToken.IsCancellationRequested)
        {
            var sharedMemoryPath = Path.Combine(Path.GetTempPath(), $"alliance-video-{Guid.NewGuid():N}.mmap");
            var pipeName = $"alliance-video-{Guid.NewGuid():N}";
            Directory.CreateDirectory(Path.GetDirectoryName(sharedMemoryPath)!);
            using var backingStream = new FileStream(sharedMemoryPath, FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite);
            backingStream.SetLength(new SharedFrameLayout(_settings.Video.FrameWidth, _settings.Video.FrameHeight).TotalBytes);

            var workerMessage = new VideoControlMessage
            {
                SharedMemoryPath = sharedMemoryPath,
                StatusPipeName = pipeName,
                UdpPort = _settings.Video.UdpPort,
                FrameWidth = _settings.Video.FrameWidth,
                FrameHeight = _settings.Video.FrameHeight,
                ExpectedFps = _settings.Video.ExpectedFps,
                PresentFps = _settings.Video.PresentFps,
                FrameAssemblyTimeoutMs = _settings.Video.FrameAssemblyTimeoutMs,
                HeartbeatIntervalMs = _settings.Video.HeartbeatIntervalMs,
                SignalLostAfterMs = _settings.Video.SignalLostAfterMs,
                ClearFrameAfterMs = _settings.Video.ClearFrameAfterMs
            };

            using var pipeServer = new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            using var process = StartWorker(workerMessage);

            try
            {
                await pipeServer.WaitForConnectionAsync(cancellationToken)
                    .WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
                using var reader = new StreamReader(pipeServer, Encoding.UTF8);
                using var sharedReader = new SharedFrameReader(sharedMemoryPath, _settings.Video.FrameWidth, _settings.Video.FrameHeight);

                restartDelay = TimeSpan.FromMilliseconds(_settings.Video.RestartInitialDelayMs);
                long lastHeartbeatAtTicks = DateTimeOffset.UtcNow.Ticks;
                long lastFrameVersion;
                VideoStatusMessage? latestStatus = null;
                var statusLock = new object();

                using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var heartbeatTask = Task.Run(async () =>
                {
                    try
                    {
                        while (!heartbeatCts.Token.IsCancellationRequested)
                        {
                            var line = await reader.ReadLineAsync(heartbeatCts.Token);
                            if (line is null) break;
                            if (string.IsNullOrWhiteSpace(line)) continue;

                            var message = JsonSerializer.Deserialize<VideoStatusMessage>(line, WorkerProtocol.JsonOptions);
                            if (message is not null)
                            {
                                Interlocked.Exchange(ref lastHeartbeatAtTicks, DateTimeOffset.UtcNow.Ticks);
                                lock (statusLock) { latestStatus = message; }
                            }
                        }
                    }
                    catch (OperationCanceledException) { }
                    catch (IOException) { }
                }, heartbeatCts.Token);

                try
                {
                    lastFrameVersion = 0;
                    while (!cancellationToken.IsCancellationRequested && !process.HasExited)
                    {
                        VideoStatusMessage? status;
                        lock (statusLock) { status = latestStatus; latestStatus = null; }
                        if (status is not null)
                        {
                            await ApplyWorkerStatusAsync(status, Interlocked.Read(ref lastFrameVersion));
                        }

                        if (sharedReader.TryReadLatestFrame(out var frame, out var version, out _)
                            && version != Interlocked.Read(ref lastFrameVersion))
                        {
                            Interlocked.Exchange(ref lastFrameVersion, version);
                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                _store.UpdateFrame(frame.Span);
                                _store.SetStatus(
                                    ConnectionState.Ready,
                                    "VIDEO READY",
                                    _store.Snapshot.MetricsText,
                                    _store.Snapshot.LastPacketAt,
                                    DateTimeOffset.UtcNow,
                                    version);
                            });
                        }

                        var silence = TimeSpan.FromTicks(DateTimeOffset.UtcNow.Ticks - Interlocked.Read(ref lastHeartbeatAtTicks));
                        if (silence.TotalMilliseconds > _settings.Video.HeartbeatTimeoutMs)
                        {
                            throw new TimeoutException("Video worker heartbeat timed out.");
                        }

                        if (_store.Snapshot.LastFrameAt is { } lastFrameAt)
                        {
                            var frameAgeMs = (DateTimeOffset.UtcNow - lastFrameAt).TotalMilliseconds;
                            if (frameAgeMs > _settings.Video.ClearFrameAfterMs)
                            {
                                await Dispatcher.UIThread.InvokeAsync(() => _store.ClearFrame());
                            }
                        }

                        await Task.Delay(5, cancellationToken);
                    }
                }
                finally
                {
                    heartbeatCts.Cancel();
                    try { await heartbeatTask; } catch (OperationCanceledException) { }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Video supervisor loop failed; worker will restart.");
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _store.SetStatus(ConnectionState.NotConnected, "VIDEO RESTARTING", _store.Snapshot.MetricsText, null, null, _store.Snapshot.FrameVersion);
                    _store.ClearFrame();
                });
            }
            finally
            {
                TryTerminate(process);
                TryDelete(sharedMemoryPath);
            }

            await Task.Delay(restartDelay, cancellationToken);
            restartDelay = TimeSpan.FromMilliseconds(Math.Min(_settings.Video.RestartMaxDelayMs, restartDelay.TotalMilliseconds * 2));
        }
    }

    private Process StartWorker(VideoControlMessage workerMessage)
    {
        var workerDllPath = ResolveWorkerDllPath();
        var payloadJson = JsonSerializer.Serialize(workerMessage, WorkerProtocol.JsonOptions);
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(payloadJson));
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"\"{workerDllPath}\" {payload}",
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start Alliance.VideoWorker.");
        _ = DrainStreamAsync(process.StandardOutput);
        _ = DrainStreamAsync(process.StandardError);
        return process;
    }

    private static string ResolveWorkerDllPath()
    {
        var outputPath = Path.Combine(AppContext.BaseDirectory, "Alliance.VideoWorker.dll");
        if (File.Exists(outputPath))
        {
            return outputPath;
        }

        var repoPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../src/Alliance.VideoWorker/bin/Debug/net10.0/Alliance.VideoWorker.dll"));
        if (File.Exists(repoPath))
        {
            return repoPath;
        }

        throw new FileNotFoundException("Alliance.VideoWorker.dll was not found in the application output or repo build path.", outputPath);
    }

    private async Task ApplyWorkerStatusAsync(VideoStatusMessage message, long frameVersion)
    {
        var state = message.StreamState switch
        {
            "Ready" => ConnectionState.Ready,
            "Degraded" => ConnectionState.Degraded,
            "Connecting" => ConnectionState.Connecting,
            _ => ConnectionState.NotConnected
        };

        var statusText = message.LastFrameAt is null
            ? "WAITING FOR STREAM"
            : state == ConnectionState.Degraded
                ? "VIDEO LOST"
                : "VIDEO READY";
        var metricsText = $"{message.PresentFps:F1} fps | {message.DecodedFrameCount} frames";

        await Dispatcher.UIThread.InvokeAsync(() =>
            _store.SetStatus(state, statusText, metricsText, message.LastPacketAt, message.LastFrameAt, frameVersion));
    }

    private static async Task DrainStreamAsync(StreamReader reader)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            _ = line;
        }
    }

    private static void TryTerminate(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
        }
    }
}
