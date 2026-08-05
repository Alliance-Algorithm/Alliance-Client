using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Alliance.Client.Infrastructure.Power;

public sealed partial class ScreenWakeLockService : IDisposable
{
    private const string AppName = "Alliance Client";
    private const string Reason = "Keeping screen awake during match";

    private readonly ILogger<ScreenWakeLockService> _logger;
    private uint? _cookie;
    private bool _disposed;

    public ScreenWakeLockService(ILogger<ScreenWakeLockService> logger)
    {
        _logger = logger;
    }

    public void Start()
    {
        if (_disposed)
            return;

        if (_cookie.HasValue)
        {
            _logger.LogDebug("Screen wake lock already active (cookie={Cookie})", _cookie.Value);
            return;
        }

        try
        {
            var cookie = CallInhibit();
            if (cookie.HasValue)
            {
                _cookie = cookie;
                _logger.LogInformation("Screen wake lock acquired (cookie={Cookie})", _cookie.Value);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to acquire screen wake lock via DBus");
        }
    }

    public void Stop()
    {
        if (!_cookie.HasValue)
            return;

        try
        {
            CallUnInhibit(_cookie.Value);
            _logger.LogInformation("Screen wake lock released (cookie={Cookie})", _cookie.Value);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to release screen wake lock (cookie={Cookie})", _cookie.Value);
        }
        finally
        {
            _cookie = null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Stop();
    }

    private static uint? CallInhibit()
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dbus-send",
                Arguments = "--session --print-reply " +
                            "--dest=org.freedesktop.ScreenSaver " +
                            "/org/freedesktop/ScreenSaver " +
                            "org.freedesktop.ScreenSaver.Inhibit " +
                            $"string:\"{AppName}\" string:\"{Reason}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            var error = process.StandardError.ReadToEnd();
            throw new InvalidOperationException(
                $"dbus-send Inhibit failed (exit={process.ExitCode}): {error}");
        }

        return ParseCookie(output);
    }

    private static void CallUnInhibit(uint cookie)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dbus-send",
                Arguments = "--session --print-reply " +
                            "--dest=org.freedesktop.ScreenSaver " +
                            "/org/freedesktop/ScreenSaver " +
                            "org.freedesktop.ScreenSaver.UnInhibit " +
                            $"uint32:{cookie}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            var error = process.StandardError.ReadToEnd();
            throw new InvalidOperationException(
                $"dbus-send UnInhibit failed (exit={process.ExitCode}): {error}");
        }
    }

    private static uint? ParseCookie(string output)
    {
        var match = CookieRegex().Match(output);
        if (!match.Success)
            return null;

        return uint.Parse(match.Groups[1].Value);
    }

    [GeneratedRegex(@"uint32\s+(\d+)")]
    private static partial Regex CookieRegex();
}
