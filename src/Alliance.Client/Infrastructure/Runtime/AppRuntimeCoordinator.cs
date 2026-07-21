using Alliance.Client.Features.Video;
using Alliance.Client.Features.Telemetry;
using Microsoft.Extensions.Logging;

namespace Alliance.Client.Infrastructure.Runtime;

public sealed class AppRuntimeCoordinator
{
    private readonly ITelemetryService _telemetryService;
    private readonly IVideoSupervisorService _videoSupervisorService;
    private readonly ILogger<AppRuntimeCoordinator> _logger;

    public AppRuntimeCoordinator(
        ITelemetryService telemetryService,
        IVideoSupervisorService videoSupervisorService,
        ILogger<AppRuntimeCoordinator> logger)
    {
        _telemetryService = telemetryService;
        _videoSupervisorService = videoSupervisorService;
        _logger = logger;
    }

    public void Start()
    {
        _ = StartInternalAsync();
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _videoSupervisorService.StopAsync(cancellationToken);
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
            // TODO: re-enable when video is stable
            // await _videoSupervisorService.StartAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start application runtime services.");
        }
    }
}
