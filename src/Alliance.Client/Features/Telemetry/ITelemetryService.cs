namespace Alliance.Client.Features.Telemetry;

public interface ITelemetryService
{
    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}
