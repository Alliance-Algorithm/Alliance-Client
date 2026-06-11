namespace Alliance.Client.Features.Telemetry;

public interface ITelemetryService
{
    TelemetrySnapshot GetSnapshot();
}
