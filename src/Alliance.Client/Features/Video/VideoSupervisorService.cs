using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
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
    private DateTimeOffset _lastDiagnosticsAt = DateTimeOffset.MinValue;

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
            Process? process = null;
            Task drainStdout = Task.CompletedTask;
            Task drainStderr = Task.CompletedTask;

            try
            {
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
                (process, drainStdout, drainStderr) = StartWorker(workerMessage, cancellationToken);

                using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    connectCts.CancelAfter(TimeSpan.FromSeconds(5));
                    try
                    {
                        await pipeServer.WaitForConnectionAsync(connectCts.Token);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        throw new TimeoutException("Video worker did not connect within 5 seconds.");
                    }
                }

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
                    catch (JsonException ex)
                    {
                        _logger.LogWarning(ex, "Invalid video worker heartbeat payload received; supervisor will wait for restart conditions.");
                    }
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
                try
                {
                    await Task.WhenAll(drainStdout, drainStderr).WaitAsync(TimeSpan.FromSeconds(3));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Draining worker output streams failed during cleanup.");
                }
                process?.Dispose();
                TryDelete(sharedMemoryPath);
            }

            await Task.Delay(restartDelay, cancellationToken);
            restartDelay = TimeSpan.FromMilliseconds(Math.Min(_settings.Video.RestartMaxDelayMs, restartDelay.TotalMilliseconds * 2));
        }
    }

    private (Process Process, Task DrainStdout, Task DrainStderr) StartWorker(VideoControlMessage workerMessage, CancellationToken cancellationToken)
    {
        var payloadJson = JsonSerializer.Serialize(workerMessage, WorkerProtocol.JsonOptions);
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(payloadJson));
        var launchInfo = ResolveWorkerLaunchInfo(AppContext.BaseDirectory, payload);
        var startInfo = new ProcessStartInfo
        {
            FileName = launchInfo.FileName,
            Arguments = launchInfo.Arguments,
            WorkingDirectory = launchInfo.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start Alliance.VideoWorker.");
        var drainStdout = DrainStreamAsync(process.StandardOutput, isError: false, cancellationToken);
        var drainStderr = DrainStreamAsync(process.StandardError, isError: true, cancellationToken);
        return (process, drainStdout, drainStderr);
    }

    internal static WorkerLaunchInfo ResolveWorkerLaunchInfo(string baseDirectory, string payload)
    {
        foreach (var candidate in EnumerateWorkerCandidates(baseDirectory))
        {
            if (File.Exists(candidate.ExecutablePath))
            {
                return new WorkerLaunchInfo(candidate.ExecutablePath, payload, baseDirectory);
            }

            if (File.Exists(candidate.DllPath))
            {
                return new WorkerLaunchInfo("dotnet", $"\"{candidate.DllPath}\" {payload}", baseDirectory);
            }
        }

        throw new FileNotFoundException(
            "Alliance.VideoWorker was not found in the packaged worker directory, the application output, or repo build paths.",
            Path.Combine(baseDirectory, GetWorkerDllName()));
    }

    private static IEnumerable<WorkerCandidate> EnumerateWorkerCandidates(string baseDirectory)
    {
        yield return new WorkerCandidate(
            Path.Combine(baseDirectory, "worker", GetWorkerExecutableName()),
            Path.Combine(baseDirectory, "worker", GetWorkerDllName()));

        yield return new WorkerCandidate(
            Path.Combine(baseDirectory, GetWorkerExecutableName()),
            Path.Combine(baseDirectory, GetWorkerDllName()));

        var repoBinRoot = TryResolveRepoBinRoot(baseDirectory);
        if (repoBinRoot is null)
        {
            yield break;
        }

        foreach (var workerDirectory in EnumerateRepoWorkerDirectories(repoBinRoot))
        {
            yield return new WorkerCandidate(
                Path.Combine(workerDirectory, GetWorkerExecutableName()),
                Path.Combine(workerDirectory, GetWorkerDllName()));
        }
    }

    private static string? TryResolveRepoBinRoot(string baseDirectory)
    {
        for (var current = new DirectoryInfo(baseDirectory); current is not null; current = current.Parent)
        {
            if (!File.Exists(Path.Combine(current.FullName, "Alliance.sln")))
            {
                continue;
            }

            var repoBinRoot = Path.Combine(current.FullName, "src", "Alliance.VideoWorker", "bin");
            return Directory.Exists(repoBinRoot) ? repoBinRoot : null;
        }

        return null;
    }

    private static IEnumerable<string> EnumerateRepoWorkerDirectories(string repoBinRoot)
    {
        var currentRid = GetCurrentRid();
        foreach (var configuration in new[] { "Debug", "Release" })
        {
            var tfmDirectory = Path.Combine(repoBinRoot, configuration, "net10.0");
            if (!Directory.Exists(tfmDirectory))
            {
                continue;
            }

            if (currentRid is not null)
            {
                var ridDirectory = Path.Combine(tfmDirectory, currentRid);
                if (Directory.Exists(ridDirectory))
                {
                    yield return ridDirectory;
                }
            }

            yield return tfmDirectory;
        }
    }

    private static string? GetCurrentRid()
    {
        if (OperatingSystem.IsLinux())
        {
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "linux-x64",
                Architecture.Arm64 => "linux-arm64",
                _ => null
            };
        }

        if (OperatingSystem.IsWindows())
        {
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "win-x64",
                Architecture.Arm64 => "win-arm64",
                _ => null
            };
        }

        if (OperatingSystem.IsMacOS())
        {
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "osx-x64",
                Architecture.Arm64 => "osx-arm64",
                _ => null
            };
        }

        return null;
    }

    private static string GetWorkerExecutableName()
    {
        return OperatingSystem.IsWindows() ? "Alliance.VideoWorker.exe" : "Alliance.VideoWorker";
    }

    private static string GetWorkerDllName()
    {
        return "Alliance.VideoWorker.dll";
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

        var now = DateTimeOffset.UtcNow;
        if ((now - _lastDiagnosticsAt).TotalMilliseconds >= 1000)
        {
            _lastDiagnosticsAt = now;
            _logger.LogInformation(
                "[video-diag] state={State} note={Note} pkts={Packets} assembled={Assembled} decoded={Decoded} presented={Presented} decodeErr={DecodeErrors} fps={Fps:F1} lastPacket={LastPacket} lastFrame={LastFrame}",
                message.StreamState,
                message.Note,
                message.PacketCount,
                message.AssembledFrameCount,
                message.DecodedFrameCount,
                message.PresentedFrameCount,
                message.DecodeErrorCount,
                message.PresentFps,
                message.LastPacketAt?.ToLocalTime().ToString("HH:mm:ss.fff") ?? "-",
                message.LastFrameAt?.ToLocalTime().ToString("HH:mm:ss.fff") ?? "-");
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
            _store.SetStatus(state, statusText, metricsText, message.LastPacketAt, message.LastFrameAt, frameVersion));
    }

    private async Task DrainStreamAsync(StreamReader reader, bool isError, CancellationToken cancellationToken)
    {
        try
        {
            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (isError)
                {
                    _logger.LogWarning("[worker] {Line}", line);
                }
                else
                {
                    _logger.LogInformation("[worker] {Line}", line);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
    }

    private static void TryTerminate(Process? process)
    {
        try
        {
            if (process is not null && !process.HasExited)
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

    internal sealed record WorkerLaunchInfo(string FileName, string Arguments, string WorkingDirectory);

    private sealed record WorkerCandidate(string ExecutablePath, string DllPath);
}
