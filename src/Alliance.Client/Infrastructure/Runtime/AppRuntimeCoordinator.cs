using Alliance.Client.Features.Telemetry;
using Alliance.Client.Features.Video;
using Microsoft.Extensions.Logging;

namespace Alliance.Client.Infrastructure.Runtime;

public sealed class AppRuntimeCoordinator
{
    private readonly ITelemetryService _telemetryService;
    private readonly IVideoStreamService _videoStreamService;
    private readonly ILogger<AppRuntimeCoordinator> _logger;

    public AppRuntimeCoordinator(
        ITelemetryService telemetryService,
        IVideoStreamService videoStreamService,
        ILogger<AppRuntimeCoordinator> logger)
    {
        _telemetryService = telemetryService;
        _videoStreamService = videoStreamService;
        _logger = logger;
    }

    public void Start()
    {
        _ = StartInternalAsync();
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _videoStreamService.StopAsync(cancellationToken);
        await _telemetryService.StopAsync(cancellationToken);
    }

    public async Task RestartTelemetryAsync(CancellationToken cancellationToken = default)
    {
        await _telemetryService.StopAsync(cancellationToken);
        await _telemetryService.StartAsync(cancellationToken);
    }

    private async Task StartInternalAsync()
    {
        try
        {
            await _telemetryService.StartAsync();
            await _videoStreamService.StartAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start application runtime services.");
        }
    }
}
