using System.ComponentModel;
using System.Diagnostics;
using Alliance.Client.Features.Telemetry;
using Microsoft.Extensions.Logging;

namespace Alliance.Client.Features.Audio;

public sealed class SentinelAmmolessAlertService
{
    private static readonly byte[] SentinelBeepWav = GenerateSentinelBeepWav();

    private readonly TelemetryStore _telemetryStore;
    private readonly RespawnHighlightState _respawnHighlightState;
    private readonly SentinelAmmolessHighlightState _sentinelHighlightState;
    private readonly ILogger<SentinelAmmolessAlertService> _logger;

    private CancellationTokenSource? _loopCts;
    private bool _isTestMode;

    public SentinelAmmolessAlertService(
        TelemetryStore telemetryStore,
        RespawnHighlightState respawnHighlightState,
        SentinelAmmolessHighlightState sentinelHighlightState,
        ILogger<SentinelAmmolessAlertService> logger)
    {
        _telemetryStore = telemetryStore;
        _respawnHighlightState = respawnHighlightState;
        _sentinelHighlightState = sentinelHighlightState;
        _logger = logger;
        _telemetryStore.PropertyChanged += HandleTelemetryChanged;
    }

    private void HandleTelemetryChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (_isTestMode || args.PropertyName != nameof(TelemetryStore.CurrentSnapshot))
            return;

        var snapshot = _telemetryStore.CurrentSnapshot;
        var sentinel = snapshot.AllyRobots.FirstOrDefault(r => r.SlotLabel == "7");
        if (sentinel is null)
            return;

        var ammoValue = sentinel.AmmoValue ?? -1;
        var isAmmoless = ammoValue == 0;

        if (isAmmoless && !_sentinelHighlightState.IsActive)
        {
            _sentinelHighlightState.IsActive = true;
            StartLoop();
            _telemetryStore.ForceRefresh();
        }
        else if (!isAmmoless && _sentinelHighlightState.IsActive)
        {
            _sentinelHighlightState.IsActive = false;
            StopLoop();
            _telemetryStore.ForceRefresh();
        }
    }

    public void TestTrigger()
    {
        if (_sentinelHighlightState.IsActive)
            return;

        _isTestMode = true;
        _sentinelHighlightState.IsActive = true;
        StartLoop();
        _telemetryStore.ForceRefresh();

        _ = Task.Delay(5000).ContinueWith(_ =>
        {
            _sentinelHighlightState.IsActive = false;
            StopLoop();
            _isTestMode = false;
            _telemetryStore.ForceRefresh();
        });
    }

    private void StartLoop()
    {
        _loopCts?.Cancel();
        _loopCts?.Dispose();
        _loopCts = new CancellationTokenSource();
        _ = LoopAsync(_loopCts.Token);
    }

    private void StopLoop()
    {
        _loopCts?.Cancel();
        _loopCts?.Dispose();
        _loopCts = null;
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        var ffplayPath = ResolveFfplayPath();

        while (!ct.IsCancellationRequested)
        {
            var hasRespawn = CheckRespawnActive();

            if (!hasRespawn)
            {
                PlaySentinelBeep(ffplayPath, ct);
                try { await Task.Delay(1000, ct); } catch (OperationCanceledException) { return; }
            }
            else
            {
                try { await Task.Delay(2000, ct); } catch (OperationCanceledException) { return; }
            }
        }
    }

    private bool CheckRespawnActive()
    {
        var now = DateTime.UtcNow;
        foreach (var slot in new[] { "1", "2", "3", "4", "7" })
        {
            if (_respawnHighlightState.IsHighlighted(slot, now))
                return true;
        }

        return false;
    }

    private void PlaySentinelBeep(string ffplayPath, CancellationToken ct)
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ffplayPath,
                    Arguments = "-nodisp -autoexit -loglevel quiet -f wav -",
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    CreateNoWindow = true
                },
                EnableRaisingEvents = true
            };

            var processStarted = new TaskCompletionSource<bool>();
            process.Exited += (_, _) =>
            {
                try { processStarted.TrySetResult(true); } catch { }
            };

            process.Start();
            process.StandardInput.BaseStream.Write(SentinelBeepWav);
            process.StandardInput.BaseStream.Flush();
            process.StandardInput.Close();

            using var ctr = ct.Register(() =>
            {
                try { if (!process.HasExited) process.Kill(); } catch { }
            });

            process.WaitForExit();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to play sentinel ammo alert via {Path}", ffplayPath);
        }
    }

    private static string ResolveFfplayPath()
    {
        var envPath = Environment.GetEnvironmentVariable("ALLIANCE_FFMPEG_ROOT");
        if (envPath is not null)
        {
            var candidate = Path.Combine(envPath, OperatingSystem.IsWindows() ? "ffplay.exe" : "ffplay");
            if (File.Exists(candidate))
                return candidate;
        }

        var localDir = Path.Combine(AppContext.BaseDirectory, "ffmpeg");
        var localName = OperatingSystem.IsWindows() ? "ffplay.exe" : "ffplay";
        var localPath = Path.Combine(localDir, localName);
        if (File.Exists(localPath))
            return localPath;

        return OperatingSystem.IsWindows() ? "ffplay.exe" : "ffplay";
    }

    private static byte[] GenerateSentinelBeepWav()
    {
        const int sampleRate = 44100;
        const short bitsPerSample = 16;
        const short channels = 1;
        const double frequency = 880;
        const int durationMs = 1000;
        const int edgeSamples = 5;
        var bytesPerSample = bitsPerSample / 8;
        var sampleCount = sampleRate * durationMs / 1000;
        var dataSize = sampleCount * bytesPerSample * channels;
        var fileSize = 36 + dataSize;

        using var stream = new MemoryStream(44 + dataSize);
        var writer = new BinaryWriter(stream);

        writer.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
        writer.Write(fileSize);
        writer.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E' });
        writer.Write(new[] { (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * bytesPerSample * channels);
        writer.Write((short)(bytesPerSample * channels));
        writer.Write(bitsPerSample);
        writer.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
        writer.Write(dataSize);

        var totalSamples = dataSize / bytesPerSample;
        var maxAmp = short.MaxValue;

        for (var i = 0; i < totalSamples; i++)
        {
            var edge = Math.Min(1.0, Math.Min(
                (double)i / edgeSamples,
                (double)(totalSamples - 1 - i) / edgeSamples));
            if (edge <= 0) edge = 0;

            var t = (double)i / sampleRate;
            var raw = Math.Sin(2 * Math.PI * frequency * t)
                    + 0.5 * Math.Sin(2 * Math.PI * frequency * 2 * t)
                    + 0.3 * Math.Sin(2 * Math.PI * frequency * 3 * t);
            raw /= 1.8;
            var value = Math.Sign(raw) * 0.95;
            writer.Write((short)(value * maxAmp * edge));
        }

        return stream.ToArray();
    }
}
