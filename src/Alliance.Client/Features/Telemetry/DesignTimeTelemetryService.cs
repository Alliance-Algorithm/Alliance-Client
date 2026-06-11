namespace Alliance.Client.Features.Telemetry;

public sealed class DesignTimeTelemetryService : ITelemetryService
{
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
