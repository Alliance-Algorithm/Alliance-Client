using System.Diagnostics;
using System.Runtime.InteropServices;
using Alliance.Client.Features.ScreenRecording;
using Microsoft.Extensions.Logging;

namespace Alliance.Client.Features.ScreenRecording;

public sealed class ScreenRecorderService
{
    private const string LibX11 = "libX11.so.6";

    [DllImport(LibX11)]
    private static extern IntPtr XOpenDisplay(IntPtr display);

    [DllImport(LibX11)]
    private static extern int XDisplayWidth(IntPtr display, int screen);

    [DllImport(LibX11)]
    private static extern int XDisplayHeight(IntPtr display, int screen);

    [DllImport(LibX11)]
    private static extern int XCloseDisplay(IntPtr display);

    private readonly RecordingSettings _settings;
    private readonly ILogger<ScreenRecorderService> _logger;
    private Process? _process;
    private CancellationTokenSource? _monitorCts;
    private Task? _monitorTask;
    private string? _outputPath;
    private DateTimeOffset _startedAt;
    private long _lastFileSize;
    private bool _isRecording;
    private string? _lastError;

    public ScreenRecorderService(RecordingSettings settings, ILogger<ScreenRecorderService> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public bool IsRecording => _isRecording;

    public event Action? StatusChanged;

    public RecordingStatus GetStatus()
    {
        return new RecordingStatus(
            IsRecording: _isRecording,
            Duration: _isRecording ? DateTimeOffset.UtcNow - _startedAt : TimeSpan.Zero,
            FileSizeBytes: _lastFileSize,
            Error: _lastError);
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_isRecording)
        {
            return Task.CompletedTask;
        }

        _lastError = null;

        var outputDir = ResolveOutputDirectory(_settings.OutputDirectory);
        Directory.CreateDirectory(outputDir);
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        _outputPath = Path.Combine(outputDir, $"Alliance_Client_{timestamp}.mp4");

        var screen = GetScreenBounds();
        var args = BuildFfmpegArgs(screen.Width, screen.Height, _outputPath);

        var ffmpegPath = ResolveFfmpegPath();
        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        _process = Process.Start(startInfo)
                   ?? throw new InvalidOperationException("Failed to start ffmpeg process.");

        _startedAt = DateTimeOffset.UtcNow;
        _isRecording = true;
        _lastFileSize = 0;

        _logger.LogInformation("[recording] Started: {Path} (ffmpeg PID={Pid})", _outputPath, _process.Id);

        _monitorCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _monitorTask = Task.Run(() => MonitorAsync(_monitorCts.Token), _monitorCts.Token);
        StatusChanged?.Invoke();
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!_isRecording || _process is null)
        {
            return;
        }

        try
        {
            await _process.StandardInput.WriteLineAsync("q");
            _logger.LogInformation("[recording] Sent quit signal to ffmpeg.");

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            await _process.WaitForExitAsync(linkedCts.Token);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("[recording] ffmpeg did not exit gracefully, killing process.");
            TryKill(_process);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[recording] Error stopping ffmpeg.");
            TryKill(_process);
        }

        await CleanupAsync();
        RefreshFileSize();
        _isRecording = false;
        _logger.LogInformation("[recording] Stopped. File: {Path} ({Size} bytes)", _outputPath, _lastFileSize);
        StatusChanged?.Invoke();
    }

    private async Task MonitorAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (_process is null || _process.HasExited)
            {
                var exitCode = _process?.ExitCode;
                if (_isRecording && exitCode != 0)
                {
                    _lastError = $"ffmpeg exited unexpectedly with code {exitCode}.";
                    _logger.LogError("[recording] {Error}", _lastError);
                    await CleanupAsync();
                    _isRecording = false;
                    StatusChanged?.Invoke();
                }
                break;
            }

            RefreshFileSize();
            StatusChanged?.Invoke();
            await Task.Delay(500, cancellationToken);
        }
    }

    private void RefreshFileSize()
    {
        if (_outputPath is not null && File.Exists(_outputPath))
        {
            try
            {
                _lastFileSize = new FileInfo(_outputPath).Length;
            }
            catch
            {
            }
        }
    }

    private async Task CleanupAsync()
    {
        if (_monitorCts is not null)
        {
            await _monitorCts.CancelAsync();
            _monitorCts.Dispose();
            _monitorCts = null;
        }

        if (_monitorTask is not null)
        {
            try
            {
                await _monitorTask;
            }
            catch
            {
            }
            _monitorTask = null;
        }

        _process?.Dispose();
        _process = null;
    }

    private string BuildFfmpegArgs(int width, int height, string outputPath)
    {
        return $"-f x11grab -video_size {width}x{height} -framerate {_settings.FrameRate} -i :0.0 "
               + $"-c:v libx264 -preset ultrafast -crf {_settings.Crf} -pix_fmt yuv420p "
               + $"-movflags +faststart -y \"{outputPath}\"";
    }

    private static string ResolveOutputDirectory(string path)
    {
        if (path.StartsWith("~") && (path.Length == 1 || path[1] == '/' || path[1] == '\\'))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.GetFullPath(home + path[1..]);
        }
        return Path.GetFullPath(path);
    }

    private static (int Width, int Height) GetScreenBounds()
    {
        try
        {
            var display = XOpenDisplay(IntPtr.Zero);
            if (display == IntPtr.Zero)
                return (1920, 1080);

            var w = XDisplayWidth(display, 0);
            var h = XDisplayHeight(display, 0);
            XCloseDisplay(display);
            return (w, h);
        }
        catch
        {
            return (1920, 1080);
        }
    }

    private static string ResolveFfmpegPath()
    {
        var envPath = Environment.GetEnvironmentVariable("ALLIANCE_FFMPEG_ROOT");
        if (envPath is not null)
        {
            var candidate = Path.Combine(envPath, "ffmpeg");
            if (File.Exists(candidate)) return candidate;
        }

        var localPath = Path.Combine(AppContext.BaseDirectory, "ffmpeg", "ffmpeg");
        if (File.Exists(localPath)) return localPath;

        return "ffmpeg";
    }

    private static void TryKill(Process? process)
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
}

public sealed record RecordingStatus(
    bool IsRecording,
    TimeSpan Duration,
    long FileSizeBytes,
    string? Error);
