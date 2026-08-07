using System.ComponentModel;
using System.Diagnostics;
using Alliance.Client.Features.Telemetry;
using Microsoft.Extensions.Logging;

namespace Alliance.Client.Features.Audio;

public sealed class EnemyRespawnAudioService
{
    private static readonly TimeSpan CooldownPerRobot = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan HighlightDuration = TimeSpan.FromSeconds(5);
    private const int RespawnHealthThreshold = 150;

    private static readonly byte[] BuiltInBeepWav = GenerateBeepWav(frequency: 880, durationMs: 200);

    private readonly TelemetryStore _telemetryStore;
    private readonly RespawnHighlightState _respawnHighlightState;
    private readonly ILogger<EnemyRespawnAudioService> _logger;
    private readonly Dictionary<string, int> _previousHealth = new();
    private readonly Dictionary<string, DateTime> _lastPlayedAt = new();

    public EnemyRespawnAudioService(
        TelemetryStore telemetryStore,
        RespawnHighlightState respawnHighlightState,
        ILogger<EnemyRespawnAudioService> logger)
    {
        _telemetryStore = telemetryStore;
        _respawnHighlightState = respawnHighlightState;
        _logger = logger;
        _telemetryStore.PropertyChanged += HandleTelemetryChanged;
    }

    private void HandleTelemetryChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(TelemetryStore.CurrentSnapshot))
            return;

        var snapshot = _telemetryStore.CurrentSnapshot;
        var now = DateTime.UtcNow;

        foreach (var robot in snapshot.EnemyRobots)
        {
            if (robot.IsAerial)
                continue;

            var currentHealth = robot.HealthValue ?? 0;
            var prevHealth = _previousHealth.GetValueOrDefault(robot.SlotLabel, 0);

            if (prevHealth == 0 && currentHealth >= RespawnHealthThreshold)
            {
                if (!_lastPlayedAt.TryGetValue(robot.SlotLabel, out var lastPlay) ||
                    now - lastPlay >= CooldownPerRobot)
                {
                    _logger.LogInformation(
                        "Enemy robot {Slot} respawned (health: {Prev} -> {Current})",
                        robot.SlotLabel, prevHealth, currentHealth);
                    _respawnHighlightState.MarkRespawn(robot.SlotLabel, now, HighlightDuration);
                    PlayRespawnAudio();
                    _lastPlayedAt[robot.SlotLabel] = now;
                }
            }

            _previousHealth[robot.SlotLabel] = currentHealth;
        }
    }

    public void TestTrigger()
    {
        var now = DateTime.UtcNow;
        foreach (var slot in new[] { "1", "2", "3", "4", "7" })
        {
            _respawnHighlightState.MarkRespawn(slot, now, HighlightDuration);
        }

        _telemetryStore.ForceRefresh();

        _logger.LogInformation("Test trigger: all enemy robot highlights activated");
        PlayRespawnAudio();
    }

    private void PlayRespawnAudio()
    {
        var ffplayPath = ResolveFfplayPath();

        var audioPath = ResolveAudioPath();
        if (File.Exists(audioPath))
        {
            PlayFile(ffplayPath, audioPath);
            return;
        }

        PlayBuiltInBeep(ffplayPath);
    }

    private void PlayFile(string ffplayPath, string audioPath)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = ffplayPath,
                Arguments = $"-nodisp -autoexit -loglevel quiet \"{audioPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to play audio via {Path}", ffplayPath);
        }
    }

    private void PlayBuiltInBeep(string ffplayPath)
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
                }
            };
            process.Start();
            process.StandardInput.BaseStream.Write(BuiltInBeepWav);
            process.StandardInput.BaseStream.Flush();
            process.StandardInput.Close();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to play built-in beep via {Path}", ffplayPath);
        }
    }

    private static string ResolveAudioPath()
    {
        var envPath = Environment.GetEnvironmentVariable("ALLIANCE_RESPAWN_AUDIO");
        if (envPath is not null && File.Exists(envPath))
            return envPath;

        return Path.Combine(AppContext.BaseDirectory, "respawn.wav");
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

    private static byte[] GenerateBeepWav(double frequency, int durationMs)
    {
        const int sampleRate = 44100;
        const short bitsPerSample = 16;
        const short channels = 1;
        const int beepMs = 120;
        const int gapMs = 40;
        const int beepCount = 6;
        const int edgeSamples = 2;
        var bytesPerSample = bitsPerSample / 8;
        var totalMs = beepCount * beepMs + (beepCount - 1) * gapMs;
        var sampleCount = sampleRate * totalMs / 1000;
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
        var beepSamples = sampleRate * beepMs / 1000;
        var gapSamples = sampleRate * gapMs / 1000;
        var maxAmp = short.MaxValue;

        for (var i = 0; i < totalSamples; i++)
        {
            var posInPattern = i % (beepSamples + gapSamples);

            if (posInPattern >= beepSamples)
            {
                writer.Write((short)0);
                continue;
            }

            var edge = Math.Min(1.0, Math.Min(
                (double)posInPattern / edgeSamples,
                (double)(beepSamples - 1 - posInPattern) / edgeSamples));
            if (edge <= 0) edge = 0;

            var t = (double)posInPattern / sampleRate;
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
