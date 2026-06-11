namespace Alliance.Client.Features.Telemetry;

public sealed class DesignTimeTelemetryService : ITelemetryService
{
    private readonly TelemetryStore _telemetryStore;

    public DesignTimeTelemetryService(TelemetryStore telemetryStore)
    {
        _telemetryStore = telemetryStore;
    }

    public TelemetrySnapshot GetSnapshot()
    {
        return _telemetryStore.CurrentSnapshot;
    }
}
